using UniGetUI.Core.Logging;
using UniGetUI.PackageEngine;
using UniGetUI.PackageEngine.Enums;
using UniGetUI.PackageEngine.Interfaces;
using UniGetUI.PackageEngine.PackageLoader;
using UniGetUI.PackageEngine.PackageClasses;
using UniGetUI.PackageEngine.Classes.Packages.Classes;
using UniGetUI.PackageEngine.Operations;
using UniGetUI.PackageEngine.Serializable;
using UniGetUI.PackageOperations;

namespace UniGetUI.Interface;

public sealed class AutomationPackageInfo
{
    public string Name { get; set; } = "";
    public string Id { get; set; } = "";
    public string Version { get; set; } = "";
    public string NewVersion { get; set; } = "";
    public string Source { get; set; } = "";
    public string Manager { get; set; } = "";
    public bool IsUpgradable { get; set; }
}

public sealed class AutomationPackageActionRequest
{
    public string PackageId { get; set; } = "";
    public string? ManagerName { get; set; }
    public string? PackageSource { get; set; }
    public string? Version { get; set; }
    public string? Scope { get; set; }
    public bool? PreRelease { get; set; }
    public bool? Elevated { get; set; }
    public bool? Interactive { get; set; }
    public bool? SkipHash { get; set; }
    public bool? RemoveData { get; set; }
    public string? Architecture { get; set; }
    public string? InstallLocation { get; set; }
    public string? OutputPath { get; set; }
}

public sealed class AutomationPackageOperationResult
{
    public string Status { get; set; } = "success";
    public string Command { get; set; } = "";
    public string OperationStatus { get; set; } = "";
    public string? Message { get; set; }
    public AutomationPackageInfo? Package { get; set; }
    public string? OutputPath { get; set; }
    public IReadOnlyList<string> Output { get; set; } = [];
}

public sealed class AutomationPackageDependencyInfo
{
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public bool Mandatory { get; set; }
}

public sealed class AutomationPackageDetailsInfo
{
    public string Name { get; set; } = "";
    public string Id { get; set; } = "";
    public string Version { get; set; } = "";
    public string NewVersion { get; set; } = "";
    public string Source { get; set; } = "";
    public string Manager { get; set; } = "";
    public string Description { get; set; } = "";
    public string Publisher { get; set; } = "";
    public string Author { get; set; } = "";
    public string HomepageUrl { get; set; } = "";
    public string License { get; set; } = "";
    public string LicenseUrl { get; set; } = "";
    public string InstallerUrl { get; set; } = "";
    public string InstallerHash { get; set; } = "";
    public string InstallerType { get; set; } = "";
    public long InstallerSize { get; set; }
    public string ManifestUrl { get; set; } = "";
    public string UpdateDate { get; set; } = "";
    public string ReleaseNotes { get; set; } = "";
    public string ReleaseNotesUrl { get; set; } = "";
    public string IconUrl { get; set; } = "";
    public string InstallLocation { get; set; } = "";
    public string? IgnoredVersion { get; set; }
    public IReadOnlyList<string> Tags { get; set; } = [];
    public IReadOnlyList<string> Versions { get; set; } = [];
    public IReadOnlyList<string> Screenshots { get; set; } = [];
    public IReadOnlyList<AutomationPackageDependencyInfo> Dependencies { get; set; } = [];
}

public sealed class AutomationIgnoredUpdateInfo
{
    public string IgnoredId { get; set; } = "";
    public string Manager { get; set; } = "";
    public string PackageId { get; set; } = "";
    public string Version { get; set; } = "";
    public bool IgnoreAllVersions { get; set; }
    public bool IsPauseUntilDate { get; set; }
    public string PauseUntil { get; set; } = "";
}

internal enum AutomationPackageLookupMode
{
    Search,
    Installed,
    Upgradable,
    InstalledOrUpgradable,
    Any,
}

public static class AutomationPackageApi
{
    public static IReadOnlyList<AutomationPackageInfo> SearchPackages(
        string query,
        string? managerName = null,
        int maxResults = 50
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        maxResults = Math.Clamp(maxResults, 1, 500);

        return GetManagers(managerName)
            .SelectMany(manager => manager.FindPackages(query))
            .DistinctBy(GetPackageIdentity)
            .Select(ToAutomationPackageInfo)
            .OrderBy(package => package.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(package => package.Id, StringComparer.OrdinalIgnoreCase)
            .Take(maxResults)
            .ToArray();
    }

    public static IReadOnlyList<AutomationPackageInfo> ListInstalledPackages(string? managerName = null)
    {
        return GetInstalledPackagesSnapshot(managerName)
            .DistinctBy(GetPackageIdentity)
            .Select(ToAutomationPackageInfo)
            .OrderBy(package => package.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(package => package.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<AutomationPackageInfo> ListUpgradablePackages(string? managerName = null)
    {
        return GetUpgradablePackagesSnapshot(managerName)
            .DistinctBy(GetPackageIdentity)
            .Select(ToAutomationPackageInfo)
            .OrderBy(package => package.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(package => package.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static Task<AutomationPackageOperationResult> InstallPackageAsync(
        AutomationPackageActionRequest request
    )
    {
        ArgumentNullException.ThrowIfNull(request);

        var package = FindSearchResult(request);
        return ExecuteOperationAsync(
            "install-package",
            package,
            request,
            (pkg, options) => new InstallPackageOperation(pkg, options)
        );
    }

    public static Task<AutomationPackageOperationResult> UpdatePackageAsync(
        AutomationPackageActionRequest request
    )
    {
        ArgumentNullException.ThrowIfNull(request);

        var package = FindUpgradablePackageOrInstalledPackage(request);
        return ExecuteOperationAsync(
            "update-package",
            package,
            request,
            (pkg, options) => new UpdatePackageOperation(pkg, options)
        );
    }

    public static Task<AutomationPackageOperationResult> UninstallPackageAsync(
        AutomationPackageActionRequest request
    )
    {
        ArgumentNullException.ThrowIfNull(request);

        var package = FindInstalledPackage(request);
        return ExecuteOperationAsync(
            "uninstall-package",
            package,
            request,
            (pkg, options) => new UninstallPackageOperation(pkg, options)
        );
    }

    public static async Task<AutomationPackageOperationResult> DownloadPackageAsync(
        AutomationPackageActionRequest request
    )
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.OutputPath))
        {
            throw new InvalidOperationException(
                "The outputPath parameter is required when downloading a package."
            );
        }

        var package = FindAnyPackage(request);
        if (!package.Manager.Capabilities.CanDownloadInstaller)
        {
            throw new InvalidOperationException(
                $"The manager \"{package.Manager.Name}\" does not support installer downloads."
            );
        }

        using var operation = new DownloadOperation(package, request.OutputPath);
        await operation.MainThread();

        return CreateOperationResult(
            "download-package",
            package,
            operation,
            operation.DownloadLocation
        );
    }

    public static Task<AutomationPackageOperationResult> ReinstallPackageAsync(
        AutomationPackageActionRequest request
    )
    {
        ArgumentNullException.ThrowIfNull(request);

        var package = FindInstalledPackage(request);
        return ExecuteOperationAsync(
            "reinstall-package",
            package,
            request,
            (pkg, options) => new InstallPackageOperation(pkg, options)
        );
    }

    public static async Task<AutomationPackageOperationResult> UninstallThenReinstallPackageAsync(
        AutomationPackageActionRequest request
    )
    {
        ArgumentNullException.ThrowIfNull(request);

        var package = FindInstalledPackage(request);
        var options = await InstallOptionsFactory.LoadApplicableAsync(package);
        ApplyRequestOptions(options, request);

        using var uninstallOperation = new UninstallPackageOperation(package, options);
        using var installOperation = new InstallPackageOperation(
            package,
            options,
            req: uninstallOperation
        );
        await installOperation.MainThread();

        return CreateOperationResult("uninstall-then-reinstall-package", package, installOperation);
    }

    public static async Task<AutomationPackageDetailsInfo> GetPackageDetailsAsync(
        AutomationPackageActionRequest request
    )
    {
        ArgumentNullException.ThrowIfNull(request);

        var package = FindAnyPackage(request);
        await package.Details.Load();

        return new AutomationPackageDetailsInfo
        {
            Name = package.Name,
            Id = package.Id,
            Version = package.VersionString,
            NewVersion = package.IsUpgradable ? package.NewVersionString : "",
            Source = package.Source.AsString_DisplayName,
            Manager = package.Manager.Name,
            Description = package.Details.Description ?? "",
            Publisher = package.Details.Publisher ?? "",
            Author = package.Details.Author ?? "",
            HomepageUrl = package.Details.HomepageUrl?.ToString() ?? "",
            License = package.Details.License ?? "",
            LicenseUrl = package.Details.LicenseUrl?.ToString() ?? "",
            InstallerUrl = package.Details.InstallerUrl?.ToString() ?? "",
            InstallerHash = package.Details.InstallerHash ?? "",
            InstallerType = package.Details.InstallerType ?? "",
            InstallerSize = package.Details.InstallerSize,
            ManifestUrl = package.Details.ManifestUrl?.ToString() ?? "",
            UpdateDate = package.Details.UpdateDate ?? "",
            ReleaseNotes = package.Details.ReleaseNotes ?? "",
            ReleaseNotesUrl = package.Details.ReleaseNotesUrl?.ToString() ?? "",
            IconUrl = package.GetIconUrl().ToString(),
            InstallLocation = package.Manager.DetailsHelper.GetInstallLocation(package) ?? "",
            IgnoredVersion = await package.GetIgnoredUpdatesVersionAsync(),
            Tags = package.Details.Tags.Where(tag => !string.IsNullOrWhiteSpace(tag)).ToArray(),
            Versions = package.Manager.DetailsHelper.GetVersions(package),
            Screenshots = package
                .GetScreenshots()
                .Select(screenshot => screenshot.ToString())
                .ToArray(),
            Dependencies = package.Details.Dependencies
                .Select(dependency => new AutomationPackageDependencyInfo
                {
                    Name = dependency.Name,
                    Version = dependency.Version,
                    Mandatory = dependency.Mandatory,
                })
                .ToArray(),
        };
    }

    public static IReadOnlyList<string> GetPackageVersions(AutomationPackageActionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var package = FindAnyPackage(request);
        return package.Manager.DetailsHelper.GetVersions(package);
    }

    public static IReadOnlyList<AutomationIgnoredUpdateInfo> ListIgnoredUpdates()
    {
        return IgnoredUpdatesDatabase.GetDatabase()
            .Select(entry =>
            {
                string[] parts = entry.Key.Split('\\', 2);
                string version = entry.Value;

                return new AutomationIgnoredUpdateInfo
                {
                    IgnoredId = entry.Key,
                    Manager = parts.Length > 0 ? parts[0] : "",
                    PackageId = parts.Length > 1 ? parts[1] : "",
                    Version = version,
                    IgnoreAllVersions = version == "*",
                    IsPauseUntilDate = version.StartsWith("<", StringComparison.Ordinal),
                    PauseUntil = version.StartsWith("<", StringComparison.Ordinal)
                        ? version[1..]
                        : "",
                };
            })
            .OrderBy(entry => entry.Manager, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.PackageId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static async Task<BackgroundApiCommandResult> IgnorePackageUpdateAsync(
        AutomationPackageActionRequest request
    )
    {
        ArgumentNullException.ThrowIfNull(request);

        var package = FindPackageForStateMutation(request);
        await package.AddToIgnoredUpdatesAsync(
            string.IsNullOrWhiteSpace(request.Version) ? "*" : request.Version
        );
        await RefreshUpgradablePackagesSnapshotAsync();

        return new BackgroundApiCommandResult
        {
            Command = "ignore-package",
            Message = $"Ignored updates for {package.Id}.",
        };
    }

    public static async Task<BackgroundApiCommandResult> RemoveIgnoredUpdateAsync(
        AutomationPackageActionRequest request
    )
    {
        ArgumentNullException.ThrowIfNull(request);

        var package = TryFindPackageForStateMutation(request);
        if (package is not null)
        {
            await package.RemoveFromIgnoredUpdatesAsync();
        }
        else
        {
            var manager = GetManagers(request.ManagerName).FirstOrDefault()
                ?? throw new InvalidOperationException(
                    "The manager parameter is required when removing an ignored update for a package that is not currently discoverable."
                );
            IgnoredUpdatesDatabase.Remove($"{manager.Properties.Name.ToLowerInvariant()}\\{request.PackageId}");
        }

        await RefreshUpgradablePackagesSnapshotAsync();
        return BackgroundApiCommandResult.Success("unignore-package");
    }

    private static async Task<AutomationPackageOperationResult> ExecuteOperationAsync(
        string command,
        IPackage package,
        AutomationPackageActionRequest request,
        Func<IPackage, InstallOptions, AbstractOperation> operationFactory
    )
    {
        var options = await InstallOptionsFactory.LoadApplicableAsync(package);
        ApplyRequestOptions(options, request);

        using var operation = operationFactory(package, options);
        await operation.MainThread();

        return CreateOperationResult(command, package, operation);
    }

    private static void ApplyRequestOptions(
        InstallOptions options,
        AutomationPackageActionRequest request
    )
    {
        if (!string.IsNullOrWhiteSpace(request.Version))
        {
            options.Version = request.Version;
        }

        if (!string.IsNullOrWhiteSpace(request.Scope))
        {
            options.InstallationScope = request.Scope;
        }

        if (request.PreRelease.HasValue)
        {
            options.PreRelease = request.PreRelease.Value;
        }

        if (request.Elevated.HasValue)
        {
            options.RunAsAdministrator = request.Elevated.Value;
        }

        if (request.Interactive.HasValue)
        {
            options.InteractiveInstallation = request.Interactive.Value;
        }

        if (request.SkipHash.HasValue)
        {
            options.SkipHashCheck = request.SkipHash.Value;
        }

        if (request.RemoveData.HasValue)
        {
            options.RemoveDataOnUninstall = request.RemoveData.Value;
        }

        if (!string.IsNullOrWhiteSpace(request.Architecture))
        {
            options.Architecture = request.Architecture;
        }

        if (!string.IsNullOrWhiteSpace(request.InstallLocation))
        {
            options.CustomInstallLocation = request.InstallLocation;
        }
    }

    internal static void ApplyRequestedOptions(
        InstallOptions options,
        AutomationPackageActionRequest request
    )
    {
        ApplyRequestOptions(options, request);
    }

    internal static IPackage ResolvePackage(
        AutomationPackageActionRequest request,
        AutomationPackageLookupMode lookupMode = AutomationPackageLookupMode.Any
    )
    {
        return lookupMode switch
        {
            AutomationPackageLookupMode.Search => FindSearchResult(request),
            AutomationPackageLookupMode.Installed => FindInstalledPackage(request),
            AutomationPackageLookupMode.Upgradable => FindUpgradablePackage(request),
            AutomationPackageLookupMode.InstalledOrUpgradable => FindUpgradablePackageOrInstalledPackage(
                request
            ),
            _ => FindAnyPackage(request),
        };
    }

    internal static AutomationPackageInfo CreateAutomationPackageInfo(IPackage package)
    {
        return ToAutomationPackageInfo(package);
    }

    internal static AutomationPackageOperationResult CreateOperationResult(
        string command,
        IPackage package,
        AbstractOperation operation,
        string? outputPath = null
    )
    {
        return new AutomationPackageOperationResult
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
            Package = ToAutomationPackageInfo(package),
            OutputPath = outputPath,
            Output = operation.GetOutput().Select(line => line.Item1).ToArray(),
        };
    }

    private static IPackage FindSearchResult(AutomationPackageActionRequest request)
    {
        foreach (var manager in GetManagers(request.ManagerName))
        {
            var package = manager.FindPackages(request.PackageId).FirstOrDefault(candidate =>
                MatchesIdentity(candidate, request)
            );
            if (package is not null)
            {
                return package;
            }
        }

        throw new InvalidOperationException(
            $"No package matching id \"{request.PackageId}\" was found."
        );
    }

    private static IPackage FindAnyPackage(AutomationPackageActionRequest request)
    {
        return TryFindPackageForStateMutation(request)
            ?? FindSearchResult(request);
    }

    private static IPackage FindInstalledPackage(AutomationPackageActionRequest request)
    {
        var package = GetInstalledPackagesSnapshot(request.ManagerName).FirstOrDefault(candidate =>
            MatchesIdentity(candidate, request)
        );
        if (package is not null)
        {
            return package;
        }

        throw new InvalidOperationException(
            $"No installed package matching id \"{request.PackageId}\" was found."
        );
    }

    private static IPackage FindUpgradablePackage(AutomationPackageActionRequest request)
    {
        var package = GetUpgradablePackagesSnapshot(request.ManagerName).FirstOrDefault(candidate =>
            MatchesIdentity(candidate, request)
        );
        if (package is not null)
        {
            return package;
        }

        throw new InvalidOperationException(
            $"No upgradable package matching id \"{request.PackageId}\" was found."
        );
    }

    private static IPackage FindUpgradablePackageOrInstalledPackage(
        AutomationPackageActionRequest request
    )
    {
        try
        {
            return FindUpgradablePackage(request);
        }
        catch (InvalidOperationException)
        {
            return FindInstalledPackage(request);
        }
    }

    private static IPackage FindPackageForStateMutation(AutomationPackageActionRequest request)
    {
        return TryFindPackageForStateMutation(request)
            ?? throw new InvalidOperationException(
                $"No package matching id \"{request.PackageId}\" was found."
            );
    }

    private static IPackage? TryFindPackageForStateMutation(AutomationPackageActionRequest request)
    {
        try
        {
            return FindUpgradablePackageOrInstalledPackage(request);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static IReadOnlyList<IPackage> GetInstalledPackagesSnapshot(string? managerName)
    {
        var loaderPackages = GetLoaderPackages(
            InstalledPackagesLoader.Instance,
            managerName,
            loader => loader.ReloadPackages()
        );
        if (loaderPackages.Count > 0)
        {
            return loaderPackages;
        }

        return GetManagers(managerName).SelectMany(manager => manager.GetInstalledPackages()).ToArray();
    }

    private static async Task RefreshUpgradablePackagesSnapshotAsync()
    {
        if (UpgradablePackagesLoader.Instance is null || UpgradablePackagesLoader.Instance.IsLoading)
        {
            return;
        }

        await UpgradablePackagesLoader.Instance.ReloadPackages();
    }

    private static IReadOnlyList<IPackage> GetUpgradablePackagesSnapshot(string? managerName)
    {
        var loaderPackages = GetLoaderPackages(
            UpgradablePackagesLoader.Instance,
            managerName,
            loader => loader.ReloadPackages()
        );
        if (loaderPackages.Count > 0)
        {
            return loaderPackages;
        }

        return GetManagers(managerName).SelectMany(manager => manager.GetAvailableUpdates()).ToArray();
    }

    private static IReadOnlyList<IPackage> GetLoaderPackages(
        AbstractPackageLoader? loader,
        string? managerName,
        Func<AbstractPackageLoader, Task> reload
    )
    {
        if (loader is null)
        {
            return [];
        }

        if (loader.Packages.Count > 0)
        {
            return loader.Packages.Where(package => MatchesManager(package.Manager, managerName)).ToArray();
        }

        if (!loader.IsLoaded && !loader.IsLoading)
        {
            reload(loader).GetAwaiter().GetResult();
        }

        return loader.Packages.Where(package => MatchesManager(package.Manager, managerName)).ToArray();
    }

    private static IReadOnlyList<IPackageManager> GetManagers(string? managerName)
    {
        var managers = PEInterface.Managers
            .Where(manager => manager.IsEnabled() && manager.IsReady())
            .Where(manager => MatchesManager(manager, managerName))
            .ToArray();

        if (managers.Length == 0)
        {
            if (string.IsNullOrWhiteSpace(managerName))
            {
                throw new InvalidOperationException("No ready package managers are available.");
            }

            throw new InvalidOperationException(
                $"No ready package manager matching \"{managerName}\" is available."
            );
        }

        return managers;
    }

    private static bool MatchesManager(IPackageManager manager, string? requestedManager)
    {
        return string.IsNullOrWhiteSpace(requestedManager)
            || manager.Name.Equals(requestedManager, StringComparison.OrdinalIgnoreCase)
            || manager.DisplayName.Equals(requestedManager, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesIdentity(IPackage package, AutomationPackageActionRequest request)
    {
        if (!package.Id.Equals(request.PackageId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(request.PackageSource)
            || package.Source.Name.Equals(request.PackageSource, StringComparison.OrdinalIgnoreCase)
            || package.Source.AsString_DisplayName.Equals(
                request.PackageSource,
                StringComparison.OrdinalIgnoreCase
            );
    }

    private static string GetPackageIdentity(IPackage package)
    {
        return $"{package.Manager.Name}|{package.Source.Name}|{package.Id}|{package.VersionString}|{package.NewVersionString}";
    }

    private static AutomationPackageInfo ToAutomationPackageInfo(IPackage package)
    {
        return new AutomationPackageInfo
        {
            Name = package.Name,
            Id = package.Id,
            Version = package.VersionString,
            NewVersion = package.IsUpgradable ? package.NewVersionString : "",
            Source = package.Source.AsString_DisplayName,
            Manager = package.Manager.Name,
            IsUpgradable = package.IsUpgradable,
        };
    }
}
