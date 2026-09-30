using Microsoft.Extensions.Logging;

namespace RetakeV4.Configuration;

public static class ConfigLogging
{
    public static void Report(ILogger logger, IEnumerable<ConfigIssue> issues)
    {
        foreach (var issue in issues)
        {
            logger.LogWarning("Config {File} [{Key}]: {Message}", issue.File, issue.Key, issue.Message);
        }
    }
}
