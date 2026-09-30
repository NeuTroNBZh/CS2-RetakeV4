using CounterStrikeSharp.API.Core;

namespace RetakeV4.Localization;

public interface ITextService
{
    string Server(string key, params object[] args);

    string For(CCSPlayerController player, string key, params object[] args);

    void Chat(CCSPlayerController player, string key, params object[] args);

    void ChatAll(string key, params object[] args);
}
