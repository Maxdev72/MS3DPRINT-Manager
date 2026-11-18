using System.ComponentModel;
using System.Diagnostics;
using MS3DPRINT.Manager.Core.Files;

namespace MS3DPRINT.Manager.Desktop;

/// <summary>Opens a new Thunderbird message with files already attached (drag and drop to Thunderbird is refused by its drop target).</summary>
public static class MailComposer
{
    /// <summary>Returns null when the message opened, otherwise the reason to show the user.</summary>
    public static string? TryCompose(IReadOnlyList<string> paths)
    {
        var usable = paths.Where(p => File.Exists(p) && !ExecutableFileGuard.IsExecutable(p)).ToList();
        if (usable.Count == 0) return "Aucun fichier joignable : les programmes et scripts ne sont pas envoyés par e-mail.";
        if (FindThunderbird() is not { } exe) return "Thunderbird est introuvable sur cet ordinateur.";
        // Thunderbird splits the list on commas and the whole value is single-quoted.
        var urls = usable.Select(p => new Uri(p).AbsoluteUri.Replace(",", "%2C").Replace("'", "%27"));
        try
        {
            var start = new ProcessStartInfo(exe) { UseShellExecute = false };
            start.ArgumentList.Add("-compose");
            start.ArgumentList.Add("attachment='" + string.Join(",", urls) + "'");
            Process.Start(start);
            return null;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or IOException)
        { return "Impossible de lancer Thunderbird : " + exception.Message; }
    }

    private static string? FindThunderbird()
    {
        var candidates = new List<string>();
        if (OperatingSystem.IsWindows())
        {
            foreach (var folder in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.LocalApplicationData })
                candidates.Add(Path.Combine(Environment.GetFolderPath(folder), "Mozilla Thunderbird", "thunderbird.exe"));
        }
        var names = OperatingSystem.IsWindows() ? new[] { "thunderbird.exe" } : ["thunderbird"];
        var path = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        candidates.AddRange(path.SelectMany(dir => names.Select(name => Path.Combine(dir, name))));
        return candidates.FirstOrDefault(File.Exists);
    }
}
