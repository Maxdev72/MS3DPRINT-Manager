using MS3DPRINT.Manager.Desktop;

namespace MS3DPRINT.Manager.Core.Tests.Desktop;

public sealed class ErrorLogTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ms3d-log-" + Guid.NewGuid().ToString("N"));

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true); }

    [Fact]
    public void Write_appends_each_error_with_a_timestamp()
    {
        var path = Path.Combine(_directory, "erreurs.log");

        ErrorLog.Write(new InvalidOperationException("premier"), path);
        ErrorLog.Write(new IOException("second"), path);

        var text = File.ReadAllText(path);
        Assert.Contains("premier", text);
        Assert.Contains("second", text);
        Assert.Equal(2, text.Split('[', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public void Write_never_throws_when_the_location_is_unusable()
    {
        var blocked = Path.Combine(_directory, "file.txt");
        Directory.CreateDirectory(_directory);
        File.WriteAllText(blocked, "x");

        var failure = Record.Exception(() => ErrorLog.Write(new Exception("x"), Path.Combine(blocked, "erreurs.log")));

        Assert.Null(failure);
    }

    [Fact]
    public void Write_sets_an_oversized_log_aside_and_starts_a_new_one()
    {
        var path = Path.Combine(_directory, "erreurs.log");
        Directory.CreateDirectory(_directory);
        File.WriteAllText(path, new string('x', (int)ErrorLog.MaxBytes + 10));

        ErrorLog.Write(new InvalidOperationException("récent"), path);

        Assert.True(File.Exists(Path.Combine(_directory, "erreurs.old.log")));
        var fresh = File.ReadAllText(path);
        Assert.Contains("récent", fresh);
        Assert.True(fresh.Length < 2000);
    }
}
