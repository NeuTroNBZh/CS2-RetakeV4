using System.Collections.Immutable;

namespace RetakeV4.Domain.Hud;

// Slot keys sent by the server itself (weapon selection after a loadout) come back as client commands: they must not drive a menu.
public sealed record KeyMute(ImmutableDictionary<int, DateTimeOffset> Until)
{
    public static KeyMute Empty { get; } = new(ImmutableDictionary<int, DateTimeOffset>.Empty);

    public KeyMute Mute(int slot, DateTimeOffset now, TimeSpan duration) => new(Until.SetItem(slot, now + duration));

    public KeyMute Forget(int slot) => new(Until.Remove(slot));

    public bool IsMuted(int slot, DateTimeOffset now) => Until.TryGetValue(slot, out var until) && now < until;
}
