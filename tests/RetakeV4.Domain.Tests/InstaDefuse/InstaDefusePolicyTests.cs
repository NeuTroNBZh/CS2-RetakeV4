using RetakeV4.Domain.Geometry;
using RetakeV4.Domain.InstaDefuse;

namespace RetakeV4.Domain.Tests.InstaDefuse;

public class InstaDefusePolicyTests
{
    private static readonly InstaDefuseRules Rules = new(RequireNoTAlive: true, BlockOnHe: true, BlockOnMolotov: true, BlockOnInferno: true, ForceExplodeIfNoTime: true);

    private static DefuseSituation Situation(bool tAlive = false, float secondsLeft = 20f, float defuseLength = 10f, bool kit = false) =>
        new(tAlive, secondsLeft, defuseLength, kit);

    [Fact]
    public void TerroristsAlive_IsNotApplicable() =>
        Assert.IsType<InstaDefuseDecision.NotApplicable>(InstaDefusePolicy.Evaluate(Rules, ThreatState.Empty, Situation(tAlive: true)));

    [Fact]
    public void TerroristsAlive_WithoutRequirement_IsEvaluated() =>
        Assert.IsType<InstaDefuseDecision.Allowed>(InstaDefusePolicy.Evaluate(Rules with { RequireNoTAlive = false }, ThreatState.Empty, Situation(tAlive: true)));

    [Fact]
    public void EnoughTime_IsAllowed_WithSecondsLeft()
    {
        var decision = Assert.IsType<InstaDefuseDecision.Allowed>(InstaDefusePolicy.Evaluate(Rules, ThreatState.Empty, Situation(secondsLeft: 12.5f)));
        Assert.Equal(12.5f, decision.SecondsLeft);
    }

    [Theory]
    [InlineData("hegrenade", ThreatKind.He)]
    [InlineData("molotov", ThreatKind.Molotov)]
    public void GrenadeInFlight_Blocks(string weapon, ThreatKind expected)
    {
        var decision = InstaDefusePolicy.Evaluate(Rules, ThreatState.Empty.GrenadeThrown(weapon), Situation());
        Assert.Equal(expected, Assert.IsType<InstaDefuseDecision.Blocked>(decision).Threat);
    }

    [Fact]
    public void FireNearBomb_Blocks()
    {
        var threats = ThreatState.Empty.InfernoStarted(1, new Vec3(0, 0, 0), new Vec3(10, 0, 0), 250f);
        Assert.Equal(ThreatKind.Inferno, Assert.IsType<InstaDefuseDecision.Blocked>(InstaDefusePolicy.Evaluate(Rules, threats, Situation())).Threat);
    }

    [Fact]
    public void DisabledBlocks_AreIgnored()
    {
        var rules = Rules with { BlockOnHe = false, BlockOnMolotov = false, BlockOnInferno = false };
        var threats = ThreatState.Empty.GrenadeThrown("hegrenade").GrenadeThrown("molotov")
            .InfernoStarted(1, new Vec3(0, 0, 0), new Vec3(0, 0, 0), 250f);
        Assert.IsType<InstaDefuseDecision.Allowed>(InstaDefusePolicy.Evaluate(rules, threats, Situation()));
    }

    [Fact]
    public void NotEnoughTime_ReportsMissingSeconds_AndForcesExplosionWhenConfigured()
    {
        var decision = Assert.IsType<InstaDefuseDecision.NotEnoughTime>(InstaDefusePolicy.Evaluate(Rules, ThreatState.Empty, Situation(secondsLeft: 7f)));
        Assert.Equal(3f, decision.MissingSeconds, 3);
        Assert.True(decision.ForceExplode);
    }

    [Fact]
    public void NotEnoughTime_WithoutForceExplode_OnlyReports()
    {
        var decision = InstaDefusePolicy.Evaluate(Rules with { ForceExplodeIfNoTime = false }, ThreatState.Empty, Situation(secondsLeft: 7f));
        Assert.False(Assert.IsType<InstaDefuseDecision.NotEnoughTime>(decision).ForceExplode);
    }

    [Fact]
    public void Kit_ShortensDefuse_WhenEngineLengthIsUnusual()
    {
        var withKit = InstaDefusePolicy.Evaluate(Rules, ThreatState.Empty, Situation(secondsLeft: 7f, defuseLength: 0f, kit: true));
        var withoutKit = InstaDefusePolicy.Evaluate(Rules, ThreatState.Empty, Situation(secondsLeft: 7f, defuseLength: 0f, kit: false));
        Assert.IsType<InstaDefuseDecision.Allowed>(withKit);
        Assert.IsType<InstaDefuseDecision.NotEnoughTime>(withoutKit);
    }

    [Fact]
    public void EngineDefuseLength_IsTrusted_WhenStandard()
    {
        var decision = InstaDefusePolicy.Evaluate(Rules, ThreatState.Empty, Situation(secondsLeft: 7f, defuseLength: 5f, kit: false));
        Assert.IsType<InstaDefuseDecision.Allowed>(decision);
    }
}
