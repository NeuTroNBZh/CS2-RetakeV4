using System.Text.Encodings.Web;
using System.Text.Json;

namespace RetakeV4.Domain.Remote;

public sealed record RemotePlayer(int UserId, string Name, string Team, bool Alive, bool Bot);

public sealed record ServerStateSnapshot(
    string Map, string Phase, bool Warmup, bool Paused, int Round, int MaxRounds, int ScoreT, int ScoreCt,
    string? Site, string? RoundType, IReadOnlyList<RemotePlayer> Players, int Queue, bool VoteOpen, string? NextMap,
    bool SpawnEditor, bool CleanupEditor);

// One console line read by remote tools (Retake Deck over RCON). JSON still escapes quotes and control characters,
// so a player name can never break the line or the document; other characters stay UTF-8 to keep the line short.
public static class ServerStateFormat
{
    public const string Prefix = "RETAKE_STATE ";
    public const int Version = 1;

    private static readonly JsonSerializerOptions Options = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string Format(ServerStateSnapshot s) => Prefix + JsonSerializer.Serialize(new
    {
        v = Version,
        map = s.Map,
        phase = s.Phase,
        warmup = s.Warmup,
        paused = s.Paused,
        round = s.Round,
        maxRounds = s.MaxRounds,
        score = new { t = s.ScoreT, ct = s.ScoreCt },
        site = s.Site,
        roundType = s.RoundType,
        players = s.Players.Select(p => new { userId = p.UserId, name = p.Name, team = p.Team, alive = p.Alive, bot = p.Bot }),
        queue = s.Queue,
        vote = new { open = s.VoteOpen, nextMap = s.NextMap },
        editors = new { spawns = s.SpawnEditor, cleanup = s.CleanupEditor },
    }, Options);
}
