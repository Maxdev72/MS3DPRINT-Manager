using MS3DPRINT.Manager.Desktop;

namespace MS3DPRINT.Manager.Core.Tests.Desktop;

public sealed class DisplayNamesTests
{
    [Theory]
    [InlineData("GUY_LALLEMAND", "Guy Lallemand")]
    [InlineData("JEAN_NOEL_ROISNE", "Jean Noël Roisne")]
    [InlineData("MPO_FRANCE", "MPO France")]
    [InlineData("MV_CREATIONS", "MV Créations")]
    [InlineData("FELL AGENCY", "Fell Agency")]
    [InlineData("PLAQUE_EPW175S", "Plaque EPW175S")]
    [InlineData("EPEE_FOURREAU_REPLIQUE_DE_ZORO", "Épée Fourreau Réplique de Zoro")]
    [InlineData("PIONS_D_ECHECS", "Pions d’Échecs")]
    [InlineData("Alexis Leroy", "Alexis Leroy")]
    [InlineData("Pot Crâne", "Pot Crâne")]
    [InlineData("SAMURAI_70CM", "Samouraï 70CM")]
    [InlineData("GACHETTE_DE_TRONCONNEUSE", "Gâchette de Tronçonneuse")]
    [InlineData("ROUE_CRANTEE", "Roue Crantée")]
    [InlineData("COTE_ET_OU", "Cote et Ou")]   // ambiguous words are left alone
    [InlineData("  ", "")]
    [InlineData(null, "")]
    public void Format_makes_folder_style_names_readable(string? raw, string expected)
        => Assert.Equal(expected, DisplayNames.Format(raw));
}
