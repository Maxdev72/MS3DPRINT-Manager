using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using MaterialDesignThemes.Wpf;
using MaterialDesignColors;
using MS3DPRINT.Manager.Core.Storage;

namespace MS3DPRINT.Manager.App;

public static class ThemeManager
{
    public static void Apply(ThemePreference preference)
        => Apply(new ThemeAppearance(preference));

    public static void Apply(ThemeAppearance appearance)
        => Apply(appearance, Application.Current.Resources);

    public static void Apply(ThemePreference preference, ResourceDictionary resources)
        => Apply(new ThemeAppearance(preference), resources);

    public static void Apply(ThemeAppearance appearance, ResourceDictionary resources)
    {
        ArgumentNullException.ThrowIfNull(appearance);
        ArgumentNullException.ThrowIfNull(resources);
        void SetBrush(string key, string color) => resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)!);
        var preference = appearance.Theme == ThemePreference.Automatic
            ? (WindowsUsesDarkTheme() ? ThemePreference.Dark : ThemePreference.Light)
            : appearance.Theme;
        var amoled = preference == ThemePreference.Amoled;
        var paper = preference == ThemePreference.Paper;
        var dark = preference == ThemePreference.Dark || amoled;
        var accent = appearance.Accent switch
        {
            AccentPreference.Green => dark ? "#71D9A0" : "#237B4B",
            AccentPreference.Violet => dark ? "#C5ADFF" : "#6F42B5",
            AccentPreference.Red => dark ? "#FF9D9D" : "#B42332",
            AccentPreference.Orange => dark ? "#FFAE66" : "#EF8A32",
            AccentPreference.Yellow => dark ? "#F9DE67" : "#F6CD35",
            _ => dark ? "#79B7FF" : "#1769AA"
        };
        SetBrush("AccentBrush", accent);
        SetBrush("AccentForegroundBrush", !dark && appearance.Accent == AccentPreference.Green ? "#1F7044"
            : !dark && appearance.Accent == AccentPreference.Yellow ? "#806000"
            : !dark && appearance.Accent == AccentPreference.Orange ? "#9A4600" : accent);
        var accentColor = ((SolidColorBrush)resources["AccentBrush"]).Color;
        var accentText = ReadableText(accentColor);
        resources["AccentTextBrush"] = new SolidColorBrush(accentText);
        resources["CalendarSelectedTextBrush"] = new SolidColorBrush(accentText);
        SetBrush("BackgroundBrush", amoled ? "#000000" : dark ? "#2A2A2A" : paper ? "#FFF8E1" : "#FFFFFF");
        SetBrush("SurfaceBrush", amoled ? "#000000" : dark ? "#363636" : paper ? "#FFFDF3" : "#FFFFFF");
        SetBrush("CardHoverBrush", amoled ? "#171717" : dark ? "#454545" : paper ? "#F6ECCB" : "#F1F1F1");
        SetBrush("TextBrush", dark ? "#F2F2F2" : paper ? "#302A1F" : "#202020");
        SetBrush("InkBrush", dark ? "#F2F2F2" : paper ? "#302A1F" : "#202020");
        SetBrush("MutedBrush", dark ? "#C2C2C2" : paper ? "#6F6042" : "#595959");
        SetBrush("ErrorBrush", dark ? "#FF8A8A" : "#B42332");
        SetBrush("InputBrush", amoled ? "#000000" : dark ? "#242424" : paper ? "#FFFDF3" : "#FFFFFF");
        SetBrush("ReadOnlyInputBrush", amoled ? "#111111" : dark ? "#303030" : paper ? "#F6ECCB" : "#EEEEEE");
        SetBrush("BorderBrush", amoled ? "#454545" : dark ? "#686868" : paper ? "#D7C89D" : "#CCCCCC");
        SetBrush("HeaderBrush", amoled ? "#000000" : dark ? "#202020" : paper ? "#FFF3C4" : "#FFFFFF");
        SetBrush("HeaderTextBrush", dark ? "#F2F2F2" : paper ? "#302A1F" : "#202020");
        SetBrush("HeaderMutedBrush", dark ? "#C2C2C2" : paper ? "#6F6042" : "#595959");
        SetBrush("DisabledButtonBrush", dark ? "#626262" : "#586C80");
        SetBrush("DisabledButtonTextBrush", "#F4F8FC");
        SetBrush("NavigationHoverBrush", amoled ? "#171717" : dark ? "#363636" : paper ? "#F6ECCB" : "#EEEEEE");

        // Keep toolkit controls and existing views on the same persisted palette.
        var theme = Theme.Create(dark ? BaseTheme.Dark : BaseTheme.Light, accentColor, accentColor);
        theme.Background = ((SolidColorBrush)resources["BackgroundBrush"]).Color;
        theme.Foreground = ((SolidColorBrush)resources["TextBrush"]).Color;
        theme.Cards.Background = ((SolidColorBrush)resources["SurfaceBrush"]).Color;
        theme.PrimaryMid = new ColorPair(theme.PrimaryMid.Color, ((SolidColorBrush)resources["AccentTextBrush"]).Color);
        resources.SetTheme(theme);
    }

    private static Color ReadableText(Color background)
    {
        static double Linear(byte channel)
        {
            var value = channel / 255.0;
            return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4);
        }
        var luminance = .2126 * Linear(background.R) + .7152 * Linear(background.G) + .0722 * Linear(background.B);
        // Choose the higher-contrast label for each accent, including bright yellow.
        return (1.05 / (luminance + .05)) >= ((luminance + .05) / (Linear(17) + .05))
            ? Colors.White : Color.FromRgb(17, 17, 17);
    }

    private static bool WindowsUsesDarkTheme()
        => Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) is int value && value == 0;

}
