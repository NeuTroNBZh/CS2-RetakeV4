using Microsoft.Extensions.Logging.Abstractions;
using RetakeV4.Configuration;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: RetakeV4.ConfigExporter <output directory>");
    return 1;
}
foreach (var file in ConfigExport.Run(args[0], NullLogger.Instance))
{
    Console.WriteLine(file);
}
return 0;
