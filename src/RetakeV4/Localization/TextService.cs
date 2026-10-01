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

    public void Chat(CCSPlayerController player, string key, params object[] args) =>
        player.PrintToChat($" {For(player, "core.prefix")} {For(player, key, args)}");

    public void ChatAll(string key, params object[] args)
    {
        foreach (var player in Utilities.GetPlayers().Where(p => p is { IsValid: true, IsBot: false }))
        {
            Chat(player, key, args);
        }
    }
}
