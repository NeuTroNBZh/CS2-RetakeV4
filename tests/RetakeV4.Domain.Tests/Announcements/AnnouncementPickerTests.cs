using RetakeV4.Domain.Announcements;
using RetakeV4.Domain.Tests.TestDoubles;

namespace RetakeV4.Domain.Tests.Announcements;

public class AnnouncementPickerTests
{
    private static readonly Dictionary<string, IReadOnlyList<string>> ByMap = new()
    {
        ["de_mirage"] = new[] { "mirage 1", "mirage 2" },
        ["de_nuke"] = new[] { "  " },
    };

    [Fact]
    public void MapWithItsOwnList_UsesIt_CaseInsensitively()
    {
        var picker = AnnouncementPicker.Create(new[] { "general" }, ByMap);
        Assert.Equal("mirage 2", picker.Pick("DE_MIRAGE", new FixedRandom(1)).Message);
    }

    [Fact]
    public void MapWithoutUsableList_UsesTheGeneralList()
    {
        var picker = AnnouncementPicker.Create(new[] { "general" }, ByMap);
        Assert.Equal("general", picker.Pick("de_nuke", new FixedRandom(0)).Message);
        Assert.Equal("general", picker.Pick("de_dust2", new FixedRandom(0)).Message);
    }

    [Fact]
    public void SameMessage_NeverTwiceInARow_WhenThereIsAChoice()
    {
        var picker = AnnouncementPicker.Create(new[] { "a", "b" }, new Dictionary<string, IReadOnlyList<string>>());
        var random = new FixedRandom(0);
        var (next, first) = picker.Pick("de_x", random);
        var (_, second) = next.Pick("de_x", random);
        Assert.Equal("a", first);
        Assert.Equal("b", second);
    }

    [Fact]
    public void SingleMessage_IsRepeated()
    {
        var picker = AnnouncementPicker.Create(new[] { "only" }, new Dictionary<string, IReadOnlyList<string>>());
        var (next, _) = picker.Pick("de_x", new FixedRandom(0));
        Assert.Equal("only", next.Pick("de_x", new FixedRandom(0)).Message);
    }

    [Fact]
    public void NoMessages_GivesNothing()
    {
        var picker = AnnouncementPicker.Create(new[] { " " }, new Dictionary<string, IReadOnlyList<string>>());
        Assert.True(picker.IsEmpty);
        Assert.Null(picker.Pick("de_x", new FixedRandom(0)).Message);
    }
}
