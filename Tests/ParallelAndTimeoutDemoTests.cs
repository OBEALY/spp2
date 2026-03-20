using MiniTestFramework;

namespace Tests;

[TestClass]
public sealed class ParallelAndTimeoutDemoTests
{
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    [TestCase(5)]
    [TestCase(6)]
    [TestCase(7)]
    [TestCase(8)]
    [Timeout(1_000)]
    public async Task SlowCase_ShouldPassWithinTimeout(int seed)
    {
        await Task.Delay(350 + seed);
        AssertEx.True(seed > 0);
    }

    [Test]
    [Timeout(150)]
    public async Task SlowCase_ShouldBeMarkedAsTimeout()
    {
        await Task.Delay(600);
    }
}
