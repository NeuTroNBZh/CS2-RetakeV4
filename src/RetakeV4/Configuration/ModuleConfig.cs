namespace RetakeV4.Configuration;

public abstract record ModuleConfig
{
    public int Version { get; init; }

    public bool Enabled { get; init; } = true;

    public bool Debug { get; init; }
}
