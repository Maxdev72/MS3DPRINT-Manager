using MS3DPRINT.Manager.Desktop;

namespace MS3DPRINT.Manager.Core.Tests.Desktop;

public sealed class DisplayNamesTests
{
    [Theory]
    [InlineData("GUY_LALLEMAND", "Guy Lallemand")]
    [InlineData("JEAN_NOEL_ROISNE", "Jean Noel Roisne")]
    [InlineData("MPO_FRANCE", "MPO France")]
    [InlineData("MV_CREATIONS", "MV Creations")]
    [InlineData("FELL AGENCY", "Fell Agency")]
    [InlineData("PLAQUE_EPW175S", "Plaque EPW175S")]
    [InlineData("EPEE_FOURREAU_REPLIQUE_DE_ZORO", "Epee Fourreau Replique de Zoro")]
    [InlineData("PIONS_D_ECHECS", "Pions d Echecs")]
    [InlineData("Alexis Leroy", "Alexis Leroy")]
    [InlineData("Pot Crâne", "Pot Crâne")]
    [InlineData("SAMURAI_70CM", "Samurai 70CM")]
    [InlineData("  ", "")]
    [InlineData(null, "")]
    public void Format_makes_folder_style_names_readable(string? raw, string expected)
        => Assert.Equal(expected, DisplayNames.Format(raw));
}
