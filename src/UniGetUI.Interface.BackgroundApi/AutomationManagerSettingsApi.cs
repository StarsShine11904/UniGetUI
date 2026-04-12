using UniGetUI.Core.SettingsEngine;
using UniGetUI.PackageEngine;
using UniGetUI.PackageEngine.Classes.Manager;
using UniGetUI.PackageEngine.Enums;
using UniGetUI.PackageEngine.Interfaces;
using UniGetUI.PackageEngine.Operations;
using UniGetUI.PackageOperations;

namespace UniGetUI.Interface;

public sealed class AutomationManagerCapabilitiesInfo
{
    public bool CanRunAsAdmin { get; set; }
    public bool CanSkipIntegrityChecks { get; set; }
    public bool CanRunInteractively { get; set; }
    public bool CanRemoveDataOnUninstall { get; set; }
    public bool CanDownloadInstaller { get; set; }
    public bool SupportsCustomVersions { get; set; }
    public bool SupportsCustomArchitectures { get; set; }
    public bool SupportsCustomScopes { get; set; }
    public bool SupportsPreRelease { get; set; }
    public bool SupportsCustomLocations { get; set; }
    public bool SupportsCustomSources { get; set; }
    public bool MustInstallSourcesAsAdmin { get; set; }
}

public sealed class AutomationManagerInfo
{
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public bool Enabled { get; set; }
    public bool Ready { get; set; }
    public string ExecutablePath { get; set; } = "";
    public string ExecutableArguments { get; set; } = "";
    public AutomationManagerCapabilitiesInfo Capabilities { get; set; } = new();
}

public sealed class AutomationSourceInfo
{
    public string Manager { get; set; } = "";
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Url { get; set; } = "";
    public int? PackageCount { get; set; }
    public string UpdateDate { get; set; } = "";
    public bool IsKnown { get; set; }
    public bool IsConfigured { get; set; }
}

public sealed class AutomationSourceRequest
{
    public string ManagerName { get; set; } = "";
    public string SourceName { get; set; } = "";
    public string? SourceUrl { get; set; }
}

public sealed class AutomationSourceOperationResult
{
    public string Status { get; set; } = "success";
    public string Command { get; set; } = "";
    public string OperationStatus { get; set; } = "";
    public string? Message { get; set; }
    public AutomationSourceInfo? Source { get; set; }
}

public sealed class AutomationSettingInfo
{
    public string Name { get; set; } = "";
    public string Key { get; set; } = "";
    public bool IsSet { get; set; }
    public bool BoolValue { get; set; }
    public string StringValue { get; set; } = "";
    public bool HasStringValue { get; set; }
}

public sealed class AutomationSettingValueRequest
{
    public string SettingKey { get; set; } = "";
    public bool? Enabled { get; set; }
    public string? Value { get; set; }
}

public static class AutomationManagerSettingsApi
{
    private static readonly HashSet<Settings.K> HiddenSettings =
    [
        Settings.K.CurrentSessionToken,
        Settings.K.TelemetryClientToken,
    ];

    public static IReadOnlyList<AutomationManagerInfo> ListManagers()
    {
        return PEInterface.Managers
            .OrderBy(manager => manager.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(ToManagerInfo)
            .ToArray();
    }

    public static IReadOnlyList<AutomationSourceInfo> ListSources(string? managerName = null)
    {
        return ResolveManagers(managerName)
            .SelectMany(GetMergedSources)
            .OrderBy(source => source.Manager, StringComparer.OrdinalIgnoreCase)
            .ThenBy(source => source.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static async Task<AutomationSourceOperationResult> AddSourceAsync(
        AutomationSourceRequest request
    )
    {
        var manager = ResolveManager(request.ManagerName);
        var source = ResolveSourceForAdd(manager, request);

        using var operation = new AddSourceOperation(source);
        await operation.MainThread();

        return ToSourceOperationResult("add-source", operation, source);
    }

    public static async Task<AutomationSourceOperationResult> RemoveSourceAsync(
        AutomationSourceRequest request
    )
    {
        var manager = ResolveManager(request.ManagerName);
        var source = ResolveSourceForRemove(manager, request);

        using var operation = new RemoveSourceOperation(source);
        await operation.MainThread();

        return ToSourceOperationResult("remove-source", operation, source);
    }

    public static IReadOnlyList<AutomationSettingInfo> ListSettings()
    {
        return Enum.GetValues<Settings.K>()
            .Where(IsVisibleSetting)
            .OrderBy(setting => setting.ToString(), StringComparer.OrdinalIgnoreCase)
            .Select(ToSettingInfo)
            .ToArray();
    }

    public static AutomationSettingInfo GetSetting(string settingKey)
    {
        return ToSettingInfo(ResolveSettingKey(settingKey));
    }

    public static AutomationSettingInfo SetSetting(AutomationSettingValueRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var key = ResolveSettingKey(request.SettingKey);
        var hasEnabled = request.Enabled.HasValue;
        var hasValue = request.Value is not null;

        if (hasEnabled == hasValue)
        {
            throw new InvalidOperationException(
                "Provide exactly one of enabled or value when setting a setting."
            );
        }

        if (hasValue)
        {
            Settings.SetValue(key, request.Value ?? "");
        }
        else
        {
            Settings.Set(key, request.Enabled!.Value);
        }

        return ToSettingInfo(key);
    }

    public static AutomationSettingInfo ClearSetting(string settingKey)
    {
        var key = ResolveSettingKey(settingKey);
        Settings.SetValue(key, string.Empty);
        return ToSettingInfo(key);
    }

    public static void ResetSettingsPreservingSession()
    {
        string currentSessionToken = Settings.GetValue(Settings.K.CurrentSessionToken);
        Settings.ResetSettings();

        if (!string.IsNullOrWhiteSpace(currentSessionToken))
        {
            Settings.SetValue(Settings.K.CurrentSessionToken, currentSessionToken);
        }
    }

    private static AutomationManagerInfo ToManagerInfo(IPackageManager manager)
    {
        return new AutomationManagerInfo
        {
            Name = manager.Name,
            DisplayName = manager.DisplayName,
            Enabled = manager.IsEnabled(),
            Ready = manager.IsReady(),
            ExecutablePath = manager.Status.ExecutablePath,
            ExecutableArguments = manager.Status.ExecutableCallArgs,
            Capabilities = new AutomationManagerCapabilitiesInfo
            {
                CanRunAsAdmin = manager.Capabilities.CanRunAsAdmin,
                CanSkipIntegrityChecks = manager.Capabilities.CanSkipIntegrityChecks,
                CanRunInteractively = manager.Capabilities.CanRunInteractively,
                CanRemoveDataOnUninstall = manager.Capabilities.CanRemoveDataOnUninstall,
                CanDownloadInstaller = manager.Capabilities.CanDownloadInstaller,
                SupportsCustomVersions = manager.Capabilities.SupportsCustomVersions,
                SupportsCustomArchitectures = manager.Capabilities.SupportsCustomArchitectures,
                SupportsCustomScopes = manager.Capabilities.SupportsCustomScopes,
                SupportsPreRelease = manager.Capabilities.SupportsPreRelease,
                SupportsCustomLocations = manager.Capabilities.SupportsCustomLocations,
                SupportsCustomSources = manager.Capabilities.SupportsCustomSources,
                MustInstallSourcesAsAdmin = manager.Capabilities.Sources.MustBeInstalledAsAdmin,
            },
        };
    }

    private static IReadOnlyList<AutomationSourceInfo> GetMergedSources(IPackageManager manager)
    {
        Dictionary<string, AutomationSourceInfo> sources = new(StringComparer.OrdinalIgnoreCase);

        foreach (var knownSource in manager.Properties.KnownSources)
        {
            sources[GetSourceIdentity(knownSource)] = ToSourceInfo(
                knownSource,
                isKnown: true,
                isConfigured: false
            );
        }

        foreach (var configuredSource in GetConfiguredSources(manager))
        {
            var sourceKey = GetSourceIdentity(configuredSource);
            if (sources.TryGetValue(sourceKey, out var existing))
            {
                existing.IsConfigured = true;
                existing.PackageCount = configuredSource.PackageCount;
                existing.UpdateDate = configuredSource.UpdateDate ?? "";
                existing.Url = configuredSource.Url.ToString();
            }
            else
            {
                sources[sourceKey] = ToSourceInfo(
                    configuredSource,
                    isKnown: false,
                    isConfigured: true
                );
            }
        }

        return sources.Values.ToArray();
    }

    private static string GetSourceIdentity(IManagerSource source)
    {
        return $"{source.Manager.Name}|{source.Name}|{source.Url}";
    }

    private static AutomationSourceInfo ToSourceInfo(
        IManagerSource source,
        bool isKnown,
        bool isConfigured
    )
    {
        return new AutomationSourceInfo
        {
            Manager = source.Manager.Name,
            Name = source.Name,
            DisplayName = source.AsString_DisplayName,
            Url = source.Url.ToString(),
            PackageCount = source.PackageCount,
            UpdateDate = source.UpdateDate ?? "",
            IsKnown = isKnown,
            IsConfigured = isConfigured,
        };
    }

    private static AutomationSourceOperationResult ToSourceOperationResult(
        string command,
        AbstractOperation operation,
        IManagerSource source
    )
    {
        return new AutomationSourceOperationResult
        {
            Status = operation.Status == OperationStatus.Succeeded ? "success" : "error",
            Command = command,
            OperationStatus = operation.Status.ToString().ToLowerInvariant(),
            Message = operation.Status switch
            {
                OperationStatus.Succeeded => null,
                OperationStatus.Canceled => "The operation was canceled.",
                _ => operation.GetOutput().LastOrDefault().Item1,
            },
            Source = ToSourceInfo(source, isKnown: true, isConfigured: true),
        };
    }

    private static IReadOnlyList<IPackageManager> ResolveManagers(string? managerName)
    {
        var managers = PEInterface.Managers
            .Where(manager =>
                string.IsNullOrWhiteSpace(managerName)
                || manager.Name.Equals(managerName, StringComparison.OrdinalIgnoreCase)
                || manager.DisplayName.Equals(managerName, StringComparison.OrdinalIgnoreCase)
            )
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

    private static IPackageManager ResolveManager(string managerName)
    {
        if (string.IsNullOrWhiteSpace(managerName))
        {
            throw new InvalidOperationException("The manager parameter is required.");
        }

        return ResolveManagers(managerName).First();
    }

    private static IManagerSource ResolveSourceForAdd(
        IPackageManager manager,
        AutomationSourceRequest request
    )
    {
        if (string.IsNullOrWhiteSpace(request.SourceName))
        {
            throw new InvalidOperationException("The source name is required.");
        }

        if (!string.IsNullOrWhiteSpace(request.SourceUrl))
        {
            return new ManagerSource(
                manager,
                request.SourceName,
                new Uri(request.SourceUrl, UriKind.Absolute)
            );
        }

        return manager.Properties.KnownSources.FirstOrDefault(source =>
                SourceMatches(source, request.SourceName, null)
            )
            ?? throw new InvalidOperationException(
                $"No known source matching \"{request.SourceName}\" was found for manager \"{manager.Name}\"."
            );
    }

    private static IManagerSource ResolveSourceForRemove(
        IPackageManager manager,
        AutomationSourceRequest request
    )
    {
        if (string.IsNullOrWhiteSpace(request.SourceName))
        {
            throw new InvalidOperationException("The source name is required.");
        }

        var configuredSource = GetConfiguredSources(manager).FirstOrDefault(source =>
            SourceMatches(source, request.SourceName, request.SourceUrl)
        );
        if (configuredSource is not null)
        {
            return configuredSource;
        }

        if (!string.IsNullOrWhiteSpace(request.SourceUrl))
        {
            return new ManagerSource(
                manager,
                request.SourceName,
                new Uri(request.SourceUrl, UriKind.Absolute)
            );
        }

        var knownSource = manager.Properties.KnownSources.FirstOrDefault(source =>
            SourceMatches(source, request.SourceName, null)
        );
        if (knownSource is not null)
        {
            return knownSource;
        }

        return new ManagerSource(manager, request.SourceName, new Uri("https://localhost/"));
    }

    private static bool SourceMatches(
        IManagerSource source,
        string sourceName,
        string? sourceUrl
    )
    {
        return source.Name.Equals(sourceName, StringComparison.OrdinalIgnoreCase)
            || source.AsString_DisplayName.Equals(sourceName, StringComparison.OrdinalIgnoreCase)
            || (
                !string.IsNullOrWhiteSpace(sourceUrl)
                && source.Url.ToString().Equals(sourceUrl, StringComparison.OrdinalIgnoreCase)
            );
    }

    private static IReadOnlyList<IManagerSource> GetConfiguredSources(IPackageManager manager)
    {
        try
        {
            return manager.SourcesHelper.GetSources();
        }
        catch (NotImplementedException)
        {
            return [];
        }
    }

    private static bool IsVisibleSetting(Settings.K setting)
    {
        return setting != Settings.K.Unset && !HiddenSettings.Contains(setting);
    }

    private static Settings.K ResolveSettingKey(string settingKey)
    {
        if (string.IsNullOrWhiteSpace(settingKey))
        {
            throw new InvalidOperationException("The setting key is required.");
        }

        foreach (var candidate in Enum.GetValues<Settings.K>())
        {
            if (!IsVisibleSetting(candidate))
            {
                continue;
            }

            if (
                candidate.ToString().Equals(settingKey, StringComparison.OrdinalIgnoreCase)
                || Settings.ResolveKey(candidate).Equals(
                    settingKey,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return candidate;
            }
        }

        throw new InvalidOperationException($"No setting matching \"{settingKey}\" was found.");
    }

    private static AutomationSettingInfo ToSettingInfo(Settings.K setting)
    {
        string stringValue = Settings.GetValue(setting);
        bool isSet = Settings.Get(setting);

        return new AutomationSettingInfo
        {
            Name = setting.ToString(),
            Key = Settings.ResolveKey(setting),
            IsSet = isSet,
            BoolValue = isSet,
            StringValue = stringValue,
            HasStringValue = !string.IsNullOrWhiteSpace(stringValue),
        };
    }
}
