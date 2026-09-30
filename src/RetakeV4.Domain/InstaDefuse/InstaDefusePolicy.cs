namespace RetakeV4.Domain.InstaDefuse;

public sealed record InstaDefuseRules(bool RequireNoTAlive, bool BlockOnHe, bool BlockOnMolotov, bool BlockOnInferno, bool ForceExplodeIfNoTime);

public sealed record DefuseSituation(bool TerroristsAlive, float SecondsUntilExplosion, float DefuseLength, bool DefuserHasKit);

public enum ThreatKind
{
    He,
    Molotov,
    Inferno,
}

public abstract record InstaDefuseDecision
{
    public sealed record NotApplicable : InstaDefuseDecision;

    public sealed record Blocked(ThreatKind Threat) : InstaDefuseDecision;

    public sealed record NotEnoughTime(float MissingSeconds, bool ForceExplode) : InstaDefuseDecision;

    public sealed record Allowed(float SecondsLeft) : InstaDefuseDecision;
}

public static class InstaDefusePolicy
{
    private const float KitDefuseSeconds = 5f;
    private const float NoKitDefuseSeconds = 10f;

    public static InstaDefuseDecision Evaluate(InstaDefuseRules rules, ThreatState threats, DefuseSituation situation)
    {
        if (rules.RequireNoTAlive && situation.TerroristsAlive)
        {
            return new InstaDefuseDecision.NotApplicable();
        }
        if (ActiveThreat(rules, threats) is { } threat)
        {
            return new InstaDefuseDecision.Blocked(threat);
        }
        var defuseLength = situation.DefuseLength is KitDefuseSeconds or NoKitDefuseSeconds
            ? situation.DefuseLength
            : situation.DefuserHasKit ? KitDefuseSeconds : NoKitDefuseSeconds;
        var spare = situation.SecondsUntilExplosion - defuseLength;
        return spare < 0f
            ? new InstaDefuseDecision.NotEnoughTime(-spare, rules.ForceExplodeIfNoTime)
            : new InstaDefuseDecision.Allowed(situation.SecondsUntilExplosion);
    }

    private static ThreatKind? ActiveThreat(InstaDefuseRules rules, ThreatState threats)
    {
        if (rules.BlockOnHe && threats.HeInFlight > 0)
        {
            return ThreatKind.He;
        }
        if (rules.BlockOnMolotov && threats.MolotovInFlight > 0)
        {
            return ThreatKind.Molotov;
        }
        return rules.BlockOnInferno && threats.NearInfernos.Count > 0 ? ThreatKind.Inferno : null;
    }
}
