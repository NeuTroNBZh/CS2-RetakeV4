using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Translations;
using Microsoft.Extensions.Logging;
using RetakeV4.Configuration;
using RetakeV4.Localization;

namespace RetakeV4.Modules.Links;

// Community commands (!discord, !regles...) from links.json: a single message, or lines shown with the help prefix.
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
                context.Hooks.Command($"css_{command}", "Community link", (player, _) =>
                {
                    if (link.Lines.Count > 0)
                    {
                        ShowLines(context.Text, player, link.Lines);
                        return;
                    }
                    Show(player, message);
                });
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

    private static void ShowLines(ITextService text, CCSPlayerController? player, IReadOnlyList<string> lines)
    {
        foreach (var line in lines)
        {
            if (player is { IsValid: true })
            {
                text.ChatContent(player, line, ChatPrefix.Help);
                continue;
            }
            Server.PrintToConsole(StringExtensions.ReplaceColorTags(line));
        }
    }
}
