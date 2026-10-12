using Avalonia.Controls;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Desktop;

/// <summary>
/// Asks for a new client code and shows, as it is typed, exactly which project folders, fiches and estimates will follow.
/// Nothing is changed here: the caller applies the plan only when the dialog is confirmed.
/// </summary>
public sealed class ChangeClientCodeWindow : DialogWindow
{
    private const int MaxListedProjects = 8;
    private readonly TextBox _code;
    private readonly TextBlock _preview = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly TextBlock _error = FormKit.ErrorText();
    private readonly Button _confirm = new() { Content = "Changer le code", IsEnabled = false };
    private readonly Func<string, ClientCodeChangePlan> _plan;
    private bool _adjusting;

    /// <summary>The plan for the code that was confirmed.</summary>
    public ClientCodeChangePlan? Plan { get; private set; }

    public ChangeClientCodeWindow(string clientName, string oldCode, string suggestion, Func<string, ClientCodeChangePlan> plan)
    {
        _plan = plan;
        Title = "Changer le code client";
        Width = 560;
        Height = 560;
        MinWidth = 440;
        _code = new TextBox { Text = suggestion, MaxLength = 6, PlaceholderText = "ex. FA" };
        _code.TextChanged += (_, _) => Refresh();
        _confirm.Click += (_, _) => { if (Refresh()) Close(true); };
        Layout("Changer le code de " + clientName, $"Code actuel : {oldCode}. Le code commence les références de ses projets : le changer renomme leurs dossiers.",
            [FormKit.Field("Nouveau code", _code, hint: "De 1 à 6 lettres ou chiffres, sans espace ni accent."), _error, _preview], _confirm);
        Opened += (_, _) => { _code.Focus(); _code.SelectAll(); Refresh(); };
    }

    /// <summary>Checks the typed code, shows what it would rename and enables the confirmation; returns true when it can be applied.</summary>
    private bool Refresh()
    {
        if (_adjusting) return false;
        var typed = _code.Text ?? string.Empty;
        if (typed != typed.ToUpperInvariant())
        {
            _adjusting = true;
            _code.Text = typed.ToUpperInvariant();
            _code.CaretIndex = _code.Text.Length;
            _adjusting = false;
        }
        try
        {
            var plan = _plan((_code.Text ?? string.Empty).Trim());
            Plan = plan;
            _error.IsVisible = false;
            _confirm.IsEnabled = true;
            _preview.Text = Describe(plan);
            return true;
        }
        catch (ArgumentException exception)
        {
            Plan = null;
            _confirm.IsEnabled = false;
            _preview.Text = string.Empty;
            _error.Text = string.IsNullOrWhiteSpace(_code.Text) ? string.Empty : exception.Message;
            _error.IsVisible = _error.Text.Length > 0;
            return false;
        }
    }

    public static string Describe(ClientCodeChangePlan plan)
    {
        var lines = new List<string>();
        if (plan.Projects.Count == 0) lines.Add("Aucun dossier de projet à renommer : seul le code du client change.");
        else
        {
            lines.Add(plan.Projects.Count == 1 ? "1 dossier de projet sera renommé :" : $"{plan.Projects.Count} dossiers de projet seront renommés :");
            foreach (var rename in plan.Projects.Take(MaxListedProjects)) lines.Add($"   {rename.OldFolderName}  →  {rename.NewFolderName}");
            if (plan.Projects.Count > MaxListedProjects) lines.Add($"   … et {plan.Projects.Count - MaxListedProjects} autres");
            var estimates = plan.Projects.Sum(rename => rename.EstimateCount);
            if (estimates > 0) lines.Add(estimates == 1 ? "1 estimation liée sera mise à jour." : $"{estimates} estimations liées seront mises à jour.");
        }
        if (plan.Skipped.Count > 0) lines.Add($"{plan.Skipped.Count} dossier(s) dont la référence ne commence pas par {plan.OldCode} restent tels quels.");
        lines.Add("Les fichiers à l’intérieur des projets ne sont pas renommés.");
        return string.Join(Environment.NewLine, lines);
    }
}
