using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;

namespace MiniTestFramework;

public sealed class TestRunner
{
    public Task<TestRunReport> RunAsync(Assembly assembly) =>
        RunAsync(assembly, new TestRunnerOptions(), liveOutput: null);

    public async Task<TestRunReport> RunAsync(
        Assembly assembly,
        TestRunnerOptions options,
        ITestOutput? liveOutput = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

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

            var classContext = await PrepareTestClassAsync(testType, results, liveOutput, cancellationToken);
            if (classContext is not null)
            {
                classContexts.Add(classContext);
            }
        }

        using var semaphore = new SemaphoreSlim(options.MaxDegreeOfParallelism);
        var plannedTests = classContexts
            .SelectMany(c => c.Tests.Select(t => new PlannedTest(c, t)))
            .ToArray();

        var executionTasks = plannedTests
            .Select(plan => RunTestWithSemaphoreAsync(semaphore, plan, options, results, liveOutput, cancellationToken))
            .ToArray();

        await Task.WhenAll(executionTasks);

        foreach (var classContext in classContexts)
        {
            await FinalizeTestClassAsync(classContext, results, liveOutput, cancellationToken);
        }

        runStopwatch.Stop();

        return new TestRunReport
        {
            Results = results
                .OrderBy(r => r.DisplayName, StringComparer.Ordinal)
                .ToArray(),
            Duration = runStopwatch.Elapsed
        };
    }

    private static bool IsTestClass(Type type)
    {
        return type.GetCustomAttribute<TestClassAttribute>() is not null
               || type.GetCustomAttribute<TestClassInfoAttribute>() is not null;
    }

    private static async Task<TestClassContext?> PrepareTestClassAsync(
        Type testType,
        ConcurrentBag<TestCaseResult> results,
        ITestOutput? liveOutput,
        CancellationToken cancellationToken)
    {
        ISharedContext? sharedContext = null;

        try
        {
            sharedContext = await TryCreateSharedContextAsync(testType);
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
                await InvokeLifecycleAsync(instance, beforeAll, "BeforeAll", cancellationToken);
            }
            catch (Exception ex)
            {
                await AddLifecycleErrorAsync(testType, "BeforeAll", ex, results, liveOutput, cancellationToken);
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

    private static async Task FinalizeTestClassAsync(
        TestClassContext classContext,
        ConcurrentBag<TestCaseResult> results,
        ITestOutput? liveOutput,
        CancellationToken cancellationToken)
    {
        try
        {
            await InvokeLifecycleAsync(classContext.Instance, classContext.AfterAll, "AfterAll", cancellationToken);
        }
        catch (Exception ex)
        {
            await AddLifecycleErrorAsync(classContext.TestType, "AfterAll", ex, results, liveOutput, cancellationToken);
        }
        finally
        {
            classContext.SharedContext?.Dispose();
        }
    }

    private static async Task RunTestWithSemaphoreAsync(
        SemaphoreSlim semaphore,
        PlannedTest plan,
        TestRunnerOptions options,
        ConcurrentBag<TestCaseResult> results,
        ITestOutput? liveOutput,
        CancellationToken cancellationToken)
    {
        await semaphore.WaitAsync(cancellationToken);
        try
        {
            await RunSingleTestAsync(plan, options, results, liveOutput, cancellationToken);
        }
        finally
        {
            semaphore.Release();
        }
    }

    private static async Task RunSingleTestAsync(
        PlannedTest plan,
        TestRunnerOptions options,
        ConcurrentBag<TestCaseResult> results,
        ITestOutput? liveOutput,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var instance = plan.ClassContext.Instance;
        var entry = plan.Test;

        try
        {
            await InvokeLifecycleAsync(instance, plan.ClassContext.BeforeEach, "BeforeEach", cancellationToken);
        }
        catch (Exception ex)
        {
            await AddResultAsync(new TestCaseResult
            {
                ClassName = instance.GetType().Name,
                MethodName = entry.Method.Name,
                DisplayName = entry.DisplayName,
                Status = TestStatus.Error,
                Message = ex.InnerException?.Message ?? ex.Message,
                Duration = sw.Elapsed
            }, results, liveOutput, cancellationToken);
            return;
        }

        try
        {
            var timeoutMilliseconds = entry.TimeoutMilliseconds ?? options.DefaultTimeoutMilliseconds;
            await InvokeMethodWithTimeoutAsync(
                instance,
                entry.Method,
                entry.Args,
                timeoutMilliseconds,
                cancellationToken);

            await AddResultAsync(new TestCaseResult
            {
                ClassName = instance.GetType().Name,
                MethodName = entry.Method.Name,
                DisplayName = entry.DisplayName,
                Status = TestStatus.Passed,
                Duration = sw.Elapsed
            }, results, liveOutput, cancellationToken);
        }
        catch (AssertionFailedException ex)
        {
            await AddResultAsync(new TestCaseResult
            {
                ClassName = instance.GetType().Name,
                MethodName = entry.Method.Name,
                DisplayName = entry.DisplayName,
                Status = TestStatus.Failed,
                Message = ex.Message,
                Duration = sw.Elapsed
            }, results, liveOutput, cancellationToken);
        }
        catch (TestTimeoutException ex)
        {
            await AddResultAsync(new TestCaseResult
            {
                ClassName = instance.GetType().Name,
                MethodName = entry.Method.Name,
                DisplayName = entry.DisplayName,
                Status = TestStatus.TimedOut,
                Message = ex.Message,
                Duration = sw.Elapsed
            }, results, liveOutput, cancellationToken);
        }
        catch (Exception ex)
        {
            await AddResultAsync(new TestCaseResult
            {
                ClassName = instance.GetType().Name,
                MethodName = entry.Method.Name,
                DisplayName = entry.DisplayName,
                Status = TestStatus.Error,
                Message = ex.InnerException?.Message ?? ex.Message,
                Duration = sw.Elapsed
            }, results, liveOutput, cancellationToken);
        }
        finally
        {
            try
            {
                await InvokeLifecycleAsync(instance, plan.ClassContext.AfterEach, "AfterEach", cancellationToken);
            }
            catch (Exception ex)
            {
                await AddResultAsync(new TestCaseResult
                {
                    ClassName = instance.GetType().Name,
                    MethodName = entry.Method.Name,
                    DisplayName = $"{entry.DisplayName}::AfterEach",
                    Status = TestStatus.Error,
                    Message = ex.InnerException?.Message ?? ex.Message,
                    Duration = sw.Elapsed
                }, results, liveOutput, cancellationToken);
            }
        }
    }

    private static async Task AddResultAsync(
        TestCaseResult result,
        ConcurrentBag<TestCaseResult> results,
        ITestOutput? liveOutput,
        CancellationToken cancellationToken)
    {
        results.Add(result);

        if (liveOutput is not null)
        {
            var message = string.IsNullOrWhiteSpace(result.Message)
                ? "-"
                : result.Message.Replace(Environment.NewLine, " ");

            var line =
                $"[{DateTime.Now:HH:mm:ss.fff}] {result.Status,-8} {result.DisplayName,-60} {result.Duration.TotalMilliseconds,8:F1} ms | {message}";

            await liveOutput.WriteLineAsync(line, cancellationToken);
        }
    }

    private static async Task AddLifecycleErrorAsync(
        Type testType,
        string stage,
        Exception ex,
        ConcurrentBag<TestCaseResult> results,
        ITestOutput? liveOutput,
        CancellationToken cancellationToken)
    {
        await AddResultAsync(new TestCaseResult
        {
            ClassName = testType.Name,
            MethodName = stage,
            DisplayName = $"{testType.Name}::{stage}",
            Status = TestStatus.Error,
            Message = ex.InnerException?.Message ?? ex.Message,
            Duration = TimeSpan.Zero
        }, results, liveOutput, cancellationToken);
    }

    private static async Task<ISharedContext?> TryCreateSharedContextAsync(Type testType)
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

        await context.InitializeAsync();
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

    private static async Task InvokeLifecycleAsync(
        object instance,
        List<MethodInfo> methods,
        string stageName,
        CancellationToken cancellationToken)
    {
        foreach (var method in methods)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await InvokeMethodAsync(instance, method, Array.Empty<object?>());
            }
            catch (Exception ex)
            {
                throw new TestExecutionException($"Error in {stageName}: {method.Name}", ex);
            }
        }
    }

    private static async Task InvokeMethodWithTimeoutAsync(
        object instance,
        MethodInfo method,
        object?[] args,
        int? timeoutMilliseconds,
        CancellationToken cancellationToken)
    {
        if (timeoutMilliseconds is null)
        {
            await RunMethodOnThreadPoolAsync(instance, method, args, cancellationToken);
            return;
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var invocationTask = RunMethodOnThreadPoolAsync(instance, method, args, timeoutCts.Token);
        var timeoutTask = Task.Delay(timeoutMilliseconds.Value, cancellationToken);
        var completedTask = await Task.WhenAny(invocationTask, timeoutTask);

        if (completedTask == timeoutTask)
        {
            timeoutCts.Cancel();

            _ = invocationTask.ContinueWith(
                t => _ = t.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

            throw new TestTimeoutException($"Timeout exceeded ({timeoutMilliseconds.Value} ms).");
        }

        await invocationTask;
    }

    private static Task RunMethodOnThreadPoolAsync(
        object instance,
        MethodInfo method,
        object?[] args,
        CancellationToken cancellationToken)
    {
        return Task.Run(
            async () => await InvokeMethodAsync(instance, method, args),
            cancellationToken);
    }

    private static async Task InvokeMethodAsync(object instance, MethodInfo method, object?[] args)
    {
        ValidateMethodParameters(method, args);

        object? result;
        try
        {
            result = method.Invoke(instance, args);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }

        switch (result)
        {
            case Task task:
                await task;
                break;
            case ValueTask valueTask:
                await valueTask;
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
}
