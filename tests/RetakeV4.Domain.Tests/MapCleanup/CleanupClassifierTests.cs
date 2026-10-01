using RetakeV4.Domain.Geometry;
using RetakeV4.Domain.MapCleanup;

namespace RetakeV4.Domain.Tests.MapCleanup;

public class CleanupClassifierTests
{
    private static EntityFacts Facts(string cls, string? model = null) => new(cls, model, null, new Vec3(0, 0, 0));

    [Theory]
    [InlineData("func_door", null, CleanupKind.Door)]
    [InlineData("func_door_rotating", null, CleanupKind.Door)]
    [InlineData("prop_door_rotating", "models/props/de_inferno/door.vmdl", CleanupKind.Door)]
    [InlineData("func_shatterglass", null, CleanupKind.Window)]
    [InlineData("func_breakable", "models/props_windows/glass_pane.vmdl", CleanupKind.Window)]
    [InlineData("func_breakable", "maps/de_mirage/WINDOW_frame.vmdl", CleanupKind.Window)]
    [InlineData("func_breakable", "models/props/de_nuke/vent_cover.vmdl", CleanupKind.Vent)]
    [InlineData("func_breakable", "models/props/metal_grate01.vmdl", CleanupKind.Vent)]
    [InlineData("func_breakable", "models/props/crate.vmdl", CleanupKind.Ignore)]
    [InlineData("func_breakable", null, CleanupKind.Ignore)]
    [InlineData("prop_physics", "models/props/glass_bottle.vmdl", CleanupKind.Ignore)]
    [InlineData("prop_physics_multiplayer", "models/vent.vmdl", CleanupKind.Ignore)]
    [InlineData("func_brush", "models/glass.vmdl", CleanupKind.Ignore)]
    [InlineData("prop_dynamic", "models/door.vmdl", CleanupKind.Ignore)]
    public void Detect_IsConservative(string cls, string? model, CleanupKind expected)
    {
        Assert.Equal(expected, CleanupClassifier.Detect(Facts(cls, model)));
    }

    [Fact]
    public void Override_AlwaysWins()
    {
        var overrides = new Dictionary<string, CleanupKind> { ["name:door_a"] = CleanupKind.Ignore, ["name:box"] = CleanupKind.Vent };
        Assert.Equal(CleanupKind.Ignore, CleanupClassifier.Classify(Facts("func_door"), "name:door_a", overrides));
        Assert.Equal(CleanupKind.Vent, CleanupClassifier.Classify(Facts("func_breakable", "crate"), "name:box", overrides));
        Assert.Equal(CleanupKind.Door, CleanupClassifier.Classify(Facts("func_door"), "name:other", overrides));
    }

    [Fact]
    public void CandidateClasses_AreTheFiveKnownClasses()
    {
        Assert.Equal(new[] { "func_breakable", "func_door", "func_door_rotating", "func_shatterglass", "prop_door_rotating" },
            CleanupClassifier.CandidateClasses.Order());
    }
}
