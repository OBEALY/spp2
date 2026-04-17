namespace DynamicThreadPoolModule;

public sealed class DynamicThreadPoolOptions
{
    public int MinWorkerCount { get; init; } = 2;
    public int MaxWorkerCount { get; init; } = Math.Max(4, Environment.ProcessorCount);
    public int ScaleUpStep { get; init; } = 1;
    public TimeSpan WorkerIdleTimeout { get; init; } = TimeSpan.FromSeconds(2);
    public TimeSpan QueueWaitThreshold { get; init; } = TimeSpan.FromMilliseconds(400);
    public TimeSpan MonitorInterval { get; init; } = TimeSpan.FromMilliseconds(250);
    public TimeSpan WorkerHangThreshold { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan ShutdownJoinTimeout { get; init; } = TimeSpan.FromSeconds(1);

    public void Validate()
    {
        if (MinWorkerCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MinWorkerCount),
                "MinWorkerCount must be greater than zero.");
        }

        if (MaxWorkerCount < MinWorkerCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxWorkerCount),
                "MaxWorkerCount must be greater than or equal to MinWorkerCount.");
        }

        if (ScaleUpStep <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ScaleUpStep),
                "ScaleUpStep must be greater than zero.");
        }

        if (WorkerIdleTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(WorkerIdleTimeout),
                "WorkerIdleTimeout must be greater than zero.");
        }

        if (QueueWaitThreshold <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(QueueWaitThreshold),
                "QueueWaitThreshold must be greater than zero.");
        }

        if (MonitorInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MonitorInterval),
                "MonitorInterval must be greater than zero.");
        }

        if (WorkerHangThreshold <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(WorkerHangThreshold),
                "WorkerHangThreshold must be greater than zero.");
        }

        if (ShutdownJoinTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ShutdownJoinTimeout),
                "ShutdownJoinTimeout must be greater than zero.");
        }
    }
}
