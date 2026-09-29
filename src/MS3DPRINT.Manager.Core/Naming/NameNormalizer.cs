using System.Globalization;
using System.Text;

namespace MS3DPRINT.Manager.Core.Naming;

public static class NameNormalizer
{
    public static string Normalize(string input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var decomposed = input.Normalize(NormalizationForm.FormD);
        var result = new StringBuilder(decomposed.Length);
        var separatorPending = false;

        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            var upper = char.ToUpperInvariant(character);
            if (upper is >= 'A' and <= 'Z' or >= '0' and <= '9')
            {
                if (separatorPending && result.Length > 0)
                {
                    result.Append('_');
                }

                result.Append(upper);
                separatorPending = false;
            }
            else
            {
                separatorPending = true;
            }
        }

        return result.ToString();
    }
}
