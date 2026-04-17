using DynamicThreadPoolModule;

namespace MiniTestFramework;

public sealed class LoadSimulationReport
{
    public required TestRunReport TestReport { get; init; }
    public required DynamicThreadPoolStatistics PoolStatistics { get; init; }
    public required int SubmittedCount { get; init; }

    public string ToConsoleText(string title)
    {
        return string.Join(
            Environment.NewLine,
            [
                $"=== {title} ===",
                $"Submitted: {SubmittedCount}",
                $"Results: total={TestReport.Total}, passed={TestReport.Passed}, failed={TestReport.Failed}, errors={TestReport.Errored}, timeouts={TestReport.TimedOut}",
                $"Duration: {TestReport.Duration.TotalMilliseconds:F1} ms",
                $"Pool max workers: {PoolStatistics.MaxObservedWorkers}",
                $"Pool max busy workers: {PoolStatistics.MaxObservedBusyWorkers}",
                $"Pool max queue length: {PoolStatistics.MaxObservedQueueLength}",
                $"Worker starts/stops: {PoolStatistics.WorkerStarts}/{PoolStatistics.WorkerStops}",
                $"Replacement workers: {PoolStatistics.ReplacementWorkersCreated}",
                $"Suspected hung workers: {PoolStatistics.SuspectedHungWorkers}",
                $"Worker failures: {PoolStatistics.WorkerFailures}"
            ]);
    }
}
