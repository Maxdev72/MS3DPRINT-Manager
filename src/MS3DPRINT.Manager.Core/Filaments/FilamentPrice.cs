using System.Globalization;

namespace MS3DPRINT.Manager.Core.Filaments;

public static class FilamentPrice
{
    public static bool TryParse(string? text, out decimal price)
    {
        const NumberStyles styles = NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite |
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;
        return (decimal.TryParse(text, styles, CultureInfo.GetCultureInfo("fr-FR"), out price)
            || decimal.TryParse(text, styles, CultureInfo.InvariantCulture, out price)) && price >= 0;
    }
}
