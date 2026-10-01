using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Translations;
using Microsoft.Extensions.Logging;
using RetakeV4.Configuration;

namespace RetakeV4.Modules.Links;

// Community commands (!discord, !site...) from links.json; the message is server content, shown as written.
public sealed class LinksModule : IRetakeModule
{
    private LinksConfig _config = new();

    public string Name => "Links";

    public IReadOnlyList<string> DependsOn { get; } = new[] { "Core" };

    public ModuleConfig LoadConfig(JsonConfigStore store, ILogger logger)
    {
        var result = store.Load("links.json", new LinksConfig(), new LinksConfigValidator());
        ConfigLogging.Report(logger, result.Issues);
        _config = result.Config;
        return _config;
    }

    public void Load(ModuleContext context)
    {
        foreach (var link in _config.Links)
        {
            var message = StringExtensions.ReplaceColorTags(link.Message);
            foreach (var command in link.Commands)
            {
                context.Hooks.Command($"css_{command}", "Community link", (player, _) => Show(player, message));
            }
        }
    }

    public void Unload()
    {
    }

    private static void Show(CCSPlayerController? player, string message)
    {
        if (player is { IsValid: true })
        {
            player.PrintToChat($" {message}");
            return;
        }
        Server.PrintToConsole(message);
    }
}
