namespace RetakeV4.Domain.Rounds;

// The server's own configs (server.cfg, gamemode_*.cfg) run after the plugin's map start and override the retake cvars:
// the retake config is executed once more on the map's first round start, after them.
public sealed record MapConfigGate(bool Pending)
{
    public static MapConfigGate Idle { get; } = new(false);

    public MapConfigGate MapStarted() => new(true);

    public (MapConfigGate Gate, bool Execute) RoundStarted() => Pending ? (Idle, true) : (this, false);
}
