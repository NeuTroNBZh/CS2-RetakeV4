using System.Text.RegularExpressions;
using RetakeV4.Configuration;

namespace RetakeV4.Modules.Links;

public sealed partial class LinksConfigValidator : IConfigValidator<LinksConfig>
{
    public const int MaxLinks = 32;
    public const int MaxMessageLength = 512;
    public const int MaxLines = 16;

    // Commands the plugin already registers (weapon menu aliases, !awp): a link must never shadow them.
    private static readonly HashSet<string> Reserved = new(StringComparer.Ordinal)
    {
        "guns", "gans", "gun", "g", "gns", "gnus", "weapon", "waepon", "weapons", "waepons", "waffen", "menu", "allocator", "select", "awp",
    };

    [GeneratedRegex("^[a-z0-9_]{1,32}$")]
    private static partial Regex CommandName();

    public ValidationResult<LinksConfig> Validate(LinksConfig config, LinksConfig defaults, string file)
    {
        var issues = new List<ConfigIssue>();
        if (config.Links is null)
        {
            issues.Add(new ConfigIssue(file, nameof(LinksConfig.Links), "missing; using an empty list"));
            return new ValidationResult<LinksConfig>(config with { Links = defaults.Links }, issues);
        }
        var used = new HashSet<string>(StringComparer.Ordinal);
        var kept = new List<LinkConfig>();
        for (var i = 0; i < config.Links.Count; i++)
        {
            var key = $"Links[{i}]";
            if (Check(config.Links[i], used, kept.Count) is { } problem)
            {
                issues.Add(new ConfigIssue(file, key, $"{problem}; link ignored"));
                continue;
            }
            var commands = config.Links[i].Commands.Select(Normalize).ToList();
            used.UnionWith(commands);
            kept.Add(config.Links[i] with { Commands = commands, Lines = CleanLines(config.Links[i]) });
        }
        return new ValidationResult<LinksConfig>(config with { Links = kept }, issues);
    }

    private static string? Check(LinkConfig? link, IReadOnlySet<string> used, int keptCount)
    {
        if (link is null)
        {
            return "empty entry";
        }
        if (keptCount >= MaxLinks)
        {
            return $"more than {MaxLinks} links";
        }
        var commands = (link.Commands ?? Array.Empty<string>()).Select(Normalize).ToList();
        if (commands.Count == 0)
        {
            return "no command";
        }
        if (commands.FirstOrDefault(c => !IsAllowed(c, used)) is { } refused)
        {
            return $"command '{refused}' is invalid, reserved or already used";
        }
        if (commands.Distinct(StringComparer.Ordinal).Count() != commands.Count)
        {
            return "duplicate command";
        }
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
    }

    private static IReadOnlyList<string> CleanLines(LinkConfig link) =>
        (link.Lines ?? Array.Empty<string>()).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();

    private static bool IsAllowed(string command, IReadOnlySet<string> used) =>
        CommandName().IsMatch(command)
        && !command.StartsWith("retake", StringComparison.Ordinal)
        && !Reserved.Contains(command)
        && !used.Contains(command);

    private static string Normalize(string? command) => (command ?? string.Empty).Trim().ToLowerInvariant();
}
