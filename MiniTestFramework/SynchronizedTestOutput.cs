namespace MiniTestFramework;

public interface ITestOutput : IAsyncDisposable
{
    Task WriteLineAsync(string line, CancellationToken cancellationToken = default);
}

public sealed class SynchronizedTestOutput : ITestOutput
{
    private readonly SemaphoreSlim _gate = new(1, 1);
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
        ThrowIfDisposed();

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_writeToConsole)
            {
                Console.WriteLine(line);
            }

            if (_fileWriter is not null)
            {
                await _fileWriter.WriteLineAsync(line);
                await _fileWriter.FlushAsync();
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        await _gate.WaitAsync();
        try
        {
            if (_fileWriter is not null)
            {
                await _fileWriter.FlushAsync();
                await _fileWriter.DisposeAsync();
            }
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(SynchronizedTestOutput));
        }
    }
}
