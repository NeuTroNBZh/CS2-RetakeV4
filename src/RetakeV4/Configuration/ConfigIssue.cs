namespace RetakeV4.Configuration;

public sealed record ConfigIssue(string File, string Key, string Message);

public sealed record ValidationResult<T>(T Config, IReadOnlyList<ConfigIssue> Issues);

public sealed record ConfigLoadResult<T>(T Config, IReadOnlyList<ConfigIssue> Issues, bool CreatedDefault)
    where T : ModuleConfig;
