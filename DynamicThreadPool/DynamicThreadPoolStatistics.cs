namespace DynamicThreadPoolModule;

public sealed class DynamicThreadPoolStatistics
{
    public required int TotalQueued { get; init; }
    public required int TotalCompleted { get; init; }
    public required int WorkerStarts { get; init; }
    public required int WorkerStops { get; init; }
    public required int WorkerFailures { get; init; }
    public required int ReplacementWorkersCreated { get; init; }
    public required int SuspectedHungWorkers { get; init; }
    public required int MaxObservedWorkers { get; init; }
    public required int MaxObservedBusyWorkers { get; init; }
    public required int MaxObservedQueueLength { get; init; }
}
