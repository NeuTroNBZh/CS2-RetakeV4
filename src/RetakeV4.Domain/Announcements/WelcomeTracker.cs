using System.Collections.Immutable;

namespace RetakeV4.Domain.Announcements;

// The welcome message is sent once per session (connection to disconnection), whatever the team or map changes.
public sealed record WelcomeTracker(ImmutableHashSet<ulong> Greeted)
{
    public static WelcomeTracker Empty { get; } = new(ImmutableHashSet<ulong>.Empty);

    public (WelcomeTracker Next, bool Greet) Joined(ulong steamId) =>
        Greeted.Contains(steamId) ? (this, false) : (this with { Greeted = Greeted.Add(steamId) }, true);

    public WelcomeTracker Left(ulong steamId) => this with { Greeted = Greeted.Remove(steamId) };
}
