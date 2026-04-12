using UniGetUI.Core.Logging;
using UniGetUI.Core.SettingsEngine;
using UniGetUI.PackageEngine;
using UniGetUI.PackageEngine.Interfaces;

namespace UniGetUI.Interface;

public sealed class AutomationAppLogEntry
{
    public string Time { get; set; } = "";
    public string Severity { get; set; } = "";
    public string Content { get; set; } = "";
}

public sealed class AutomationOperationHistoryEntry
{
    public string Content { get; set; } = "";
}

public sealed class AutomationManagerLogTask
{
    public int Index { get; set; }
    public string[] Lines { get; set; } = [];
}

public sealed class AutomationManagerLogInfo
{
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Version { get; set; } = "";
    public AutomationManagerLogTask[] Tasks { get; set; } = [];
}

public static class AutomationLogsApi
{
    public static IReadOnlyList<AutomationAppLogEntry> ListAppLog(int level = 4)
    {
        return Logger.GetLogs()
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Content) && !ShouldSkip(entry.Severity, level))
            .Select(entry => new AutomationAppLogEntry
            {
                Time = entry.Time.ToString("O"),
                Severity = entry.Severity.ToString().ToLowerInvariant(),
                Content = entry.Content,
            })
            .ToArray();
    }

    public static IReadOnlyList<AutomationOperationHistoryEntry> ListOperationHistory()
    {
        return Settings.GetValue(Settings.K.OperationHistory)
            .Split('\n')
            .Select(line => line.Replace("\r", "").Replace("\n", "").Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => new AutomationOperationHistoryEntry { Content = line })
            .ToArray();
    }

    public static IReadOnlyList<AutomationManagerLogInfo> ListManagerLogs(
        string? managerName = null,
        bool verbose = false
    )
    {
        return ResolveManagers(managerName)
            .Select(manager => new AutomationManagerLogInfo
            {
                Name = manager.Name,
                DisplayName = manager.DisplayName,
                Version = manager.Status.Version,
                Tasks = manager.TaskLogger.Operations
                    .Select((operation, index) => new AutomationManagerLogTask
                    {
                        Index = index,
                        Lines = operation
                            .AsColoredString(verbose)
                            .Where(line => !string.IsNullOrWhiteSpace(line))
                            .Select(StripColorCode)
                            .Where(line => !string.IsNullOrWhiteSpace(line))
                            .ToArray(),
                    })
                    .Where(task => task.Lines.Length > 0)
                    .ToArray(),
            })
            .ToArray();
    }

    private static IReadOnlyList<IPackageManager> ResolveManagers(string? managerName)
    {
        var managers = PEInterface.Managers
            .Where(manager =>
                string.IsNullOrWhiteSpace(managerName)
                || manager.Name.Equals(managerName, StringComparison.OrdinalIgnoreCase)
                || manager.DisplayName.Equals(managerName, StringComparison.OrdinalIgnoreCase)
            )
            .OrderBy(manager => manager.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (managers.Length == 0)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(managerName)
                    ? "No package managers are available."
                    : $"No package manager matching \"{managerName}\" was found."
            );
        }

        return managers;
    }

    private static bool ShouldSkip(LogEntry.SeverityLevel severity, int level) =>
        level switch
        {
            <= 1 => severity != LogEntry.SeverityLevel.Error,
            2 => severity is LogEntry.SeverityLevel.Debug
                      or LogEntry.SeverityLevel.Info
                      or LogEntry.SeverityLevel.Success,
            3 => severity is LogEntry.SeverityLevel.Debug or LogEntry.SeverityLevel.Info,
            4 => severity == LogEntry.SeverityLevel.Debug,
            _ => false,
        };

    private static string StripColorCode(string line)
    {
        return line.Length > 1 && char.IsDigit(line[0]) ? line[1..] : line;
    }
}
