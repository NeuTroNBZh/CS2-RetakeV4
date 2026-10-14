using System.Text.Json;
using System.Text.RegularExpressions;

namespace RetakeV4.Integration.Tests.Docs;

// The stats documentation follows the contract: every view and every column of views.v1.json and views.v2.json is named
// in STATS-CATALOG.md, and the GRANT examples cover every view. A column name used by several views must be written
// `view.column` (a bare `column` would hide a missing view); a name unique to one view may stay bare.
public class StatsDocsTests
{
    private static string Doc(string file) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "docs", file));

    private static Dictionary<string, List<string>> Contract(string file)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "contract", "stats", file)));
        return document.RootElement.GetProperty("views").EnumerateObject()
            .ToDictionary(v => v.Name, v => v.Value.EnumerateArray().Select(c => c.GetString()!).ToList());
    }

    [Theory]
    [InlineData("views.v1.json")]
    [InlineData("views.v2.json")]
    public void Catalog_NamesEveryViewAndColumn(string contract)
    {
        var catalog = Doc("STATS-CATALOG.md");
        var missing = new List<string>();
        var owners = Contract("views.v1.json").Concat(Contract("views.v2.json"))
            .SelectMany(v => v.Value.Select(column => (column, view: v.Key)))
            .GroupBy(x => x.column)
            .ToDictionary(g => g.Key, g => g.Select(x => x.view).Distinct().Count());
        foreach (var (view, columns) in Contract(contract))
        {
            if (!Regex.IsMatch(catalog, $@"`{Regex.Escape(view)}(\.[a-z0-9_]+)?`"))
            {
                missing.Add(view);
            }
            missing.AddRange(columns
                .Where(column => !Regex.IsMatch(catalog, owners[column] > 1
                    ? $@"`{Regex.Escape(view)}\.{Regex.Escape(column)}`"
                    : $@"`([a-z0-9_]+\.)?{Regex.Escape(column)}`"))
                .Select(column => $"{view}.{column}"));
        }
        Assert.Empty(missing);
    }

    [Fact]
    public void Catalog_QualifiedColumns_ExistInTheContracts()
    {
        var contracts = Contract("views.v1.json").Concat(Contract("views.v2.json")).ToDictionary(v => v.Key, v => v.Value);
        var stale = Regex.Matches(Doc("STATS-CATALOG.md"), @"`(v2?_stats_[a-z_]+)\.([a-z0-9_]+)`")
            .Select(m => (view: m.Groups[1].Value, column: m.Groups[2].Value))
            .Where(x => !contracts.TryGetValue(x.view, out var columns) || !columns.Contains(x.column))
            .Select(x => $"{x.view}.{x.column}")
            .Distinct()
            .ToList();
        Assert.Empty(stale);
    }

    [Theory]
    [InlineData("STATS.md")]
    [InlineData("STATS-INTEGRATION.md")]
    public void GrantExamples_CoverEveryView(string doc)
    {
        var text = Doc(doc);
        var views = Contract("views.v1.json").Keys.Concat(Contract("views.v2.json").Keys).ToList();
        Assert.All(views, view => Assert.Contains($"GRANT SELECT ON retakev4.{view} TO 'stats_reader'@'%';", text, StringComparison.Ordinal));
    }

    // The radar table and the JavaScript RADARS object of the replay recipe must list the same maps and values.
    [Fact]
    public void ReplayRecipe_RadarTable_MatchesTheJavaScriptObject()
    {
        var text = Doc("STATS-INTEGRATION.md");
        var table = Regex.Matches(text, @"^\| (de_[a-z0-9_]+) \| (-?[0-9.]+) \| (-?[0-9.]+) \| ([0-9.]+) \|", RegexOptions.Multiline)
            .ToDictionary(m => m.Groups[1].Value, m => new[] { m.Groups[2].Value, m.Groups[3].Value, m.Groups[4].Value }.Select(ParseNumber).ToArray());
        var script = Regex.Match(text, @"const RADARS = \{(.*?)\};", RegexOptions.Singleline).Groups[1].Value;
        var radars = Regex.Matches(script, @"(de_[a-z0-9_]+): \[(-?[0-9.]+), (-?[0-9.]+), ([0-9.]+)\]")
            .ToDictionary(m => m.Groups[1].Value, m => new[] { m.Groups[2].Value, m.Groups[3].Value, m.Groups[4].Value }.Select(ParseNumber).ToArray());
        Assert.NotEmpty(table);
        Assert.Equal(table.Keys.OrderBy(k => k), radars.Keys.OrderBy(k => k));
        Assert.All(table, row => Assert.Equal(row.Value, radars[row.Key]));
    }

    private static double ParseNumber(string value) => double.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
}
