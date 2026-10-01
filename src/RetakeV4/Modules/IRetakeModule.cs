using Microsoft.Extensions.Logging;
using RetakeV4.Configuration;

namespace RetakeV4.Modules;

public interface IRetakeModule
{
    string Name { get; }

    IReadOnlyList<string> DependsOn { get; }

    ModuleConfig LoadConfig(JsonConfigStore store, ILogger logger);

    void Load(ModuleContext context);

    void Unload();
}
