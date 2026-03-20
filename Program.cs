using MiniTestFramework;

var runner = new TestRunner();
var assembly = typeof(Program).Assembly;
var outputDirectory = AppContext.BaseDirectory;
var maxParallelism = ResolveParallelism(args);

var sequentialOptions = new TestRunnerOptions
{
    MaxDegreeOfParallelism = 1,
    DefaultTimeoutMilliseconds = 2_000
};

var parallelOptions = new TestRunnerOptions
{
    MaxDegreeOfParallelism = maxParallelism,
    DefaultTimeoutMilliseconds = 2_000
};

var sequentialLivePath = Path.Combine(outputDirectory, "LiveResults.Sequential.log");
var parallelLivePath = Path.Combine(outputDirectory, "LiveResults.Parallel.log");

Console.WriteLine($"Sequential run started. MaxDegreeOfParallelism={sequentialOptions.MaxDegreeOfParallelism}");
TestRunReport sequentialReport;
await using (var output = new SynchronizedTestOutput(sequentialLivePath))
{
    sequentialReport = await runner.RunAsync(assembly, sequentialOptions, output);
}

Console.WriteLine();
Console.WriteLine($"Parallel run started. MaxDegreeOfParallelism={parallelOptions.MaxDegreeOfParallelism}");
TestRunReport parallelReport;
await using (var output = new SynchronizedTestOutput(parallelLivePath))
{
    parallelReport = await runner.RunAsync(assembly, parallelOptions, output);
}

var sequentialReportPath = Path.Combine(outputDirectory, "TestResults.Sequential.txt");
var parallelReportPath = Path.Combine(outputDirectory, "TestResults.Parallel.txt");
var comparisonReportPath = Path.Combine(outputDirectory, "TestResults.Comparison.txt");

await File.WriteAllTextAsync(sequentialReportPath, sequentialReport.ToFileText());
await File.WriteAllTextAsync(parallelReportPath, parallelReport.ToFileText());

var comparisonText = BuildComparisonText(maxParallelism, sequentialReport, parallelReport);
await File.WriteAllTextAsync(comparisonReportPath, comparisonText);

Console.WriteLine();
Console.WriteLine("=== FINAL REPORTS ===");
Console.WriteLine($"Sequential summary file: {sequentialReportPath}");
Console.WriteLine($"Parallel summary file:   {parallelReportPath}");
Console.WriteLine($"Comparison file:         {comparisonReportPath}");
Console.WriteLine($"Live sequential log:     {sequentialLivePath}");
Console.WriteLine($"Live parallel log:       {parallelLivePath}");
Console.WriteLine();
Console.WriteLine(comparisonText);

static int ResolveParallelism(string[] args)
{
    if (args.Length > 0
        && int.TryParse(args[0], out var parsed)
        && parsed > 0)
    {
        return parsed;
    }

    return Math.Max(2, Environment.ProcessorCount);
}

static string BuildComparisonText(int maxParallelism, TestRunReport sequentialReport, TestRunReport parallelReport)
{
    var sequentialMs = sequentialReport.Duration.TotalMilliseconds;
    var parallelMs = parallelReport.Duration.TotalMilliseconds;
    var speedup = parallelMs > 0
        ? sequentialMs / parallelMs
        : 0d;

    return string.Join(Environment.NewLine,
    [
        "=== EFFICIENCY COMPARISON ===",
        $"Parallel MaxDegreeOfParallelism: {maxParallelism}",
        $"Sequential duration: {sequentialMs:F1} ms",
        $"Parallel duration:   {parallelMs:F1} ms",
        $"Speedup: {speedup:F2}x",
        $"Sequential status summary: Passed={sequentialReport.Passed}, Failed={sequentialReport.Failed}, Errors={sequentialReport.Errored}, TimedOut={sequentialReport.TimedOut}",
        $"Parallel status summary:   Passed={parallelReport.Passed}, Failed={parallelReport.Failed}, Errors={parallelReport.Errored}, TimedOut={parallelReport.TimedOut}"
    ]);
}
