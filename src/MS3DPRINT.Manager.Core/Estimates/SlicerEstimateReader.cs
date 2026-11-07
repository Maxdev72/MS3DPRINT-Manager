using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace MS3DPRINT.Manager.Core.Estimates;

/// <summary>Filament weight and print time announced by the slicer, for the whole file (every plate).</summary>
public sealed record SlicerEstimate(decimal Grams, int Minutes, int Plates)
{
    /// <summary>Weight computed from the filament length (Cura), assuming 1.75 mm PLA.</summary>
    public bool FromLength { get; init; }
}

/// <summary>Reads the slicer's own estimate from a G-code (Bambu Studio, OrcaSlicer, PrusaSlicer) or a sliced 3MF (.gcode.3mf).</summary>
public static class SlicerEstimateReader
{
    private const int ScanBytes = 1024 * 1024;
    private static readonly Regex Weight = new(@"^;\s*(?:total filament weight \[g\]|total filament used \[g\]|filament used \[g\])\s*[:=]\s*([0-9.,\s]+)$", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant);
    private static readonly Regex Time = new(@"^;.*?(?:total estimated time|estimated printing time(?: \(normal mode\))?)\s*[:=]\s*([0-9dhms ]+)", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant);
    private static readonly Regex Part = new(@"(\d+)\s*([dhms])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex CuraTime = new(@"^;TIME:(\d+)\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant);
    private static readonly Regex CuraLength = new(@"^;Filament used:\s*([0-9.,m\s]+?)\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex PlateMeta = new(@"<metadata\s+key=""(prediction|weight)""\s+value=""([0-9.]+)""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Null when the file holds no slicer estimate (for example a 3MF project that was never sliced).</summary>
    public static SlicerEstimate? Read(string path)
    {
        try
        {
            if (path.EndsWith(".3mf", StringComparison.OrdinalIgnoreCase)) return ReadThreeMf(path);
            if (path.EndsWith(".gcode", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".gco", StringComparison.OrdinalIgnoreCase))
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                return ReadGcode(HeadAndTail(stream));
            }
            return null;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        { return null; }
    }

    /// <summary>Bambu and Orca write the summary at the top, PrusaSlicer at the end: read both ends only.</summary>
    private static string HeadAndTail(Stream stream)
    {
        var length = stream.Length;
        var head = new byte[(int)Math.Min(length, ScanBytes)];
        stream.ReadExactly(head);
        if (length <= 2L * ScanBytes) { var rest = new byte[length - head.Length]; stream.ReadExactly(rest); return Encoding.UTF8.GetString([.. head, .. rest]); }
        stream.Seek(-ScanBytes, SeekOrigin.End);
        var tail = new byte[ScanBytes];
        stream.ReadExactly(tail);
        return Encoding.UTF8.GetString(head) + "\n" + Encoding.UTF8.GetString(tail);
    }

    internal static SlicerEstimate? ReadGcode(string text)
    {
        var weight = Weight.Match(text);
        var time = Time.Match(text);
        if (!weight.Success || !time.Success) return ReadCura(text);
        // PrusaSlicer lists one value per extruder: "12.34, 0.00".
        var grams = weight.Groups[1].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Sum(value => decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var g) ? g : 0m);
        var seconds = Seconds(time.Groups[1].Value);
        return grams > 0 && seconds > 0 ? new(Math.Round(grams, 2), Minutes(seconds), 1) : null;
    }

    /// <summary>Cura gives seconds and filament length in metres, without weight.</summary>
    private static SlicerEstimate? ReadCura(string text)
    {
        var time = CuraTime.Match(text);
        var length = CuraLength.Match(text);
        if (!time.Success || !length.Success) return null;
        var metres = length.Groups[1].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Sum(value => decimal.TryParse(value.TrimEnd('m', 'M'), NumberStyles.Number, CultureInfo.InvariantCulture, out var m) ? m : 0m);
        var seconds = decimal.Parse(time.Groups[1].Value, CultureInfo.InvariantCulture);
        // shortcut: Cura does not write the weight; 1.75 mm PLA (1.24 g/cm³) is assumed, adjust by hand for other materials.
        var grams = metres * 1000m * 3.14159265m * 0.875m * 0.875m * 1.24m / 1000m;
        return grams > 0 && seconds > 0 ? new(Math.Round(grams, 2), Minutes(seconds), 1) { FromLength = true } : null;
    }

    private static SlicerEstimate? ReadThreeMf(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        if (archive.GetEntry("Metadata/slice_info.config") is { Length: < ScanBytes } info)
        {
            using var reader = new StreamReader(info.Open(), Encoding.UTF8);
            decimal grams = 0, seconds = 0; var plates = 0;
            foreach (var plate in reader.ReadToEnd().Split("<plate>", StringSplitOptions.RemoveEmptyEntries).Skip(1))
            {
                var values = PlateMeta.Matches(plate).ToDictionary(m => m.Groups[1].Value.ToLowerInvariant(), m => decimal.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture));
                if (!values.TryGetValue("prediction", out var plateSeconds) || plateSeconds <= 0) continue;
                seconds += plateSeconds; grams += values.GetValueOrDefault("weight"); plates++;
            }
            if (plates > 0 && grams > 0) return new(Math.Round(grams, 2), Minutes(seconds), plates);
        }
        // Without a summary, each sliced plate still carries its G-code header.
        decimal totalGrams = 0; var totalMinutes = 0; var count = 0;
        foreach (var entry in archive.Entries.Where(e => e.FullName.StartsWith("Metadata/plate_", StringComparison.OrdinalIgnoreCase) && e.FullName.EndsWith(".gcode", StringComparison.OrdinalIgnoreCase)))
        {
            using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
            var buffer = new char[ScanBytes / 4];
            var read = reader.ReadBlock(buffer, 0, buffer.Length);
            if (ReadGcode(new string(buffer, 0, read)) is not { } plate) continue;
            totalGrams += plate.Grams; totalMinutes += plate.Minutes; count++;
        }
        return count > 0 ? new(totalGrams, totalMinutes, count) : null;
    }

    private static decimal Seconds(string text) => Part.Matches(text).Sum(m => decimal.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) * (char.ToLowerInvariant(m.Groups[2].Value[0]) switch { 'd' => 86400, 'h' => 3600, 'm' => 60, _ => 1 }));

    /// <summary>Rounded to the minute, which is how the estimate form shows durations.</summary>
    private static int Minutes(decimal seconds) => (int)Math.Max(1, Math.Round(seconds / 60m, MidpointRounding.AwayFromZero));
}
