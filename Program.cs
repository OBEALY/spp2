using System.Text;
using MiniTestFramework;

Console.OutputEncoding = Encoding.UTF8;

var runner = new TestRunner();
var assembly = typeof(Program).Assembly;
var outputDirectory = AppContext.BaseDirectory;
var simulation = LoadSimulationOptions.CreateDefault();

var minWorkers = ResolveArgument(args, index: 0, defaultValue: 1);
var dynamicMaxWorkers = ResolveArgument(
    args,
    index: 1,
    defaultValue: Math.Max(minWorkers + 3, Math.Min(Environment.ProcessorCount, 8)));

dynamicMaxWorkers = Math.Max(dynamicMaxWorkers, minWorkers);

var fixedOptions = CreateFixedOptions(minWorkers);
var dynamicOptions = CreateDynamicOptions(minWorkers, dynamicMaxWorkers);

Console.WriteLine("Лабораторная работа 3. Динамический пул потоков для запуска тестов.");
Console.WriteLine($"Сценарий нагрузки: {simulation.TotalSubmissions} запусков тестов.");
Console.WriteLine($"Фиксированный режим: {fixedOptions.MinWorkerCount} потока(ов).");
Console.WriteLine($"Динамический режим: min={dynamicOptions.MinWorkerCount}, max={dynamicOptions.MaxWorkerCount}.");
Console.WriteLine();

var fixedLivePath = Path.Combine(outputDirectory, "Lab3.Fixed.Live.log");
var dynamicLivePath = Path.Combine(outputDirectory, "Lab3.Dynamic.Live.log");

Console.WriteLine("=== Запуск фиксированного пула ===");
LoadSimulationReport fixedReport;
await using (var output = new SynchronizedTestOutput(fixedLivePath))
{
    fixedReport = await runner.RunLoadSimulationAsync(assembly, fixedOptions, simulation, output);
}

Console.WriteLine();
Console.WriteLine("=== Запуск динамического пула ===");
LoadSimulationReport dynamicReport;
await using (var output = new SynchronizedTestOutput(dynamicLivePath))
{
    dynamicReport = await runner.RunLoadSimulationAsync(assembly, dynamicOptions, simulation, output);
}

var fixedReportPath = Path.Combine(outputDirectory, "Lab3.Fixed.Report.txt");
var dynamicReportPath = Path.Combine(outputDirectory, "Lab3.Dynamic.Report.txt");
var comparisonReportPath = Path.Combine(outputDirectory, "Lab3.Comparison.txt");

await File.WriteAllTextAsync(
    fixedReportPath,
    BuildDetailedReport("Фиксированный пул", simulation, fixedOptions, fixedReport),
    Encoding.UTF8);

await File.WriteAllTextAsync(
    dynamicReportPath,
    BuildDetailedReport("Динамический пул", simulation, dynamicOptions, dynamicReport),
    Encoding.UTF8);

var comparisonText = BuildComparisonText(simulation, fixedOptions, dynamicOptions, fixedReport, dynamicReport);
await File.WriteAllTextAsync(comparisonReportPath, comparisonText, Encoding.UTF8);

Console.WriteLine();
Console.WriteLine("=== Итог ===");
Console.WriteLine(fixedReport.ToConsoleText("Фиксированный пул"));
Console.WriteLine();
Console.WriteLine(dynamicReport.ToConsoleText("Динамический пул"));
Console.WriteLine();
Console.WriteLine(comparisonText);
Console.WriteLine();
Console.WriteLine($"Отчёт фиксированного режима: {fixedReportPath}");
Console.WriteLine($"Отчёт динамического режима:  {dynamicReportPath}");
Console.WriteLine($"Сравнение режимов:          {comparisonReportPath}");
Console.WriteLine($"Лог фиксированного режима:  {fixedLivePath}");
Console.WriteLine($"Лог динамического режима:   {dynamicLivePath}");

static int ResolveArgument(string[] args, int index, int defaultValue)
{
    if (args.Length > index
        && int.TryParse(args[index], out var parsed)
        && parsed > 0)
    {
        return parsed;
    }

    return defaultValue;
}

static TestRunnerOptions CreateFixedOptions(int workers)
{
    return new TestRunnerOptions
    {
        MinWorkerCount = workers,
        MaxWorkerCount = workers,
        ScaleUpStep = 1,
        DefaultTimeoutMilliseconds = 2_000,
        WorkerIdleTimeout = TimeSpan.FromSeconds(2),
        QueueWaitThreshold = TimeSpan.FromMilliseconds(400),
        MonitorInterval = TimeSpan.FromMilliseconds(250),
        WorkerHangThreshold = TimeSpan.FromSeconds(5),
        ShutdownJoinTimeout = TimeSpan.FromMilliseconds(800)
    };
}

static TestRunnerOptions CreateDynamicOptions(int minWorkers, int maxWorkers)
{
    return new TestRunnerOptions
    {
        MinWorkerCount = minWorkers,
        MaxWorkerCount = maxWorkers,
        ScaleUpStep = 1,
        DefaultTimeoutMilliseconds = 2_000,
        WorkerIdleTimeout = TimeSpan.FromSeconds(1.5),
        QueueWaitThreshold = TimeSpan.FromMilliseconds(250),
        MonitorInterval = TimeSpan.FromMilliseconds(200),
        WorkerHangThreshold = TimeSpan.FromSeconds(5),
        ShutdownJoinTimeout = TimeSpan.FromMilliseconds(800)
    };
}

static string BuildDetailedReport(
    string title,
    LoadSimulationOptions simulation,
    TestRunnerOptions options,
    LoadSimulationReport report)
{
    return string.Join(
        Environment.NewLine,
        [
            report.ToConsoleText(title),
            string.Empty,
            "=== Конфигурация ===",
            $"Min workers: {options.MinWorkerCount}",
            $"Max workers: {options.MaxWorkerCount}",
            $"Scale step: {options.ScaleUpStep}",
            $"Idle timeout: {options.WorkerIdleTimeout.TotalMilliseconds:F0} ms",
            $"Queue wait threshold: {options.QueueWaitThreshold.TotalMilliseconds:F0} ms",
            $"Monitor interval: {options.MonitorInterval.TotalMilliseconds:F0} ms",
            $"Hang threshold: {options.WorkerHangThreshold.TotalMilliseconds:F0} ms",
            string.Empty,
            "=== Сценарий нагрузки ===",
            FormatSimulation(simulation),
            string.Empty,
            "=== Результаты тестов ===",
            report.TestReport.ToFileText()
        ]);
}

static string BuildComparisonText(
    LoadSimulationOptions simulation,
    TestRunnerOptions fixedOptions,
    TestRunnerOptions dynamicOptions,
    LoadSimulationReport fixedReport,
    LoadSimulationReport dynamicReport)
{
    var fixedMs = fixedReport.TestReport.Duration.TotalMilliseconds;
    var dynamicMs = dynamicReport.TestReport.Duration.TotalMilliseconds;
    var speedup = dynamicMs > 0d ? fixedMs / dynamicMs : 0d;

    return string.Join(
        Environment.NewLine,
        [
            "=== Сравнение эффективности ===",
            $"Подач по сценарию: {simulation.TotalSubmissions}",
            $"Фиксированный пул: min=max={fixedOptions.MinWorkerCount}",
            $"Динамический пул: min={dynamicOptions.MinWorkerCount}, max={dynamicOptions.MaxWorkerCount}",
            $"Время фиксированного режима: {fixedMs:F1} ms",
            $"Время динамического режима:  {dynamicMs:F1} ms",
            $"Ускорение динамического режима: {speedup:F2}x",
            $"Максимум потоков (фиксированный): {fixedReport.PoolStatistics.MaxObservedWorkers}",
            $"Максимум потоков (динамический):  {dynamicReport.PoolStatistics.MaxObservedWorkers}",
            $"Максимальная очередь (фиксированный): {fixedReport.PoolStatistics.MaxObservedQueueLength}",
            $"Максимальная очередь (динамический):  {dynamicReport.PoolStatistics.MaxObservedQueueLength}",
            $"Замещающие потоки (фиксированный): {fixedReport.PoolStatistics.ReplacementWorkersCreated}",
            $"Замещающие потоки (динамический):  {dynamicReport.PoolStatistics.ReplacementWorkersCreated}",
            $"Подозрения на зависание (фиксированный): {fixedReport.PoolStatistics.SuspectedHungWorkers}",
            $"Подозрения на зависание (динамический):  {dynamicReport.PoolStatistics.SuspectedHungWorkers}",
            $"Итоги тестов (фиксированный): passed={fixedReport.TestReport.Passed}, failed={fixedReport.TestReport.Failed}, errors={fixedReport.TestReport.Errored}, timeouts={fixedReport.TestReport.TimedOut}",
            $"Итоги тестов (динамический):  passed={dynamicReport.TestReport.Passed}, failed={dynamicReport.TestReport.Failed}, errors={dynamicReport.TestReport.Errored}, timeouts={dynamicReport.TestReport.TimedOut}"
        ]);
}

static string FormatSimulation(LoadSimulationOptions simulation)
{
    return string.Join(
        Environment.NewLine,
        simulation.Phases.Select((phase, index) =>
            $"{index + 1}. {phase.Name}: submissions={phase.SubmissionCount}, interval={phase.IntervalBetweenSubmissions.TotalMilliseconds:F0} ms, pauseAfter={phase.PauseAfterPhase.TotalMilliseconds:F0} ms"));
}
