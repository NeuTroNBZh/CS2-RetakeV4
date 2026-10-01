namespace RetakeV4.Domain.Modules;

public sealed record ModuleDescriptor(string Name, IReadOnlyList<string> DependsOn, bool Enabled);

public enum SkipReason
{
    Disabled,
    MissingDependency,
    DependencyCycle,
}

public sealed record SkippedModule(string Name, SkipReason Reason, string Detail);

public sealed record ModuleLoadPlan(IReadOnlyList<string> Order, IReadOnlyList<SkippedModule> Skipped);
