namespace MiniTestFramework;

public enum TestStatus
{
    Passed,
    Failed,
    Error,
    TimedOut
}

public sealed class TestCaseResult
{
    public required string ClassName { get; init; }
    public required string MethodName { get; init; }
    public required string DisplayName { get; init; }
    public required TestStatus Status { get; init; }
    public string? Message { get; init; }
    public TimeSpan Duration { get; init; }
}

public sealed class TestRunReport
{
    public required IReadOnlyList<TestCaseResult> Results { get; init; }
    public TimeSpan Duration { get; init; }

    public int Total => Results.Count;
    public int Passed => Results.Count(r => r.Status == TestStatus.Passed);
    public int Failed => Results.Count(r => r.Status == TestStatus.Failed);
    public int Errored => Results.Count(r => r.Status == TestStatus.Error);
    public int TimedOut => Results.Count(r => r.Status == TestStatus.TimedOut);

    public string ToConsoleText()
    {
        var lines = new List<string>
        {
            "=== TEST RUN REPORT ===",
            $"Total: {Total}, Passed: {Passed}, Failed: {Failed}, Errors: {Errored}, TimedOut: {TimedOut}",
            $"Run duration: {Duration.TotalMilliseconds:F1} ms"
        };

        foreach (var result in Results)
        {
            lines.Add(
                $"Test: {result.DisplayName} | Status: {result.Status} | Time: {result.Duration.TotalMilliseconds:F1} ms");

            if (!string.IsNullOrWhiteSpace(result.Message))
            {
                lines.Add($"      Message: {result.Message}");
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    public string ToFileText() => ToConsoleText();
}
