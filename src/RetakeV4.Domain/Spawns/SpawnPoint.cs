using RetakeV4.Domain.Common;
using RetakeV4.Domain.Geometry;

namespace RetakeV4.Domain.Spawns;

public sealed record SpawnPoint(Guid Id, TeamSide Team, BombSite Site, bool CanPlant, Vec3 Position, ViewAngles Angle);

public sealed record SpawnFileResult(IReadOnlyList<SpawnPoint> Spawns, bool IsLegacyFormat, IReadOnlyList<string> Issues);
