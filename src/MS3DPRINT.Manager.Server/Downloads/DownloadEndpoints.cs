using System.Security.Cryptography;
using System.Text.Json;

namespace MS3DPRINT.Manager.Server.Downloads;

public sealed record DownloadResponse(string Kind, string FileName, string Label, long Size, string Sha256, DateTimeOffset PublishedAt);

/// <summary>Manager installers, published from the native app and downloadable only with a session.</summary>
public static class DownloadEndpoints
{
    public const long MaximumBytes = 400L * 1024 * 1024;
    private static readonly Dictionary<string, (string File, string Label, byte[] Magic)> Kinds = new(StringComparer.Ordinal)
    {
        ["windows"] = ("MS3DPRINT Manager.exe", "Windows 10 et 11 (64 bits)", "MZ"u8.ToArray()),
        ["linux"] = ("MS3DPRINT-Manager-linux-x64.tar.gz", "Ubuntu et Linux Mint (64 bits)", [0x1F, 0x8B])
    };
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static void MapDownloads(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/downloads").RequireAuthorization();
        group.MapGet("", (ServerStorage storage) => Results.Ok(Kinds.Keys.Select(kind => Read(storage, kind)).OfType<DownloadResponse>().ToArray()));
        group.MapGet("/{kind}", (string kind, ServerStorage storage) =>
            Kinds.TryGetValue(kind, out var entry) && Read(storage, kind) is not null
                ? Results.File(Path.Combine(Root(storage), entry.File), "application/octet-stream", entry.File, enableRangeProcessing: true)
                : Results.NotFound());
        group.MapPut("/{kind}", Publish).WithMetadata(new Migration.MigrationBodyLimit(MaximumBytes));
    }

    private static async Task<IResult> Publish(string kind, HttpContext context, ServerStorage storage, TimeProvider clock)
    {
        // Publishing is reserved to the native app: no browser cookie can replace an installer.
        if (context.User.FindFirst("transport")?.Value != "bearer") return Results.StatusCode(StatusCodes.Status403Forbidden);
        if (!Kinds.TryGetValue(kind, out var entry)) return Results.NotFound();
        var root = Root(storage);
        Directory.CreateDirectory(root);
        var temporary = Path.Combine(root, "." + Guid.NewGuid().ToString("N") + ".upload");
        await Gate.WaitAsync(context.RequestAborted);
        try
        {
            long size = 0;
            var header = new byte[entry.Magic.Length];
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await context.Request.Body.ReadAsync(buffer, context.RequestAborted)) > 0)
                {
                    if (size < header.Length) Array.Copy(buffer, 0, header, (int)size, (int)Math.Min(read, header.Length - size));
                    size += read;
                    if (size > MaximumBytes) return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
                    hash.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), context.RequestAborted);
                }
                await output.FlushAsync(context.RequestAborted);
            }
            if (size < 1024 || !header.AsSpan().SequenceEqual(entry.Magic))
                return Results.BadRequest(new { error = kind == "windows" ? "Ce fichier n’est pas un exécutable Windows." : "Ce fichier n’est pas une archive .tar.gz." });
            var response = new DownloadResponse(kind, entry.File, entry.Label, size, Convert.ToHexString(hash.GetHashAndReset()), clock.GetUtcNow());
            File.Move(temporary, Path.Combine(root, entry.File), overwrite: true);
            await File.WriteAllTextAsync(Path.Combine(root, kind + ".json"), JsonSerializer.Serialize(response), CancellationToken.None);
            return Results.Ok(response);
        }
        finally
        {
            File.Delete(temporary);
            Gate.Release();
        }
    }

    private static string Root(ServerStorage storage) => Path.Combine(storage.StateRoot, "downloads");

    private static DownloadResponse? Read(ServerStorage storage, string kind)
    {
        var root = Root(storage);
        var meta = Path.Combine(root, kind + ".json");
        if (!File.Exists(meta) || !File.Exists(Path.Combine(root, Kinds[kind].File))) return null;
        try { return JsonSerializer.Deserialize<DownloadResponse>(File.ReadAllText(meta)); }
        catch (Exception e) when (e is JsonException or IOException) { return null; }
    }
}
