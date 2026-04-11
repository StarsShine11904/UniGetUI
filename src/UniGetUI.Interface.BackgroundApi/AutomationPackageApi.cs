using UniGetUI.Core.Logging;
using UniGetUI.PackageEngine;
using UniGetUI.PackageEngine.Enums;
using UniGetUI.PackageEngine.Interfaces;
using UniGetUI.PackageEngine.PackageClasses;
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
}

public sealed class AutomationPackageOperationResult
{
    public string Status { get; set; } = "success";
    public string Command { get; set; } = "";
    public string OperationStatus { get; set; } = "";
    public string? Message { get; set; }
    public AutomationPackageInfo? Package { get; set; }
    public IReadOnlyList<string> Output { get; set; } = [];
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
        return GetManagers(managerName)
            .SelectMany(manager => manager.GetInstalledPackages())
            .DistinctBy(GetPackageIdentity)
            .Select(ToAutomationPackageInfo)
            .OrderBy(package => package.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(package => package.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<AutomationPackageInfo> ListUpgradablePackages(string? managerName = null)
    {
        return GetManagers(managerName)
            .SelectMany(manager => manager.GetAvailableUpdates())
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

        var package = FindUpgradablePackage(request);
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
            Output = operation.GetOutput().Select(line => line.Item1).ToArray(),
        };
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

    private static IPackage FindInstalledPackage(AutomationPackageActionRequest request)
    {
        foreach (var manager in GetManagers(request.ManagerName))
        {
            var package = manager.GetInstalledPackages().FirstOrDefault(candidate =>
                MatchesIdentity(candidate, request)
            );
            if (package is not null)
            {
                return package;
            }
        }

        throw new InvalidOperationException(
            $"No installed package matching id \"{request.PackageId}\" was found."
        );
    }

    private static IPackage FindUpgradablePackage(AutomationPackageActionRequest request)
    {
        foreach (var manager in GetManagers(request.ManagerName))
        {
            var package = manager.GetAvailableUpdates().FirstOrDefault(candidate =>
                MatchesIdentity(candidate, request)
            );
            if (package is not null)
            {
                return package;
            }
        }

        throw new InvalidOperationException(
            $"No upgradable package matching id \"{request.PackageId}\" was found."
        );
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
