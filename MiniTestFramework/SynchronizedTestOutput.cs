namespace MiniTestFramework;

public interface ITestOutput : IAsyncDisposable
{
    Task WriteLineAsync(string line, CancellationToken cancellationToken = default);
}

public sealed class SynchronizedTestOutput : ITestOutput
{
    private readonly object _gate = new();
    private readonly StreamWriter? _fileWriter;
    private readonly bool _writeToConsole;
    private bool _disposed;

    public SynchronizedTestOutput(string? filePath = null, bool writeToConsole = true)
    {
        _writeToConsole = writeToConsole;

        if (!string.IsNullOrWhiteSpace(filePath))
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            _fileWriter = new StreamWriter(
                new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.Read),
                System.Text.Encoding.UTF8);
        }
    }

    public async Task WriteLineAsync(string line, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            ThrowIfDisposed();

            if (_writeToConsole)
            {
                Console.WriteLine(line);
            }

            if (_fileWriter is not null)
            {
                _fileWriter.WriteLine(line);
                _fileWriter.Flush();
            }
        }

        await Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (_fileWriter is not null)
            {
                _fileWriter.Flush();
                _fileWriter.Dispose();
            }
        }

        await ValueTask.CompletedTask;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(SynchronizedTestOutput));
        }
    }
}
