using System.Text;
using System.Text.Json;
using RetakeV4.Domain.Common;

namespace RetakeV4.Domain.Loadouts;

// What the web panel may offer: computed with the in-game menu's rule so both always agree.
public static class CatalogExport
{
    public const int FormatVersion = 1;

    private static readonly TeamSide[] Sides = { TeamSide.T, TeamSide.CT };

    public static string Build(IReadOnlyList<RoundTypeDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteStartArray("roundTypes");
            foreach (var definition in definitions)
            {
                WriteRoundType(writer, definition);
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteRoundType(Utf8JsonWriter writer, RoundTypeDefinition definition)
    {
        writer.WriteStartObject();
        writer.WriteString("name", definition.Name);
        writer.WriteStartObject("teams");
        foreach (var side in Sides)
        {
            var defaults = definition.DefaultFor(side);
            writer.WriteStartObject(side.ToString());
            WriteList(writer, "primaries", WeaponMenu.Options(definition, side, WeaponSlot.Primary));
            WriteList(writer, "secondaries", WeaponMenu.Options(definition, side, WeaponSlot.Secondary));
            writer.WriteString("defaultPrimary", defaults.Primary);
            writer.WriteString("defaultSecondary", defaults.Secondary);
            writer.WriteBoolean("awp", definition.Awp.Enabled);
            writer.WriteEndObject();
        }
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void WriteList(Utf8JsonWriter writer, string name, IReadOnlyList<string> values)
    {
        writer.WriteStartArray(name);
        foreach (var value in values)
        {
            writer.WriteStringValue(value);
        }
        writer.WriteEndArray();
    }
}
