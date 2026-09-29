using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using MS3DPRINT.Manager.Core.Storage;

namespace MS3DPRINT.Manager.App;

public static class ThemeManager
{
    public static void Apply(ThemePreference preference)
    {
        var dark = preference == ThemePreference.Dark || (preference == ThemePreference.Automatic && WindowsUsesDarkTheme());
        SetBrush("InkBrush", dark ? "#DCEBFA" : "#183153");
        SetBrush("AccentBrush", dark ? "#58A6FF" : "#1769AA");
        SetBrush("BackgroundBrush", dark ? "#17212B" : "#F5F7FA");
        SetBrush("SurfaceBrush", dark ? "#22303D" : "#FFFFFF");
        SetBrush("CardHoverBrush", dark ? "#2C4052" : "#E8F3FC");
        SetBrush("TextBrush", dark ? "#EAF2FA" : "#183153");
        SetBrush("MutedBrush", dark ? "#B2C5D7" : "#52657A");
        SetBrush("InputBrush", dark ? "#111A22" : "#FFFFFF");
        SetBrush("ReadOnlyInputBrush", dark ? "#1C2935" : "#E9EEF3");
        SetBrush("BorderBrush", dark ? "#405263" : "#D8E2EC");
        SetBrush("HeaderBrush", dark ? "#101923" : "#183153");
        SetBrush("HeaderTextBrush", "#FFFFFF");
        SetBrush("HeaderMutedBrush", dark ? "#B2C5D7" : "#D7E5F0");
    }

    private static bool WindowsUsesDarkTheme()
        => Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) is int value && value == 0;

    private static void SetBrush(string key, string color)
    {
        Application.Current.Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)!);
    }
}
