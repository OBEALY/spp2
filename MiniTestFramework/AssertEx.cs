using System.Collections;

namespace MiniTestFramework;

public static class AssertEx
{
    public static void True(bool condition, string? message = null)
    {
        if (!condition)
        {
            throw new AssertionFailedException(message ?? "Expected condition to be true.");
        }
    }

    public static void False(bool condition, string? message = null)
    {
        if (condition)
        {
            throw new AssertionFailedException(message ?? "Expected condition to be false.");
        }
    }

    public static void Equal<T>(T expected, T actual, string? message = null)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new AssertionFailedException(message ?? $"Expected: {expected}. Actual: {actual}.");
        }
    }

    public static void NotEqual<T>(T notExpected, T actual, string? message = null)
    {
        if (EqualityComparer<T>.Default.Equals(notExpected, actual))
        {
            throw new AssertionFailedException(message ?? $"Did not expect: {notExpected}.");
        }
    }

    public static void Null(object? value, string? message = null)
    {
        if (value is not null)
        {
            throw new AssertionFailedException(message ?? "Expected value to be null.");
        }
    }

    public static void NotNull(object? value, string? message = null)
    {
        if (value is null)
        {
            throw new AssertionFailedException(message ?? "Expected value to be not null.");
        }
    }

    public static void Contains(string expectedSubstring, string actualText, string? message = null)
    {
        if (!actualText.Contains(expectedSubstring, StringComparison.Ordinal))
        {
            throw new AssertionFailedException(message ?? $"Expected '{actualText}' to contain '{expectedSubstring}'.");
        }
    }

    public static void DoesNotContain(string unexpectedSubstring, string actualText, string? message = null)
    {
        if (actualText.Contains(unexpectedSubstring, StringComparison.Ordinal))
        {
            throw new AssertionFailedException(message ?? $"Expected '{actualText}' to not contain '{unexpectedSubstring}'.");
        }
    }

    public static void Greater<T>(T left, T right, string? message = null) where T : IComparable<T>
    {
        if (left.CompareTo(right) <= 0)
        {
            throw new AssertionFailedException(message ?? $"Expected {left} to be greater than {right}.");
        }
    }

    public static void Less<T>(T left, T right, string? message = null) where T : IComparable<T>
    {
        if (left.CompareTo(right) >= 0)
        {
            throw new AssertionFailedException(message ?? $"Expected {left} to be less than {right}.");
        }
    }

    public static void SequenceEqual(IEnumerable expected, IEnumerable actual, string? message = null)
    {
        var expectedItems = expected.Cast<object?>().ToArray();
        var actualItems = actual.Cast<object?>().ToArray();

        if (expectedItems.Length != actualItems.Length)
        {
            throw new AssertionFailedException(message ?? $"Expected length {expectedItems.Length}, actual {actualItems.Length}.");
        }

        for (var i = 0; i < expectedItems.Length; i++)
        {
            if (!Equals(expectedItems[i], actualItems[i]))
            {
                throw new AssertionFailedException(message ?? $"Sequence mismatch at index {i}.");
            }
        }
    }

    public static async Task ThrowsAsync<TException>(Func<Task> action, string? message = null) where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException)
        {
            return;
        }
        catch (Exception ex)
        {
            throw new AssertionFailedException(message ?? $"Expected {typeof(TException).Name}, but got {ex.GetType().Name}.");
        }

        throw new AssertionFailedException(message ?? $"Expected exception {typeof(TException).Name}, but no exception was thrown.");
    }

    public static void Throws<TException>(Action action, string? message = null) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }
        catch (Exception ex)
        {
            throw new AssertionFailedException(message ?? $"Expected {typeof(TException).Name}, but got {ex.GetType().Name}.");
        }

        throw new AssertionFailedException(message ?? $"Expected exception {typeof(TException).Name}, but no exception was thrown.");
    }

    public static void IsType<TExpected>(object value, string? message = null)
    {
        if (value.GetType() != typeof(TExpected))
        {
            throw new AssertionFailedException(message ?? $"Expected type {typeof(TExpected).Name}, actual {value.GetType().Name}.");
        }
    }
}
