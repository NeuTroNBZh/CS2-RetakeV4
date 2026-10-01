using System.Text.Json;
using System.Text.RegularExpressions;

namespace RetakeV4.Integration.Tests.Localization;

public partial class LangFilesTests
{
    private static readonly string LangDirectory = Path.Combine(AppContext.BaseDirectory, "lang");

    [GeneratedRegex(@"^[a-z]+(\.[a-z0-9_]+)+$")]
    private static partial Regex KeyPattern();

    [GeneratedRegex(@"\{(\d+)\}")]
    private static partial Regex PlaceholderPattern();

    private static Dictionary<string, string> Load(string language) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(LangDirectory, $"{language}.json")))
        ?? throw new InvalidDataException($"{language}.json is empty");

    [Fact]
    public void EnglishAndFrench_HaveTheSameKeys()
    {
        Assert.Equal(Load("en").Keys.Order(), Load("fr").Keys.Order());
    }

    [Theory]
    [InlineData("en")]
    [InlineData("fr")]
    public void Keys_FollowModuleSectionKeyConvention(string language)
    {
        Assert.All(Load(language).Keys, key => Assert.Matches(KeyPattern(), key));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("fr")]
    public void AlertAndHelpPrefixes_DefaultToTheNormalPrefix(string language)
    {
        var texts = Load(language);
        Assert.Equal(texts["core.prefix"], texts["core.prefix_alert"]);
        Assert.Equal(texts["core.prefix"], texts["core.prefix_help"]);
    }

    [Fact]
    public void Placeholders_MatchAcrossLanguages()
    {
        var en = Load("en");
        var fr = Load("fr");
        foreach (var key in en.Keys)
        {
            var expected = PlaceholderPattern().Matches(en[key]).Select(m => m.Value).Order();
            var actual = PlaceholderPattern().Matches(fr[key]).Select(m => m.Value).Order();
            Assert.True(expected.SequenceEqual(actual), $"placeholder mismatch for '{key}'");
        }
    }

    [Theory]
    [InlineData("spawns.round.announce")]
    [InlineData("teams.queue.joined")]
    [InlineData("teams.switch.refused")]
    [InlineData("teams.move.switched_after_ct_win")]
    [InlineData("teams.move.entered_from_queue")]
    [InlineData("teams.move.scrambled")]
    [InlineData("teams.move.balanced")]
    [InlineData("teams.scramble.requested")]
    [InlineData("teams.round.t_streak")]
    [InlineData("teams.inconsistent")]
    [InlineData("teams.no_permission")]
    public void Phase2aKeys_ArePresent(string key)
    {
        Assert.Contains(key, Load("en").Keys);
    }

    [Theory]
    [InlineData("plant.fast.instructions")]
    [InlineData("plant.failed")]
    public void Phase2bPlantKeys_ArePresent(string key)
    {
        Assert.Contains(key, Load("en").Keys);
    }

    [Theory]
    [InlineData("instadefuse.blocked.he")]
    [InlineData("instadefuse.blocked.molotov")]
    [InlineData("instadefuse.blocked.inferno")]
    [InlineData("instadefuse.not_enough_time")]
    [InlineData("instadefuse.success")]
    public void Phase2bInstaDefuseKeys_ArePresent(string key)
    {
        Assert.Contains(key, Load("en").Keys);
    }

    [Fact]
    public void Phase1Keys_ArePresent()
    {
        var en = Load("en");
        Assert.Contains("core.prefix", en.Keys);
        Assert.Contains("core.info.version", en.Keys);
        Assert.Contains("core.warmup.forced_end", en.Keys);
    }

    [Theory]
    [InlineData("allocation.awp.enabled")]
    [InlineData("allocation.awp.disabled")]
    [InlineData("allocation.import.done")]
    [InlineData("allocation.import.failed")]
    [InlineData("allocation.no_permission")]
    public void Phase3aKeys_ArePresent(string key)
    {
        Assert.Contains(key, Load("en").Keys);
    }

    [Theory]
    [InlineData("hud.menu.back")]
    [InlineData("hud.menu.close")]
    [InlineData("hud.menu.next")]
    [InlineData("hud.menu.previous")]
    [InlineData("hud.menu.on")]
    [InlineData("hud.menu.off")]
    [InlineData("hud.round.title")]
    [InlineData("hud.round.teams")]
    [InlineData("hud.round.streak")]
    [InlineData("hud.queue.position")]
    [InlineData("hud.queue.priority")]
    [InlineData("allocation.menu.title")]
    [InlineData("allocation.menu.current")]
    [InlineData("allocation.menu.summary")]
    [InlineData("allocation.menu.team_t")]
    [InlineData("allocation.menu.team_ct")]
    [InlineData("allocation.menu.config")]
    [InlineData("allocation.menu.primary")]
    [InlineData("allocation.menu.secondary")]
    [InlineData("allocation.menu.awp")]
    [InlineData("allocation.menu.applied_now")]
    [InlineData("allocation.menu.applied_next_round")]
    [InlineData("allocation.awp.received")]
    public void Phase3bKeys_ArePresent(string key)
    {
        Assert.Contains(key, Load("en").Keys);
    }

    [Theory]
    [InlineData("spawns.editor.entered")]
    [InlineData("spawns.editor.left")]
    [InlineData("spawns.editor.saved")]
    [InlineData("spawns.editor.save_failed")]
    [InlineData("spawns.editor.reloaded")]
    [InlineData("spawns.editor.added")]
    [InlineData("spawns.editor.deleted")]
    [InlineData("spawns.editor.updated")]
    [InlineData("spawns.editor.none_nearby")]
    [InlineData("spawns.editor.teleported")]
    [InlineData("spawns.editor.not_found")]
    [InlineData("spawns.editor.noclip_on")]
    [InlineData("spawns.editor.noclip_off")]
    [InlineData("spawns.editor.usage_add")]
    [InlineData("spawns.editor.usage_tp")]
    [InlineData("spawns.editor.usage_teleport")]
    [InlineData("spawns.editor.usage_edit")]
    [InlineData("spawns.editor.unsaved")]
    [InlineData("spawns.editor.player_only")]
    [InlineData("spawns.editor.no_permission")]
    [InlineData("spawns.editor.menu.title")]
    [InlineData("spawns.editor.menu.add")]
    [InlineData("spawns.editor.menu.nearest")]
    [InlineData("spawns.editor.menu.teleport")]
    [InlineData("spawns.editor.menu.save")]
    [InlineData("spawns.editor.menu.reload")]
    [InlineData("spawns.editor.menu.noclip")]
    [InlineData("spawns.editor.menu.exit")]
    [InlineData("spawns.editor.menu.exit_save")]
    [InlineData("spawns.editor.menu.exit_discard")]
    [InlineData("spawns.editor.menu.delete")]
    [InlineData("spawns.editor.menu.set_team")]
    [InlineData("spawns.editor.menu.set_site")]
    [InlineData("spawns.editor.menu.can_plant")]
    [InlineData("spawns.forcesite.set_once")]
    [InlineData("spawns.forcesite.set_sticky")]
    [InlineData("spawns.forcesite.cleared")]
    [InlineData("spawns.forcesite.usage")]
    [InlineData("spawns.forcesite.no_spawns")]
    [InlineData("spawns.missing.admin")]
    [InlineData("spawns.editor.not_editing")]
    public void Phase4aKeys_ArePresent(string key)
    {
        Assert.Contains(key, Load("en").Keys);
    }

    [Theory]
    [InlineData("admin.menu.title")]
    [InlineData("admin.menu.editor")]
    [InlineData("admin.menu.forcesite")]
    [InlineData("admin.menu.force_once")]
    [InlineData("admin.menu.force_sticky")]
    [InlineData("admin.menu.force_off")]
    [InlineData("admin.menu.scramble")]
    [InlineData("admin.no_permission")]
    [InlineData("admin.usage")]
    [InlineData("admin.player_only")]
    public void Phase4bKeys_ArePresent(string key)
    {
        Assert.Contains(key, Load("en").Keys);
    }

    [Theory]
    [InlineData("allocation.buy.auto_managed")]
    [InlineData("allocation.buy.not_available")]
    [InlineData("allocation.buy.awp_volunteer")]
    [InlineData("allocation.buy.freeze_only")]
    [InlineData("allocation.howto.menu")]
    [InlineData("allocation.howto.native")]
    [InlineData("allocation.howto.both")]
    public void Phase5aKeys_ArePresent(string key)
    {
        Assert.Contains(key, Load("en").Keys);
    }
}
