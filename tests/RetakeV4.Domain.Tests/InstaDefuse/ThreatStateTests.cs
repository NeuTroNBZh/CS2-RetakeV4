using RetakeV4.Domain.Geometry;
using RetakeV4.Domain.InstaDefuse;

namespace RetakeV4.Domain.Tests.InstaDefuse;

public class ThreatStateTests
{
    [Theory]
    [InlineData("hegrenade")]
    [InlineData("weapon_hegrenade")]
    public void HeThrown_IsTracked_UntilDetonation(string weapon)
    {
        var state = ThreatState.Empty.GrenadeThrown(weapon);
        Assert.Equal(1, state.HeInFlight);
        Assert.Equal(0, state.HeDetonated().HeInFlight);
    }

    [Theory]
    [InlineData("molotov")]
    [InlineData("incgrenade")]
    [InlineData("weapon_molotov")]
    public void FireGrenadeThrown_IsTracked_UntilDetonation(string weapon)
    {
        var state = ThreatState.Empty.GrenadeThrown(weapon);
        Assert.Equal(1, state.MolotovInFlight);
        Assert.Equal(0, state.MolotovDetonated().MolotovInFlight);
    }

    [Fact]
    public void OtherGrenades_AreIgnored() =>
        Assert.Equal(ThreatState.Empty, ThreatState.Empty.GrenadeThrown("flashbang"));

    [Fact]
    public void Detonations_NeverGoBelowZero()
    {
        var state = ThreatState.Empty.HeDetonated().MolotovDetonated();
        Assert.Equal(0, state.HeInFlight);
        Assert.Equal(0, state.MolotovInFlight);
    }

    [Fact]
    public void Inferno_NearTheBomb_IsTracked_UntilItEnds()
    {
        var state = ThreatState.Empty.InfernoStarted(7, new Vec3(100, 0, 0), new Vec3(0, 0, 0), 250f);
        Assert.Contains(7, state.NearInfernos);
        Assert.Empty(state.InfernoEnded(7).NearInfernos);
    }

    [Fact]
    public void Inferno_FarFromTheBomb_OrWithoutBomb_IsIgnored()
    {
        Assert.Empty(ThreatState.Empty.InfernoStarted(7, new Vec3(300, 0, 0), new Vec3(0, 0, 0), 250f).NearInfernos);
        Assert.Empty(ThreatState.Empty.InfernoStarted(7, new Vec3(0, 0, 0), null, 250f).NearInfernos);
    }
}
