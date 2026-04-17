namespace MiniTestFramework;

public sealed class LoadSimulationOptions
{
    public required IReadOnlyList<LoadSimulationPhase> Phases { get; init; }

    public int TotalSubmissions => Phases.Sum(p => p.SubmissionCount);

    public void Validate()
    {
        if (Phases.Count == 0)
        {
            throw new ArgumentException("At least one load phase is required.", nameof(Phases));
        }

        foreach (var phase in Phases)
        {
            phase.Validate();
        }
    }

    public static LoadSimulationOptions CreateDefault()
    {
        return new LoadSimulationOptions
        {
            Phases =
            [
                new LoadSimulationPhase
                {
                    Name = "Single submissions",
                    SubmissionCount = 4,
                    IntervalBetweenSubmissions = TimeSpan.FromMilliseconds(300),
                    PauseAfterPhase = TimeSpan.FromMilliseconds(1200)
                },
                new LoadSimulationPhase
                {
                    Name = "Peak burst",
                    SubmissionCount = 24,
                    IntervalBetweenSubmissions = TimeSpan.FromMilliseconds(10),
                    PauseAfterPhase = TimeSpan.FromMilliseconds(700)
                },
                new LoadSimulationPhase
                {
                    Name = "Sparse recovery",
                    SubmissionCount = 4,
                    IntervalBetweenSubmissions = TimeSpan.FromMilliseconds(400),
                    PauseAfterPhase = TimeSpan.FromMilliseconds(600)
                },
                new LoadSimulationPhase
                {
                    Name = "Final spike",
                    SubmissionCount = 28,
                    IntervalBetweenSubmissions = TimeSpan.FromMilliseconds(5),
                    PauseAfterPhase = TimeSpan.Zero
                }
            ]
        };
    }
}

public sealed class LoadSimulationPhase
{
    public required string Name { get; init; }
    public required int SubmissionCount { get; init; }
    public TimeSpan IntervalBetweenSubmissions { get; init; } = TimeSpan.Zero;
    public TimeSpan PauseAfterPhase { get; init; } = TimeSpan.Zero;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new ArgumentException("Phase name must not be empty.", nameof(Name));
        }

        if (SubmissionCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(SubmissionCount),
                "SubmissionCount must be greater than zero.");
        }

        if (IntervalBetweenSubmissions < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(IntervalBetweenSubmissions),
                "IntervalBetweenSubmissions must not be negative.");
        }

        if (PauseAfterPhase < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(PauseAfterPhase),
                "PauseAfterPhase must not be negative.");
        }
    }
}
