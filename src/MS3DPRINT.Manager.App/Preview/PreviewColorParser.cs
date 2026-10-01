using System.Globalization;
using System.Windows.Media;

namespace MS3DPRINT.Manager.App.Preview;

public static class PreviewColorParser
{
    public static bool TryParse(string? text, out Color color)
    {
        color = default;
        if (text is null || text.Length != 7 || text[0] != '#') return false;
        if (!byte.TryParse(text.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var red) ||
            !byte.TryParse(text.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var green) ||
            !byte.TryParse(text.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var blue))
            return false;
        color = Color.FromRgb(red, green, blue);
        return true;
    }
}
