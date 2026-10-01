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
