using System.Text.Json.Nodes;
using UniGetUI.Core.Data;
using UniGetUI.Core.SettingsEngine;
using UniGetUI.Core.SettingsEngine.SecureSettings;
using UniGetUI.Core.Tools;
using UniGetUI.Interface.Enums;
using UniGetUI.PackageEngine;
using UniGetUI.PackageEngine.Enums;
using UniGetUI.PackageEngine.Interfaces;
using UniGetUI.PackageEngine.Operations;
using UniGetUI.PackageEngine.PackageClasses;
using UniGetUI.PackageEngine.PackageLoader;
using UniGetUI.PackageEngine.Serializable;
using UniGetUI.PackageEngine.Classes.Serializable;

namespace UniGetUI.Interface;

public sealed class AutomationBundlePackageInfo
{
    public string Name { get; set; } = "";
    public string Id { get; set; } = "";
    public string Version { get; set; } = "";
    public string DisplayVersion { get; set; } = "";
    public string? SelectedVersion { get; set; }
    public string? Scope { get; set; }
    public bool PreRelease { get; set; }
    public string Source { get; set; } = "";
    public string Manager { get; set; } = "";
    public bool IsCompatible { get; set; }
    public bool IsInstalled { get; set; }
    public bool IsUpgradable { get; set; }
}

public sealed class AutomationBundleInfo
{
    public int PackageCount { get; set; }
    public IReadOnlyList<AutomationBundlePackageInfo> Packages { get; set; } = [];
}

public sealed class AutomationBundleImportRequest
{
    public string? Content { get; set; }
    public string? Path { get; set; }
    public string? Format { get; set; }
    public bool Append { get; set; }
}

public sealed class AutomationBundleExportRequest
{
    public string? Path { get; set; }
}

public sealed class AutomationBundlePackageRequest
{
    public string PackageId { get; set; } = "";
    public string? ManagerName { get; set; }
    public string? PackageSource { get; set; }
    public string? Version { get; set; }
    public string? Scope { get; set; }
    public bool? PreRelease { get; set; }
    public string? Selection { get; set; }
}

public sealed class AutomationBundleInstallRequest
{
    public bool? IncludeInstalled { get; set; }
    public bool? Elevated { get; set; }
    public bool? Interactive { get; set; }
    public bool? SkipHash { get; set; }
}

public sealed class AutomationBundleSecurityEntry
{
    public string PackageId { get; set; } = "";
    public string Line { get; set; } = "";
    public bool Allowed { get; set; }
}

public class AutomationBundleCommandResult
{
    public string Status { get; set; } = "success";
    public string Command { get; set; } = "";
    public string? Message { get; set; }
}

public sealed class AutomationBundleImportResult : AutomationBundleCommandResult
{
    public double SchemaVersion { get; set; }
    public string Format { get; set; } = "";
    public AutomationBundleInfo Bundle { get; set; } = new();
    public IReadOnlyList<AutomationBundleSecurityEntry> SecurityReport { get; set; } = [];
}

public sealed class AutomationBundleExportResult : AutomationBundleCommandResult
{
    public string Format { get; set; } = "";
    public string Content { get; set; } = "";
    public string? Path { get; set; }
    public AutomationBundleInfo Bundle { get; set; } = new();
}

public sealed class AutomationBundlePackageOperationResult : AutomationBundleCommandResult
{
    public AutomationBundlePackageInfo? Package { get; set; }
    public int RemovedCount { get; set; }
    public AutomationBundleInfo Bundle { get; set; } = new();
}

public sealed class AutomationBundleInstallResult : AutomationBundleCommandResult
{
    public int RequestedCount { get; set; }
    public int SucceededCount { get; set; }
    public int FailedCount { get; set; }
    public int SkippedCount { get; set; }
    public AutomationBundleInfo Bundle { get; set; } = new();
    public IReadOnlyList<AutomationPackageOperationResult> Results { get; set; } = [];
}

public static class AutomationBundleApi
{
    public static async Task<AutomationBundleInfo> GetCurrentBundleAsync()
    {
        return await BuildBundleInfoAsync(GetLoader().Packages);
    }

    public static BackgroundApiCommandResult ResetBundle()
    {
        GetLoader().ClearPackages();
        return BackgroundApiCommandResult.Success("reset-bundle");
    }

    public static async Task<AutomationBundleImportResult> ImportBundleAsync(
        AutomationBundleImportRequest request
    )
    {
        ArgumentNullException.ThrowIfNull(request);

        var loader = GetLoader();
        var format = ResolveImportFormat(request);
        var content = await ReadBundleContentAsync(request);

        if (!request.Append)
        {
            loader.ClearPackages();
        }

        var (schemaVersion, report) = await AddFromBundleAsync(content, format);
        return new AutomationBundleImportResult
        {
            Status = "success",
            Command = "import-bundle",
            SchemaVersion = schemaVersion,
            Format = format.ToString().ToLowerInvariant(),
            Bundle = await BuildBundleInfoAsync(loader.Packages),
            SecurityReport = FlattenReport(report),
        };
    }

    public static async Task<AutomationBundleExportResult> ExportBundleAsync(
        AutomationBundleExportRequest? request = null
    )
    {
        request ??= new AutomationBundleExportRequest();
        var packages = GetLoader().Packages;
        var content = await CreateBundleAsync(packages);
        var format = ResolveExportFormat(request.Path);

        if (!string.IsNullOrWhiteSpace(request.Path))
        {
            await File.WriteAllTextAsync(request.Path, content);
        }

        return new AutomationBundleExportResult
        {
            Status = "success",
            Command = "export-bundle",
            Format = format.ToString().ToLowerInvariant(),
            Path = request.Path,
            Content = content,
            Bundle = await BuildBundleInfoAsync(packages),
        };
    }

    public static async Task<AutomationBundlePackageOperationResult> AddPackageAsync(
        AutomationBundlePackageRequest request
    )
    {
        ArgumentNullException.ThrowIfNull(request);

        var loader = GetLoader();
        var package = await CreateBundlePackageAsync(request);
        await loader.AddPackagesAsync([package]);

        return new AutomationBundlePackageOperationResult
        {
            Status = "success",
            Command = "add-bundle-package",
            Package = await ToBundlePackageInfoAsync(package),
            Bundle = await BuildBundleInfoAsync(loader.Packages),
        };
    }

    public static async Task<AutomationBundlePackageOperationResult> RemovePackageAsync(
        AutomationBundlePackageRequest request
    )
    {
        ArgumentNullException.ThrowIfNull(request);

        var loader = GetLoader();
        var packages = loader.Packages;
        var toRemove = new List<IPackage>();
        foreach (var package in packages)
        {
            if (await MatchesBundleRequestAsync(package, request))
            {
                toRemove.Add(package);
            }
        }

        loader.RemoveRange(toRemove);

        return new AutomationBundlePackageOperationResult
        {
            Status = "success",
            Command = "remove-bundle-package",
            RemovedCount = toRemove.Count,
            Bundle = await BuildBundleInfoAsync(loader.Packages),
        };
    }

    public static async Task<AutomationBundleInstallResult> InstallBundleAsync(
        AutomationBundleInstallRequest? request = null
    )
    {
        request ??= new AutomationBundleInstallRequest();

        var packages = GetLoader().Packages;
        bool includeInstalled =
            request.IncludeInstalled ?? Settings.Get(Settings.K.InstallInstalledPackagesBundlesPage);
        List<AutomationPackageOperationResult> results = [];

        foreach (var package in packages)
        {
            if (package is not ImportedPackage imported)
            {
                results.Add(
                    new AutomationPackageOperationResult
                    {
                        Status = "error",
                        Command = "install-bundle",
                        OperationStatus = "invalid",
                        Message = "The bundle entry is incompatible and cannot be installed.",
                        Package = AutomationPackageApi.CreateAutomationPackageInfo(package),
                    }
                );
                continue;
            }

            if (!includeInstalled && package.Tag == PackageTag.AlreadyInstalled)
            {
                results.Add(
                    new AutomationPackageOperationResult
                    {
                        Status = "success",
                        Command = "install-bundle",
                        OperationStatus = "skipped",
                        Message = "The package is already installed and include-installed is disabled.",
                        Package = AutomationPackageApi.CreateAutomationPackageInfo(package),
                    }
                );
                continue;
            }

            var registeredPackage = await imported.RegisterAndGetPackageAsync();
            var bundleOptions = await imported.GetInstallOptions();
            var options = await InstallOptionsFactory.LoadApplicableAsync(
                registeredPackage,
                elevated: request.Elevated,
                interactive: request.Interactive,
                no_integrity: request.SkipHash,
                overridePackageOptions: bundleOptions
            );

            using var operation = new InstallPackageOperation(registeredPackage, options);
            await operation.MainThread();
            if (operation.Status == OperationStatus.Succeeded)
            {
                imported.SetTag(PackageTag.AlreadyInstalled);
            }
            results.Add(
                AutomationPackageApi.CreateOperationResult(
                    "install-bundle",
                    imported,
                    operation
                )
            );
        }

        return new AutomationBundleInstallResult
        {
            Status = results.Any(result => result.Status == "error") ? "error" : "success",
            Command = "install-bundle",
            RequestedCount = packages.Count,
            SucceededCount = results.Count(result =>
                result.Status == "success" && result.OperationStatus != "skipped"
            ),
            FailedCount = results.Count(result => result.Status == "error"),
            SkippedCount = results.Count(result => result.OperationStatus == "skipped"),
            Bundle = await BuildBundleInfoAsync(GetLoader().Packages),
            Results = results,
        };
    }

    private static PackageBundlesLoader GetLoader()
    {
        return PackageBundlesLoader.Instance
            ?? throw new InvalidOperationException("The package bundle loader is not available.");
    }

    private static async Task<AutomationBundleInfo> BuildBundleInfoAsync(
        IReadOnlyList<IPackage> packages
    )
    {
        var bundlePackages = await Task.WhenAll(packages.Select(ToBundlePackageInfoAsync));
        var sortedPackages = bundlePackages
            .OrderBy(package => package.Manager, StringComparer.OrdinalIgnoreCase)
            .ThenBy(package => package.Id, StringComparer.OrdinalIgnoreCase)
            .ThenBy(package => package.Version, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new AutomationBundleInfo
        {
            PackageCount = sortedPackages.Length,
            Packages = sortedPackages,
        };
    }

    private static async Task<AutomationBundlePackageInfo> ToBundlePackageInfoAsync(IPackage package)
    {
        if (package is ImportedPackage imported)
        {
            var serialized = await imported.AsSerializableAsync();
            return new AutomationBundlePackageInfo
            {
                Name = imported.Name,
                Id = imported.Id,
                Version = serialized.Version,
                DisplayVersion = imported.VersionString,
                SelectedVersion = string.IsNullOrWhiteSpace(serialized.InstallationOptions.Version)
                    ? null
                    : serialized.InstallationOptions.Version,
                Scope = string.IsNullOrWhiteSpace(serialized.InstallationOptions.InstallationScope)
                    ? null
                    : serialized.InstallationOptions.InstallationScope,
                PreRelease = serialized.InstallationOptions.PreRelease,
                Source = imported.Source.AsString_DisplayName,
                Manager = imported.Manager.Name,
                IsCompatible = true,
                IsInstalled = imported.Tag == PackageTag.AlreadyInstalled,
                IsUpgradable = imported.Tag == PackageTag.IsUpgradable || imported.IsUpgradable,
            };
        }

        if (package is InvalidImportedPackage invalid)
        {
            var serialized = invalid.AsSerializable_Incompatible();
            return new AutomationBundlePackageInfo
            {
                Name = invalid.Name,
                Id = invalid.Id,
                Version = serialized.Version,
                DisplayVersion = invalid.VersionString,
                Source = invalid.SourceAsString,
                Manager = invalid.Manager.Name,
                IsCompatible = false,
                IsInstalled = false,
                IsUpgradable = false,
            };
        }

        return new AutomationBundlePackageInfo
        {
            Name = package.Name,
            Id = package.Id,
            Version = package.VersionString,
            DisplayVersion = package.VersionString,
            Source = package.Source.AsString_DisplayName,
            Manager = package.Manager.Name,
            IsCompatible = !package.Source.IsVirtualManager,
            IsInstalled = package.Tag == PackageTag.AlreadyInstalled,
            IsUpgradable = package.Tag == PackageTag.IsUpgradable || package.IsUpgradable,
        };
    }

    private static async Task<IPackage> CreateBundlePackageAsync(
        AutomationBundlePackageRequest request
    )
    {
        var packageRequest = new AutomationPackageActionRequest
        {
            PackageId = request.PackageId,
            ManagerName = request.ManagerName,
            PackageSource = request.PackageSource,
            Version = request.Version,
            Scope = request.Scope,
            PreRelease = request.PreRelease,
        };
        var package = AutomationPackageApi.ResolvePackage(
            packageRequest,
            ParseLookupMode(request.Selection)
        );

        if (package.Source.IsVirtualManager)
        {
            return new InvalidImportedPackage(package.AsSerializable_Incompatible(), NullSource.Instance);
        }

        var serialized = await package.AsSerializableAsync();
        AutomationPackageApi.ApplyRequestedOptions(serialized.InstallationOptions, packageRequest);
        return new ImportedPackage(serialized, package.Manager, package.Source);
    }

    private static async Task<bool> MatchesBundleRequestAsync(
        IPackage package,
        AutomationBundlePackageRequest request
    )
    {
        if (!package.Id.Equals(request.PackageId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (
            !string.IsNullOrWhiteSpace(request.ManagerName)
            && !package.Manager.Name.Equals(request.ManagerName, StringComparison.OrdinalIgnoreCase)
            && !package.Manager.DisplayName.Equals(request.ManagerName, StringComparison.OrdinalIgnoreCase)
        )
        {
            return false;
        }

        if (
            !string.IsNullOrWhiteSpace(request.PackageSource)
            && !package.Source.Name.Equals(request.PackageSource, StringComparison.OrdinalIgnoreCase)
            && !package.Source.AsString_DisplayName.Equals(
                request.PackageSource,
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(request.Version))
        {
            return true;
        }

        return request.Version.Equals(
            await GetBundlePackageVersionAsync(package),
            StringComparison.OrdinalIgnoreCase
        );
    }

    private static async Task<string> GetBundlePackageVersionAsync(IPackage package)
    {
        if (package is ImportedPackage imported)
        {
            return (await imported.AsSerializableAsync()).Version;
        }

        if (package is InvalidImportedPackage invalid)
        {
            return invalid.AsSerializable_Incompatible().Version;
        }

        return package.VersionString;
    }

    private static async Task<string> ReadBundleContentAsync(AutomationBundleImportRequest request)
    {
        bool hasContent = !string.IsNullOrWhiteSpace(request.Content);
        bool hasPath = !string.IsNullOrWhiteSpace(request.Path);

        if (hasContent == hasPath)
        {
            throw new InvalidOperationException(
                "Exactly one of content or path must be supplied when importing a bundle."
            );
        }

        if (hasContent)
        {
            return request.Content!;
        }

        return await File.ReadAllTextAsync(request.Path!);
    }

    private static BundleFormatType ResolveImportFormat(AutomationBundleImportRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.Format))
        {
            return ParseFormat(request.Format);
        }

        if (string.IsNullOrWhiteSpace(request.Path))
        {
            return BundleFormatType.UBUNDLE;
        }

        return ParseFormat(Path.GetExtension(request.Path));
    }

    private static BundleFormatType ResolveExportFormat(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return BundleFormatType.UBUNDLE;
        }

        var extension = Path.GetExtension(path);
        return extension.ToLowerInvariant() switch
        {
            ".json" => BundleFormatType.JSON,
            ".ubundle" or "" => BundleFormatType.UBUNDLE,
            _ => throw new InvalidOperationException(
                "Bundle export only supports .ubundle and .json output files."
            ),
        };
    }

    private static BundleFormatType ParseFormat(string? format)
    {
        return format?.Trim().TrimStart('.').ToLowerInvariant() switch
        {
            null or "" or "ubundle" => BundleFormatType.UBUNDLE,
            "json" => BundleFormatType.JSON,
            "yaml" or "yml" => BundleFormatType.YAML,
            "xml" => BundleFormatType.XML,
            _ => throw new InvalidOperationException(
                $"The bundle format \"{format}\" is not supported."
            ),
        };
    }

    private static AutomationPackageLookupMode ParseLookupMode(string? selection)
    {
        return selection?.Trim().ToLowerInvariant() switch
        {
            null or "" or "search" => AutomationPackageLookupMode.Search,
            "installed" => AutomationPackageLookupMode.Installed,
            "updates" or "upgradable" => AutomationPackageLookupMode.Upgradable,
            "auto" => AutomationPackageLookupMode.Any,
            _ => throw new InvalidOperationException(
                $"The bundle selection mode \"{selection}\" is not supported."
            ),
        };
    }

    internal static async Task<string> CreateBundleAsync(IReadOnlyList<IPackage> unsortedPackages)
    {
        var exportableData = new SerializableBundle();
        var packages = unsortedPackages.ToList();
        packages.Sort((x, y) =>
        {
            if (x.Id != y.Id)
            {
                return string.Compare(x.Id, y.Id, StringComparison.Ordinal);
            }

            if (x.Name != y.Name)
            {
                return string.Compare(x.Name, y.Name, StringComparison.Ordinal);
            }

            return x.NormalizedVersion > y.NormalizedVersion ? -1 : 1;
        });

        foreach (var package in packages)
        {
            if (package is ImportedPackage imported)
            {
                exportableData.packages.Add(await imported.AsSerializableAsync());
            }
            else
            {
                exportableData.incompatible_packages.Add(package.AsSerializable_Incompatible());
            }
        }

        return exportableData.AsJsonString();
    }

    internal static async Task<(double SchemaVersion, BundleReport Report)> AddFromBundleAsync(
        string content,
        BundleFormatType format
    )
    {
        if (format == BundleFormatType.YAML)
        {
            content = await SerializationHelpers.YAML_to_JSON(content);
        }
        else if (format == BundleFormatType.XML)
        {
            content = await SerializationHelpers.XML_to_JSON(content);
        }

        var deserializedData = await Task.Run(() =>
            new SerializableBundle(
                JsonNode.Parse(content)
                    ?? throw new InvalidOperationException("The bundle content could not be parsed.")
            )
        );

        var report = new BundleReport { IsEmpty = true };
        bool allowCliArguments =
            SecureSettings.Get(SecureSettings.K.AllowCLIArguments)
            && SecureSettings.Get(SecureSettings.K.AllowImportingCLIArguments);
        bool allowPrePostCommands =
            SecureSettings.Get(SecureSettings.K.AllowPrePostOpCommand)
            && SecureSettings.Get(SecureSettings.K.AllowImportPrePostOpCommands);

        List<IPackage> packages = [];
        foreach (var package in deserializedData.packages)
        {
            var options = package.InstallationOptions;
            ReportList(
                ref report,
                package.Id,
                options.CustomParameters_Install,
                "Custom install arguments",
                allowCliArguments
            );
            ReportList(
                ref report,
                package.Id,
                options.CustomParameters_Update,
                "Custom update arguments",
                allowCliArguments
            );
            ReportList(
                ref report,
                package.Id,
                options.CustomParameters_Uninstall,
                "Custom uninstall arguments",
                allowCliArguments
            );
            options.PreInstallCommand = ReportString(
                ref report,
                package.Id,
                options.PreInstallCommand,
                "Pre-install command",
                allowPrePostCommands
            );
            options.PostInstallCommand = ReportString(
                ref report,
                package.Id,
                options.PostInstallCommand,
                "Post-install command",
                allowPrePostCommands
            );
            options.PreUpdateCommand = ReportString(
                ref report,
                package.Id,
                options.PreUpdateCommand,
                "Pre-update command",
                allowPrePostCommands
            );
            options.PostUpdateCommand = ReportString(
                ref report,
                package.Id,
                options.PostUpdateCommand,
                "Post-update command",
                allowPrePostCommands
            );
            options.PreUninstallCommand = ReportString(
                ref report,
                package.Id,
                options.PreUninstallCommand,
                "Pre-uninstall command",
                allowPrePostCommands
            );
            options.PostUninstallCommand = ReportString(
                ref report,
                package.Id,
                options.PostUninstallCommand,
                "Post-uninstall command",
                allowPrePostCommands
            );
            package.InstallationOptions = options;
            packages.Add(DeserializePackage(package));
        }

        foreach (var incompatiblePackage in deserializedData.incompatible_packages)
        {
            packages.Add(new InvalidImportedPackage(incompatiblePackage, NullSource.Instance));
        }

        await GetLoader().AddPackagesAsync(packages);
        return (deserializedData.export_version, report);
    }

    private static IPackage DeserializePackage(SerializablePackage raw)
    {
        IPackageManager? manager = null;
        foreach (var candidate in PEInterface.Managers)
        {
            if (
                candidate.Name == raw.ManagerName || candidate.DisplayName == raw.ManagerName
            )
            {
                manager = candidate;
                break;
            }
        }

        IManagerSource? source;
        if (manager?.Capabilities.SupportsCustomSources == true)
        {
            if (raw.Source.Contains(": "))
            {
                raw.Source = raw.Source.Split(": ")[^1];
            }

            source = manager.SourcesHelper?.Factory.GetSourceIfExists(raw.Source);
        }
        else
        {
            source = manager?.DefaultSource;
        }

        if (manager is null || source is null)
        {
            return new InvalidImportedPackage(raw.GetInvalidEquivalent(), NullSource.Instance);
        }

        return new ImportedPackage(raw, manager, source);
    }

    private static void ReportList(
        ref BundleReport report,
        string packageId,
        List<string> values,
        string label,
        bool allowed
    )
    {
        if (!values.Any(value => value.Any()))
        {
            return;
        }

        if (!report.Contents.ContainsKey(packageId))
        {
            report.Contents[packageId] = [];
        }

        report.Contents[packageId].Add(
            new BundleReportEntry($"{label}: [{string.Join(", ", values)}]", allowed)
        );
        report.IsEmpty = false;
        if (!allowed)
        {
            values.Clear();
        }
    }

    private static string ReportString(
        ref BundleReport report,
        string packageId,
        string value,
        string label,
        bool allowed
    )
    {
        if (!value.Any())
        {
            return value;
        }

        if (!report.Contents.ContainsKey(packageId))
        {
            report.Contents[packageId] = [];
        }

        report.Contents[packageId].Add(new BundleReportEntry($"{label}: {value}", allowed));
        report.IsEmpty = false;
        return allowed ? value : "";
    }

    private static IReadOnlyList<AutomationBundleSecurityEntry> FlattenReport(BundleReport report)
    {
        if (report.IsEmpty)
        {
            return [];
        }

        return report
            .Contents.SelectMany(pair =>
                pair.Value.Select(entry => new AutomationBundleSecurityEntry
                {
                    PackageId = pair.Key,
                    Line = entry.Line,
                    Allowed = entry.Allowed,
                })
            )
            .OrderBy(entry => entry.PackageId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Line, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
