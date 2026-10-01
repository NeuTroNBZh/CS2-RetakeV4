using CounterStrikeSharp.API.Core;

namespace RetakeV4.Localization;

public interface ITextService
{
    string Server(string key, params object[] args);

    string For(CCSPlayerController player, string key, params object[] args);

    void Chat(CCSPlayerController player, string key, params object[] args);

    void ChatAll(string key, params object[] args);

    void ChatAlert(CCSPlayerController player, string key, params object[] args);

    void ChatHelp(CCSPlayerController player, string key, params object[] args);

    // Server content (announcements, links): written as configured, color tags allowed, never looked up in lang/.
    void ChatContent(CCSPlayerController player, string content, ChatPrefix prefix);

    void ChatContentAll(string content);
}
