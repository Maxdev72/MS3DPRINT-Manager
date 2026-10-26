namespace MS3DPRINT.Manager.Core.Recognition;

/// <summary>One recognised word with its position in the picture (pixels, origin top left).</summary>
public readonly record struct OcrWord(string Text, double X, double Y, double Width, double Height);

/// <summary>Puts recognised words back on their printed rows. OCR engines return blocks (a column of labels, then a column of amounts); an invoice is read row by row ("Total TTC … 14,40").</summary>
public static class OcrLayout
{
    /// <summary>Rows of text, top to bottom. <paramref name="angleDegrees"/> is the tilt measured by the engine, when it measures one: the rows are tried straight and straightened both ways, and the arrangement with the fewest rows (the best aligned) wins.</summary>
    public static string ToText(IReadOnlyList<OcrWord> words, double angleDegrees = 0)
    {
        if (words.Count == 0) return "";
        var candidates = new List<List<string>> { Rows(words, 0) };
        if (Math.Abs(angleDegrees) > 0.3) { candidates.Add(Rows(words, angleDegrees)); candidates.Add(Rows(words, -angleDegrees)); }
        return string.Join("\n", candidates.OrderBy(c => c.Count).First());
    }

    public static List<string> Rows(IReadOnlyList<OcrWord> words, double degrees)
    {
        var radians = degrees * Math.PI / 180;
        var cos = Math.Cos(radians); var sin = Math.Sin(radians);
        var placed = words.Select(w => (Word: w, Cx: (w.X + w.Width / 2) * cos + (w.Y + w.Height / 2) * sin, Cy: -(w.X + w.Width / 2) * sin + (w.Y + w.Height / 2) * cos)).ToList();
        var height = placed.Select(p => p.Word.Height).OrderBy(h => h).ElementAt(placed.Count / 2);
        var rows = new List<List<(OcrWord Word, double Cx, double Cy)>>();
        foreach (var item in placed.OrderBy(p => p.Cy))
        {
            var row = rows.Count == 0 ? null : rows[^1];
            if (row is not null && Math.Abs(item.Cy - row.Average(r => r.Cy)) <= height * 0.55) row.Add(item);
            else rows.Add([item]);
        }
        var lines = new List<string>();
        foreach (var row in rows)
        {
            var ordered = row.OrderBy(r => r.Cx).ToList();
            var builder = new System.Text.StringBuilder(ordered[0].Word.Text);
            for (var i = 1; i < ordered.Count; i++)
            {
                var previous = ordered[i - 1].Word; var current = ordered[i].Word;
                var gap = ordered[i].Cx - current.Width / 2 - (ordered[i - 1].Cx + previous.Width / 2);
                // A wide gap is a column break: two spaces, which the reader treats as a separator between a label and its amount.
                builder.Append(gap > Math.Max(previous.Height, current.Height) * 1.8 ? "  " : " ").Append(current.Text);
            }
            lines.Add(builder.ToString());
        }
        return lines;
    }
}
