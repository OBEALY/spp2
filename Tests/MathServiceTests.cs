using MiniTestFramework;
using TestedProject;

namespace Tests;

[TestClass]
[UseSharedContext(typeof(AppSharedContext))]
public sealed class MathServiceTests
{
    private MathService _math = null!;
    private TextService _text = null!;

    private int _beforeEachCalls;
    private int _afterEachCalls;

    public AppSharedContext? SharedContext { get; private set; }

    [BeforeAll]
    private void Init()
    {
        _math = new MathService();
        _text = new TextService();
        AssertEx.NotNull(_math);
        AssertEx.NotNull(_text);
    }

    [AfterAll]
    private void Done()
    {
        AssertEx.Greater(_beforeEachCalls, 0);
        AssertEx.Equal(_beforeEachCalls, _afterEachCalls);
    }

    [BeforeEach]
    private void SetUp()
    {
        Interlocked.Increment(ref _beforeEachCalls);
    }

    [AfterEach]
    private void TearDown()
    {
        Interlocked.Increment(ref _afterEachCalls);
    }

    [Test]
    [TestInfo("Simple add test", priority: 1)]
    public void Add_ShouldReturnSum()
    {
        var result = _math.Add(2, 3);
        AssertEx.Equal(5, result);
        AssertEx.NotEqual(6, result);
        AssertEx.Greater(result, 4);
        AssertEx.Less(result, 10);
    }

    [TestCase(5, 3, 2)]
    [TestCase(2, 7, -5)]
    public void Subtract_ShouldSupportMultipleCases(int left, int right, int expected)
    {
        var result = _math.Subtract(left, right);
        AssertEx.Equal(expected, result);
        AssertEx.IsType<int>(result);
    }

    [Test]
    public async Task DivideByZero_ShouldThrow()
    {
        await AssertEx.ThrowsAsync<DivideByZeroException>(() =>
            Task.Run(() => _math.Divide(10, 0)));
    }

    [Test]
    public async Task MultiplyAsync_ShouldWork()
    {
        var result = await _math.MultiplyAsync(4, 6);
        AssertEx.Equal(24, result);
    }

    [Test]
    public void TextAssertions_ShouldWork()
    {
        var text = _text.Join("hello", "world");
        AssertEx.Contains("hello", text);
        AssertEx.DoesNotContain("bye", text);
        AssertEx.True(text.StartsWith("hello", StringComparison.Ordinal));
        AssertEx.False(text.EndsWith("nope", StringComparison.Ordinal));
    }

    [Test]
    public void NullAndSequenceAssertions_ShouldWork()
    {
        var maybeNull = _text.MaybeNull(true);
        var notNull = _text.MaybeNull(false);
        var seq = _text.Range(1, 3);

        AssertEx.Null(maybeNull);
        AssertEx.NotNull(notNull);
        AssertEx.SequenceEqual(new[] { 1, 2, 3 }, seq);
    }

    [Test]
    public void SharedContext_ShouldBeInjected()
    {
        AssertEx.NotNull(SharedContext);
        AssertEx.Equal(42, SharedContext!.Seed);
        AssertEx.True(SharedContext.CreatedAtUtc <= DateTime.UtcNow);
    }

    [Test]
    [TestInfo("Intentional failure to demonstrate failed assertion handling", priority: 99)]
    public void FailureDemo_ShouldBeReportedAsFailed()
    {
        AssertEx.True(false, "Intentional failure for demo.");
    }
}
