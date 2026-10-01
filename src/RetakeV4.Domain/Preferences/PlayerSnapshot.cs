namespace RetakeV4.Domain.Preferences;

// A player's stored rows with their stamp (latest updated_at, compared for equality only).
public sealed record PlayerSnapshot(IReadOnlyList<StoredPreference> Preferences, string Stamp);
