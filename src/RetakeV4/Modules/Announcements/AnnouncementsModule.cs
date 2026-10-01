using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using Microsoft.Extensions.Logging;
using RetakeV4.Configuration;
using RetakeV4.Domain.Announcements;
using RetakeV4.Domain.Common;
using RetakeV4.Localization;

namespace RetakeV4.Modules.Announcements;

// Server messages from announcements.json: one every IntervalSeconds (map list first) and a welcome once per session.
public sealed class AnnouncementsModule : IRetakeModule
{
    private AnnouncementsConfig _config = new();
    private ModuleContext? _context;
    private AnnouncementPicker _picker = AnnouncementPicker.Create(Array.Empty<string>(), new Dictionary<string, IReadOnlyList<string>>());
    private WelcomeTracker _welcomes = WelcomeTracker.Empty;

    public string Name => "Announcements";

    public IReadOnlyList<string> DependsOn { get; } = new[] { "Core" };

    public ModuleConfig LoadConfig(JsonConfigStore store, ILogger logger)
    {
        var result = store.Load("announcements.json", new AnnouncementsConfig(), new AnnouncementsConfigValidator());
        ConfigLogging.Report(logger, result.Issues);
        _config = result.Config;
        return _config;
    }

    public void Load(ModuleContext context)
    {
        _context = context;
        _picker = AnnouncementPicker.Create(_config.Messages, _config.MapMessages);
        if (!_picker.IsEmpty)
        {
            context.Hooks.RepeatTimer("announce", _config.IntervalSeconds, Announce);
        }
        if (!string.IsNullOrWhiteSpace(_config.Welcome))
        {
            context.Hooks.OnEvent<EventPlayerTeam>("welcome", e => Welcome(e.Userid, e.Team));
            context.Hooks.OnEvent<EventPlayerDisconnect>("welcome_reset", e =>
            {
                if (e.Userid is { IsValid: true } player)
                {
                    _welcomes = _welcomes.Left(player.SteamID);
                }
            });
        }
    }

    public void Unload() => _context = null;

    private void Announce()
    {
        if (_context is not { } context)
        {
            return;
        }
        var (next, message) = _picker.Pick(Server.MapName, SystemRandom.Shared);
        _picker = next;
        if (message is not null)
        {
            context.Text.ChatContentAll(message);
        }
    }

    private void Welcome(CCSPlayerController? player, int team)
    {
        if (_context is not { } context || team == 0 || player is not { IsValid: true, IsBot: false })
        {
            return;
        }
        var (next, greet) = _welcomes.Joined(player.SteamID);
        _welcomes = next;
        if (greet)
        {
            context.Text.ChatContent(player, _config.Welcome, ChatPrefix.Help);
        }
    }
}
