using RetakeV4.Domain.Modules;
using RetakeV4.Domain.Rounds;

namespace RetakeV4.Domain.Tests.Rounds;

public class PreparationPipelineTests
{
    private sealed class Step(string name, int order, Func<PreparationContext, PreparationContext> run, List<string> log) : IPreparationStep
    {
        public string Name { get; } = name;
        public int Order { get; } = order;

        public PreparationContext Execute(PreparationContext context)
        {
            log.Add(Name);
            return run(context);
        }
    }

    private readonly List<GuardFailure> _failures = new();
    private readonly List<string> _log = new();

    [Fact]
    public void Execute_RunsStepsByOrderThenRegistration()
    {
        var pipeline = new PreparationPipeline(new ModuleGuard(5, _failures.Add));
        pipeline.Register("Spawns", new Step("site", 20, c => c, _log));
        pipeline.Register("RoundTypes", new Step("type", 10, c => c, _log));
        pipeline.Register("Teams", new Step("teams", 20, c => c, _log));
        pipeline.Execute(new PreparationContext(1));
        Assert.Equal(new[] { "type", "site", "teams" }, _log);
    }

    [Fact]
    public void Execute_PassesContextAlongTheChain()
    {
        var pipeline = new PreparationPipeline(new ModuleGuard(5, _failures.Add));
        pipeline.Register("A", new Step("a", 1, c => c with { RoundNumber = c.RoundNumber + 10 }, _log));
        pipeline.Register("B", new Step("b", 2, c => c with { RoundNumber = c.RoundNumber * 2 }, _log));
        Assert.Equal(22, pipeline.Execute(new PreparationContext(1)).RoundNumber);
    }

    [Fact]
    public void FailingStep_IsSkipped_AndReportedAgainstItsModule()
    {
        var pipeline = new PreparationPipeline(new ModuleGuard(5, _failures.Add));
        pipeline.Register("Broken", new Step("broken", 1, _ => throw new InvalidOperationException(), _log));
        pipeline.Register("Ok", new Step("ok", 2, c => c with { RoundNumber = 99 }, _log));
        Assert.Equal(99, pipeline.Execute(new PreparationContext(1)).RoundNumber);
        var failure = Assert.Single(_failures);
        Assert.Equal("Broken", failure.Module);
        Assert.Equal("prepare:broken", failure.Stage);
    }

    [Fact]
    public void DisposedRegistration_IsRemoved()
    {
        var pipeline = new PreparationPipeline(new ModuleGuard(5, _failures.Add));
        var registration = pipeline.Register("A", new Step("a", 1, c => c, _log));
        registration.Dispose();
        pipeline.Execute(new PreparationContext(1));
        Assert.Empty(_log);
    }
}
