using System.Diagnostics;

namespace DynamicThreadPoolModule;

public sealed class DynamicThreadPool : IDisposable
{
    private readonly object _gate = new();
    private readonly Queue<QueuedWorkItem> _queue = new();
    private readonly Dictionary<int, WorkerState> _workers = new();
    private readonly DynamicThreadPoolOptions _options;
    private readonly Action<string>? _logger;
    private readonly Thread _monitorThread;

    private bool _disposed;
    private bool _shutdownRequested;
    private int _nextWorkerId;
    private int _nextWorkItemId;

    private int _totalQueued;
    private int _totalCompleted;
    private int _workerStarts;
    private int _workerStops;
    private int _workerFailures;
    private int _replacementWorkersCreated;
    private int _suspectedHungWorkers;
    private int _maxObservedWorkers;
    private int _maxObservedBusyWorkers;
    private int _maxObservedQueueLength;
    private DynamicThreadPoolSnapshot? _lastSnapshot;

    public DynamicThreadPool(DynamicThreadPoolOptions options, Action<string>? logger = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
        _logger = logger;

        var bootstrapMessages = new List<string>();
        lock (_gate)
        {
            EnsureMinimumWorkersNoLock("bootstrap", bootstrapMessages);
        }

        PublishMessages(bootstrapMessages);

        _monitorThread = new Thread(MonitorLoop)
        {
            IsBackground = true,
            Name = "DynamicThreadPool-Monitor"
        };
        _monitorThread.Start();
    }

    public void Enqueue(string name, Action action)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(action);

        List<string> messages;
        lock (_gate)
        {
            ThrowIfDisposedNoLock();

            var workItem = new QueuedWorkItem(
                Id: ++_nextWorkItemId,
                Name: name,
                Action: action,
                EnqueuedAtUtc: DateTime.UtcNow);

            _queue.Enqueue(workItem);
            _totalQueued++;
            _maxObservedQueueLength = Math.Max(_maxObservedQueueLength, _queue.Count);

            messages = new List<string>
            {
                $"[POOL] queued #{workItem.Id}: {workItem.Name}. queue={_queue.Count}"
            };

            MaybeScaleUpNoLock("enqueue", messages);
            Monitor.PulseAll(_gate);
        }

        PublishMessages(messages);
    }

    public DynamicThreadPoolStatistics GetStatistics()
    {
        lock (_gate)
        {
            return new DynamicThreadPoolStatistics
            {
                TotalQueued = _totalQueued,
                TotalCompleted = _totalCompleted,
                WorkerStarts = _workerStarts,
                WorkerStops = _workerStops,
                WorkerFailures = _workerFailures,
                ReplacementWorkersCreated = _replacementWorkersCreated,
                SuspectedHungWorkers = _suspectedHungWorkers,
                MaxObservedWorkers = _maxObservedWorkers,
                MaxObservedBusyWorkers = _maxObservedBusyWorkers,
                MaxObservedQueueLength = _maxObservedQueueLength
            };
        }
    }

    public void Dispose()
    {
        List<string> messages = new();
        Thread[] workersToJoin;

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _shutdownRequested = true;
            workersToJoin = _workers.Values.Select(w => w.Thread).ToArray();
            Monitor.PulseAll(_gate);
        }

        if (!_monitorThread.Join(_options.ShutdownJoinTimeout))
        {
            messages.Add("[POOL] monitor thread did not stop within the timeout window.");
        }

        foreach (var workerThread in workersToJoin)
        {
            if (!workerThread.Join(_options.ShutdownJoinTimeout))
            {
                messages.Add($"[POOL] worker thread {workerThread.Name} did not stop within the timeout window.");
            }
        }

        messages.Add("[POOL] shutdown completed.");
        PublishMessages(messages);
    }

    private void MonitorLoop()
    {
        try
        {
            while (true)
            {
                Thread.Sleep(_options.MonitorInterval);

                List<string>? messages = null;
                bool shouldStop;

                lock (_gate)
                {
                    shouldStop = _shutdownRequested;

                    messages = new List<string>();

                    if (!shouldStop)
                    {
                        ReplaceHungWorkersNoLock(messages);
                        EnsureMinimumWorkersNoLock("monitor", messages);
                        MaybeScaleUpNoLock("monitor", messages);

                        var snapshot = CreateSnapshotNoLock();
                        if (snapshot.HasMeaningfulDifference(_lastSnapshot))
                        {
                            messages.Add(snapshot.ToLogLine());
                            _lastSnapshot = snapshot;
                        }
                    }
                }

                PublishMessages(messages);

                if (shouldStop)
                {
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            PublishMessages(
            [
                $"[POOL] monitor failed: {ex.GetType().Name}: {ex.Message}"
            ]);
        }
    }

    private void WorkerLoop(object? state)
    {
        if (state is not WorkerState worker)
        {
            return;
        }

        try
        {
            while (true)
            {
                QueuedWorkItem? workItem = null;
                string? exitReason = null;
                List<string>? messages = null;

                lock (_gate)
                {
                    worker.LastActivityUtc = DateTime.UtcNow;
                    worker.IsBusy = false;
                    worker.CurrentWorkName = null;
                    worker.CurrentWorkStartedUtc = null;

                    while (!_shutdownRequested && _queue.Count == 0)
                    {
                        if (worker.IsStopping)
                        {
                            exitReason = "stop-request";
                            break;
                        }

                        if (ShouldRetireWorkerNoLock(worker))
                        {
                            exitReason = "idle-timeout";
                            break;
                        }

                        Monitor.Wait(_gate, _options.WorkerIdleTimeout);
                    }

                    if (exitReason is null && _shutdownRequested && _queue.Count == 0)
                    {
                        exitReason = "shutdown";
                    }

                    if (exitReason is not null)
                    {
                        messages = new List<string>();
                        RemoveWorkerNoLock(worker, exitReason, messages);
                    }
                    else
                    {
                        workItem = _queue.Dequeue();
                        worker.IsBusy = true;
                        worker.CurrentWorkName = workItem.Name;
                        worker.CurrentWorkStartedUtc = DateTime.UtcNow;
                        worker.LastActivityUtc = DateTime.UtcNow;

                        var busyWorkers = BusyHealthyWorkersNoLock();
                        _maxObservedBusyWorkers = Math.Max(_maxObservedBusyWorkers, busyWorkers);
                    }
                }

                if (exitReason is not null)
                {
                    PublishMessages(messages);
                    return;
                }

                Debug.Assert(workItem is not null);

                List<string> executionMessages = new();

                try
                {
                    workItem.Action();
                }
                catch (Exception ex)
                {
                    executionMessages.Add(
                        $"[POOL] worker #{worker.Id} caught task exception in '{workItem.Name}': {ex.GetType().Name}: {ex.Message}");
                }
                finally
                {
                    bool mustExitAfterCompletion;

                    lock (_gate)
                    {
                        _totalCompleted++;
                        worker.IsBusy = false;
                        worker.LastActivityUtc = DateTime.UtcNow;
                        worker.CurrentWorkName = null;
                        worker.CurrentWorkStartedUtc = null;

                        mustExitAfterCompletion = worker.IsSuspectedHung;
                        if (mustExitAfterCompletion)
                        {
                            worker.IsStopping = true;
                        }

                        Monitor.PulseAll(_gate);
                    }

                    if (mustExitAfterCompletion)
                    {
                        lock (_gate)
                        {
                            RemoveWorkerNoLock(worker, "recovered-after-suspected-hang", executionMessages);
                        }
                    }
                }

                PublishMessages(executionMessages);
            }
        }
        catch (Exception ex)
        {
            List<string> messages = new();

            lock (_gate)
            {
                _workerFailures++;
                RemoveWorkerNoLock(worker, "worker-crash", messages);
                EnsureMinimumWorkersNoLock("worker-crash", messages);
            }

            messages.Add($"[POOL] worker #{worker.Id} crashed: {ex.GetType().Name}: {ex.Message}");
            PublishMessages(messages);
        }
    }

    private void MaybeScaleUpNoLock(string reason, List<string> messages)
    {
        var healthyWorkers = HealthyWorkerCountNoLock();
        if (healthyWorkers >= _options.MaxWorkerCount || _queue.Count == 0)
        {
            return;
        }

        var idleHealthyWorkers = IdleHealthyWorkersNoLock();
        var oldestWait = GetOldestQueueWaitNoLock();
        var needMoreWorkers = _queue.Count > idleHealthyWorkers || oldestWait >= _options.QueueWaitThreshold;

        if (!needMoreWorkers)
        {
            return;
        }

        var createCount = Math.Min(_options.ScaleUpStep, _options.MaxWorkerCount - healthyWorkers);
        for (var i = 0; i < createCount; i++)
        {
            StartWorkerNoLock($"scale-up:{reason}", replacement: false, messages);
        }
    }

    private void ReplaceHungWorkersNoLock(List<string> messages)
    {
        var now = DateTime.UtcNow;
        foreach (var worker in _workers.Values.ToArray())
        {
            if (!worker.IsBusy || worker.IsSuspectedHung || worker.CurrentWorkStartedUtc is null)
            {
                continue;
            }

            if (now - worker.CurrentWorkStartedUtc.Value < _options.WorkerHangThreshold)
            {
                continue;
            }

            worker.IsSuspectedHung = true;
            worker.IsStopping = true;
            _suspectedHungWorkers++;

            messages.Add(
                $"[POOL] worker #{worker.Id} is suspected hung on '{worker.CurrentWorkName}'. A replacement will be started.");

            if (HealthyWorkerCountNoLock() < _options.MaxWorkerCount)
            {
                StartWorkerNoLock("hung-replacement", replacement: true, messages);
            }
        }
    }

    private void EnsureMinimumWorkersNoLock(string reason, List<string> messages)
    {
        while (HealthyWorkerCountNoLock() < _options.MinWorkerCount)
        {
            StartWorkerNoLock(reason, replacement: false, messages);
        }
    }

    private void StartWorkerNoLock(string reason, bool replacement, List<string> messages)
    {
        var worker = new WorkerState(++_nextWorkerId);
        var thread = new Thread(WorkerLoop)
        {
            IsBackground = true,
            Name = $"DynamicThreadPool-Worker-{worker.Id}"
        };

        worker.Thread = thread;
        worker.LastActivityUtc = DateTime.UtcNow;

        _workers.Add(worker.Id, worker);
        _workerStarts++;
        if (replacement)
        {
            _replacementWorkersCreated++;
        }

        _maxObservedWorkers = Math.Max(_maxObservedWorkers, HealthyWorkerCountNoLock());

        messages.Add(
            $"[POOL] worker #{worker.Id} started ({reason}). healthy-workers={HealthyWorkerCountNoLock()}");

        thread.Start(worker);
    }

    private void RemoveWorkerNoLock(WorkerState worker, string reason, List<string> messages)
    {
        if (!_workers.Remove(worker.Id))
        {
            return;
        }

        _workerStops++;
        messages.Add($"[POOL] worker #{worker.Id} stopped ({reason}).");
    }

    private bool ShouldRetireWorkerNoLock(WorkerState worker)
    {
        if (worker.IsBusy || worker.IsSuspectedHung)
        {
            return false;
        }

        if (HealthyWorkerCountNoLock() <= _options.MinWorkerCount)
        {
            return false;
        }

        return DateTime.UtcNow - worker.LastActivityUtc >= _options.WorkerIdleTimeout;
    }

    private int HealthyWorkerCountNoLock()
    {
        return _workers.Values.Count(w => !w.IsSuspectedHung);
    }

    private int BusyHealthyWorkersNoLock()
    {
        return _workers.Values.Count(w => w.IsBusy && !w.IsSuspectedHung);
    }

    private int IdleHealthyWorkersNoLock()
    {
        return _workers.Values.Count(w => !w.IsBusy && !w.IsSuspectedHung);
    }

    private TimeSpan GetOldestQueueWaitNoLock()
    {
        if (_queue.Count == 0)
        {
            return TimeSpan.Zero;
        }

        return DateTime.UtcNow - _queue.Peek().EnqueuedAtUtc;
    }

    private DynamicThreadPoolSnapshot CreateSnapshotNoLock()
    {
        var busyWorkers = BusyHealthyWorkersNoLock();
        var workerCount = HealthyWorkerCountNoLock();

        return new DynamicThreadPoolSnapshot
        {
            WorkerCount = workerCount,
            BusyWorkers = busyWorkers,
            IdleWorkers = Math.Max(0, workerCount - busyWorkers),
            SuspectedHungWorkers = _workers.Values.Count(w => w.IsSuspectedHung),
            QueueLength = _queue.Count,
            OldestQueueWait = GetOldestQueueWaitNoLock()
        };
    }

    private void ThrowIfDisposedNoLock()
    {
        if (_disposed || _shutdownRequested)
        {
            throw new ObjectDisposedException(nameof(DynamicThreadPool));
        }
    }

    private void PublishMessages(IEnumerable<string>? messages)
    {
        if (messages is null || _logger is null)
        {
            return;
        }

        foreach (var message in messages)
        {
            _logger(message);
        }
    }

    private sealed class WorkerState
    {
        public WorkerState(int id)
        {
            Id = id;
        }

        public int Id { get; }
        public Thread Thread { get; set; } = null!;
        public bool IsBusy { get; set; }
        public bool IsStopping { get; set; }
        public bool IsSuspectedHung { get; set; }
        public DateTime LastActivityUtc { get; set; }
        public DateTime? CurrentWorkStartedUtc { get; set; }
        public string? CurrentWorkName { get; set; }
    }

    private sealed record QueuedWorkItem(
        int Id,
        string Name,
        Action Action,
        DateTime EnqueuedAtUtc);
}
