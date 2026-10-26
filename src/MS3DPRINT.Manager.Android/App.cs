using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace MS3DPRINT.Manager.Companion;

public sealed class App : Application
{
    public static MainView? Main { get; private set; }

    public override void Initialize()
    {
        var french = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = french; System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = french;
        System.Globalization.CultureInfo.CurrentCulture = french; System.Globalization.CultureInfo.CurrentUICulture = french;
        Styles.Add(new FluentTheme());
        // Fluent paints a focused field black; keep the app's raised surface with an accent outline instead.
        foreach (var state in new[] { ":focus", ":pointerover", ":focus:pointerover" })
        {
            var style = new Style(x => x.OfType<TextBox>().Class(state).Template().OfType<Border>().Name("PART_BorderElement"));
            style.Setters.Add(new Setter(Border.BackgroundProperty, Ui.Pal.Raised));
            style.Setters.Add(new Setter(Border.BorderBrushProperty, state.StartsWith(":focus") ? Ui.Pal.Accent : Ui.Pal.Border));
            Styles.Add(style);
        }
        RequestedThemeVariant = ThemeVariant.Dark;
        // Reading of invoices, receipts and screenshots: text recognition and PDF rendering are those of the phone, nothing is sent anywhere.
        try { MS3DPRINT.Manager.Core.Recognition.DocumentReader.Ocr = new Services.MlKitOcrEngine(); }
        catch (Java.Lang.Exception) { MS3DPRINT.Manager.Core.Recognition.DocumentReader.Ocr = null; }
        MS3DPRINT.Manager.Core.Recognition.DocumentReader.Rasterizer = new Services.AndroidPdfRasterizer();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is ISingleViewApplicationLifetime single) single.MainView = Main = new MainView();
        base.OnFrameworkInitializationCompleted();
    }
}
