using RetakeV4.Domain.Common;

namespace RetakeV4.Domain.Events;

public sealed record SpawnEditorRequested(PlayerId Player);

// Active = true when the first editor enters (game paused in warmup), false when the last one leaves.
public sealed record SpawnEditorStateChanged(bool Active);
