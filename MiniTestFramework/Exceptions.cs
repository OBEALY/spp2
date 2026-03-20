namespace MiniTestFramework;

public class TestFrameworkException(string message, Exception? inner = null) : Exception(message, inner)
{
}

public sealed class TestDiscoveryException(string message, Exception? inner = null) : TestFrameworkException(message, inner)
{
}

public sealed class AssertionFailedException(string message) : TestFrameworkException(message)
{
}

public sealed class TestExecutionException(string message, Exception? inner = null) : TestFrameworkException(message, inner)
{
}

public sealed class TestTimeoutException(string message) : TestFrameworkException(message)
{
}
