using System.Text.Json.Nodes;
using RetakeV4.Domain.Loadouts;

namespace RetakeV4.Domain.Tests.Loadouts;

public class CatalogExportTests
{
    private static readonly RoundTypeDefinition Pistol = new(
        "Pistol", ArmorKind.Kevlar,
        TeamWeapons.Empty,
        new TeamWeapons(new[] { "weapon_glock", "weapon_tec9" }, new[] { "weapon_usp_silencer", "weapon_p250" }, Array.Empty<string>()),
        new TeamDefault(null, "weapon_glock"), new TeamDefault(null, "weapon_usp_silencer"),
        new AwpSettings(false, 0, 0, 0), new DefuseKitSettings(DefuseKitMode.All, 0, 0, false), new ZeusSettings(false, 0), "Pistol");

    private static readonly RoundTypeDefinition FullBuy = new(
        "FullBuy", ArmorKind.KevlarHelmet,
        new TeamWeapons(new[] { "weapon_ak47", "weapon_sg556" }, new[] { "weapon_m4a1_silencer", "weapon_aug" }, new[] { "weapon_awp" }),
        new TeamWeapons(new[] { "weapon_glock" }, new[] { "weapon_usp_silencer" }, new[] { "weapon_deagle" }),
        new TeamDefault("weapon_ak47", "weapon_glock"), new TeamDefault("weapon_m4a1_silencer", "weapon_usp_silencer"),
        new AwpSettings(true, 1, 0, 100), new DefuseKitSettings(DefuseKitMode.All, 0, 0, false), new ZeusSettings(false, 0), "FullBuy");

    private static JsonNode Fixture() =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "contract", "catalog.v1.json")))!;

    [Fact]
    public void Build_MatchesTheContractFixture() =>
        Assert.True(JsonNode.DeepEquals(Fixture(), JsonNode.Parse(CatalogExport.Build(new[] { Pistol, FullBuy }))));

    [Fact]
    public void Build_NeverOffersTheAwpAsAPrimary() =>
        Assert.DoesNotContain("weapon_awp\"", CatalogExport.Build(new[] { FullBuy }).Replace("\"awp\"", string.Empty, StringComparison.Ordinal));

    [Fact]
    public void Build_WithoutRoundTypes_GivesAnEmptyList() =>
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""{"roundTypes":[]}"""), JsonNode.Parse(CatalogExport.Build(Array.Empty<RoundTypeDefinition>()))));

    [Fact]
    public void FormatVersion_IsOne() => Assert.Equal(1, CatalogExport.FormatVersion);
}
