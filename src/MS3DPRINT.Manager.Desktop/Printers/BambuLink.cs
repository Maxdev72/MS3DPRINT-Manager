using System.Buffers.Binary;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using MQTTnet;
using MS3DPRINT.Manager.Core.Printers;

namespace MS3DPRINT.Manager.Desktop.Printers;

/// <summary>Read-only LAN link to a Bambu Lab printer: MQTT status (port 8883) and, for A1/P1, the JPEG camera (port 6000).</summary>
public static class BambuLink
{
    private const string User = "bblp";
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(10);

    /// <summary>A1 and P1 send JPEG frames; X1, H2 and X2 models stream RTSP on port 322.</summary>
    public static bool HasJpegCamera(string model) =>
        model.Contains("A1", StringComparison.OrdinalIgnoreCase) || model.TrimStart().StartsWith("P1", StringComparison.OrdinalIgnoreCase);

    public static string RtspUrl(PrinterConnection c) => $"rtsps://{User}:{Uri.EscapeDataString(c.AccessCode)}@{c.Host}:322/streaming/live/1";

    /// <summary>Follows the printer until cancelled, reconnecting after a loss; <paramref name="offline"/> receives the reason.</summary>
    public static async Task WatchStatusAsync(PrinterConnection c, Action<BambuStatus> status, Action<string> offline, CancellationToken cancel)
    {
        while (!cancel.IsCancellationRequested)
        {
            try
            {
                var report = new BambuReport();
                var lost = new TaskCompletionSource();
                using var client = new MqttClientFactory().CreateMqttClient();
                client.ApplicationMessageReceivedAsync += e =>
                {
                    if (report.Apply(e.ApplicationMessage.ConvertPayloadToString()) is { } s) status(s);
                    return Task.CompletedTask;
                };
                client.DisconnectedAsync += _ => { lost.TrySetResult(); return Task.CompletedTask; };
                var options = new MqttClientOptionsBuilder().WithTcpServer(c.Host, 8883).WithCredentials(User, c.AccessCode)
                    .WithClientId("ms3dprint-" + Guid.NewGuid().ToString("N")[..8]).WithTimeout(TimeSpan.FromSeconds(10))
                    // shortcut: the printer's certificate is signed by Bambu's private CA and is accepted unchecked; pin that CA if LAN spoofing becomes a concern.
                    .WithTlsOptions(o => o.UseTls().WithCertificateValidationHandler(_ => true)).Build();
                await client.ConnectAsync(options, cancel);
                await client.SubscribeAsync(new MqttClientSubscribeOptionsBuilder().WithTopicFilter($"device/{c.Serial}/report").Build(), cancel);
                // The printer then sends only changes: ask once for the full state.
                await client.PublishStringAsync($"device/{c.Serial}/request", """{"pushing":{"sequence_id":"0","command":"pushall"}}""", cancellationToken: cancel);
                await lost.Task.WaitAsync(cancel);
                offline("Connexion perdue, nouvelle tentative…");
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested) { return; }
            catch (Exception exception) { offline(OfflineReason(exception)); }
            try { await Task.Delay(RetryDelay, cancel); } catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>Delivers each JPEG frame of an A1/P1 camera until cancelled, reconnecting after a loss.</summary>
    public static async Task WatchJpegCameraAsync(PrinterConnection c, Action<byte[]> frame, Action<string> offline, CancellationToken cancel)
    {
        while (!cancel.IsCancellationRequested)
        {
            try
            {
                using var tcp = new TcpClient();
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(10));
                    await tcp.ConnectAsync(c.Host, 6000, timeout.Token);
                }
                await using var tls = new SslStream(tcp.GetStream(), false, (_, _, _, _) => true);
                await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = c.Host }, cancel);
                await tls.WriteAsync(AuthPacket(c.AccessCode), cancel);
                var header = new byte[16];
                while (true)
                {
                    await tls.ReadExactlyAsync(header, cancel);
                    var size = BinaryPrimitives.ReadInt32LittleEndian(header);
                    if (size is <= 0 or > 8_000_000) throw new IOException("Flux caméra illisible.");
                    var jpeg = new byte[size];
                    await tls.ReadExactlyAsync(jpeg, cancel);
                    frame(jpeg);
                }
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested) { return; }
            catch (Exception exception) { offline(OfflineReason(exception)); }
            try { await Task.Delay(RetryDelay, cancel); } catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>80 bytes: 0x40, 0x3000, two zero words, then user and access code, each padded to 32 bytes.</summary>
    internal static byte[] AuthPacket(string accessCode)
    {
        var packet = new byte[80];
        BinaryPrimitives.WriteInt32LittleEndian(packet, 0x40);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(4), 0x3000);
        Encoding.ASCII.GetBytes(User).CopyTo(packet, 16);
        Encoding.ASCII.GetBytes(accessCode).AsSpan(0, Math.Min(32, accessCode.Length)).CopyTo(packet.AsSpan(48));
        return packet;
    }

    private static string OfflineReason(Exception exception) => exception switch
    {
        MQTTnet.Adapter.MqttConnectingFailedException => "Connexion refusée : vérifiez le code d’accès et le mode LAN.",
        SocketException or TimeoutException or OperationCanceledException => "Imprimante injoignable : vérifiez l’adresse et qu’elle est allumée.",
        _ => "Hors ligne : " + exception.Message
    };
}
