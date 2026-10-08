using System.Security.Cryptography;
using System.Text;

namespace MS3DPRINT.Manager.Core.Workspace;

// Coordinates cooperating local processes; it is not a distributed Nextcloud lock.
public sealed class ProfileMutationLock : IDisposable
{
    private readonly Mutex _mutex;
    private ProfileMutationLock(Mutex mutex) => _mutex = mutex;

    public static ProfileMutationLock Acquire(string profilePath)
    {
        var path = Path.GetFullPath(profilePath);
        if (OperatingSystem.IsWindows()) path = path.ToUpperInvariant();
        var name = (OperatingSystem.IsWindows() ? @"Global\" : string.Empty)
            + "MS3DPRINT-PROFILE-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path)));
        var mutex = new Mutex(false, name);
        try
        {
            try { mutex.WaitOne(); }
            catch (AbandonedMutexException) { } // The caller rereads disk after taking ownership.
            return new ProfileMutationLock(mutex);
        }
        catch { mutex.Dispose(); throw; }
    }

    public static void CheckVersion(DateTimeOffset actual, DateTimeOffset? expected)
    {
        if (expected is { } version && actual != version) throw new ProfileConflictException();
    }

    public static DateTimeOffset NextVersion(DateTimeOffset previous, DateTimeOffset requested)
    {
        var now = DateTimeOffset.UtcNow;
        return requested > previous ? requested : now > previous ? now : previous.AddTicks(1);
    }

    public static IDisposable AcquireMany(IEnumerable<string> profilePaths)
    {
        var locks = new List<ProfileMutationLock>();
        try
        {
            var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            foreach (var path in profilePaths.Select(Path.GetFullPath).Distinct(comparer).OrderBy(path => path, comparer)) locks.Add(Acquire(path));
            return new LockSet(locks);
        }
        catch { foreach (var mutation in locks.AsEnumerable().Reverse()) mutation.Dispose(); throw; }
    }

    private sealed class LockSet(List<ProfileMutationLock> locks) : IDisposable
    {
        public void Dispose() { foreach (var mutation in locks.AsEnumerable().Reverse()) mutation.Dispose(); }
    }

    public void Dispose() { _mutex.ReleaseMutex(); _mutex.Dispose(); }
}

public sealed class ProfileConflictException : InvalidOperationException
{
    public ProfileConflictException() : base("Cette fiche a été modifiée depuis son ouverture. Votre brouillon est conservé. Rechargez la fiche avant de réappliquer vos modifications.") { }
}
