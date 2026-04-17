namespace DynamicThreadPoolModule;

internal sealed class DynamicThreadPoolSnapshot
{
    public required int WorkerCount { get; init; }
    public required int BusyWorkers { get; init; }
    public required int IdleWorkers { get; init; }
    public required int SuspectedHungWorkers { get; init; }
    public required int QueueLength { get; init; }
    public required TimeSpan OldestQueueWait { get; init; }

    public bool HasMeaningfulDifference(DynamicThreadPoolSnapshot? other)
    {
        if (other is null)
        {
            return true;
        }

        return WorkerCount != other.WorkerCount
               || BusyWorkers != other.BusyWorkers
               || IdleWorkers != other.IdleWorkers
               || SuspectedHungWorkers != other.SuspectedHungWorkers
               || QueueLength != other.QueueLength
               || Math.Abs((OldestQueueWait - other.OldestQueueWait).TotalMilliseconds) >= 200;
    }

    public string ToLogLine()
    {
        return string.Join(
            " | ",
            $"[POOL] workers={WorkerCount}",
            $"busy={BusyWorkers}",
            $"idle={IdleWorkers}",
            $"hung={SuspectedHungWorkers}",
            $"queue={QueueLength}",
            $"oldest-wait={OldestQueueWait.TotalMilliseconds:F0} ms");
    }
}
