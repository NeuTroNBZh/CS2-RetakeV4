using System.Globalization;

namespace RetakeV4.Domain.MapCleanup;

// Stable across rounds: entities are recreated at every round restart, so the entity index cannot be used.
public static class EntityKey
{
    public static string For(EntityFacts facts, bool nameIsUnique)
    {
        if (nameIsUnique && !string.IsNullOrWhiteSpace(facts.TargetName))
        {
            return $"name:{facts.TargetName}";
        }
        var o = facts.Origin;
        return string.Create(CultureInfo.InvariantCulture,
            $"pos:{facts.ClassName.ToLowerInvariant()}@{Round(o.X)},{Round(o.Y)},{Round(o.Z)}");
    }

    // Away from zero so x.5 does not depend on parity; the cast drops negative zero.
    private static int Round(float value) => (int)MathF.Round(value, MidpointRounding.AwayFromZero);
}
