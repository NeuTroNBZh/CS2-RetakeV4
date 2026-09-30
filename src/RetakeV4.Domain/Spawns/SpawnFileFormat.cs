using System.Text.Json;
using System.Text.Json.Serialization;
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Geometry;

namespace RetakeV4.Domain.Spawns;

public static class SpawnFileFormat
{
    public const int CurrentSchemaVersion = 2;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private sealed record SpawnFileDto(int SchemaVersion, string Map, List<SpawnPoint>? Spawns);

    private sealed record LegacySpawnDto(
        Guid SpawnId, int Team, int BombSite, bool IsInBombZone,
        float PositionX, float PositionY, float PositionZ, float QAngleX, float QAngleY, float QAngleZ);

    public static SpawnFileResult Parse(string json)
    {
        try
        {
            return json.TrimStart().StartsWith('[') ? ParseLegacy(json) : ParseCurrent(json);
        }
        catch (JsonException ex)
        {
            return Failure($"invalid spawn file ({ex.Message})");
        }
    }

    public static string Serialize(string mapName, IReadOnlyList<SpawnPoint> spawns) =>
        JsonSerializer.Serialize(new SpawnFileDto(CurrentSchemaVersion, mapName, spawns.ToList()), Options);

    private static SpawnFileResult ParseCurrent(string json)
    {
        var dto = JsonSerializer.Deserialize<SpawnFileDto>(json, Options);
        if (dto is null)
        {
            return Failure("spawn file is empty");
        }
        if (dto.SchemaVersion > CurrentSchemaVersion)
        {
            return Failure($"unsupported spawn file schema version {dto.SchemaVersion}");
        }
        var issues = new List<string>();
        return new SpawnFileResult(Deduplicate(dto.Spawns ?? new List<SpawnPoint>(), issues), false, issues);
    }

    private static SpawnFileResult ParseLegacy(string json)
    {
        var dtos = JsonSerializer.Deserialize<List<LegacySpawnDto>>(json, Options) ?? new List<LegacySpawnDto>();
        var issues = new List<string>();
        var spawns = new List<SpawnPoint>();
        for (var i = 0; i < dtos.Count; i++)
        {
            var converted = Convert(dtos[i], i, issues);
            if (converted is not null)
            {
                spawns.Add(converted);
            }
        }
        return new SpawnFileResult(Deduplicate(spawns, issues), true, issues);
    }

    private static SpawnPoint? Convert(LegacySpawnDto dto, int index, List<string> issues)
    {
        TeamSide? team = dto.Team switch { 2 => TeamSide.T, 3 => TeamSide.CT, _ => null };
        BombSite? site = dto.BombSite switch { 0 => BombSite.A, 1 => BombSite.B, _ => null };
        if (team is null || site is null)
        {
            issues.Add($"spawn #{index}: invalid team {dto.Team} or bombsite {dto.BombSite}, skipped");
            return null;
        }
        return new SpawnPoint(
            dto.SpawnId == Guid.Empty ? Guid.NewGuid() : dto.SpawnId,
            team.Value, site.Value, dto.IsInBombZone,
            new Vec3(dto.PositionX, dto.PositionY, dto.PositionZ),
            new ViewAngles(dto.QAngleX, dto.QAngleY, dto.QAngleZ));
    }

    private static IReadOnlyList<SpawnPoint> Deduplicate(IEnumerable<SpawnPoint> spawns, List<string> issues)
    {
        var seen = new HashSet<Guid>();
        var kept = new List<SpawnPoint>();
        foreach (var spawn in spawns)
        {
            if (seen.Add(spawn.Id))
            {
                kept.Add(spawn);
                continue;
            }
            issues.Add($"duplicate spawn id {spawn.Id}, kept the first one");
        }
        return kept;
    }

    private static SpawnFileResult Failure(string issue) =>
        new(Array.Empty<SpawnPoint>(), false, new[] { issue });
}
