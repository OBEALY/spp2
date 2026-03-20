namespace MiniTestFramework;

public sealed class TestRunnerOptions
{
    public int MaxDegreeOfParallelism { get; init; } = Math.Max(1, Environment.ProcessorCount);
    public int? DefaultTimeoutMilliseconds { get; init; }

    public void Validate()
    {
        if (MaxDegreeOfParallelism <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxDegreeOfParallelism),
                "MaxDegreeOfParallelism must be greater than zero.");
        }

        if (DefaultTimeoutMilliseconds is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(DefaultTimeoutMilliseconds),
                "DefaultTimeoutMilliseconds must be greater than zero when specified.");
        }
    }
}
