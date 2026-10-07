namespace MS3DPRINT.Manager.Core.Storage;

public enum AccentPreference
{
    Blue,
    Green,
    Violet,
    Red,
    Orange,
    Yellow
}

public sealed record ThemeAppearance(
    ThemePreference Theme = ThemePreference.Automatic,
    AccentPreference Accent = AccentPreference.Blue);
