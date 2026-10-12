using MS3DPRINT.Manager.Core.Naming;

namespace MS3DPRINT.Manager.Core.Tests.Naming;

public sealed class ClientCodeSuggesterUniqueTests
{
    [Theory]
    [InlineData("Alexis Leroy", new string[0], "AL")]
    [InlineData("Alexis Leroy", new[] { "AL" }, "AL2")]
    [InlineData("Alexis Leroy", new[] { "al", "AL2" }, "AL3")]
    [InlineData("Fell agency", new[] { "AL" }, "FA")]
    [InlineData("", new string[0], "")]
    public void SuggestUnique_returns_the_first_free_code(string name, string[] used, string expected)
        => Assert.Equal(expected, ClientCodeSuggester.SuggestUnique(name, used));
}
