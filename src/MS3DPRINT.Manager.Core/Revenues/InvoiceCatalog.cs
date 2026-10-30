using System.Text.RegularExpressions;

namespace MS3DPRINT.Manager.Core.Revenues;

/// <summary>An invoice PDF found in the workspace, with what could be read from it.</summary>
public sealed record InvoiceCandidate(string FilePath, string RelativePath, IndyInvoiceData Data, string? FolderClient);

public sealed record InvoiceScan(IReadOnlyList<InvoiceCandidate> Candidates, IReadOnlyList<string> Unreadable, bool Truncated);

/// <summary>Finds the invoices already filed in the workspace and matches them with the revenue book.</summary>
public static class InvoiceCatalog
{
    private const int MaxPdfFiles = 3000;
    private static readonly Regex InvoiceName = new(@"^(facture|invoice)[-_ ]", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly HashSet<string> ClientFolders = new(StringComparer.OrdinalIgnoreCase) { "CLIENTS", "01_CLIENTS" };

    /// <summary>
    /// Reads every PDF named like an invoice. The same invoice number filed twice keeps the copy with the shortest path.
    /// Files that cannot be read completely (number and total) are listed apart and never proposed.
    /// </summary>
    public static InvoiceScan Scan(string workspaceRoot, IProgress<int>? progress = null, CancellationToken cancellation = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspaceRoot));
        var files = new List<string>();
        var truncated = false;
        var pending = new Stack<string>(); pending.Push(root);
        while (pending.Count > 0 && !truncated)
        {
            cancellation.ThrowIfCancellationRequested();
            var directory = pending.Pop();
            string[] entries;
            try { entries = Directory.GetFileSystemEntries(directory); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { continue; }
            foreach (var entry in entries)
            {
                var name = Path.GetFileName(entry);
                FileAttributes attributes;
                try { attributes = File.GetAttributes(entry); } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { continue; }
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    if ((attributes & FileAttributes.ReparsePoint) != 0 || name.StartsWith('.') || name.Equals("06_FOURNISSEURS", StringComparison.OrdinalIgnoreCase)) continue;
                    pending.Push(entry);
                }
                else if (name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) && InvoiceName.IsMatch(name))
                {
                    if (files.Count >= MaxPdfFiles) { truncated = true; break; }
                    files.Add(entry);
                }
            }
        }

        var byNumber = new Dictionary<string, InvoiceCandidate>(StringComparer.OrdinalIgnoreCase);
        var unreadable = new List<string>();
        var done = 0;
        foreach (var file in files.OrderBy(f => f.Length).ThenBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            cancellation.ThrowIfCancellationRequested();
            progress?.Report(++done);
            var data = IndyInvoiceReader.Read(file);
            var relative = Path.GetRelativePath(root, file);
            if (data?.InvoiceNumber is null || data.TotalTtc is null) { unreadable.Add(relative); continue; }
            if (byNumber.ContainsKey(data.InvoiceNumber)) continue;
            byNumber[data.InvoiceNumber] = new InvoiceCandidate(file, relative, data, ClientFolderOf(relative));
        }
        return new(byNumber.Values.OrderBy(c => c.Data.IssuedOn ?? DateOnly.MinValue).ThenBy(c => c.Data.InvoiceNumber, StringComparer.OrdinalIgnoreCase).ToArray(), unreadable, truncated);
    }

    /// <summary>Invoices whose number is not in the book (cancelled lines do not count as recorded).</summary>
    public static IReadOnlyList<InvoiceCandidate> Unrecorded(IEnumerable<InvoiceCandidate> candidates, IEnumerable<RevenueLine> lines)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(lines);
        var recorded = new HashSet<string>(lines.Where(l => !l.IsCancelled).Select(l => l.Entry.InvoiceNumber.Trim()), StringComparer.OrdinalIgnoreCase);
        return candidates.Where(c => !recorded.Contains(c.Data.InvoiceNumber!.Trim())).ToArray();
    }

    private static string? ClientFolderOf(string relativePath)
    {
        var parts = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        for (var i = 0; i < parts.Length - 2; i++)
            if (ClientFolders.Contains(parts[i])) return parts[i + 1];
        return null;
    }
}
