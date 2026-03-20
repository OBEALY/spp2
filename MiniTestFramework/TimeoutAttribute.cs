namespace MiniTestFramework;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class TimeoutAttribute : Attribute
{
    public TimeoutAttribute(int milliseconds)
    {
        if (milliseconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(milliseconds), "Timeout must be greater than zero.");
        }

        Milliseconds = milliseconds;
    }

    public int Milliseconds { get; }
}
