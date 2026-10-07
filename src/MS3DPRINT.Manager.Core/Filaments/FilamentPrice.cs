using System.Globalization;

namespace MS3DPRINT.Manager.Core.Filaments;

public static class FilamentPrice
{
    public static bool TryParse(string? text, out decimal price) =>
        decimal.TryParse(text, NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite |
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.GetCultureInfo("fr-FR"), out price) && price >= 0;
}
