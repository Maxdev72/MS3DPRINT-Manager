using MS3DPRINT.Manager.Core.Naming;

namespace MS3DPRINT.Manager.Core.Tests.Naming;

public class NameNormalizerTests
{
    [Theory]
    [InlineData(" Société Dupont & Fils ", "SOCIETE_DUPONT_FILS")]
    [InlineData("déjà---vu", "DEJA_VU")]
    [InlineData("___", "")]
    [InlineData("a  b__c9", "A_B_C9")]
    [InlineData("Æther 42", "THER_42")]
    public void Normalize_ReturnsSafeDirectorySegment(string input, string expected)
        => Assert.Equal(expected, NameNormalizer.Normalize(input));

    [Theory]
    [InlineData("MAIRIE_PARIS_OUEST", "MPO")]
    [InlineData("DUPONT", "DUPON")]
    [InlineData("déjà vu", "DV")]
    [InlineData("___", "")]
    public void Suggest_ReturnsInitialsOrFiveCharacters(string input, string expected)
        => Assert.Equal(expected, ClientCodeSuggester.Suggest(input));
}
