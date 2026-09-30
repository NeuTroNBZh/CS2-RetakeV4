using RetakeV4.Domain.Spawns;

if (args.Length != 2)
{
    Console.Error.WriteLine("usage: RetakeV4.SpawnMigrator <inputDir> <outputDir>");
    return 1;
}

Directory.CreateDirectory(args[1]);
var failures = 0;
foreach (var file in Directory.GetFiles(args[0], "*.json").Order())
{
    var map = Path.GetFileNameWithoutExtension(file);
    var result = SpawnFileFormat.Parse(File.ReadAllText(file));
    foreach (var issue in result.Issues)
    {
        Console.Error.WriteLine($"{map}: {issue}");
    }
    if (result.Spawns.Count == 0)
    {
        failures++;
        continue;
    }
    File.WriteAllText(Path.Combine(args[1], map + ".json"), SpawnFileFormat.Serialize(map, result.Spawns));
    Console.WriteLine($"{map}: {result.Spawns.Count} spawns");
}
return failures == 0 ? 0 : 2;
