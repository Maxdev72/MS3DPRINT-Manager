using System.Text.Json.Nodes;
using MS3DPRINT.Manager.Core.ModelSearch;

namespace MS3DPRINT.Manager.Core.Tests.ModelSearch;

public sealed class DownloadBufferTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ms3d-buffer-" + Guid.NewGuid().ToString("N"));

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Announced_downloads_remain_protected_while_their_offer_is_pending(bool archive)
    {
        var folder = Path.Combine(_root, "tampon"); using var buffer = new DownloadBuffer(folder);
        var offered = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        buffer.FileReady += file => offered.TrySetResult(file.Path);
        buffer.BatchReady += batch => offered.TrySetResult(batch.Files.Single().Path);
        buffer.StartWatching();
        if (archive) WriteZip(Path.Combine(folder, "pack.zip"), ("piece.stl", "modèle"));
        else File.WriteAllText(Path.Combine(folder, "piece.stl"), "modèle");
        var path = await offered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        buffer.Maintain(TimeSpan.Zero, 0); // A previously scheduled cleanup may have no UI keep snapshot yet.
        Assert.True(File.Exists(path));
        buffer.ReleaseOffered([path]);
        // The watcher may still be returning from the announcement callback.
        for (var attempt = 0; attempt < 20 && File.Exists(path); attempt++) { buffer.Maintain(TimeSpan.Zero, 0); await Task.Delay(10); }
        Assert.False(File.Exists(path));
    }

    [Theory]
    [InlineData("root")]
    [InlineData("entry")]
    [InlineData("nested")]
    public void Cleanup_never_follows_or_removes_directory_links(string location)
    {
        var folder = Path.Combine(_root, "tampon"); Directory.CreateDirectory(folder);
        var outside = Path.Combine(_root, "outside"); var protectedFile = Path.Combine(outside, "document.stl"); Fill(protectedFile, 100, 10);
        var link = location == "nested" ? Path.Combine(folder, "pack", "lien") : Path.Combine(folder, "lien");
        if (location == "nested") Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        static string Quote(string value) => "'" + value.Replace("'", "''") + "'";
        var script = "$ErrorActionPreference='Stop'; New-Item -ItemType Junction -Path " + Quote(link) + " -Target " + Quote(outside) + " | Out-Null";
        var start = new System.Diagnostics.ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-EncodedCommand"); start.ArgumentList.Add(Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script)));
        using var process = System.Diagnostics.Process.Start(start)!; var error = process.StandardError.ReadToEnd(); process.WaitForExit(); Assert.True(process.ExitCode == 0, error);
        try
        {
            if (location == "nested") Directory.SetLastWriteTimeUtc(Path.GetDirectoryName(link)!, DateTime.UtcNow.AddDays(-10));
            new DownloadBuffer(location == "root" ? link : folder).Maintain(TimeSpan.FromDays(2), 0);
            Assert.True(Directory.Exists(link)); Assert.True(File.Exists(protectedFile));
        }
        finally { if (Directory.Exists(link)) Directory.Delete(link, false); }
    }

    [Fact]
    public void Cleanup_preserves_a_final_model_file_which_is_still_open_for_writing()
    {
        var folder = Path.Combine(_root, "tampon"); var active = Path.Combine(folder, "actif.stl"); Fill(active, 100, 10);
        using var writing = new FileStream(active, FileMode.Open, FileAccess.Write, FileShare.Read);
        new DownloadBuffer(folder).Maintain(TimeSpan.FromDays(2), 0);
        Assert.True(File.Exists(active));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Maintain_keeps_protected_and_active_files_during_both_cleanup_phases(bool nested)
    {
        var folder = Path.Combine(_root, "tampon");
        var container = nested ? Path.Combine(folder, "pack") : folder;
        var kept = Path.Combine(container, "offert.stl");
        var active = Path.Combine(container, "actif.stl.crdownload");
        Fill(kept, 500, 10); Fill(active, 500, 10);
        var ordinary = Path.Combine(folder, "ancien.stl"); Fill(ordinary, 500, 10);
        if (nested) Directory.SetLastWriteTimeUtc(container, DateTime.UtcNow.AddDays(-10));
        new DownloadBuffer(folder).Maintain(TimeSpan.FromDays(2), 0, [kept]);
        Assert.True(File.Exists(kept)); Assert.True(File.Exists(active));
        Assert.False(File.Exists(ordinary));
    }

    [Fact]
    public void A_nested_active_download_alone_protects_its_container()
    {
        var folder = Path.Combine(_root, "tampon");
        var active = Path.Combine(folder, "pack", "nested", "en-cours.tmp"); Fill(active, 500, 10);
        Directory.SetLastWriteTimeUtc(Path.Combine(folder, "pack"), DateTime.UtcNow.AddDays(-10));
        new DownloadBuffer(folder).Maintain(TimeSpan.FromDays(2), 0);
        Assert.True(File.Exists(active));
    }

    [Theory]
    [InlineData("piece.STL", true)]
    [InlineData("boite.3mf", true)]
    [InlineData("support.step", true)]
    [InlineData("support.stp", true)]
    [InlineData("scan.obj", true)]
    [InlineData("modele.amf", true)]
    [InlineData("archive.zip", false)]
    [InlineData("programme.exe", false)]
    [InlineData("piece.stl.crdownload", false)]
    [InlineData("sans-extension", false)]
    [InlineData("", false)]
    public void Only_slicer_formats_are_models(string name, bool expected) => Assert.Equal(expected, DownloadBuffer.IsModelFile(name));

    [Fact]
    public void PrepareProfile_points_the_browser_at_the_buffer_and_keeps_other_preferences()
    {
        var profile = Path.Combine(_root, "webview");
        var preferences = Path.Combine(profile, "EBWebView", "Default", "Preferences");
        Directory.CreateDirectory(Path.GetDirectoryName(preferences)!);
        File.WriteAllText(preferences, """{"intl":{"selected_languages":"fr"},"download":{"extensions_to_open":"pdf"}}""");
        var buffer = new DownloadBuffer(Path.Combine(_root, "tampon"));

        buffer.PrepareProfile(profile);

        var json = JsonNode.Parse(File.ReadAllText(preferences))!;
        Assert.Equal(buffer.Folder, (string?)json["download"]!["default_directory"]);
        Assert.False((bool?)json["download"]!["prompt_for_download"]);
        Assert.Equal("pdf", (string?)json["download"]!["extensions_to_open"]);
        Assert.Equal("fr", (string?)json["intl"]!["selected_languages"]);
        Assert.True(Directory.Exists(buffer.Folder));
    }

    [Fact]
    public void PrepareProfile_creates_the_preferences_of_a_profile_that_never_ran()
    {
        var buffer = new DownloadBuffer(Path.Combine(_root, "tampon"));

        buffer.PrepareProfile(Path.Combine(_root, "neuf"));

        Assert.True(File.Exists(Path.Combine(_root, "neuf", "EBWebView", "Default", "Preferences")));
    }

    [Fact]
    public void PrepareProfile_replaces_unreadable_preferences_instead_of_failing()
    {
        var profile = Path.Combine(_root, "webview");
        var preferences = Path.Combine(profile, "EBWebView", "Default", "Preferences");
        Directory.CreateDirectory(Path.GetDirectoryName(preferences)!);
        File.WriteAllText(preferences, "{ pas du json");

        new DownloadBuffer(Path.Combine(_root, "tampon")).PrepareProfile(profile);

        Assert.NotNull(JsonNode.Parse(File.ReadAllText(preferences)));
    }

    [Fact]
    public async Task A_model_is_announced_once_it_is_complete_and_not_while_it_downloads()
    {
        var folder = Path.Combine(_root, "tampon");
        using var buffer = new DownloadBuffer(folder);
        var ready = new TaskCompletionSource<BufferedFile>(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        buffer.FileReady += file => { Interlocked.Increment(ref count); ready.TrySetResult(file); };
        buffer.StartWatching();

        // like the browser: a temporary name while it writes, the real name once it is done
        var temporary = Path.Combine(folder, "spatule.3mf.crdownload");
        await File.WriteAllBytesAsync(temporary, new byte[2048]);
        await Task.Delay(700);
        Assert.Equal(0, count);
        File.Move(temporary, Path.Combine(folder, "spatule.3mf"));

        var announced = await ready.Task.WaitAsync(TimeSpan.FromSeconds(15));
        await Task.Delay(800);

        Assert.Equal("spatule.3mf", announced.Name);
        Assert.Equal(2048, announced.Length);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Other_files_are_not_announced()
    {
        var folder = Path.Combine(_root, "tampon");
        using var buffer = new DownloadBuffer(folder);
        var count = 0;
        buffer.FileReady += _ => Interlocked.Increment(ref count);
        buffer.StartWatching();

        await File.WriteAllTextAsync(Path.Combine(folder, "notes.txt"), "bonjour");
        await Task.Delay(1800);

        Assert.Equal(0, count);
    }

    private static void WriteZip(string path, params (string Name, string Content)[] entries)
    {
        using var stream = File.Create(path);
        using var zip = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
        {
            using var writer = new StreamWriter(zip.CreateEntry(name).Open());
            writer.Write(content);
        }
    }

    [Fact]
    public async Task A_downloaded_zip_is_opened_in_the_background_and_its_models_announced_together()
    {
        var folder = Path.Combine(_root, "tampon");
        using var buffer = new DownloadBuffer(folder);
        var ready = new TaskCompletionSource<BufferedBatch>(TaskCreationOptions.RunContinuationsAsynchronously);
        var singles = 0;
        buffer.FileReady += _ => Interlocked.Increment(ref singles);
        buffer.BatchReady += batch => ready.TrySetResult(batch);
        buffer.StartWatching();

        var temporary = Path.Combine(folder, "pack.zip.crdownload");
        WriteZip(temporary, ("pack/corps.stl", "a"), ("pack/couvercle.3mf", "bb"), ("pack/lisez-moi.txt", "x"), ("pack/photo.jpg", "x"));
        File.Move(temporary, Path.Combine(folder, "pack.zip"));

        var batch = await ready.Task.WaitAsync(TimeSpan.FromSeconds(15));

        Assert.Null(batch.Problem);
        Assert.Equal(["corps.stl", "couvercle.3mf"], batch.Files.Select(f => f.Name).Order().ToArray());
        Assert.All(batch.Files, file => Assert.True(File.Exists(file.Path)));
        Assert.False(File.Exists(Path.Combine(folder, "pack.zip")));
        await Task.Delay(800);
        Assert.Equal(0, singles);
    }

    [Fact]
    public void Extraction_keeps_only_models_flat_and_never_writes_outside_the_target()
    {
        Directory.CreateDirectory(_root);
        var zip = Path.Combine(_root, "piege.zip");
        WriteZip(zip, ("../../dehors.stl", "evil"), ("a/piece.stl", "1"), ("b/piece.stl", "2"), ("__MACOSX/._piece.stl", "x"), ("run.exe", "x"));
        var target = Path.Combine(_root, "cible");

        var result = DownloadArchive.ExtractModels(zip, target);

        Assert.Null(result.Problem);
        Assert.Equal(3, result.Files.Count);
        Assert.All(result.Files, file => Assert.Equal(target, Path.GetDirectoryName(file.Path)));
        Assert.Contains(result.Files, file => file.Name == "piece (2).stl");
        Assert.False(File.Exists(Path.Combine(_root, "dehors.stl")));
    }

    [Fact]
    public void An_archive_without_models_or_unreadable_gives_a_message_instead_of_files()
    {
        Directory.CreateDirectory(_root);
        var empty = Path.Combine(_root, "vide.zip");
        WriteZip(empty, ("notes.txt", "x"));
        var broken = Path.Combine(_root, "casse.zip");
        File.WriteAllBytes(broken, new byte[100]);

        Assert.Contains("aucun fichier 3D", DownloadArchive.ExtractModels(empty, Path.Combine(_root, "c1")).Problem);
        Assert.NotNull(DownloadArchive.ExtractModels(broken, Path.Combine(_root, "c2")).Problem);
    }

    [Fact]
    public void PurgeOlderThan_also_removes_old_folders_of_opened_archives()
    {
        var folder = Path.Combine(_root, "tampon");
        var old = Path.Combine(folder, "pack-abc123");
        Directory.CreateDirectory(old);
        File.WriteAllText(Path.Combine(old, "a.stl"), "x");
        Directory.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-10));

        new DownloadBuffer(folder).PurgeOlderThan(TimeSpan.FromDays(7));

        Assert.False(Directory.Exists(old));
    }

    private static void Fill(string path, int bytes, int daysOld)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[bytes]);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-daysOld));
    }

    [Fact]
    public void TrimToSize_removes_the_oldest_first_and_stops_under_the_limit()
    {
        var folder = Path.Combine(_root, "tampon");
        Fill(Path.Combine(folder, "vieux.stl"), 400, 3);
        Fill(Path.Combine(folder, "milieu.stl"), 400, 2);
        Fill(Path.Combine(folder, "recent.stl"), 400, 1);

        var removed = new DownloadBuffer(folder).TrimToSize(900);

        Assert.Equal(1, removed);
        Assert.False(File.Exists(Path.Combine(folder, "vieux.stl")));
        Assert.True(File.Exists(Path.Combine(folder, "milieu.stl")));
        Assert.True(File.Exists(Path.Combine(folder, "recent.stl")));
    }

    [Fact]
    public void TrimToSize_counts_opened_zip_folders_and_spares_downloads_in_progress_and_kept_files()
    {
        var folder = Path.Combine(_root, "tampon");
        Fill(Path.Combine(folder, "pack-abc123", "a.stl"), 600, 5);
        Fill(Path.Combine(folder, "pack-abc123", "b.stl"), 600, 5);
        Fill(Path.Combine(folder, "en-cours.stl.crdownload"), 500, 9);
        Fill(Path.Combine(folder, "offert.stl"), 500, 8);
        Fill(Path.Combine(folder, "autre.stl"), 500, 1);

        new DownloadBuffer(folder).TrimToSize(1000, keep: [Path.Combine(folder, "offert.stl")]);

        Assert.False(Directory.Exists(Path.Combine(folder, "pack-abc123")));
        Assert.True(File.Exists(Path.Combine(folder, "en-cours.stl.crdownload")));
        Assert.True(File.Exists(Path.Combine(folder, "offert.stl")));
    }

    [Fact]
    public void A_file_inside_a_kept_zip_folder_keeps_the_whole_folder()
    {
        var folder = Path.Combine(_root, "tampon");
        var inside = Path.Combine(folder, "pack-abc123", "a.stl");
        Fill(inside, 800, 5);
        Fill(Path.Combine(folder, "autre.stl"), 800, 1);

        new DownloadBuffer(folder).TrimToSize(100, keep: [inside]);

        Assert.True(File.Exists(inside));
    }

    [Fact]
    public void MoveModels_keeps_existing_files_and_leaves_what_is_not_a_model()
    {
        var folder = Path.Combine(_root, "tampon");
        var target = Path.Combine(_root, "projet");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "piece.stl"), "deja la");
        Fill(Path.Combine(folder, "piece.stl"), 10, 0);
        Fill(Path.Combine(folder, "notes.txt"), 10, 0);

        var moved = DownloadBuffer.MoveModels([Path.Combine(folder, "piece.stl"), Path.Combine(folder, "notes.txt")], target);

        Assert.Equal([Path.Combine(target, "piece (2).stl")], moved);
        Assert.Equal("deja la", File.ReadAllText(Path.Combine(target, "piece.stl")));
        Assert.True(File.Exists(Path.Combine(folder, "notes.txt")));
        Assert.False(File.Exists(Path.Combine(folder, "piece.stl")));
        Assert.Throws<DirectoryNotFoundException>(() => DownloadBuffer.MoveModels([], Path.Combine(_root, "absent")));
    }

    [Fact]
    public void PurgeOlderThan_removes_only_old_files()
    {
        var folder = Path.Combine(_root, "tampon");
        Directory.CreateDirectory(folder);
        var old = Path.Combine(folder, "ancien.stl");
        var recent = Path.Combine(folder, "recent.stl");
        File.WriteAllText(old, "x");
        File.WriteAllText(recent, "x");
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-10));

        var removed = new DownloadBuffer(folder).PurgeOlderThan(TimeSpan.FromDays(7));

        Assert.Equal(1, removed);
        Assert.False(File.Exists(old));
        Assert.True(File.Exists(recent));
    }
}
