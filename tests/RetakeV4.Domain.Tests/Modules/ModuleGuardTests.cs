using RetakeV4.Domain.Modules;

namespace RetakeV4.Domain.Tests.Modules;

public class ModuleGuardTests
{
    private readonly List<GuardFailure> _failures = new();

    [Fact]
    public void Run_ReturnsActionResult()
    {
        var guard = new ModuleGuard(3, _failures.Add);
        Assert.Equal(42, guard.Run("Core", "stage", () => 42, -1));
        Assert.Empty(_failures);
    }

    [Fact]
    public void Run_CatchesAndReturnsFallback()
    {
        var guard = new ModuleGuard(3, _failures.Add);
        var result = guard.Run<int>("Core", "round_start", () => throw new InvalidOperationException(), -1);
        Assert.Equal(-1, result);
        var failure = Assert.Single(_failures);
        Assert.Equal("round_start", failure.Stage);
        Assert.False(failure.ModuleDisabled);
    }

    [Fact]
    public void ReachingBudget_DisablesModule_AndSkipsFurtherRuns()
    {
        var guard = new ModuleGuard(2, _failures.Add);
        var executions = 0;
        void Failing()
        {
            executions++;
            throw new InvalidOperationException();
        }
        guard.Run("Plant", "s", Failing);
        guard.Run("Plant", "s", Failing);
        guard.Run("Plant", "s", Failing);
        Assert.Equal(2, executions);
        Assert.True(guard.IsDisabled("Plant"));
        Assert.True(_failures[^1].ModuleDisabled);
    }

    [Fact]
    public void ResetRound_ClearsCounts_ButKeepsDisabledModules()
    {
        var guard = new ModuleGuard(2, _failures.Add);
        void Failing() => throw new InvalidOperationException();
        guard.Run("A", "s", Failing);
        guard.Run("B", "s", Failing);
        guard.Run("B", "s", Failing);
        guard.ResetRound();
        guard.Run("A", "s", Failing);
        Assert.False(guard.IsDisabled("A"));
        Assert.True(guard.IsDisabled("B"));
    }

    [Fact]
    public void ErrorsAreCountedPerModule()
    {
        var budget = ErrorBudget.Create(2).RecordError("A").RecordError("B");
        Assert.False(budget.IsDisabled("A"));
        Assert.False(budget.IsDisabled("B"));
    }

    [Fact]
    public void Budget_RejectsNonPositiveMax()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ErrorBudget.Create(0));
    }

    [Fact]
    public void Disable_StopsRunningTheModule()
    {
        var guard = new ModuleGuard(5, _ => { });
        guard.Disable("Allocation");
        var ran = false;
        guard.Run("Allocation", "test", () => ran = true);
        Assert.False(ran);
        Assert.True(guard.IsDisabled("Allocation"));
        Assert.False(guard.IsDisabled("Core"));
    }

    [Fact]
    public void SlowRun_IsReported_WithItsModuleStageAndDuration()
    {
        var now = TimeSpan.Zero;
        var slow = new List<SlowRun>();
        var guard = new ModuleGuard(3, _failures.Add, new SlowRunReporter(TimeSpan.FromMilliseconds(50), () => now, slow.Add));
        guard.Run("Teams", "round_end", () => now += TimeSpan.FromMilliseconds(80));
        Assert.Equal(new[] { new SlowRun("Teams", "round_end", TimeSpan.FromMilliseconds(80)) }, slow);
    }

    [Fact]
    public void FastRun_IsNotReported()
    {
        var now = TimeSpan.Zero;
        var slow = new List<SlowRun>();
        var guard = new ModuleGuard(3, _failures.Add, new SlowRunReporter(TimeSpan.FromMilliseconds(50), () => now, slow.Add));
        guard.Run("Teams", "round_end", () => now += TimeSpan.FromMilliseconds(10));
        Assert.Empty(slow);
    }

    [Fact]
    public void SlowFailingRun_IsReportedToo()
    {
        var now = TimeSpan.Zero;
        var slow = new List<SlowRun>();
        var guard = new ModuleGuard(3, _failures.Add, new SlowRunReporter(TimeSpan.FromMilliseconds(50), () => now, slow.Add));
        guard.Run("Teams", "round_end", () =>
        {
            now += TimeSpan.FromMilliseconds(60);
            throw new InvalidOperationException();
        });
        Assert.Single(slow);
        Assert.Single(_failures);
    }
}
