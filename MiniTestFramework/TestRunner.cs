using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.ExceptionServices;
using DynamicThreadPoolModule;

namespace MiniTestFramework;

public sealed class TestRunner
{
    public async Task<TestRunReport> RunAsync(
        Assembly assembly,
        TestRunnerOptions options,
        ITestOutput? liveOutput = null,
        CancellationToken cancellationToken = default)
    {
        var report = await ExecuteAsync(
            assembly,
            options,
            simulationOptions: null,
            liveOutput,
            cancellationToken);

        return report.TestReport;
    }

    public Task<TestRunReport> RunAsync(Assembly assembly) =>
        RunAsync(assembly, new TestRunnerOptions(), liveOutput: null);

    public Task<LoadSimulationReport> RunLoadSimulationAsync(
        Assembly assembly,
        TestRunnerOptions options,
        LoadSimulationOptions simulationOptions,
        ITestOutput? liveOutput = null,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(assembly, options, simulationOptions, liveOutput, cancellationToken);
    }

    private static async Task<LoadSimulationReport> ExecuteAsync(
        Assembly assembly,
        TestRunnerOptions options,
        LoadSimulationOptions? simulationOptions,
        ITestOutput? liveOutput,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(options);

        options.Validate();
        simulationOptions?.Validate();

        var runStopwatch = Stopwatch.StartNew();
        var results = new ConcurrentBag<TestCaseResult>();
        var classContexts = new List<TestClassContext>();

        var testTypes = assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract)
            .Where(IsTestClass)
            .ToArray();

        foreach (var testType in testTypes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var classContext = PrepareTestClass(testType, results, liveOutput);
            if (classContext is not null)
            {
                classContexts.Add(classContext);
            }
        }

        var plannedTests = classContexts
            .SelectMany(c => c.Tests.Select(t => new PlannedTest(c, t)))
            .ToArray();

        var completion = new CompletionCounter();
        var submittedCount = 0;
        DynamicThreadPoolStatistics poolStatistics;

        var pool = new DynamicThreadPool(
            options.ToThreadPoolOptions(),
            line => PublishLine(liveOutput, line));

        try
        {
            if (simulationOptions is null)
            {
                submittedCount = QueueImmediateRun(pool, completion, plannedTests, options, results, liveOutput);
            }
            else
            {
                PublishLine(
                    liveOutput,
                    $"[LOAD] Starting simulated load. Planned submissions: {simulationOptions.TotalSubmissions}.");

                submittedCount = await QueueSimulatedLoadAsync(
                    pool,
                    completion,
                    plannedTests,
                    simulationOptions,
                    options,
                    results,
                    liveOutput,
                    cancellationToken);
            }

            completion.Wait(cancellationToken);
        }
        finally
        {
            pool.Dispose();
            poolStatistics = pool.GetStatistics();
        }

        foreach (var classContext in classContexts)
        {
            FinalizeTestClass(classContext, results, liveOutput);
        }

        runStopwatch.Stop();

        return new LoadSimulationReport
        {
            SubmittedCount = submittedCount,
            PoolStatistics = poolStatistics,
            TestReport = new TestRunReport
            {
                Results = results
                    .OrderBy(r => r.DisplayName, StringComparer.Ordinal)
                    .ToArray(),
                Duration = runStopwatch.Elapsed
            }
        };
    }

    private static int QueueImmediateRun(
        DynamicThreadPool pool,
        CompletionCounter completion,
        PlannedTest[] plannedTests,
        TestRunnerOptions options,
        ConcurrentBag<TestCaseResult> results,
        ITestOutput? liveOutput)
    {
        var submittedCount = 0;

        foreach (var plan in plannedTests)
        {
            var launch = new ScheduledLaunch(plan, ++submittedCount, $"{plan.Test.DisplayName}#run{submittedCount:000}");
            EnqueueLaunch(pool, completion, launch, options, results, liveOutput);
        }

        return submittedCount;
    }

    private static async Task<int> QueueSimulatedLoadAsync(
        DynamicThreadPool pool,
        CompletionCounter completion,
        PlannedTest[] plannedTests,
        LoadSimulationOptions simulationOptions,
        TestRunnerOptions options,
        ConcurrentBag<TestCaseResult> results,
        ITestOutput? liveOutput,
        CancellationToken cancellationToken)
    {
        if (plannedTests.Length == 0)
        {
            return 0;
        }

        var submittedCount = 0;
        var planCursor = 0;

        foreach (var phase in simulationOptions.Phases)
        {
            cancellationToken.ThrowIfCancellationRequested();

            PublishLine(
                liveOutput,
                $"[LOAD] Phase '{phase.Name}': submissions={phase.SubmissionCount}, interval={phase.IntervalBetweenSubmissions.TotalMilliseconds:F0} ms.");

            for (var i = 0; i < phase.SubmissionCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var plan = plannedTests[planCursor % plannedTests.Length];
                planCursor++;
                submittedCount++;

                var launch = new ScheduledLaunch(
                    plan,
                    submittedCount,
                    $"{plan.Test.DisplayName}#load{submittedCount:000}");

                EnqueueLaunch(pool, completion, launch, options, results, liveOutput);
                PublishLine(liveOutput, $"[LOAD] queued {launch.DisplayName}");

                if (phase.IntervalBetweenSubmissions > TimeSpan.Zero)
                {
                    await Task.Delay(phase.IntervalBetweenSubmissions, cancellationToken);
                }
            }

            if (phase.PauseAfterPhase > TimeSpan.Zero)
            {
                PublishLine(
                    liveOutput,
                    $"[LOAD] pause after '{phase.Name}' for {phase.PauseAfterPhase.TotalMilliseconds:F0} ms.");

                await Task.Delay(phase.PauseAfterPhase, cancellationToken);
            }
        }

        return submittedCount;
    }

    private static void EnqueueLaunch(
        DynamicThreadPool pool,
        CompletionCounter completion,
        ScheduledLaunch launch,
        TestRunnerOptions options,
        ConcurrentBag<TestCaseResult> results,
        ITestOutput? liveOutput)
    {
        completion.Add();

        pool.Enqueue(
            launch.DisplayName,
            () =>
            {
                try
                {
                    RunSingleTest(launch, options, results, liveOutput);
                }
                finally
                {
                    completion.Signal();
                }
            });
    }

    private static bool IsTestClass(Type type)
    {
        return type.GetCustomAttribute<TestClassAttribute>() is not null
               || type.GetCustomAttribute<TestClassInfoAttribute>() is not null;
    }

    private static TestClassContext? PrepareTestClass(
        Type testType,
        ConcurrentBag<TestCaseResult> results,
        ITestOutput? liveOutput)
    {
        ISharedContext? sharedContext = null;

        try
        {
            sharedContext = TryCreateSharedContext(testType);
            var instance = Activator.CreateInstance(testType)
                           ?? throw new TestDiscoveryException($"Cannot create an instance of {testType.Name}.");

            InjectSharedContextIfNeeded(instance, sharedContext);

            var beforeAll = FindLifecycleMethods(testType, typeof(BeforeAllAttribute));
            var afterAll = FindLifecycleMethods(testType, typeof(AfterAllAttribute));
            var beforeEach = FindLifecycleMethods(testType, typeof(BeforeEachAttribute));
            var afterEach = FindLifecycleMethods(testType, typeof(AfterEachAttribute));
            var tests = FindTests(testType);

            try
            {
                InvokeLifecycle(instance, beforeAll, "BeforeAll");
            }
            catch (Exception ex)
            {
                AddLifecycleError(testType, "BeforeAll", ex, results, liveOutput);
                sharedContext?.Dispose();
                return null;
            }

            return new TestClassContext(
                testType,
                instance,
                sharedContext,
                afterAll,
                beforeEach,
                afterEach,
                tests);
        }
        catch
        {
            sharedContext?.Dispose();
            throw;
        }
    }

    private static void FinalizeTestClass(
        TestClassContext classContext,
        ConcurrentBag<TestCaseResult> results,
        ITestOutput? liveOutput)
    {
        try
        {
            InvokeLifecycle(classContext.Instance, classContext.AfterAll, "AfterAll");
        }
        catch (Exception ex)
        {
            AddLifecycleError(classContext.TestType, "AfterAll", ex, results, liveOutput);
        }
        finally
        {
            classContext.SharedContext?.Dispose();
        }
    }

    private static void RunSingleTest(
        ScheduledLaunch launch,
        TestRunnerOptions options,
        ConcurrentBag<TestCaseResult> results,
        ITestOutput? liveOutput)
    {
        var sw = Stopwatch.StartNew();
        var instance = launch.Plan.ClassContext.Instance;
        var entry = launch.Plan.Test;

        try
        {
            InvokeLifecycle(instance, launch.Plan.ClassContext.BeforeEach, "BeforeEach");
        }
        catch (Exception ex)
        {
            AddResult(
                new TestCaseResult
                {
                    ClassName = instance.GetType().Name,
                    MethodName = entry.Method.Name,
                    DisplayName = launch.DisplayName,
                    Status = TestStatus.Error,
                    Message = ex.InnerException?.Message ?? ex.Message,
                    Duration = sw.Elapsed
                },
                results,
                liveOutput);

            return;
        }

        try
        {
            var timeoutMilliseconds = entry.TimeoutMilliseconds ?? options.DefaultTimeoutMilliseconds;
            InvokeMethodWithTimeout(instance, entry.Method, entry.Args, timeoutMilliseconds);

            AddResult(
                new TestCaseResult
                {
                    ClassName = instance.GetType().Name,
                    MethodName = entry.Method.Name,
                    DisplayName = launch.DisplayName,
                    Status = TestStatus.Passed,
                    Duration = sw.Elapsed
                },
                results,
                liveOutput);
        }
        catch (AssertionFailedException ex)
        {
            AddResult(
                new TestCaseResult
                {
                    ClassName = instance.GetType().Name,
                    MethodName = entry.Method.Name,
                    DisplayName = launch.DisplayName,
                    Status = TestStatus.Failed,
                    Message = ex.Message,
                    Duration = sw.Elapsed
                },
                results,
                liveOutput);
        }
        catch (TestTimeoutException ex)
        {
            AddResult(
                new TestCaseResult
                {
                    ClassName = instance.GetType().Name,
                    MethodName = entry.Method.Name,
                    DisplayName = launch.DisplayName,
                    Status = TestStatus.TimedOut,
                    Message = ex.Message,
                    Duration = sw.Elapsed
                },
                results,
                liveOutput);
        }
        catch (Exception ex)
        {
            AddResult(
                new TestCaseResult
                {
                    ClassName = instance.GetType().Name,
                    MethodName = entry.Method.Name,
                    DisplayName = launch.DisplayName,
                    Status = TestStatus.Error,
                    Message = ex.InnerException?.Message ?? ex.Message,
                    Duration = sw.Elapsed
                },
                results,
                liveOutput);
        }
        finally
        {
            try
            {
                InvokeLifecycle(instance, launch.Plan.ClassContext.AfterEach, "AfterEach");
            }
            catch (Exception ex)
            {
                AddResult(
                    new TestCaseResult
                    {
                        ClassName = instance.GetType().Name,
                        MethodName = entry.Method.Name,
                        DisplayName = $"{launch.DisplayName}::AfterEach",
                        Status = TestStatus.Error,
                        Message = ex.InnerException?.Message ?? ex.Message,
                        Duration = sw.Elapsed
                    },
                    results,
                    liveOutput);
            }
        }
    }

    private static void AddResult(
        TestCaseResult result,
        ConcurrentBag<TestCaseResult> results,
        ITestOutput? liveOutput)
    {
        results.Add(result);

        if (liveOutput is not null)
        {
            var message = string.IsNullOrWhiteSpace(result.Message)
                ? "-"
                : result.Message.Replace(Environment.NewLine, " ");

            var line =
                $"[{DateTime.Now:HH:mm:ss.fff}] {result.Status,-8} {result.DisplayName,-72} {result.Duration.TotalMilliseconds,8:F1} ms | {message}";

            PublishLine(liveOutput, line);
        }
    }

    private static void AddLifecycleError(
        Type testType,
        string stage,
        Exception ex,
        ConcurrentBag<TestCaseResult> results,
        ITestOutput? liveOutput)
    {
        AddResult(
            new TestCaseResult
            {
                ClassName = testType.Name,
                MethodName = stage,
                DisplayName = $"{testType.Name}::{stage}",
                Status = TestStatus.Error,
                Message = ex.InnerException?.Message ?? ex.Message,
                Duration = TimeSpan.Zero
            },
            results,
            liveOutput);
    }

    private static ISharedContext? TryCreateSharedContext(Type testType)
    {
        var useShared = testType.GetCustomAttribute<UseSharedContextAttribute>();
        if (useShared is null)
        {
            return null;
        }

        if (!typeof(ISharedContext).IsAssignableFrom(useShared.ContextType))
        {
            throw new TestDiscoveryException($"{useShared.ContextType.Name} does not implement ISharedContext.");
        }

        var context = Activator.CreateInstance(useShared.ContextType) as ISharedContext
                      ?? throw new TestDiscoveryException($"Cannot create shared context: {useShared.ContextType.Name}.");

        context.InitializeAsync().GetAwaiter().GetResult();
        return context;
    }

    private static void InjectSharedContextIfNeeded(object instance, ISharedContext? sharedContext)
    {
        if (sharedContext is null)
        {
            return;
        }

        var prop = instance.GetType()
            .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(p => p.CanWrite && p.PropertyType.IsAssignableFrom(sharedContext.GetType()));

        prop?.SetValue(instance, sharedContext);
    }

    private static List<MethodInfo> FindLifecycleMethods(Type testType, Type attrType)
    {
        return testType
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(m => m.GetCustomAttributes(attrType, false).Any())
            .ToList();
    }

    private static List<TestMethodEntry> FindTests(Type testType)
    {
        var methods = testType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var entries = new List<TestMethodEntry>();
        var classTimeout = testType.GetCustomAttribute<TimeoutAttribute>()?.Milliseconds;

        foreach (var method in methods)
        {
            var hasTestAttribute = method.GetCustomAttribute<TestAttribute>() is not null;
            var cases = method.GetCustomAttributes<TestCaseAttribute>().ToArray();
            if (!hasTestAttribute && cases.Length == 0)
            {
                continue;
            }

            var methodTimeout = method.GetCustomAttribute<TimeoutAttribute>()?.Milliseconds ?? classTimeout;

            if (cases.Length == 0)
            {
                entries.Add(new TestMethodEntry(
                    method,
                    Array.Empty<object?>(),
                    $"{testType.Name}.{method.Name}",
                    methodTimeout));
                continue;
            }

            for (var i = 0; i < cases.Length; i++)
            {
                entries.Add(new TestMethodEntry(
                    method,
                    cases[i].Args,
                    $"{testType.Name}.{method.Name}[{i}]",
                    methodTimeout));
            }
        }

        return entries;
    }

    private static void InvokeLifecycle(object instance, List<MethodInfo> methods, string stageName)
    {
        foreach (var method in methods)
        {
            try
            {
                InvokeMethod(instance, method, Array.Empty<object?>());
            }
            catch (Exception ex)
            {
                throw new TestExecutionException($"Error in {stageName}: {method.Name}", ex);
            }
        }
    }

    private static void InvokeMethodWithTimeout(
        object instance,
        MethodInfo method,
        object?[] args,
        int? timeoutMilliseconds)
    {
        if (timeoutMilliseconds is null)
        {
            InvokeMethod(instance, method, args);
            return;
        }

        ExceptionDispatchInfo? capturedException = null;

        var invocationThread = new Thread(() =>
        {
            try
            {
                InvokeMethod(instance, method, args);
            }
            catch (Exception ex)
            {
                capturedException = ExceptionDispatchInfo.Capture(ex);
            }
        })
        {
            IsBackground = true,
            Name = $"TestInvocation-{instance.GetType().Name}.{method.Name}"
        };

        invocationThread.Start();

        if (!invocationThread.Join(timeoutMilliseconds.Value))
        {
            throw new TestTimeoutException($"Timeout exceeded ({timeoutMilliseconds.Value} ms).");
        }

        capturedException?.Throw();
    }

    private static void InvokeMethod(object instance, MethodInfo method, object?[] args)
    {
        ValidateMethodParameters(method, args);

        object? result;
        try
        {
            result = method.Invoke(instance, args);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }

        switch (result)
        {
            case Task task:
                task.GetAwaiter().GetResult();
                break;
            case ValueTask valueTask:
                valueTask.GetAwaiter().GetResult();
                break;
        }
    }

    private static void ValidateMethodParameters(MethodInfo method, object?[] args)
    {
        var parameters = method.GetParameters();
        if (parameters.Length != args.Length)
        {
            throw new TestDiscoveryException(
                $"Method {method.Name} expects {parameters.Length} args but got {args.Length}.");
        }

        for (var i = 0; i < parameters.Length; i++)
        {
            var arg = args[i];
            if (arg is null)
            {
                continue;
            }

            if (!parameters[i].ParameterType.IsAssignableFrom(arg.GetType()))
            {
                throw new TestDiscoveryException(
                    $"Argument {i} for {method.Name} has type {arg.GetType().Name}, expected {parameters[i].ParameterType.Name}.");
            }
        }
    }

    private static void PublishLine(ITestOutput? liveOutput, string line)
    {
        if (liveOutput is null)
        {
            Console.WriteLine(line);
            return;
        }

        liveOutput.WriteLineAsync(line).GetAwaiter().GetResult();
    }

    private sealed class CompletionCounter
    {
        private readonly object _gate = new();
        private int _remaining;

        public void Add()
        {
            lock (_gate)
            {
                _remaining++;
            }
        }

        public void Signal()
        {
            lock (_gate)
            {
                _remaining--;
                if (_remaining <= 0)
                {
                    Monitor.PulseAll(_gate);
                }
            }
        }

        public void Wait(CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                while (_remaining > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Monitor.Wait(_gate, TimeSpan.FromMilliseconds(200));
                }
            }
        }
    }

    private sealed record TestClassContext(
        Type TestType,
        object Instance,
        ISharedContext? SharedContext,
        List<MethodInfo> AfterAll,
        List<MethodInfo> BeforeEach,
        List<MethodInfo> AfterEach,
        List<TestMethodEntry> Tests);

    private sealed record TestMethodEntry(
        MethodInfo Method,
        object?[] Args,
        string DisplayName,
        int? TimeoutMilliseconds);

    private readonly record struct PlannedTest(TestClassContext ClassContext, TestMethodEntry Test);

    private readonly record struct ScheduledLaunch(
        PlannedTest Plan,
        int LaunchNumber,
        string DisplayName);
}
