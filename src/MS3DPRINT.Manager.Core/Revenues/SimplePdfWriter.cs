using System.Globalization;
using System.Text;

namespace MS3DPRINT.Manager.Core.Revenues;

/// <summary>One printed line of a text-only PDF: columns start at fixed x positions (points).</summary>
public sealed record PdfLine(IReadOnlyList<string> Cells, bool Bold = false, bool Struck = false);

/// <summary>Minimal dependency-free PDF writer: A4 landscape, built-in Helvetica, Windows-1252 text, fixed columns, automatic pagination.</summary>
public static class SimplePdfWriter
{
    private const double PageWidth = 842, PageHeight = 595, Margin = 36, Leading = 13, FontSize = 8.5;
    private static readonly Encoding Win1252 = CreateEncoding();

    private static Encoding CreateEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1252, EncoderFallback.ReplacementFallback, DecoderFallback.ReplacementFallback);
    }

    /// <param name="columnX">Left x offset of each column inside the margins.</param>
    /// <param name="columnChars">Maximum characters printed per column; longer text is cut with an ellipsis.</param>
    public static byte[] Write(string title, IReadOnlyList<string> subtitleLines, PdfLine header, IReadOnlyList<PdfLine> lines, IReadOnlyList<double> columnX, IReadOnlyList<int> columnChars)
    {
        var headerLines = 2 + subtitleLines.Count;
        var rowsPerPage = (int)((PageHeight - 2 * Margin - 24) / Leading) - headerLines - 3;
        var chunks = new List<List<PdfLine>>();
        for (var index = 0; index < Math.Max(lines.Count, 1); index += rowsPerPage)
            chunks.Add(lines.Skip(index).Take(rowsPerPage).ToList());

        var objects = new List<byte[]>();
        void Add(string text) => objects.Add(Win1252Bytes(text));
        // 1 catalog, 2 pages, 3 regular font, 4 bold font, then (content, page) pairs.
        Add("<< /Type /Catalog /Pages 2 0 R >>");
        var kids = string.Join(' ', Enumerable.Range(0, chunks.Count).Select(i => $"{6 + 2 * i} 0 R"));
        Add($"<< /Type /Pages /Kids [{kids}] /Count {chunks.Count} >>");
        Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>");
        for (var page = 0; page < chunks.Count; page++)
        {
            var content = BuildPage(title, subtitleLines, header, chunks[page], page + 1, chunks.Count, columnX, columnChars);
            var stream = Win1252Bytes(content);
            using var contentObject = new MemoryStream();
            contentObject.Write(Win1252Bytes($"<< /Length {stream.Length} >>\nstream\n"));
            contentObject.Write(stream);
            contentObject.Write(Win1252Bytes("\nendstream"));
            objects.Add(contentObject.ToArray());
            Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {PageWidth.ToString(CultureInfo.InvariantCulture)} {PageHeight.ToString(CultureInfo.InvariantCulture)}] " +
                $"/Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents {5 + 2 * page} 0 R >>");
        }
        return Assemble(objects);
    }

    private static byte[] Assemble(List<byte[]> objects)
    {
        using var output = new MemoryStream();
        var offsets = new List<long>();
        output.Write(Win1252Bytes("%PDF-1.4\n"));
        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(output.Position);
            output.Write(Win1252Bytes($"{i + 1} 0 obj\n"));
            output.Write(objects[i]);
            output.Write(Win1252Bytes("\nendobj\n"));
        }
        var xref = output.Position;
        output.Write(Win1252Bytes($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n"));
        foreach (var offset in offsets) output.Write(Win1252Bytes($"{offset:0000000000} 00000 n \n"));
        output.Write(Win1252Bytes($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n"));
        return output.ToArray();
    }

    private static string BuildPage(string title, IReadOnlyList<string> subtitleLines, PdfLine header, IReadOnlyList<PdfLine> rows, int page, int pages,
        IReadOnlyList<double> columnX, IReadOnlyList<int> columnChars)
    {
        var builder = new StringBuilder();
        var y = PageHeight - Margin;
        void Text(double x, double yy, string text, bool bold, double size = FontSize) =>
            builder.Append($"BT /{(bold ? "F2" : "F1")} {size.ToString(CultureInfo.InvariantCulture)} Tf {(Margin + x).ToString("0.##", CultureInfo.InvariantCulture)} {yy.ToString("0.##", CultureInfo.InvariantCulture)} Td ({Escape(text)}) Tj ET\n");
        Text(0, y, title, true, 13);
        y -= Leading + 4;
        foreach (var subtitle in subtitleLines) { Text(0, y, subtitle, false); y -= Leading; }
        y -= 4;
        void Row(PdfLine line)
        {
            for (var column = 0; column < line.Cells.Count && column < columnX.Count; column++)
                Text(columnX[column], y, Fit(line.Cells[column], columnChars[column]), line.Bold);
            if (line.Struck && line.Cells.Count > 0)
                builder.Append($"{(Margin).ToString("0.##", CultureInfo.InvariantCulture)} {(y + 3).ToString("0.##", CultureInfo.InvariantCulture)} m {(PageWidth - Margin).ToString("0.##", CultureInfo.InvariantCulture)} {(y + 3).ToString("0.##", CultureInfo.InvariantCulture)} l S\n");
            y -= Leading;
        }
        Row(header);
        builder.Append($"{Margin.ToString("0.##", CultureInfo.InvariantCulture)} {(y + Leading - 2).ToString("0.##", CultureInfo.InvariantCulture)} m {(PageWidth - Margin).ToString("0.##", CultureInfo.InvariantCulture)} {(y + Leading - 2).ToString("0.##", CultureInfo.InvariantCulture)} l S\n");
        foreach (var row in rows) Row(row);
        Text(PageWidth - 2 * Margin - 60, Margin - 10, $"Page {page} / {pages}", false);
        return builder.ToString();
    }

    private static string Fit(string text, int maxChars)
    {
        text = (text ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ');
        return text.Length <= maxChars ? text : text[..Math.Max(0, maxChars - 1)] + "…";
    }

    private static string Escape(string text) => text.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");

    private static byte[] Win1252Bytes(string text) => Win1252.GetBytes(text);
}
