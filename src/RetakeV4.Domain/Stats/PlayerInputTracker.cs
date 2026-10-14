using RetakeV4.Domain.Hud;

namespace RetakeV4.Domain.Stats;

public readonly record struct InputPresses(bool Scoreboard, bool Jumped);

// Held buttons are polled every tick: CS2 sends no event for the scoreboard key, and player_jump does not reach plugins.
// A press is a button held now and not on the previous poll. A jump is a jump press while on the ground, now or on the
// previous poll (the engine may already have applied the jump when the press is read).
public sealed class PlayerInputTracker(ulong scoreboardButton, ulong jumpButton)
{
    private readonly Dictionary<int, (ulong Held, bool OnGround)> _previous = new();

    public InputPresses Update(int slot, ulong held, bool onGround)
    {
        var (previousHeld, wasOnGround) = _previous.TryGetValue(slot, out var previous) ? previous : (0UL, onGround);
        _previous[slot] = (held, onGround);
        var pressed = ButtonEdges.Pressed(previousHeld, held);
        return new InputPresses((pressed & scoreboardButton) != 0, (pressed & jumpButton) != 0 && (onGround || wasOnGround));
    }

    public void Forget(int slot) => _previous.Remove(slot);

    public void Clear() => _previous.Clear();
}
