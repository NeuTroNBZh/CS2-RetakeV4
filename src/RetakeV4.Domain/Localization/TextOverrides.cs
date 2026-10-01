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
