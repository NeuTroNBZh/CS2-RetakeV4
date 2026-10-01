# Personnalisation serveur (4.3.0) et pack Agora — plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** RetakeV4 4.3.0 gagne des textes remplaçables, des préfixes alerte/aide, un module Announcements, des liens multi-lignes et un vérificateur de config ; `agora-retake` devient un pack de configuration posé sur cette release.

**Architecture:** La logique (fusion des textes, choix des annonces, accueil une fois par session) est dans `RetakeV4.Domain` avec ses tests ; les modules ne font que brancher CSSharp. Le pack Agora ne contient que des JSON, un script `build.ps1` et une CI qui valide le pack avec l'outil `RetakeV4.ConfigCheck` publié par la release RetakeV4.

**Tech Stack:** C# / .NET 10, CounterStrikeSharp 1.0.370, xUnit, PowerShell 7, GitHub Actions.

**Spec:** `docs/superpowers/specs/2026-10-01-agora-customization-design.md`

## Global Constraints

- Build : `dotnet build RetakeV4.sln -c Release`, 0 warning (TreatWarningsAsErrors).
- `RetakeV4.Domain` ne référence jamais CounterStrikeSharp ; couverture Domain ≥ 80 %.
- Textes joueurs uniquement via `lang/*.json` (clés `module.section.key`, en + fr synchronisés) ; les textes d'annonces et de liens sont du contenu serveur écrit tel quel (balises de couleur acceptées).
- Logs en anglais avec templates constants.
- Valeurs par défaut de config uniquement dans les records `*Config.cs`.
- Tout hasard passe par `IRandom` ; timers de module uniquement via `hooks.RepeatTimer`.
- Un serveur sans configuration Agora se comporte exactement comme 4.2.1 (annonces vides = module inactif, préfixes alerte/aide = préfixe normal par défaut).
- Commits conventionnels en français, terminés par la ligne `Claude-Session: https://claude.ai/code/session_01HDPHjTUQicVkaLyXFv7jpM`. Jamais `--no-verify`.
- Version : 4.3.0 ; pack Agora `4.3.0-agora.1`.

## Rulings sur la spec (décidés à l'écriture du plan)

- `links.json` reste en version 1 : `Lines` est un champ ajouté et optionnel. Passer en version 2 ferait logger « config version 1 is older than 2 » sur chaque serveur existant, ce que la spec interdit (« une config version 1 reste valide sans changement »).
- Le timer d'annonces n'est pas recréé au changement de map : un seul timer répété lit `Server.MapName` à chaque tir, ce qui donne le même résultat (liste de la map courante).
- Les spawns vivent dans `plugins/RetakeV4/spawns/` (pas dans `configs/`). Le zip Agora `-no-configs` reçoit donc aussi les spawns du pack, sinon une mise à jour remettrait les spawns V4.
- Les remplacements de texte se comparent aux textes `en.json` du plugin (mêmes clés et mêmes placeholders dans toutes les langues, garanti par `LangFilesTests`). Une valeur remplacée peut omettre un `{n}` mais pas en ajouter un qui n'existe pas dans le texte d'origine.
- Le formatage des textes remplacés remplace uniquement les jetons `{<chiffres>}` (pas `string.Format`), pour ne jamais lever sur `{lightblue}` et les accolades du contenu.

## Review Focus

1. Fichier de remplacement `lang/fr.json` avec un BOM UTF-8 ou des commentaires : il doit se charger comme les configs (même `JsonSerializerOptions`).
2. Joueur avec une langue `fr-FR` ou `pt-BR` : le remplacement `fr` s'applique au premier, aucun au second.
3. Liste d'annonces à un seul message : le même message revient à chaque intervalle (pas de boucle infinie de non-répétition).
4. Joueur qui change d'équipe plusieurs fois ou change de map sans se déconnecter : un seul message d'accueil.
5. Pack Agora avec une clé de texte qui n'existe plus en V4 : `build.ps1 -Check` échoue avec le nom de la clé.

Chaque point a son test dans la tâche propriétaire (1 et 2 : tâches 1–2 ; 3 : tâche 4 ; 4 : tâche 4 ; 5 : tâches 7 et 12).

---

## Partie 1 — CS2-RetakeV4 (branche `feat/customization-agora`)

### Task 1: `TextOverrides` (Domain)

**Files:**
- Create: `src/RetakeV4.Domain/Localization/TextOverrides.cs`
- Test: `tests/RetakeV4.Domain.Tests/Localization/TextOverridesTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record TextOverrideIssue(string Language, string Key, string Reason);`
  - `public sealed class TextOverrides` avec `static TextOverrides Empty`, `static (TextOverrides Overrides, IReadOnlyList<TextOverrideIssue> Issues) Build(IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> byLanguage, IReadOnlyDictionary<string, string> reference)`, `string? Format(string culture, string key, IReadOnlyList<object> args)`, `int Count`.

- [ ] **Step 1: Write the failing tests**

```csharp
using RetakeV4.Domain.Localization;

namespace RetakeV4.Domain.Tests.Localization;

public class TextOverridesTests
{
    private static readonly Dictionary<string, string> Reference = new()
    {
        ["core.prefix"] = "{lightblue}[Retake]{default}",
        ["teams.queue.joined"] = "You are #{0} in the queue",
        ["allocation.menu.summary"] = "{0}: {1} / {2}",
    };

    private static (TextOverrides, IReadOnlyList<TextOverrideIssue>) Build(string language, Dictionary<string, string> values) =>
        TextOverrides.Build(new Dictionary<string, IReadOnlyDictionary<string, string>> { [language] = values }, Reference);

    [Fact]
    public void KnownKey_IsReplaced_AndPlaceholdersFilled()
    {
        var (overrides, issues) = Build("fr", new() { ["teams.queue.joined"] = "Tu es {0}e dans la file" });
        Assert.Empty(issues);
        Assert.Equal("Tu es 3e dans la file", overrides.Format("fr", "teams.queue.joined", new object[] { 3 }));
    }

    [Fact]
    public void ColorTagsAndBraces_AreLeftUntouched()
    {
        var (overrides, _) = Build("fr", new() { ["core.prefix"] = "{gold}[Agora-Retake]{default}" });
        Assert.Equal("{gold}[Agora-Retake]{default}", overrides.Format("fr", "core.prefix", Array.Empty<object>()));
    }

    [Fact]
    public void UnknownKey_EmptyValue_AndExtraPlaceholder_AreRejected()
    {
        var (overrides, issues) = Build("fr", new()
        {
            ["teams.nope"] = "x",
            ["core.prefix"] = "   ",
            ["teams.queue.joined"] = "{0} {1}",
        });
        Assert.Equal(0, overrides.Count);
        Assert.Equal(new[] { "core.prefix", "teams.nope", "teams.queue.joined" }, issues.Select(i => i.Key).Order());
    }

    [Fact]
    public void OmittedPlaceholder_IsAllowed()
    {
        var (overrides, issues) = Build("fr", new() { ["allocation.menu.summary"] = "{0}" });
        Assert.Empty(issues);
        Assert.Equal("Pistol", overrides.Format("fr", "allocation.menu.summary", new object[] { "Pistol", "Glock", "-" }));
    }

    [Theory]
    [InlineData("fr", true)]
    [InlineData("fr-FR", true)]
    [InlineData("FR", true)]
    [InlineData("pt-BR", false)]
    [InlineData("en", false)]
    public void Culture_FallsBackToItsLanguage(string culture, bool replaced)
    {
        var (overrides, _) = Build("fr", new() { ["core.prefix"] = "[A]" });
        Assert.Equal(replaced, overrides.Format(culture, "core.prefix", Array.Empty<object>()) is not null);
    }

    [Fact]
    public void Empty_ReplacesNothing()
    {
        Assert.Null(TextOverrides.Empty.Format("fr", "core.prefix", Array.Empty<object>()));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/RetakeV4.Domain.Tests --filter TextOverridesTests`
Expected: FAIL, `TextOverrides` introuvable (erreur de compilation).

- [ ] **Step 3: Implement**

```csharp
using System.Globalization;
using System.Text.RegularExpressions;

namespace RetakeV4.Domain.Localization;

public sealed record TextOverrideIssue(string Language, string Key, string Reason);

// Server-side replacements of the plugin texts (configs/plugins/RetakeV4/lang/<language>.json), checked against the plugin's own texts.
public sealed partial class TextOverrides
{
    private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> _byLanguage;

    private TextOverrides(IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> byLanguage) => _byLanguage = byLanguage;

    public static TextOverrides Empty { get; } = new(new Dictionary<string, IReadOnlyDictionary<string, string>>());

    public int Count => _byLanguage.Values.Sum(v => v.Count);

    [GeneratedRegex(@"\{(\d+)\}")]
    private static partial Regex Placeholder();

    public static (TextOverrides Overrides, IReadOnlyList<TextOverrideIssue> Issues) Build(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> byLanguage,
        IReadOnlyDictionary<string, string> reference)
    {
        ArgumentNullException.ThrowIfNull(byLanguage);
        ArgumentNullException.ThrowIfNull(reference);
        var issues = new List<TextOverrideIssue>();
        var kept = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (language, values) in byLanguage)
        {
            var accepted = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (key, value) in values)
            {
                if (Problem(key, value, reference) is { } reason)
                {
                    issues.Add(new TextOverrideIssue(language, key, reason));
                    continue;
                }
                accepted[key] = value;
            }
            kept[language.Trim()] = accepted;
        }
        return (new TextOverrides(kept), issues);
    }

    public string? Format(string culture, string key, IReadOnlyList<object> args)
    {
        var language = culture.Split('-', 2)[0];
        foreach (var candidate in new[] { culture, language })
        {
            if (_byLanguage.TryGetValue(candidate, out var values) && values.TryGetValue(key, out var value))
            {
                return Placeholder().Replace(value, m =>
                {
                    var index = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                    return index < args.Count ? Convert.ToString(args[index], CultureInfo.InvariantCulture) ?? string.Empty : m.Value;
                });
            }
        }
        return null;
    }

    private static string? Problem(string key, string? value, IReadOnlyDictionary<string, string> reference)
    {
        if (!reference.TryGetValue(key, out var original))
        {
            return "unknown key";
        }
        if (string.IsNullOrWhiteSpace(value))
        {
            return "empty text";
        }
        var allowed = Placeholder().Matches(original).Select(m => m.Value).ToHashSet(StringComparer.Ordinal);
        return Placeholder().Matches(value).Select(m => m.Value).FirstOrDefault(p => !allowed.Contains(p)) is { } extra
            ? $"placeholder {extra} does not exist in the original text"
            : null;
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/RetakeV4.Domain.Tests --filter TextOverridesTests`
Expected: PASS (10 tests).

- [ ] **Step 5: Commit**

```bash
git add src/RetakeV4.Domain/Localization tests/RetakeV4.Domain.Tests/Localization
git commit -m "feat: TextOverrides, remplacement des textes du plugin vérifié contre les textes d'origine"
```

### Task 2: Chargement des remplacements et branchement dans `TextService`

**Files:**
- Create: `src/RetakeV4/Localization/LangOverrideLoader.cs`
- Modify: `src/RetakeV4/Localization/TextService.cs`, `src/RetakeV4/RetakeV4Plugin.cs:43`
- Test: `tests/RetakeV4.Integration.Tests/Localization/LangOverrideLoaderTests.cs`

**Interfaces:**
- Consumes: `TextOverrides.Build`, `TextOverrides.Format` (Task 1).
- Produces: `public static class LangOverrideLoader { public static TextOverrides Load(string configDirectory, string pluginLangDirectory, ILogger logger); public static (TextOverrides Overrides, IReadOnlyList<string> Problems) Read(string configDirectory, string pluginLangDirectory); }` ; `TextService(IStringLocalizer localizer, TextOverrides overrides)`.

- [ ] **Step 1: Write the failing tests**

```csharp
using RetakeV4.Localization;

namespace RetakeV4.Integration.Tests.Localization;

public class LangOverrideLoaderTests : IDisposable
{
    private readonly TempDirectory _dir = new();
    private static readonly string PluginLang = Path.Combine(AppContext.BaseDirectory, "lang");

    public void Dispose() => _dir.Dispose();

    private void Write(string name, string content)
    {
        Directory.CreateDirectory(Path.Combine(_dir.Path, "lang"));
        File.WriteAllText(Path.Combine(_dir.Path, "lang", name), content);
    }

    [Fact]
    public void NoLangFolder_GivesNoOverrides_AndNoProblem()
    {
        var (overrides, problems) = LangOverrideLoader.Read(_dir.Path, PluginLang);
        Assert.Equal(0, overrides.Count);
        Assert.Empty(problems);
    }

    [Fact]
    public void ValidFile_WithBomAndComments_IsLoaded()
    {
        Write("fr.json", "\uFEFF{ // Agora\n \"core.prefix\": \"{gold}[Agora-Retake]{default}\", }");
        var (overrides, problems) = LangOverrideLoader.Read(_dir.Path, PluginLang);
        Assert.Empty(problems);
        Assert.Equal("{gold}[Agora-Retake]{default}", overrides.Format("fr-FR", "core.prefix", Array.Empty<object>()));
    }

    [Fact]
    public void InvalidJson_IsReported_AndOtherFilesStillLoad()
    {
        Write("en.json", "{ not json");
        Write("fr.json", "{ \"core.prefix\": \"[A]\" }");
        var (overrides, problems) = LangOverrideLoader.Read(_dir.Path, PluginLang);
        Assert.Contains(problems, p => p.Contains("en.json", StringComparison.Ordinal));
        Assert.Equal(1, overrides.Count);
    }

    [Fact]
    public void UnknownKey_IsReported_WithItsName()
    {
        Write("fr.json", "{ \"core.does_not_exist\": \"x\" }");
        var (_, problems) = LangOverrideLoader.Read(_dir.Path, PluginLang);
        Assert.Contains(problems, p => p.Contains("core.does_not_exist", StringComparison.Ordinal));
    }
}
```

(`TempDirectory` existe déjà dans `tests/RetakeV4.Integration.Tests/TempDirectory.cs` ; vérifier le nom de sa propriété de chemin et l'utiliser.)

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/RetakeV4.Integration.Tests --filter LangOverrideLoaderTests`
Expected: FAIL (compilation : `LangOverrideLoader` introuvable).

- [ ] **Step 3: Implement the loader**

```csharp
using System.Text.Json;
using Microsoft.Extensions.Logging;
using RetakeV4.Configuration;
using RetakeV4.Domain.Localization;

namespace RetakeV4.Localization;

// configs/plugins/RetakeV4/lang/<language>.json: partial replacements of the plugin texts, kept across updates.
public static class LangOverrideLoader
{
    public static TextOverrides Load(string configDirectory, string pluginLangDirectory, ILogger logger)
    {
        var (overrides, problems) = Read(configDirectory, pluginLangDirectory);
        foreach (var problem in problems)
        {
            logger.LogWarning("Text override ignored: {Problem}", problem);
        }
        if (overrides.Count > 0)
        {
            logger.LogInformation("Loaded {Count} text override(s)", overrides.Count);
        }
        return overrides;
    }

    public static (TextOverrides Overrides, IReadOnlyList<string> Problems) Read(string configDirectory, string pluginLangDirectory)
    {
        var folder = Path.Combine(configDirectory, "lang");
        if (!Directory.Exists(folder))
        {
            return (TextOverrides.Empty, Array.Empty<string>());
        }
        var problems = new List<string>();
        var byLanguage = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.GetFiles(folder, "*.json").Order(StringComparer.Ordinal))
        {
            try
            {
                var values = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file), JsonConfigStore.SerializerOptions);
                byLanguage[Path.GetFileNameWithoutExtension(file)] = values ?? new Dictionary<string, string>();
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                problems.Add($"{Path.GetFileName(file)}: {ex.Message}");
            }
        }
        var reference = JsonSerializer.Deserialize<Dictionary<string, string>>(
            File.ReadAllText(Path.Combine(pluginLangDirectory, "en.json")), JsonConfigStore.SerializerOptions)
            ?? new Dictionary<string, string>();
        var (overrides, issues) = TextOverrides.Build(byLanguage, reference);
        problems.AddRange(issues.Select(i => $"{i.Language}.json [{i.Key}]: {i.Reason}"));
        return (overrides, problems);
    }
}
```

- [ ] **Step 4: Branch it in `TextService` and the plugin**

`TextService` :

```csharp
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

    // Chat / ChatAll unchanged (Task 3 adds the alert and help variants)
}
```

`RetakeV4Plugin.Load`, ligne `var text = new TextService(Localizer);` devient :

```csharp
var text = new TextService(Localizer, LangOverrideLoader.Load(ConfigDirectory(), Path.Combine(ModuleDirectory, "lang"), Logger));
```

Corriger toute autre construction de `TextService` signalée par le build (tests compris) en passant `TextOverrides.Empty`.

- [ ] **Step 5: Run tests and build**

Run: `dotnet build RetakeV4.sln -c Release && dotnet test RetakeV4.sln`
Expected: 0 warning, tous les tests passent (dont les 4 de `LangOverrideLoaderTests`).

- [ ] **Step 6: Commit**

```bash
git add src/RetakeV4/Localization src/RetakeV4/RetakeV4Plugin.cs tests/RetakeV4.Integration.Tests/Localization
git commit -m "feat: textes remplaçables par le serveur (configs/plugins/RetakeV4/lang), conservés aux mises à jour"
```

### Task 3: Préfixes alerte et aide, envoi de contenu serveur

**Files:**
- Modify: `src/RetakeV4/Localization/ITextService.cs`, `src/RetakeV4/Localization/TextService.cs`, `src/RetakeV4/lang/en.json`, `src/RetakeV4/lang/fr.json`
- Modify (appels) : `src/RetakeV4/Modules/Admin/AdminModule.cs:89,101,119`, `src/RetakeV4/Modules/Allocation/AllocationModule.cs:184`, `src/RetakeV4/Modules/Teams/TeamsModule.cs:127,287`
- Test: `tests/RetakeV4.Integration.Tests/Localization/LangFilesTests.cs`
- Modify : tout double de test qui implémente `ITextService` (le build les signale).

**Interfaces:**
- Produces sur `ITextService` :

```csharp
void ChatAlert(CCSPlayerController player, string key, params object[] args);
void ChatHelp(CCSPlayerController player, string key, params object[] args);
// Server content (announcements, links): written as configured, color tags allowed, never looked up in lang/.
void ChatContent(CCSPlayerController player, string content, ChatPrefix prefix);
void ChatContentAll(string content);
```

et `public enum ChatPrefix { Normal, Alert, Help }` dans `src/RetakeV4/Localization/ChatPrefix.cs`.

- [ ] **Step 1: Write the failing test**

Dans `LangFilesTests` :

```csharp
[Theory]
[InlineData("en")]
[InlineData("fr")]
public void AlertAndHelpPrefixes_DefaultToTheNormalPrefix(string language)
{
    var texts = Load(language);
    Assert.Equal(texts["core.prefix"], texts["core.prefix_alert"]);
    Assert.Equal(texts["core.prefix"], texts["core.prefix_help"]);
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/RetakeV4.Integration.Tests --filter AlertAndHelpPrefixes`
Expected: FAIL (`KeyNotFoundException` sur `core.prefix_alert`).

- [ ] **Step 3: Add the keys and the methods**

`en.json` et `fr.json`, juste après `core.prefix`, avec la même valeur que `core.prefix` du fichier :

```json
"core.prefix_alert": "{lightblue}[Retake]{default}",
"core.prefix_help": "{lightblue}[Retake]{default}",
```

`ChatPrefix.cs` :

```csharp
namespace RetakeV4.Localization;

public enum ChatPrefix
{
    Normal,
    Alert,
    Help,
}
```

`TextService` (remplace `Chat`/`ChatAll`) :

```csharp
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
```

Ajouter les 4 signatures à `ITextService`.

- [ ] **Step 4: Switch the call sites**

- `ChatAlert` : `admin.no_permission` (AdminModule l.89 et 119), `allocation.no_permission` (AllocationModule l.184), `teams.switch.refused` (TeamsModule l.127), `teams.no_permission` (TeamsModule l.287).
- `ChatHelp` : `admin.usage` (AdminModule l.101).
- `CoreModule.OnInfoCommand` garde `ReplyToCommand` (réponse console et chat) mais préfixe la réponse joueur : `Context.Text.For(player, "core.prefix_help") + " " + Context.Text.For(player, "core.info.version", args)`.

Mettre à jour les doubles de test d'`ITextService` (méthodes ajoutées, sans logique).

- [ ] **Step 5: Run tests and build**

Run: `dotnet build RetakeV4.sln -c Release && dotnet test RetakeV4.sln`
Expected: 0 warning, tout passe.

- [ ] **Step 6: Commit**

```bash
git add -A src/RetakeV4 tests
git commit -m "feat: préfixes alerte et aide (core.prefix_alert, core.prefix_help), envoi de contenu serveur"
```

### Task 4: `AnnouncementPicker` et `WelcomeTracker` (Domain)

**Files:**
- Create: `src/RetakeV4.Domain/Announcements/AnnouncementPicker.cs`, `src/RetakeV4.Domain/Announcements/WelcomeTracker.cs`
- Test: `tests/RetakeV4.Domain.Tests/Announcements/AnnouncementPickerTests.cs`, `tests/RetakeV4.Domain.Tests/Announcements/WelcomeTrackerTests.cs`

**Interfaces:**
- Produces :
  - `public sealed record AnnouncementPicker` : `static AnnouncementPicker Create(IReadOnlyList<string> general, IReadOnlyDictionary<string, IReadOnlyList<string>> byMap)`, `bool IsEmpty`, `(AnnouncementPicker Next, string? Message) Pick(string map, IRandom random)`.
  - `public sealed record WelcomeTracker` : `static WelcomeTracker Empty`, `(WelcomeTracker Next, bool Greet) Joined(ulong steamId)`, `WelcomeTracker Left(ulong steamId)`.

- [ ] **Step 1: Write the failing tests**

```csharp
using RetakeV4.Domain.Announcements;
using RetakeV4.Domain.Tests.TestDoubles;

namespace RetakeV4.Domain.Tests.Announcements;

public class AnnouncementPickerTests
{
    private static readonly Dictionary<string, IReadOnlyList<string>> ByMap = new()
    {
        ["de_mirage"] = new[] { "mirage 1", "mirage 2" },
        ["de_nuke"] = new[] { "  " },
    };

    [Fact]
    public void MapWithItsOwnList_UsesIt_CaseInsensitively()
    {
        var picker = AnnouncementPicker.Create(new[] { "general" }, ByMap);
        Assert.Equal("mirage 2", picker.Pick("DE_MIRAGE", new FixedRandom(1)).Message);
    }

    [Fact]
    public void MapWithoutUsableList_UsesTheGeneralList()
    {
        var picker = AnnouncementPicker.Create(new[] { "general" }, ByMap);
        Assert.Equal("general", picker.Pick("de_nuke", new FixedRandom(0)).Message);
        Assert.Equal("general", picker.Pick("de_dust2", new FixedRandom(0)).Message);
    }

    [Fact]
    public void SameMessage_NeverTwiceInARow_WhenThereIsAChoice()
    {
        var picker = AnnouncementPicker.Create(new[] { "a", "b" }, new Dictionary<string, IReadOnlyList<string>>());
        var random = new FixedRandom(0);
        var (next, first) = picker.Pick("de_x", random);
        var (_, second) = next.Pick("de_x", random);
        Assert.Equal("a", first);
        Assert.Equal("b", second);
    }

    [Fact]
    public void SingleMessage_IsRepeated()
    {
        var picker = AnnouncementPicker.Create(new[] { "only" }, new Dictionary<string, IReadOnlyList<string>>());
        var (next, _) = picker.Pick("de_x", new FixedRandom(0));
        Assert.Equal("only", next.Pick("de_x", new FixedRandom(0)).Message);
    }

    [Fact]
    public void NoMessages_GivesNothing()
    {
        var picker = AnnouncementPicker.Create(new[] { " " }, new Dictionary<string, IReadOnlyList<string>>());
        Assert.True(picker.IsEmpty);
        Assert.Null(picker.Pick("de_x", new FixedRandom(0)).Message);
    }
}

public class WelcomeTrackerTests
{
    [Fact]
    public void FirstJoin_Greets_ThenNeverAgainInTheSession()
    {
        var (tracker, first) = WelcomeTracker.Empty.Joined(1);
        var (tracker2, second) = tracker.Joined(1);
        Assert.True(first);
        Assert.False(second);
        Assert.False(tracker2.Joined(1).Greet);
    }

    [Fact]
    public void Reconnecting_StartsANewSession()
    {
        var (tracker, _) = WelcomeTracker.Empty.Joined(1);
        Assert.True(tracker.Left(1).Joined(1).Greet);
    }

    [Fact]
    public void Players_AreTrackedSeparately()
    {
        var (tracker, _) = WelcomeTracker.Empty.Joined(1);
        Assert.True(tracker.Joined(2).Greet);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/RetakeV4.Domain.Tests --filter "AnnouncementPickerTests|WelcomeTrackerTests"`
Expected: FAIL (compilation).

- [ ] **Step 3: Implement**

```csharp
using RetakeV4.Domain.Common;

namespace RetakeV4.Domain.Announcements;

// Picks the next server message: the current map's list if it has one, else the general list; never the same twice in a row.
public sealed record AnnouncementPicker(
    IReadOnlyList<string> General,
    IReadOnlyDictionary<string, IReadOnlyList<string>> ByMap,
    string? Last)
{
    public static AnnouncementPicker Create(IReadOnlyList<string> general, IReadOnlyDictionary<string, IReadOnlyList<string>> byMap)
    {
        ArgumentNullException.ThrowIfNull(general);
        ArgumentNullException.ThrowIfNull(byMap);
        var maps = byMap
            .Select(kv => (Map: kv.Key.Trim(), Messages: Usable(kv.Value)))
            .Where(m => m.Messages.Count > 0)
            .ToDictionary(m => m.Map, m => m.Messages, StringComparer.OrdinalIgnoreCase);
        return new AnnouncementPicker(Usable(general), maps, null);
    }

    public bool IsEmpty => General.Count == 0 && ByMap.Count == 0;

    public (AnnouncementPicker Next, string? Message) Pick(string map, IRandom random)
    {
        ArgumentNullException.ThrowIfNull(random);
        var pool = ByMap.TryGetValue(map ?? string.Empty, out var own) ? own : General;
        if (pool.Count == 0)
        {
            return (this, null);
        }
        var candidates = pool.Count > 1 ? pool.Where(m => m != Last).ToList() : pool.ToList();
        if (candidates.Count == 0)
        {
            candidates = pool.ToList();
        }
        var message = candidates[random.Next(candidates.Count)];
        return (this with { Last = message }, message);
    }

    private static IReadOnlyList<string> Usable(IReadOnlyList<string>? messages) =>
        (messages ?? Array.Empty<string>()).Where(m => !string.IsNullOrWhiteSpace(m)).ToList();
}
```

```csharp
using System.Collections.Immutable;

namespace RetakeV4.Domain.Announcements;

// The welcome message is sent once per session (connection to disconnection), whatever the team or map changes.
public sealed record WelcomeTracker(ImmutableHashSet<ulong> Greeted)
{
    public static WelcomeTracker Empty { get; } = new(ImmutableHashSet<ulong>.Empty);

    public (WelcomeTracker Next, bool Greet) Joined(ulong steamId) =>
        Greeted.Contains(steamId) ? (this, false) : (this with { Greeted = Greeted.Add(steamId) }, true);

    public WelcomeTracker Left(ulong steamId) => this with { Greeted = Greeted.Remove(steamId) };
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/RetakeV4.Domain.Tests --filter "AnnouncementPickerTests|WelcomeTrackerTests"`
Expected: PASS (8 tests).

- [ ] **Step 5: Commit**

```bash
git add src/RetakeV4.Domain/Announcements tests/RetakeV4.Domain.Tests/Announcements
git commit -m "feat: choix des annonces par map sans répétition, accueil une fois par session (Domain)"
```

### Task 5: Module Announcements

**Files:**
- Create: `src/RetakeV4/Modules/Announcements/AnnouncementsConfig.cs`, `AnnouncementsConfigValidator.cs`, `AnnouncementsModule.cs`
- Modify: `src/RetakeV4/Modules/ModuleCatalog.cs`, `tests/RetakeV4.Integration.Tests/Modules/ModuleCatalogTests.cs`, `tests/RetakeV4.Integration.Tests/Configuration/ConfigExportTests.cs:19-20`
- Test: `tests/RetakeV4.Integration.Tests/Modules/Announcements/AnnouncementsConfigValidatorTests.cs`

**Interfaces:**
- Consumes: `AnnouncementPicker`, `WelcomeTracker` (Task 4) ; `ITextService.ChatContent`, `ChatContentAll`, `ChatPrefix.Help` (Task 3).
- Produces: `AnnouncementsConfig` (`IntervalSeconds`, `Messages`, `MapMessages`, `Welcome`), fichier `announcements.json`.

- [ ] **Step 1: Write the failing tests**

```csharp
using RetakeV4.Modules.Announcements;

namespace RetakeV4.Integration.Tests.Modules.Announcements;

public class AnnouncementsConfigValidatorTests
{
    private static readonly AnnouncementsConfig Defaults = new();
    private readonly AnnouncementsConfigValidator _validator = new();

    [Fact]
    public void Defaults_AreValid_AndInactive()
    {
        var result = _validator.Validate(Defaults, Defaults, "announcements.json");
        Assert.Empty(result.Issues);
        Assert.Equal(420, result.Config.IntervalSeconds);
        Assert.Empty(result.Config.Messages);
        Assert.Empty(result.Config.MapMessages);
        Assert.Equal(string.Empty, result.Config.Welcome);
    }

    [Fact]
    public void IntervalBelow30_IsReported_AndDefaultUsed()
    {
        var result = _validator.Validate(Defaults with { IntervalSeconds = 5 }, Defaults, "announcements.json");
        Assert.Single(result.Issues);
        Assert.Equal(420, result.Config.IntervalSeconds);
    }

    [Fact]
    public void TooLongMessages_AreDropped_AndNullListsBecomeEmpty()
    {
        var config = Defaults with
        {
            Messages = new[] { "ok", new string('x', 513) },
            MapMessages = null!,
            Welcome = null!,
        };
        var result = _validator.Validate(config, Defaults, "announcements.json");
        Assert.Equal(new[] { "ok" }, result.Config.Messages);
        Assert.Empty(result.Config.MapMessages);
        Assert.Equal(string.Empty, result.Config.Welcome);
        Assert.Single(result.Issues);
    }
}
```

`ModuleCatalogTests` : la liste attendue devient `{ "Core", "Hud", "RoundTypes", "Teams", "Spawns", "Allocation", "Plant", "InstaDefuse", "Admin", "Links", "Announcements", "Api" }`.
`ConfigExportTests` : ajouter `"announcements.json"` en tête de la liste (ordre alphabétique).

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/RetakeV4.Integration.Tests --filter "Announcements|ModuleCatalog|ConfigExport"`
Expected: FAIL (compilation puis listes).

- [ ] **Step 3: Implement config and validator**

```csharp
using RetakeV4.Configuration;

namespace RetakeV4.Modules.Announcements;

public sealed record AnnouncementsConfig : ModuleConfig
{
    public AnnouncementsConfig() => Version = 1;

    public int IntervalSeconds { get; init; } = 420;

    public IReadOnlyList<string> Messages { get; init; } = Array.Empty<string>();

    public IReadOnlyDictionary<string, IReadOnlyList<string>> MapMessages { get; init; } = new Dictionary<string, IReadOnlyList<string>>();

    public string Welcome { get; init; } = string.Empty;
}
```

```csharp
using RetakeV4.Configuration;

namespace RetakeV4.Modules.Announcements;

public sealed class AnnouncementsConfigValidator : IConfigValidator<AnnouncementsConfig>
{
    public const int MinIntervalSeconds = 30;
    public const int MaxMessageLength = 512;

    public ValidationResult<AnnouncementsConfig> Validate(AnnouncementsConfig config, AnnouncementsConfig defaults, string file)
    {
        var issues = new List<ConfigIssue>();
        var interval = config.IntervalSeconds;
        if (interval < MinIntervalSeconds)
        {
            issues.Add(new ConfigIssue(file, nameof(config.IntervalSeconds), $"must be at least {MinIntervalSeconds}; using {defaults.IntervalSeconds}"));
            interval = defaults.IntervalSeconds;
        }
        var messages = Keep(config.Messages, nameof(config.Messages), file, issues);
        var maps = (config.MapMessages ?? new Dictionary<string, IReadOnlyList<string>>())
            .ToDictionary(kv => kv.Key.Trim().ToLowerInvariant(),
                kv => Keep(kv.Value, $"{nameof(config.MapMessages)}.{kv.Key}", file, issues));
        var welcome = config.Welcome ?? string.Empty;
        if (welcome.Length > MaxMessageLength)
        {
            issues.Add(new ConfigIssue(file, nameof(config.Welcome), $"longer than {MaxMessageLength} characters; ignored"));
            welcome = string.Empty;
        }
        return new ValidationResult<AnnouncementsConfig>(
            config with { IntervalSeconds = interval, Messages = messages, MapMessages = maps, Welcome = welcome }, issues);
    }

    private static IReadOnlyList<string> Keep(IReadOnlyList<string>? messages, string key, string file, List<ConfigIssue> issues)
    {
        var kept = new List<string>();
        foreach (var message in messages ?? Array.Empty<string>())
        {
            if (message is { Length: > MaxMessageLength })
            {
                issues.Add(new ConfigIssue(file, key, $"a message is longer than {MaxMessageLength} characters; ignored"));
                continue;
            }
            if (!string.IsNullOrWhiteSpace(message))
            {
                kept.Add(message);
            }
        }
        return kept;
    }
}
```

- [ ] **Step 4: Implement the module and register it**

```csharp
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
```

Ajouter `new AnnouncementsModule(),` dans `ModuleCatalog.CreateAll()` entre `LinksModule` et `ApiModule`, avec le `using RetakeV4.Modules.Announcements;`.

- [ ] **Step 5: Run tests and build**

Run: `dotnet build RetakeV4.sln -c Release && dotnet test RetakeV4.sln`
Expected: 0 warning, tout passe.

- [ ] **Step 6: Commit**

```bash
git add -A src/RetakeV4/Modules tests/RetakeV4.Integration.Tests
git commit -m "feat: module Announcements (messages réguliers, listes par map, accueil une fois par session)"
```

### Task 6: Liens sur plusieurs lignes

**Files:**
- Modify: `src/RetakeV4/Modules/Links/LinksConfig.cs`, `LinksConfigValidator.cs`, `LinksModule.cs`
- Test: `tests/RetakeV4.Integration.Tests/Modules/Links/LinksConfigValidatorTests.cs`

**Interfaces:**
- Consumes: `ITextService.ChatContent`, `ChatPrefix.Help` (Task 3).
- Produces: `LinkConfig.Lines` (`IReadOnlyList<string>`, défaut vide).

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
public void LinkWithLines_IsKept_AndBlankLinesDropped()
{
    var link = new LinkConfig { Commands = new[] { "regles" }, Lines = new[] { "1. Respect", " ", "2. Pas de triche" } };
    var result = _validator.Validate(Defaults with { Links = new[] { link } }, Defaults, "links.json");
    Assert.Empty(result.Issues);
    Assert.Equal(new[] { "1. Respect", "2. Pas de triche" }, Assert.Single(result.Config.Links).Lines);
}

[Fact]
public void LinkWithBothMessageAndLines_OrNeither_IsSkipped()
{
    var both = new LinkConfig { Commands = new[] { "a" }, Message = "m", Lines = new[] { "l" } };
    var neither = new LinkConfig { Commands = new[] { "b" }, Lines = new[] { " " } };
    var result = _validator.Validate(Defaults with { Links = new[] { both, neither } }, Defaults, "links.json");
    Assert.Empty(result.Config.Links);
    Assert.Equal(2, result.Issues.Count);
}

[Fact]
public void TooManyOrTooLongLines_AreRefused()
{
    var many = new LinkConfig { Commands = new[] { "a" }, Lines = Enumerable.Repeat("x", 17).ToList() };
    var longLine = new LinkConfig { Commands = new[] { "b" }, Lines = new[] { new string('x', 513) } };
    var result = _validator.Validate(Defaults with { Links = new[] { many, longLine } }, Defaults, "links.json");
    Assert.Empty(result.Config.Links);
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/RetakeV4.Integration.Tests --filter LinksConfigValidatorTests`
Expected: FAIL (compilation : `Lines` inconnu).

- [ ] **Step 3: Implement**

`LinkConfig` : ajouter `public IReadOnlyList<string> Lines { get; init; } = Array.Empty<string>();` (la version reste 1).

`LinksConfigValidator` : ajouter `public const int MaxLines = 16;`. Dans `Validate`, le lien gardé devient `config.Links[i] with { Commands = commands, Lines = CleanLines(config.Links[i]) }`. Remplacer le contrôle du message dans `Check` par :

```csharp
var lines = CleanLines(link);
var hasMessage = !string.IsNullOrWhiteSpace(link.Message);
if (hasMessage == lines.Count > 0)
{
    return "set either Message or Lines";
}
if (hasMessage && link.Message.Length > MaxMessageLength)
{
    return $"message must be 1 to {MaxMessageLength} characters";
}
if (lines.Count > MaxLines || lines.Any(l => l.Length > MaxMessageLength))
{
    return $"at most {MaxLines} lines of {MaxMessageLength} characters";
}
return null;
```

avec

```csharp
private static IReadOnlyList<string> CleanLines(LinkConfig link) =>
    (link.Lines ?? Array.Empty<string>()).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
```

`LinksModule.Load` :

```csharp
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
```

(ajouter `using RetakeV4.Localization;`). Mettre à jour le commentaire d'en-tête : « Community commands (!discord, !regles...) from links.json: a single message, or lines shown with the help prefix. »

- [ ] **Step 4: Run tests and build**

Run: `dotnet build RetakeV4.sln -c Release && dotnet test RetakeV4.sln`
Expected: 0 warning, tout passe (les tests Links existants inclus).

- [ ] **Step 5: Commit**

```bash
git add src/RetakeV4/Modules/Links tests/RetakeV4.Integration.Tests/Modules/Links
git commit -m "feat: liens sur plusieurs lignes (Lines) avec le préfixe d'aide"
```

### Task 7: Vérificateur de configuration `RetakeV4.ConfigCheck`

**Files:**
- Create: `src/RetakeV4/Configuration/ConfigCheck.cs`, `tools/RetakeV4.ConfigCheck/RetakeV4.ConfigCheck.csproj`, `tools/RetakeV4.ConfigCheck/Program.cs`
- Modify: `RetakeV4.sln` (ajout du projet), `scripts/package-release.ps1`
- Test: `tests/RetakeV4.Integration.Tests/Configuration/ConfigCheckTests.cs`

**Interfaces:**
- Consumes: `ModuleCatalog.CreateAll`, `JsonConfigStore`, `LangOverrideLoader.Read` (Task 2), `SpawnFileStore.Load(map)` (`SpawnLoad.Issues`, `IsLegacy`, `Found`).
- Produces: `public static class ConfigCheck { public static IReadOnlyList<string> Run(string configDirectory, string? spawnsDirectory, string pluginLangDirectory); }` ; exécutable `RetakeV4.ConfigCheck <configs> [<spawns>]` (code 0 si aucun problème, 1 sinon, problèmes sur stdout) ; asset de release `RetakeV4-<version>-configcheck.zip`.

- [ ] **Step 1: Write the failing tests**

```csharp
using RetakeV4.Configuration;

namespace RetakeV4.Integration.Tests.Configuration;

public class ConfigCheckTests : IDisposable
{
    private readonly TempDirectory _dir = new();
    private static readonly string PluginLang = Path.Combine(AppContext.BaseDirectory, "lang");

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void ExportedDefaults_HaveNoProblem()
    {
        ConfigExport.Run(_dir.Path, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
        Assert.Empty(ConfigCheck.Run(_dir.Path, null, PluginLang));
    }

    [Fact]
    public void InvalidModuleConfig_IsReported_AndTheDirectoryIsNotModified()
    {
        File.WriteAllText(_dir.File("announcements.json"), "{ \"Version\": 1, \"IntervalSeconds\": 1 }");
        var problems = ConfigCheck.Run(_dir.Path, null, PluginLang);
        Assert.Contains(problems, p => p.Contains("announcements.json", StringComparison.Ordinal));
        Assert.Equal(new[] { "announcements.json" }, Directory.GetFiles(_dir.Path).Select(Path.GetFileName));
    }

    [Fact]
    public void UnknownTextKey_IsReported()
    {
        Directory.CreateDirectory(Path.Combine(_dir.Path, "lang"));
        File.WriteAllText(Path.Combine(_dir.Path, "lang", "fr.json"), "{ \"core.gone\": \"x\" }");
        Assert.Contains(ConfigCheck.Run(_dir.Path, null, PluginLang), p => p.Contains("core.gone", StringComparison.Ordinal));
    }

    [Fact]
    public void BrokenSpawnFile_IsReported()
    {
        var spawns = Directory.CreateDirectory(Path.Combine(_dir.Path, "spawns")).FullName;
        File.WriteAllText(Path.Combine(spawns, "de_test.json"), "{ broken");
        Assert.Contains(ConfigCheck.Run(Path.Combine(_dir.Path, "none"), spawns, PluginLang), p => p.Contains("de_test", StringComparison.Ordinal));
    }
}
```

(Utiliser les membres réels de `TempDirectory` : `Path`/`File(name)` ou leurs équivalents.)

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/RetakeV4.Integration.Tests --filter ConfigCheckTests`
Expected: FAIL (compilation).

- [ ] **Step 3: Implement `ConfigCheck`**

```csharp
using Microsoft.Extensions.Logging;
using RetakeV4.Localization;
using RetakeV4.Modules;
using RetakeV4.Modules.Spawns;

namespace RetakeV4.Configuration;

// Validates a server configuration (module configs, text overrides, spawns) the way the plugin would load it, without touching it.
public static class ConfigCheck
{
    public static IReadOnlyList<string> Run(string configDirectory, string? spawnsDirectory, string pluginLangDirectory)
    {
        var problems = new List<string>();
        var copy = Path.Combine(Path.GetTempPath(), $"retakev4-check-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(copy);
            if (Directory.Exists(configDirectory))
            {
                foreach (var file in Directory.GetFiles(configDirectory, "*.json"))
                {
                    File.Copy(file, Path.Combine(copy, Path.GetFileName(file)));
                }
            }
            var logger = new CollectingLogger();
            var store = new JsonConfigStore(copy);
            foreach (var module in ModuleCatalog.CreateAll())
            {
                module.LoadConfig(store, logger);
            }
            problems.AddRange(logger.Warnings);
        }
        finally
        {
            Directory.Delete(copy, true);
        }
        problems.AddRange(LangOverrideLoader.Read(configDirectory, pluginLangDirectory).Problems.Select(p => $"lang/{p}"));
        if (spawnsDirectory is not null && Directory.Exists(spawnsDirectory))
        {
            var store = new SpawnFileStore(spawnsDirectory);
            foreach (var map in Directory.GetFiles(spawnsDirectory, "*.json").Select(Path.GetFileNameWithoutExtension).OfType<string>().Order(StringComparer.Ordinal))
            {
                var load = store.Load(map);
                problems.AddRange(load.Issues.Select(i => $"spawns/{map}: {i}"));
                if (load.IsLegacy)
                {
                    problems.Add($"spawns/{map}: legacy format, convert it with RetakeV4.SpawnMigrator");
                }
            }
        }
        return problems;
    }

    private sealed class CollectingLogger : ILogger
    {
        public List<string> Warnings { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Warning)
            {
                Warnings.Add(formatter(state, exception));
            }
        }
    }
}
```

Note : un fichier absent est écrit par `JsonConfigStore` dans la copie temporaire seulement ; le message « config version … is older » d'un fichier ancien est un avertissement, donc un problème signalé (voulu pour un pack).

- [ ] **Step 4: The tool and the release asset**

`tools/RetakeV4.ConfigCheck/RetakeV4.ConfigCheck.csproj` (copie de celui de `ConfigExporter`, plus les textes du plugin) :

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="CounterStrikeSharp.API" Version="1.0.370" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\RetakeV4\RetakeV4.csproj" />
    <ProjectReference Include="..\..\src\RetakeV4.Contracts\RetakeV4.Contracts.csproj" />
  </ItemGroup>
</Project>
```

`Program.cs` :

```csharp
using RetakeV4.Configuration;

if (args.Length is < 1 or > 2)
{
    Console.Error.WriteLine("Usage: RetakeV4.ConfigCheck <configs/plugins/RetakeV4 directory> [<plugins/RetakeV4/spawns directory>]");
    return 2;
}
var problems = ConfigCheck.Run(args[0], args.Length == 2 ? args[1] : null, Path.Combine(AppContext.BaseDirectory, "lang"));
foreach (var problem in problems)
{
    Console.WriteLine(problem);
}
Console.WriteLine(problems.Count == 0 ? "Configuration OK" : $"{problems.Count} problem(s)");
return problems.Count == 0 ? 0 : 1;
```

Vérifier que `lang/*.json` arrive dans `bin/` de l'outil (via la référence de projet) ; sinon ajouter `<None Include="..\..\src\RetakeV4\lang\*.json" Link="lang\%(Filename)%(Extension)" CopyToOutputDirectory="PreserveNewest" />`.

`dotnet sln RetakeV4.sln add tools/RetakeV4.ConfigCheck/RetakeV4.ConfigCheck.csproj`.

`scripts/package-release.ps1`, avant `Write-Host "Release ready..."` :

```powershell
$checkDir = Join-Path $root "artifacts/configcheck"
if (Test-Path $checkDir) { Remove-Item $checkDir -Recurse -Force }
dotnet publish (Join-Path $root "tools/RetakeV4.ConfigCheck") -c Release -o $checkDir --nologo
if ($LASTEXITCODE -ne 0) { throw "ConfigCheck publish failed" }
if (-not (Test-Path (Join-Path $checkDir "lang/fr.json"))) { throw "ConfigCheck has no lang files" }
Compress-Archive -Path (Join-Path $checkDir "*") -DestinationPath (Join-Path $release "RetakeV4-$Version-configcheck.zip")
```

(Le workflow de release publie déjà `artifacts/release/*.zip`.)

- [ ] **Step 5: Run tests, build and the release script**

Run: `dotnet build RetakeV4.sln -c Release && dotnet test RetakeV4.sln && pwsh -NoProfile -File scripts/package-release.ps1 -Version 0.0.0-check`
Expected: 0 warning, tests verts, trois zips dans `artifacts/release/` dont `RetakeV4-0.0.0-check-configcheck.zip` ; `dotnet artifacts/configcheck/RetakeV4.ConfigCheck.dll artifacts/dev/addons/counterstrikesharp/configs/plugins/RetakeV4 artifacts/dev/addons/counterstrikesharp/plugins/RetakeV4/spawns` affiche `Configuration OK`.

- [ ] **Step 6: Commit**

```bash
git add src/RetakeV4/Configuration/ConfigCheck.cs tools/RetakeV4.ConfigCheck RetakeV4.sln scripts/package-release.ps1 tests/RetakeV4.Integration.Tests/Configuration/ConfigCheckTests.cs
git commit -m "feat: RetakeV4.ConfigCheck, vérification d'une config serveur (modules, textes, spawns), publiée avec la release"
```

### Task 8: Version 4.3.0 et documentation

**Files:**
- Modify: `src/RetakeV4/RetakeV4.csproj` (Version), `src/RetakeV4/RetakeV4Plugin.cs:28` (ModuleVersion), `README.md`, `docs/CHECKLIST-INGAME.md`, `CLAUDE.md`

- [ ] **Step 1: Version**

`<Version>4.3.0</Version>` et `ModuleVersion => "4.3.0"`.

- [ ] **Step 2: README — section « Personnaliser son serveur »**

Contenu, en français, au ton du README existant :
- Textes : créer `addons/counterstrikesharp/configs/plugins/RetakeV4/lang/fr.json` avec seulement les clés à changer (liste des clés dans `plugins/RetakeV4/lang/fr.json`), exemple avec `core.prefix`, `core.prefix_alert`, `core.prefix_help` ; conservé aux mises à jour ; `{0}`… ne peuvent qu'être repris du texte d'origine ; erreurs dans les logs au démarrage.
- `announcements.json` : exemple complet (2 messages, une map, accueil), `IntervalSeconds` ≥ 30.
- `links.json` : exemple `Message` et exemple `Lines` (`!regles`).
- Vérifier sa config : `dotnet RetakeV4.ConfigCheck.dll <configs> [<spawns>]` depuis le zip `-configcheck`.

- [ ] **Step 3: Checklist en jeu, section 4.3.0**

```markdown
## 4.3.0 — personnalisation

- [ ] Sans `lang/` ni annonces : préfixes et messages identiques à 4.2.1, aucune annonce.
- [ ] `lang/fr.json` avec `core.prefix` changé : le nouveau préfixe apparaît, un joueur en anglais garde l'ancien.
- [ ] Clé inconnue dans `lang/fr.json` : avertissement au démarrage, le reste fonctionne.
- [ ] `announcements.json` avec 2 messages et `IntervalSeconds` 30 : un message toutes les 30 s, jamais deux fois le même de suite ; liste de la map courante utilisée.
- [ ] Accueil : reçu une fois en rejoignant une équipe, pas après un changement d'équipe ni un changement de map, de nouveau après reconnexion.
- [ ] `!regles` avec `Lines` : chaque ligne avec le préfixe d'aide.
- [ ] Refus de permission (`!scramble` sans droit) : préfixe d'alerte.
```

- [ ] **Step 4: CLAUDE.md**

Ajouter aux règles : « Textes remplaçables : `configs/plugins/RetakeV4/lang/<langue>.json`, fusion dans `TextOverrides` (Domain) ; contenu serveur (annonces, liens) via `ITextService.ChatContent*`, jamais via `lang/`. » et à la structure : « `tools/RetakeV4.ConfigCheck` : vérifie une config serveur (`dotnet run --project tools/RetakeV4.ConfigCheck -- <configs> [<spawns>]`), publié avec chaque release. »

- [ ] **Step 5: Full verification**

Run: `dotnet build RetakeV4.sln -c Release && dotnet test RetakeV4.sln && dotnet test tests/RetakeV4.Domain.Tests -p:CollectCoverage=true -p:Include="[RetakeV4.Domain]*" -p:Threshold=80 -p:ThresholdType=line`
Expected: 0 warning, tout passe, couverture ≥ 80 %.

- [ ] **Step 6: Commit**

```bash
git add -A src/RetakeV4/RetakeV4.csproj src/RetakeV4/RetakeV4Plugin.cs README.md docs/CHECKLIST-INGAME.md CLAUDE.md
git commit -m "docs: personnalisation serveur, checklist 4.3.0 ; version 4.3.0"
```

La publication de 4.3.0 (PR, CI, fusion, tag) suit `superpowers:finishing-a-development-branch` après la revue finale ; la partie 2 a besoin de la release `v4.3.0` publiée (tâche 12).

---

## Partie 2 — agora-retake

Dépôt local : `C:\Users\alice\Documents\Mon-Code\plugincs2\agora-retake` (`gh repo clone NeuTroNBZh/agora-retake` s'il n'existe pas). Source V3 de référence : ce même dépôt au tag `v3.1.1-final`.

### Task 9: Archiver la 3.1.1 et poser le squelette du pack

**Files:**
- Delete: tout le contenu V3 (`CS2Retake/`, `CS2Retake.Tests/`, `CS2Retake.sln`, `RELEASE_NOTES_V3.0.1.md`, `docs/`)
- Create: `retakev4.version`, `.gitignore`, `pack/addons/counterstrikesharp/configs/plugins/RetakeV4/.gitkeep`
- Keep: `LICENSE`, `CHANGELOG.md` (complété en tâche 13)

- [ ] **Step 1: Archive**

```bash
git checkout main && git pull
git tag -a v3.1.1-final -m "Agora-Retake 3.1.1, dernière version V3"
git branch v3
git push origin v3 v3.1.1-final
git checkout -b feat/retakev4-pack
```

Expected: `git ls-remote origin v3 v3.1.1-final` montre les deux références.

- [ ] **Step 2: Replace the content**

```bash
git rm -r -q CS2Retake CS2Retake.Tests CS2Retake.sln RELEASE_NOTES_V3.0.1.md docs
```

`retakev4.version` : `4.3.0`
`.gitignore` :

```
artifacts/
work/
```

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "chore: la 3.1.1 est archivée (branche v3, tag v3.1.1-final), le dépôt devient un pack RetakeV4"
```

### Task 10: Textes, annonces et liens Agora

**Files:**
- Create: `pack/addons/counterstrikesharp/configs/plugins/RetakeV4/lang/fr.json`, `lang/en.json`, `announcements.json`, `links.json`

- [ ] **Step 1: `announcements.json`** (valeurs reprises de `CS2RetakeConfig.cs` de la 3.1.1)

```json
{
  "Version": 1,
  "Enabled": true,
  "IntervalSeconds": 420,
  "Messages": [
    "Merci d'être sur Agora-Retake. Tape !agora pour tous les liens utiles.",
    "Merci pour votre présence sur Agora-Retake. Tape !commandes pour la liste complète.",
    "Besoin des liens serveur ? Utilise !agora ou !discord.",
    "Merci de jouer sur Agora-Retake. Liens utiles via !agora."
  ],
  "MapMessages": {
    "de_mirage": [
      "Bienvenue sur Mirage ! Pensez à couvrir le milieu. Tape !agora pour les liens.",
      "Mirage chargée ! Qui tient mid CT ?"
    ],
    "de_inferno": [
      "Inferno en jeu ! Attention aux molotovs sur les sites. Tape !commandes pour l'aide.",
      "Bananes ou appartements ? Choisis ton camp sur Inferno !"
    ],
    "de_nuke": [
      "Nuke ! Rappel : le site B est en sous-sol. Tape !site pour le site du serveur.",
      "Le ramp rush est de sortie sur Nuke !"
    ],
    "de_dust2": [
      "Dust2 classique ! Cat CT ou long A ? Tape !discord pour rejoindre notre Discord.",
      "Le pistol rush cat est en route sur Dust2 !"
    ]
  },
  "Welcome": "Bienvenue sur Agora-Retake ! Tape !commandes pour la liste des commandes et !agora pour les liens utiles."
}
```

- [ ] **Step 2: `links.json`**

```json
{
  "Version": 1,
  "Enabled": true,
  "Links": [
    {
      "Commands": ["agora", "agoralinks"],
      "Lines": [
        "Site : {lightblue}https://agora-retake.fr{default}",
        "SkinChanger : {lightblue}https://skin-changer.agora-retake.fr{default}",
        "Discord : {lightblue}https://discord.gg/CkyU2Wehsw{default}"
      ]
    },
    { "Commands": ["dis", "discord"], "Message": "Discord : {lightblue}https://discord.gg/CkyU2Wehsw{default}" },
    { "Commands": ["skin", "skinchanger"], "Message": "SkinChanger : {lightblue}https://skin-changer.agora-retake.fr{default}" },
    { "Commands": ["site"], "Message": "Site : {lightblue}https://agora-retake.fr{default}" },
    {
      "Commands": ["commandes", "commands", "help", "aide"],
      "Lines": [
        "--- Commandes Agora-Retake ---",
        "!guns / !weapon : choisir ses armes",
        "!awp : être volontaire AWP",
        "!agora : tous les liens utiles",
        "!dis / !discord : lien Discord",
        "!skin / !skinchanger : lien SkinChanger",
        "!site : lien du site",
        "!regles / !rules : règles du serveur",
        "Créateur : {gold}NeuTroNBZh{default}"
      ]
    },
    {
      "Commands": ["regles", "rules", "regle", "rule"],
      "Lines": [
        "--- Règles du serveur ---",
        "1. Respectez tous les joueurs : insultes et harcèlement = ban.",
        "2. Pas de cheat, wallhack ou bhop automatique.",
        "3. Parlez français ou anglais dans le chat vocal.",
        "4. Ne bloquez pas volontairement vos coéquipiers.",
        "5. Profitez du jeu et amusez-vous bien ! Retake by NeuTroNBZh."
      ]
    }
  ]
}
```

Vérifier : `site` n'entre pas en conflit avec une commande RetakeV4 (le module Plant enregistre-t-il `css_site` ? `grep -rn "\"css_site\"\|\"site\"" src` dans CS2-RetakeV4 au tag `v4.3.0`) ; si oui, retirer `site` des alias et le noter dans le CHANGELOG du pack.

- [ ] **Step 3: `lang/fr.json` et `lang/en.json`**

Préfixes (identiques dans les deux fichiers) :

```json
{
  "core.prefix": "{default}[{gold}Agora-Retake{default}]",
  "core.prefix_alert": "{default}[{red}!{default} {gold}Agora-Retake{default}]",
  "core.prefix_help": "{default}[{lightblue}?{default} {gold}Agora-Retake{default}]"
}
```

Puis reformulations : pour chaque message joueur de la 3.1.1 (au tag `v3.1.1-final` : `CS2Retake/Utils/MessageUtils.cs`, `CS2Retake/Managers/*.cs`, `CS2Retake/Allocators/Implementations/CommandAllocator/**/*.cs`, InstaDefuse), trouver la clé V4 de même sens dans `src/RetakeV4/lang/fr.json` (CS2-RetakeV4 au tag `v4.3.0`) ; si la formulation Agora diffère par le ton ou le contenu (pas seulement la ponctuation), ajouter la clé avec le texte Agora en gardant exactement les `{n}` du texte V4 qui ont un équivalent. `en.json` reçoit les mêmes clés avec la traduction anglaise. Lister dans le message de commit les clés remplacées.

- [ ] **Step 4: Check with the RetakeV4 release tool**

```bash
gh release download v4.3.0 -R NeuTroNBZh/CS2-RetakeV4 -p "RetakeV4-4.3.0-configcheck.zip" -D work
pwsh -NoProfile -Command "Expand-Archive work/RetakeV4-4.3.0-configcheck.zip work/configcheck -Force"
dotnet work/configcheck/RetakeV4.ConfigCheck.dll pack/addons/counterstrikesharp/configs/plugins/RetakeV4
```

Expected: `Configuration OK`.

- [ ] **Step 5: Commit**

```bash
git add pack
git commit -m "feat: textes, préfixes, annonces et liens Agora"
```

### Task 11: Réglages et spawns propres à Agora

**Files:**
- Create (seulement si écart) : `pack/addons/counterstrikesharp/configs/plugins/RetakeV4/<module>.json`, `pack/addons/counterstrikesharp/plugins/RetakeV4/spawns/<map>.json`

- [ ] **Step 1: Settings diff**

Exporter les défauts V4 : `dotnet run --project <CS2-RetakeV4>/tools/RetakeV4.ConfigExporter -- work/v4defaults` (CS2-RetakeV4 au tag `v4.3.0`). Comparer avec la 3.1.1 (`CS2RetakeConfig.cs`, `FeatureConfig.cs`, `CommandAllocatorConfig.cs`, `FullBuyConfig.cs`, `MidConfig.cs`, `PistolConfig.cs`, `cfg/`), au minimum :

| 3.1.1 | V4 |
|---|---|
| `MaxPlayers`, `TeamBalanceRatio`, `ScrambleAfterSubsequentTerroristRoundWins`, `EnableSwitchOnRoundWin` | `teams.json` : `MaxPlayers`, `TeamBalanceRatio`, `ScrambleAfterTWins`, `SwitchTeamsOnCtWin` |
| `InstaDefuse*` | `instadefuse.json` |
| AWP (`AWPChanceCT/T`, `EnableAWPChance`, seuil de joueurs), kits, Zeus, pools d'armes | `roundtypes.json`, `grenades.json`, `allocation.json` |
| `cfg/*.cfg` | `core.json` (`ExecConfig`) et le cfg livré |

Pour chaque écart, copier le fichier V4 par défaut dans le pack et n'y changer que les valeurs Agora. Aucun écart : aucun fichier. Écrire le tableau des écarts trouvés dans `CHANGELOG.md` (tâche 13).

- [ ] **Step 2: Spawns diff**

```bash
for f in work/v3/CS2Retake/spawns/*.json; do
  dotnet run --project <CS2-RetakeV4>/tools/RetakeV4.SpawnMigrator -- "$f" "work/spawns/$(basename "$f")"
done
```

(`work/v3` = `git worktree add work/v3 v3.1.1-final`.) Pour chaque map, comparer `work/spawns/<map>.json` avec `<CS2-RetakeV4>/src/RetakeV4/spawns/<map>.json` après normalisation (`python -c "import json,sys; print(json.dumps(json.load(open(sys.argv[1])), sort_keys=True))"`). Copier dans `pack/addons/counterstrikesharp/plugins/RetakeV4/spawns/` uniquement les maps différentes.

- [ ] **Step 3: Check**

Run: `dotnet work/configcheck/RetakeV4.ConfigCheck.dll pack/addons/counterstrikesharp/configs/plugins/RetakeV4 pack/addons/counterstrikesharp/plugins/RetakeV4/spawns`
Expected: `Configuration OK`.

- [ ] **Step 4: Commit**

```bash
git add pack
git commit -m "feat: réglages et spawns Agora qui diffèrent des défauts RetakeV4"
```

(Si aucun écart ni spawn différent : pas de commit, noter « aucun écart » pour le CHANGELOG.)

### Task 12: `build.ps1` et CI

**Files:**
- Create: `build.ps1`, `.github/workflows/release.yml`, `tests/broken-key/addons/counterstrikesharp/configs/plugins/RetakeV4/lang/fr.json`

**Interfaces:**
- Produces: `pwsh build.ps1 [-Check] [-RetakeV4Dir <dir>] [-PackVersion <x>]` ; zips `artifacts/agora-retake-<packVersion>.zip` et `artifacts/agora-retake-<packVersion>-no-configs.zip`.

- [ ] **Step 1: Write `build.ps1`**

```powershell
param(
    [switch]$Check,
    [string]$RetakeV4Dir,
    [string]$PackVersion,
    [string]$PackRoot = (Join-Path $PSScriptRoot "pack")
)
$ErrorActionPreference = "Stop"

$version = (Get-Content (Join-Path $PSScriptRoot "retakev4.version") -Raw).Trim()
if (-not $PackVersion) { $PackVersion = "$version-agora.0" }
$work = Join-Path $PSScriptRoot "work"
$out = Join-Path $PSScriptRoot "artifacts"
New-Item -ItemType Directory -Force $work, $out | Out-Null

if (-not $RetakeV4Dir) {
    $RetakeV4Dir = Join-Path $work "retakev4-$version"
    if (-not (Test-Path (Join-Path $RetakeV4Dir "RetakeV4-$version.zip"))) {
        gh release download "v$version" -R NeuTroNBZh/CS2-RetakeV4 -p "RetakeV4-$version*.zip" -D $RetakeV4Dir --clobber
        if ($LASTEXITCODE -ne 0) { throw "Download of RetakeV4 $version failed" }
    }
}

$checker = Join-Path $work "configcheck-$version"
Expand-Archive (Join-Path $RetakeV4Dir "RetakeV4-$version-configcheck.zip") $checker -Force
$configs = Join-Path $PackRoot "addons/counterstrikesharp/configs/plugins/RetakeV4"
$spawns = Join-Path $PackRoot "addons/counterstrikesharp/plugins/RetakeV4/spawns"
$spawnArgs = if (Test-Path $spawns) { @($spawns) } else { @() }
dotnet (Join-Path $checker "RetakeV4.ConfigCheck.dll") $configs @spawnArgs
if ($LASTEXITCODE -ne 0) { throw "The Agora pack does not pass RetakeV4 $version checks" }
if ($Check) { return }

function New-Package([string]$Source, [string]$Name, [string[]]$Overlay) {
    $stage = Join-Path $work "stage-$Name"
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
    Expand-Archive $Source $stage
    foreach ($dir in $Overlay) {
        if (Test-Path (Join-Path $PackRoot $dir)) {
            Copy-Item (Join-Path $PackRoot $dir) (Join-Path $stage (Split-Path $dir -Parent)) -Recurse -Force
        }
    }
    $zip = Join-Path $out "$Name.zip"
    if (Test-Path $zip) { Remove-Item $zip }
    Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip
    Write-Host "Built $zip"
}

New-Package (Join-Path $RetakeV4Dir "RetakeV4-$version.zip") "agora-retake-$PackVersion" @("addons/counterstrikesharp/configs/plugins/RetakeV4", "addons/counterstrikesharp/plugins/RetakeV4/spawns")
New-Package (Join-Path $RetakeV4Dir "RetakeV4-$version-no-configs.zip") "agora-retake-$PackVersion-no-configs" @("addons/counterstrikesharp/plugins/RetakeV4/spawns")
```

- [ ] **Step 2: Negative fixture and checks**

`tests/broken-key/addons/counterstrikesharp/configs/plugins/RetakeV4/lang/fr.json` :

```json
{ "core.this_key_does_not_exist": "x" }
```

Run: `pwsh -NoProfile -File build.ps1 -Check`
Expected: `Configuration OK`, code 0.

Run: `pwsh -NoProfile -File build.ps1 -Check -PackRoot tests/broken-key`
Expected: échec, la sortie contient `core.this_key_does_not_exist`.

Run: `pwsh -NoProfile -File build.ps1 -PackVersion 4.3.0-agora.0`
Expected: deux zips dans `artifacts/` ; le complet contient `addons/counterstrikesharp/configs/plugins/RetakeV4/announcements.json` avec les messages Agora et `lang/fr.json` ; le `-no-configs` ne contient aucun chemin `configs/`.

- [ ] **Step 3: CI**

`.github/workflows/release.yml` :

```yaml
name: Pack

on:
  push:
    branches: [main]
    tags: ["v*"]
  pull_request:

permissions:
  contents: write

jobs:
  pack:
    runs-on: ubuntu-latest
    env:
      # CS2-RetakeV4 is private: a fine-grained token with read access to its contents and releases.
      GH_TOKEN: ${{ secrets.RETAKEV4_TOKEN }}
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 10.0.x
      - name: Check the pack
        shell: pwsh
        run: ./build.ps1 -Check
      - name: Broken pack is refused
        shell: pwsh
        run: |
          try { ./build.ps1 -Check -PackRoot tests/broken-key; $passed = $true } catch { $passed = $false }
          if ($passed) { throw "the broken pack passed" }
      - name: Build
        if: startsWith(github.ref, 'refs/tags/v')
        shell: pwsh
        run: ./build.ps1 -PackVersion ("${{ github.ref_name }}".TrimStart("v"))
      - uses: softprops/action-gh-release@v2
        if: startsWith(github.ref, 'refs/tags/v')
        with:
          files: artifacts/*.zip
          generate_release_notes: true
```

Vérifier l'étape « Broken pack is refused » en local : `pwsh -NoProfile -Command 'try { ./build.ps1 -Check -PackRoot tests/broken-key; $passed = $true } catch { $passed = $false }; if ($passed) { throw "the broken pack passed" }'` doit réussir (code 0).

- [ ] **Step 4: Commit**

```bash
git add build.ps1 .github tests
git commit -m "ci: construction du pack sur une release RetakeV4 fixée, vérification par RetakeV4.ConfigCheck"
```

### Task 13: README, CHANGELOG, PR et release

**Files:**
- Create/Modify: `README.md`, `CHANGELOG.md`

- [ ] **Step 1: README** (français) : ce qu'est le pack (RetakeV4 + personnalisation Agora), installation (zip complet à la racine du serveur, CounterStrikeSharp ≥ 1.0.370 et Metamod compatible), mise à jour (zip `-no-configs`), personnaliser (fichiers du pack et leur rôle), construire (`build.ps1`, secret `RETAKEV4_TOKEN` tant que CS2-RetakeV4 est privé), migrer depuis la 3.1.1 (`css_retake_import_v3 <chemin de cs2retake.db>`), lien vers la branche `v3`.

- [ ] **Step 2: CHANGELOG** : entrée `[4.3.0-agora.1] — <date>` : passage à RetakeV4 4.3.0, liste des personnalisations conservées (préfixes, annonces, accueil, liens, règles, achat natif et warmup inclus dans RetakeV4), écarts de réglages et spawns de la tâche 11, clés de texte remplacées de la tâche 10.

- [ ] **Step 3: Commit, PR**

```bash
git add README.md CHANGELOG.md
git commit -m "docs: README et CHANGELOG du pack Agora 4.3.0-agora.1"
git push -u origin feat/retakev4-pack
gh pr create --base main --title "feat: Agora-Retake sur RetakeV4 4.3.0 (pack de configuration)" --body-file <corps>
```

Le secret `RETAKEV4_TOKEN` doit exister dans le dépôt pour que la CI passe : le demander à l'utilisateur. Fusion et tag `v4.3.0-agora.1` : sur accord de l'utilisateur.
