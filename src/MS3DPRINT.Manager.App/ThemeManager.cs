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
        => Apply(preference, Application.Current.Resources);

    public static void Apply(ThemePreference preference, ResourceDictionary resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        void SetBrush(string key, string color) => resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)!);
        var dark = preference == ThemePreference.Dark || (preference == ThemePreference.Automatic && WindowsUsesDarkTheme());
        SetBrush("InkBrush", dark ? "#DCEBFA" : "#183153");
        SetBrush("AccentBrush", dark ? "#58A6FF" : "#1769AA");
        SetBrush("AccentTextBrush", dark ? "#111A22" : "#FFFFFF");
        SetBrush("CalendarSelectedTextBrush", dark ? "#111A22" : "#FFFFFF");
        SetBrush("BackgroundBrush", dark ? "#17212B" : "#F5F7FA");
        SetBrush("SurfaceBrush", dark ? "#22303D" : "#FFFFFF");
        SetBrush("CardHoverBrush", dark ? "#2C4052" : "#E8F3FC");
        SetBrush("TextBrush", dark ? "#EAF2FA" : "#183153");
        SetBrush("MutedBrush", dark ? "#B2C5D7" : "#52657A");
        SetBrush("ErrorBrush", dark ? "#FF8A8A" : "#B42332");
        SetBrush("InputBrush", dark ? "#111A22" : "#FFFFFF");
        SetBrush("ReadOnlyInputBrush", dark ? "#1C2935" : "#E9EEF3");
        SetBrush("BorderBrush", dark ? "#405263" : "#D8E2EC");
        SetBrush("HeaderBrush", dark ? "#101923" : "#183153");
        SetBrush("HeaderTextBrush", "#FFFFFF");
        SetBrush("HeaderMutedBrush", dark ? "#B2C5D7" : "#D7E5F0");
        SetBrush("DisabledButtonBrush", dark ? "#52677A" : "#586C80");
        SetBrush("DisabledButtonTextBrush", "#F4F8FC");
        SetBrush("NavigationHoverBrush", dark ? "#22303D" : "#29415C");

        // Keep toolkit controls and existing views on the same persisted palette.
        var theme = Theme.Create(dark ? BaseTheme.Dark : BaseTheme.Light,
            ((SolidColorBrush)resources["AccentBrush"]).Color,
            dark ? Color.FromRgb(144, 202, 249) : Color.FromRgb(3, 92, 150));
        theme.Background = ((SolidColorBrush)resources["BackgroundBrush"]).Color;
        theme.Foreground = ((SolidColorBrush)resources["TextBrush"]).Color;
        theme.Cards.Background = ((SolidColorBrush)resources["SurfaceBrush"]).Color;
        theme.PrimaryMid = new ColorPair(theme.PrimaryMid.Color, ((SolidColorBrush)resources["AccentTextBrush"]).Color);
        resources.SetTheme(theme);
    }

    private static bool WindowsUsesDarkTheme()
        => Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) is int value && value == 0;

}
