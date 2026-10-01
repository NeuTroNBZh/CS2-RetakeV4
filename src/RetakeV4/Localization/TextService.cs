using System.Globalization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Translations;
using Microsoft.Extensions.Localization;
using RetakeV4.Domain.Localization;

namespace RetakeV4.Localization;

public sealed class TextService : ITextService
{
    private readonly IStringLocalizer _localizer;
    private readonly TextOverrides _overrides;

    public TextService(IStringLocalizer localizer, TextOverrides overrides)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(overrides);
        _localizer = localizer;
        _overrides = overrides;
    }

    public string Server(string key, params object[] args) =>
        StringExtensions.ReplaceColorTags(_overrides.Format(CultureInfo.CurrentUICulture.Name, key, args) ?? _localizer[key, args].Value);

    public string For(CCSPlayerController player, string key, params object[] args) =>
        StringExtensions.ReplaceColorTags(_overrides.Format(player.GetLanguage().Name, key, args) ?? _localizer.ForPlayer(player, key, args));

    public void Chat(CCSPlayerController player, string key, params object[] args) => Send(player, ChatPrefix.Normal, For(player, key, args));

    public void ChatAlert(CCSPlayerController player, string key, params object[] args) => Send(player, ChatPrefix.Alert, For(player, key, args));

    public void ChatHelp(CCSPlayerController player, string key, params object[] args) => Send(player, ChatPrefix.Help, For(player, key, args));

    public void ChatContent(CCSPlayerController player, string content, ChatPrefix prefix) =>
        Send(player, prefix, StringExtensions.ReplaceColorTags(content));

    public void ChatAll(string key, params object[] args)
    {
        foreach (var player in Humans())
        {
            Chat(player, key, args);
        }
    }

    public void ChatContentAll(string content)
    {
        foreach (var player in Humans())
        {
            ChatContent(player, content, ChatPrefix.Normal);
        }
    }

    private void Send(CCSPlayerController player, ChatPrefix prefix, string text) =>
        player.PrintToChat($" {For(player, PrefixKey(prefix))} {text}");

    private static string PrefixKey(ChatPrefix prefix) => prefix switch
    {
        ChatPrefix.Alert => "core.prefix_alert",
        ChatPrefix.Help => "core.prefix_help",
        _ => "core.prefix",
    };

    private static IEnumerable<CCSPlayerController> Humans() =>
        Utilities.GetPlayers().Where(p => p is { IsValid: true, IsBot: false });
}
