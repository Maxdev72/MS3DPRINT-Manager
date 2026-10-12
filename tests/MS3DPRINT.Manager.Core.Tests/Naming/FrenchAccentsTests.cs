using MS3DPRINT.Manager.Core.Naming;

namespace MS3DPRINT.Manager.Core.Tests.Naming;

public sealed class FrenchAccentsTests
{
    [Theory]
    [InlineData("EPEE", "ÉPÉE")]
    [InlineData("Epee", "Épée")]
    [InlineData("epee", "épée")]
    [InlineData("PIONS D ECHECS", "PIONS D ÉCHECS")]
    [InlineData("Boitier de commande", "Boîtier de commande")]
    [InlineData("Réplique", "Réplique")]                 // already accented: untouched
    [InlineData("Support bobine", "Support bobine")]     // nothing to restore
    [InlineData("cote ou ete", "cote ou été")]           // only unambiguous words are restored
    [InlineData("", "")]
    public void Restore_puts_the_accents_back_and_keeps_the_case(string text, string expected)
        => Assert.Equal(expected, FrenchAccents.Restore(text));

    [Fact]
    public void TryRestore_knows_nothing_about_unlisted_words()
    {
        Assert.Equal("création", FrenchAccents.TryRestore("creation"));
        Assert.Null(FrenchAccents.TryRestore("zorro"));
        Assert.Null(FrenchAccents.TryRestore("cote"));
    }
}
