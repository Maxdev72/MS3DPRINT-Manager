namespace MS3DPRINT.Estimator;

/// <summary>How much of the processor the heavy work (estimates, orientation search, software rendering, thumbnails) may take.</summary>
public enum PerformanceMode
{
    /// <summary>Half of the processor threads: the computer stays comfortable for other work.</summary>
    Auto,
    /// <summary>A quarter of the threads and one thumbnail at a time: for a laptop on battery or a busy computer.</summary>
    Economy,
    /// <summary>Every thread: the fastest results.</summary>
    Maximum
}

/// <summary>The thread budget shared by every heavy computation of the software.</summary>
public static class Parallelism
{
    private static int _maxThreads = ThreadsFor(PerformanceMode.Auto, Environment.ProcessorCount);
    private static int _thumbnailSlots = ThumbnailSlotsFor(PerformanceMode.Auto);

    public static PerformanceMode Mode { get; private set; } = PerformanceMode.Auto;

    /// <summary>Threads a heavy computation may use at once.</summary>
    public static int MaxThreads => Volatile.Read(ref _maxThreads);

    /// <summary>Thumbnails drawn at the same time.</summary>
    public static int ThumbnailSlots => Volatile.Read(ref _thumbnailSlots);

    /// <summary>Raised after the mode changed, for the parts that size their own limits (the thumbnail queue).</summary>
    public static event Action? Changed;

    public static void Apply(PerformanceMode mode)
    {
        Mode = mode;
        Volatile.Write(ref _maxThreads, ThreadsFor(mode, Environment.ProcessorCount));
        Volatile.Write(ref _thumbnailSlots, ThumbnailSlotsFor(mode));
        Changed?.Invoke();
    }

    public static ParallelOptions Options(CancellationToken cancellationToken = default)
        => new() { MaxDegreeOfParallelism = MaxThreads, CancellationToken = cancellationToken };

    public static int ThreadsFor(PerformanceMode mode, int processors)
    {
        var cores = Math.Max(1, processors);
        return mode switch
        {
            PerformanceMode.Economy => Math.Max(1, cores / 4),
            PerformanceMode.Maximum => cores,
            _ => Math.Max(2, cores / 2)
        };
    }

    public static int ThumbnailSlotsFor(PerformanceMode mode) => mode switch
    {
        PerformanceMode.Economy => 1,
        PerformanceMode.Maximum => 4,
        _ => 2
    };
}
