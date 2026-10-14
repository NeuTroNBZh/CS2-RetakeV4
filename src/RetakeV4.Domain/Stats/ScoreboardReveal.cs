using RetakeV4.Domain.Hud;

namespace RetakeV4.Domain.Stats;

// The client shows the rank column only at round end unless the server sends ServerRankRevealAll when the player opens
// the scoreboard: held buttons are polled and only a fresh press of the scoreboard button asks for a reveal.
public sealed class ScoreboardReveal(ulong scoreboardButton)
{
    private readonly Dictionary<int, ulong> _held = new();

    public bool Pressed(int slot, ulong held)
    {
        var previous = _held.GetValueOrDefault(slot);
        _held[slot] = held;
        return (ButtonEdges.Pressed(previous, held) & scoreboardButton) != 0;
    }

    public void Forget(int slot) => _held.Remove(slot);

    public void Clear() => _held.Clear();
}
