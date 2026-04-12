using System.IO.Pipes;
using System.Net.Http.Json;
using System.Text.Json;
using UniGetUI.Core.Data;
using UniGetUI.Core.Logging;
using UniGetUI.Core.SettingsEngine;

namespace UniGetUI.Interface;

public sealed class BackgroundApiClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _token;

    public BackgroundApiTransportOptions TransportOptions { get; }

    private BackgroundApiClient(BackgroundApiTransportOptions transportOptions, string? token = null)
    {
        TransportOptions = transportOptions;
        _token = token ?? Settings.GetValue(Settings.K.CurrentSessionToken);
        _httpClient = CreateHttpClient(transportOptions);
    }

    public static BackgroundApiClient CreateForCli(IReadOnlyList<string>? args = null)
    {
        return new BackgroundApiClient(BackgroundApiTransportOptions.LoadForClient(args));
    }

    public async Task<BackgroundApiStatus> GetStatusAsync()
    {
        try
        {
            string json = await SendAsync(HttpMethod.Get, "/v3/status");
            var status = JsonSerializer.Deserialize<BackgroundApiStatus>(
                json,
                new JsonSerializerOptions(SerializationHelpers.DefaultOptions)
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true,
                }
            );
            if (status is not null)
            {
                return status;
            }
        }
        catch (Exception ex) when (IsConnectivityException(ex))
        {
            Logger.Debug($"Background API status probe failed: {ex.Message}");
        }

        return new BackgroundApiStatus
        {
            Running = false,
            Transport = TransportOptions.TransportKind switch
            {
                BackgroundApiTransportKind.NamedPipe => "named-pipe",
                _ => "tcp",
            },
            TcpPort = TransportOptions.TcpPort,
            NamedPipeName = TransportOptions.NamedPipeName,
            BaseAddress = TransportOptions.BaseAddressString,
        };
    }

    public async Task<IReadOnlyList<AutomationManagerInfo>> ListManagersAsync()
    {
        return await ReadAuthenticatedJsonAsync<IReadOnlyList<AutomationManagerInfo>>(
            HttpMethod.Get,
            "/v3/managers"
        ) ?? [];
    }

    public async Task<AutomationManagerMaintenanceInfo?> GetManagerMaintenanceAsync(
        string managerName
    )
    {
        return await ReadAuthenticatedJsonAsync<AutomationManagerMaintenanceInfo>(
            HttpMethod.Get,
            "/v3/managers/maintenance",
            new Dictionary<string, string> { ["manager"] = managerName }
        );
    }

    public async Task<AutomationManagerMaintenanceActionResult> ReloadManagerAsync(
        AutomationManagerMaintenanceRequest request
    )
    {
        return await SendManagerMaintenanceActionAsync("/v3/managers/maintenance/reload", request);
    }

    public async Task<AutomationManagerMaintenanceActionResult> SetManagerExecutablePathAsync(
        AutomationManagerMaintenanceRequest request
    )
    {
        return await SendManagerMaintenanceActionAsync(
            "/v3/managers/maintenance/executable/set",
            request
        );
    }

    public async Task<AutomationManagerMaintenanceActionResult> ClearManagerExecutablePathAsync(
        AutomationManagerMaintenanceRequest request
    )
    {
        return await SendManagerMaintenanceActionAsync(
            "/v3/managers/maintenance/executable/clear",
            request
        );
    }

    public async Task<AutomationManagerMaintenanceActionResult> RunManagerActionAsync(
        AutomationManagerMaintenanceRequest request
    )
    {
        return await SendManagerMaintenanceActionAsync("/v3/managers/maintenance/action", request);
    }

    public async Task<IReadOnlyList<AutomationSourceInfo>> ListSourcesAsync(string? managerName = null)
    {
        Dictionary<string, string>? parameters = null;
        if (!string.IsNullOrWhiteSpace(managerName))
        {
            parameters = new Dictionary<string, string> { ["manager"] = managerName };
        }

        return await ReadAuthenticatedJsonAsync<IReadOnlyList<AutomationSourceInfo>>(
            HttpMethod.Get,
            "/v3/sources",
            parameters
        ) ?? [];
    }

    public async Task<AutomationSourceOperationResult> AddSourceAsync(AutomationSourceRequest request)
    {
        return await SendSourceOperationAsync("/v3/sources/add", request);
    }

    public async Task<AutomationSourceOperationResult> RemoveSourceAsync(AutomationSourceRequest request)
    {
        return await SendSourceOperationAsync("/v3/sources/remove", request);
    }

    public async Task<IReadOnlyList<AutomationSettingInfo>> ListSettingsAsync()
    {
        return await ReadAuthenticatedJsonAsync<IReadOnlyList<AutomationSettingInfo>>(
            HttpMethod.Get,
            "/v3/settings"
        ) ?? [];
    }

    public async Task<IReadOnlyList<AutomationSecureSettingInfo>> ListSecureSettingsAsync(
        string? userName = null
    )
    {
        Dictionary<string, string>? parameters = null;
        if (!string.IsNullOrWhiteSpace(userName))
        {
            parameters = new Dictionary<string, string> { ["user"] = userName };
        }

        return await ReadAuthenticatedJsonAsync<IReadOnlyList<AutomationSecureSettingInfo>>(
            HttpMethod.Get,
            "/v3/secure-settings",
            parameters
        ) ?? [];
    }

    public async Task<AutomationSecureSettingInfo?> GetSecureSettingAsync(
        string key,
        string? userName = null
    )
    {
        Dictionary<string, string> parameters = new() { ["key"] = key };
        if (!string.IsNullOrWhiteSpace(userName))
        {
            parameters["user"] = userName;
        }

        return await ReadAuthenticatedJsonAsync<AutomationSecureSettingInfo>(
            HttpMethod.Get,
            "/v3/secure-settings/item",
            parameters
        );
    }

    public async Task<AutomationSecureSettingInfo?> SetSecureSettingAsync(
        AutomationSecureSettingRequest request
    )
    {
        Dictionary<string, string> parameters = new()
        {
            ["key"] = request.SettingKey,
            ["enabled"] = request.Enabled ? "true" : "false",
        };
        if (!string.IsNullOrWhiteSpace(request.UserName))
        {
            parameters["user"] = request.UserName;
        }

        return await ReadAuthenticatedJsonAsync<AutomationSecureSettingInfo>(
            HttpMethod.Post,
            "/v3/secure-settings/set",
            parameters
        );
    }

    public async Task<AutomationSettingInfo?> GetSettingAsync(string key)
    {
        return await ReadAuthenticatedJsonAsync<AutomationSettingInfo>(
            HttpMethod.Get,
            "/v3/settings/item",
            new Dictionary<string, string> { ["key"] = key }
        );
    }

    public async Task<AutomationSettingInfo?> SetSettingAsync(AutomationSettingValueRequest request)
    {
        Dictionary<string, string> parameters = new() { ["key"] = request.SettingKey };
        if (request.Enabled.HasValue)
        {
            parameters["enabled"] = request.Enabled.Value ? "true" : "false";
        }

        if (request.Value is not null)
        {
            parameters["value"] = request.Value;
        }

        return await ReadAuthenticatedJsonAsync<AutomationSettingInfo>(
            HttpMethod.Post,
            "/v3/settings/set",
            parameters
        );
    }

    public async Task<AutomationSettingInfo?> ClearSettingAsync(string key)
    {
        return await ReadAuthenticatedJsonAsync<AutomationSettingInfo>(
            HttpMethod.Post,
            "/v3/settings/clear",
            new Dictionary<string, string> { ["key"] = key }
        );
    }

    public async Task<AutomationManagerInfo?> SetManagerEnabledAsync(
        AutomationManagerToggleRequest request
    )
    {
        return await ReadAuthenticatedJsonAsync<AutomationManagerInfo>(
            HttpMethod.Post,
            "/v3/managers/set-enabled",
            new Dictionary<string, string>
            {
                ["manager"] = request.ManagerName,
                ["enabled"] = request.Enabled ? "true" : "false",
            }
        );
    }

    public async Task<AutomationManagerInfo?> SetManagerUpdateNotificationsAsync(
        AutomationManagerToggleRequest request
    )
    {
        return await ReadAuthenticatedJsonAsync<AutomationManagerInfo>(
            HttpMethod.Post,
            "/v3/managers/set-update-notifications",
            new Dictionary<string, string>
            {
                ["manager"] = request.ManagerName,
                ["enabled"] = request.Enabled ? "true" : "false",
            }
        );
    }

    public async Task<BackgroundApiCommandResult> ResetSettingsAsync()
    {
        return await ReadAuthenticatedJsonAsync<BackgroundApiCommandResult>(
                HttpMethod.Post,
                "/v3/settings/reset"
            )
            ?? new BackgroundApiCommandResult
            {
                Status = "error",
                Message = "The background API returned an empty response.",
            };
    }

    public async Task<IReadOnlyList<AutomationDesktopShortcutInfo>> ListDesktopShortcutsAsync()
    {
        return await ReadAuthenticatedJsonAsync<IReadOnlyList<AutomationDesktopShortcutInfo>>(
            HttpMethod.Get,
            "/v3/desktop-shortcuts"
        ) ?? [];
    }

    public async Task<AutomationDesktopShortcutOperationResult> SetDesktopShortcutAsync(
        AutomationDesktopShortcutRequest request
    )
    {
        return await SendDesktopShortcutOperationAsync("/v3/desktop-shortcuts/set", request);
    }

    public async Task<AutomationDesktopShortcutOperationResult> ResetDesktopShortcutAsync(
        string shortcutPath
    )
    {
        return await SendDesktopShortcutOperationAsync(
            "/v3/desktop-shortcuts/reset",
            new AutomationDesktopShortcutRequest { Path = shortcutPath }
        );
    }

    public async Task<BackgroundApiCommandResult> ResetDesktopShortcutsAsync()
    {
        return await ReadAuthenticatedJsonAsync<BackgroundApiCommandResult>(
                HttpMethod.Post,
                "/v3/desktop-shortcuts/reset-all"
            )
            ?? new BackgroundApiCommandResult
            {
                Status = "error",
                Message = "The background API returned an empty response.",
            };
    }

    public async Task<IReadOnlyList<AutomationAppLogEntry>> GetAppLogAsync(int level = 4)
    {
        return await ReadAuthenticatedJsonAsync<IReadOnlyList<AutomationAppLogEntry>>(
            HttpMethod.Get,
            "/v3/logs/app",
            new Dictionary<string, string> { ["level"] = level.ToString() }
        ) ?? [];
    }

    public async Task<IReadOnlyList<AutomationOperationHistoryEntry>> GetOperationHistoryAsync()
    {
        return await ReadAuthenticatedJsonAsync<IReadOnlyList<AutomationOperationHistoryEntry>>(
            HttpMethod.Get,
            "/v3/logs/history"
        ) ?? [];
    }

    public async Task<IReadOnlyList<AutomationManagerLogInfo>> GetManagerLogAsync(
        string? managerName = null,
        bool verbose = false
    )
    {
        Dictionary<string, string>? parameters = null;
        if (!string.IsNullOrWhiteSpace(managerName) || verbose)
        {
            parameters = new Dictionary<string, string>();
            if (!string.IsNullOrWhiteSpace(managerName))
            {
                parameters["manager"] = managerName;
            }

            if (verbose)
            {
                parameters["verbose"] = "true";
            }
        }

        return await ReadAuthenticatedJsonAsync<IReadOnlyList<AutomationManagerLogInfo>>(
            HttpMethod.Get,
            "/v3/logs/manager",
            parameters
        ) ?? [];
    }

    public async Task<AutomationBackupStatus?> GetBackupStatusAsync()
    {
        return await ReadAuthenticatedJsonAsync<AutomationBackupStatus>(
            HttpMethod.Get,
            "/v3/backups/status"
        );
    }

    public async Task<AutomationLocalBackupResult> CreateLocalBackupAsync()
    {
        return await ReadAuthenticatedJsonAsync<AutomationLocalBackupResult>(
                HttpMethod.Post,
                "/v3/backups/local/create"
            )
            ?? new AutomationLocalBackupResult
            {
                Status = "error",
                Message = "The background API returned an empty response.",
            };
    }

    public async Task<AutomationGitHubAuthResult> StartGitHubDeviceFlowAsync(
        AutomationGitHubDeviceFlowRequest request
    )
    {
        return await ReadAuthenticatedJsonWithBodyAsync<
                AutomationGitHubAuthResult,
                AutomationGitHubDeviceFlowRequest
            >("/v3/backups/github/sign-in/start", request)
            ?? new AutomationGitHubAuthResult
            {
                Status = "error",
                Message = "The background API returned an empty response.",
            };
    }

    public async Task<AutomationGitHubAuthResult> CompleteGitHubDeviceFlowAsync()
    {
        return await ReadAuthenticatedJsonAsync<AutomationGitHubAuthResult>(
                HttpMethod.Post,
                "/v3/backups/github/sign-in/complete"
            )
            ?? new AutomationGitHubAuthResult
            {
                Status = "error",
                Message = "The background API returned an empty response.",
            };
    }

    public async Task<AutomationGitHubAuthResult> SignOutGitHubAsync()
    {
        return await ReadAuthenticatedJsonAsync<AutomationGitHubAuthResult>(
                HttpMethod.Post,
                "/v3/backups/github/sign-out"
            )
            ?? new AutomationGitHubAuthResult
            {
                Status = "error",
                Message = "The background API returned an empty response.",
            };
    }

    public async Task<IReadOnlyList<AutomationCloudBackupEntry>> ListCloudBackupsAsync()
    {
        return await ReadAuthenticatedJsonAsync<IReadOnlyList<AutomationCloudBackupEntry>>(
            HttpMethod.Get,
            "/v3/backups/cloud"
        ) ?? [];
    }

    public async Task<AutomationCloudBackupUploadResult> CreateCloudBackupAsync()
    {
        return await ReadAuthenticatedJsonAsync<AutomationCloudBackupUploadResult>(
                HttpMethod.Post,
                "/v3/backups/cloud/create"
            )
            ?? new AutomationCloudBackupUploadResult
            {
                Status = "error",
                Message = "The background API returned an empty response.",
            };
    }

    public async Task<AutomationCloudBackupContentResult> DownloadCloudBackupAsync(
        AutomationCloudBackupRequest request
    )
    {
        return await ReadAuthenticatedJsonWithBodyAsync<
                AutomationCloudBackupContentResult,
                AutomationCloudBackupRequest
            >("/v3/backups/cloud/download", request)
            ?? new AutomationCloudBackupContentResult
            {
                Status = "error",
                Message = "The background API returned an empty response.",
            };
    }

    public async Task<AutomationCloudBackupRestoreResult> RestoreCloudBackupAsync(
        AutomationCloudBackupRequest request
    )
    {
        return await ReadAuthenticatedJsonWithBodyAsync<
                AutomationCloudBackupRestoreResult,
                AutomationCloudBackupRequest
            >("/v3/backups/cloud/restore", request)
            ?? new AutomationCloudBackupRestoreResult
            {
                Status = "error",
                Message = "The background API returned an empty response.",
            };
    }

    public async Task<AutomationBundleInfo> GetBundleAsync()
    {
        return await ReadAuthenticatedJsonAsync<AutomationBundleInfo>(HttpMethod.Get, "/v3/bundles")
            ?? new AutomationBundleInfo();
    }

    public async Task<BackgroundApiCommandResult> ResetBundleAsync()
    {
        return await ReadAuthenticatedJsonAsync<BackgroundApiCommandResult>(
                HttpMethod.Post,
                "/v3/bundles/reset"
            )
            ?? new BackgroundApiCommandResult
            {
                Status = "error",
                Message = "The background API returned an empty response.",
            };
    }

    public async Task<AutomationBundleImportResult> ImportBundleAsync(
        AutomationBundleImportRequest request
    )
    {
        return await ReadAuthenticatedJsonWithBodyAsync<
                AutomationBundleImportResult,
                AutomationBundleImportRequest
            >("/v3/bundles/import", request)
            ?? new AutomationBundleImportResult
            {
                Status = "error",
                Message = "The background API returned an empty response.",
            };
    }

    public async Task<AutomationBundleExportResult> ExportBundleAsync(
        AutomationBundleExportRequest request
    )
    {
        return await ReadAuthenticatedJsonWithBodyAsync<
                AutomationBundleExportResult,
                AutomationBundleExportRequest
            >("/v3/bundles/export", request)
            ?? new AutomationBundleExportResult
            {
                Status = "error",
                Message = "The background API returned an empty response.",
            };
    }

    public async Task<AutomationBundlePackageOperationResult> AddBundlePackageAsync(
        AutomationBundlePackageRequest request
    )
    {
        return await ReadAuthenticatedJsonWithBodyAsync<
                AutomationBundlePackageOperationResult,
                AutomationBundlePackageRequest
            >("/v3/bundles/add", request)
            ?? new AutomationBundlePackageOperationResult
            {
                Status = "error",
                Message = "The background API returned an empty response.",
            };
    }

    public async Task<AutomationBundlePackageOperationResult> RemoveBundlePackageAsync(
        AutomationBundlePackageRequest request
    )
    {
        return await ReadAuthenticatedJsonWithBodyAsync<
                AutomationBundlePackageOperationResult,
                AutomationBundlePackageRequest
            >("/v3/bundles/remove", request)
            ?? new AutomationBundlePackageOperationResult
            {
                Status = "error",
                Message = "The background API returned an empty response.",
            };
    }

    public async Task<AutomationBundleInstallResult> InstallBundleAsync(
        AutomationBundleInstallRequest request
    )
    {
        return await ReadAuthenticatedJsonWithBodyAsync<
                AutomationBundleInstallResult,
                AutomationBundleInstallRequest
            >("/v3/bundles/install", request)
            ?? new AutomationBundleInstallResult
            {
                Status = "error",
                Message = "The background API returned an empty response.",
            };
    }

    public async Task<BackgroundApiCommandResult> OpenWindowAsync()
    {
        await SendAuthenticatedGetAsync("/widgets/v1/open_wingetui");
        return BackgroundApiCommandResult.Success("open-window");
    }

    public async Task<BackgroundApiCommandResult> OpenUpdatesAsync()
    {
        await SendAuthenticatedGetAsync("/widgets/v1/view_on_wingetui");
        return BackgroundApiCommandResult.Success("open-updates");
    }

    public async Task<BackgroundApiCommandResult> ShowPackageAsync(
        string packageId,
        string packageSource
    )
    {
        await SendUnauthenticatedGetAsync(
            "/v2/show-package",
            new Dictionary<string, string>
            {
                ["pid"] = packageId,
                ["psource"] = packageSource,
            }
        );
        return BackgroundApiCommandResult.Success("show-package");
    }

    public async Task<int> GetVersionAsync()
    {
        string raw = await SendAuthenticatedGetAsync("/widgets/v1/get_wingetui_version");
        return int.Parse(raw);
    }

    public async Task<IReadOnlyList<BackgroundApiUpdateEntry>> GetUpdatesAsync()
    {
        string payload = await SendAuthenticatedGetAsync("/widgets/v1/get_updates");
        return ParseUpdatesPayload(payload);
    }

    public async Task<IReadOnlyList<AutomationPackageInfo>> SearchPackagesAsync(
        string query,
        string? managerName = null,
        int? maxResults = null
    )
    {
        Dictionary<string, string> parameters = new() { ["query"] = query };
        if (!string.IsNullOrWhiteSpace(managerName))
        {
            parameters["manager"] = managerName;
        }

        if (maxResults.HasValue)
        {
            parameters["maxResults"] = maxResults.Value.ToString();
        }

        return await ReadAuthenticatedJsonAsync<IReadOnlyList<AutomationPackageInfo>>(
            HttpMethod.Get,
            "/v3/packages/search",
            parameters
        ) ?? [];
    }

    public async Task<IReadOnlyList<AutomationPackageInfo>> ListInstalledPackagesAsync(
        string? managerName = null
    )
    {
        Dictionary<string, string>? parameters = null;
        if (!string.IsNullOrWhiteSpace(managerName))
        {
            parameters = new Dictionary<string, string> { ["manager"] = managerName };
        }

        return await ReadAuthenticatedJsonAsync<IReadOnlyList<AutomationPackageInfo>>(
            HttpMethod.Get,
            "/v3/packages/installed",
            parameters
        ) ?? [];
    }

    public async Task<IReadOnlyList<AutomationPackageInfo>> ListUpgradablePackagesAsync(
        string? managerName = null
    )
    {
        Dictionary<string, string>? parameters = null;
        if (!string.IsNullOrWhiteSpace(managerName))
        {
            parameters = new Dictionary<string, string> { ["manager"] = managerName };
        }

        return await ReadAuthenticatedJsonAsync<IReadOnlyList<AutomationPackageInfo>>(
            HttpMethod.Get,
            "/v3/packages/updates",
            parameters
        ) ?? [];
    }

    public async Task<AutomationPackageDetailsInfo?> GetPackageDetailsAsync(
        AutomationPackageActionRequest request
    )
    {
        return await ReadAuthenticatedJsonAsync<AutomationPackageDetailsInfo>(
            HttpMethod.Get,
            "/v3/packages/details",
            BuildPackageQueryParameters(request)
        );
    }

    public async Task<IReadOnlyList<string>> GetPackageVersionsAsync(
        AutomationPackageActionRequest request
    )
    {
        return await ReadAuthenticatedJsonAsync<IReadOnlyList<string>>(
            HttpMethod.Get,
            "/v3/packages/versions",
            BuildPackageQueryParameters(request)
        ) ?? [];
    }

    public async Task<IReadOnlyList<AutomationIgnoredUpdateInfo>> ListIgnoredUpdatesAsync()
    {
        return await ReadAuthenticatedJsonAsync<IReadOnlyList<AutomationIgnoredUpdateInfo>>(
            HttpMethod.Get,
            "/v3/packages/ignored"
        ) ?? [];
    }

    public async Task<BackgroundApiCommandResult> IgnorePackageUpdateAsync(
        AutomationPackageActionRequest request
    )
    {
        return await SendCommandAsync("/v3/packages/ignore", BuildPackageQueryParameters(request));
    }

    public async Task<BackgroundApiCommandResult> RemoveIgnoredUpdateAsync(
        AutomationPackageActionRequest request
    )
    {
        return await SendCommandAsync("/v3/packages/unignore", BuildPackageQueryParameters(request));
    }

    public async Task<BackgroundApiCommandResult> UpdateAllAsync()
    {
        await SendAuthenticatedGetAsync("/widgets/v1/update_all_packages");
        return BackgroundApiCommandResult.Success("update-all");
    }

    public async Task<BackgroundApiCommandResult> UpdateManagerAsync(string managerName)
    {
        await SendAuthenticatedGetAsync(
            "/widgets/v1/update_all_packages_for_source",
            new Dictionary<string, string> { ["source"] = managerName }
        );
        return BackgroundApiCommandResult.Success("update-manager");
    }

    public async Task<BackgroundApiCommandResult> UpdatePackageAsync(string packageId)
    {
        await SendAuthenticatedGetAsync(
            "/widgets/v1/update_package",
            new Dictionary<string, string> { ["id"] = packageId }
        );
        return BackgroundApiCommandResult.Success("update-package");
    }

    public async Task<AutomationPackageOperationResult> InstallPackageAsync(
        AutomationPackageActionRequest request
    )
    {
        return await SendPackageOperationAsync("/v3/packages/install", request);
    }

    public async Task<AutomationPackageOperationResult> DownloadPackageAsync(
        AutomationPackageActionRequest request
    )
    {
        return await SendPackageOperationAsync("/v3/packages/download", request);
    }

    public async Task<AutomationPackageOperationResult> ReinstallPackageAsync(
        AutomationPackageActionRequest request
    )
    {
        return await SendPackageOperationAsync("/v3/packages/reinstall", request);
    }

    public async Task<AutomationPackageOperationResult> UpdatePackageAsync(
        AutomationPackageActionRequest request
    )
    {
        return await SendPackageOperationAsync("/v3/packages/update", request);
    }

    public async Task<AutomationPackageOperationResult> UninstallPackageAsync(
        AutomationPackageActionRequest request
    )
    {
        return await SendPackageOperationAsync("/v3/packages/uninstall", request);
    }

    public async Task<AutomationPackageOperationResult> UninstallThenReinstallPackageAsync(
        AutomationPackageActionRequest request
    )
    {
        return await SendPackageOperationAsync(
            "/v3/packages/uninstall-then-reinstall",
            request
        );
    }

    public static IReadOnlyList<BackgroundApiUpdateEntry> ParseUpdatesPayload(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return [];
        }

        List<BackgroundApiUpdateEntry> updates = [];
        foreach (string row in payload.Split("&&", StringSplitOptions.RemoveEmptyEntries))
        {
            string[] columns = row.Split('|');
            if (columns.Length != 7)
            {
                Logger.Warn($"Skipping malformed background API updates row: {row}");
                continue;
            }

            updates.Add(
                new BackgroundApiUpdateEntry
                {
                    Name = columns[0],
                    Id = columns[1],
                    Version = columns[2],
                    NewVersion = columns[3],
                    Source = columns[4],
                    Manager = columns[5],
                    IconUrl = columns[6],
                }
            );
        }

        return updates;
    }

    private async Task<string> SendAuthenticatedGetAsync(
        string relativePath,
        IReadOnlyDictionary<string, string>? queryParameters = null
    )
    {
        return await SendAuthenticatedAsync(HttpMethod.Get, relativePath, queryParameters);
    }

    private async Task<string> SendAuthenticatedAsync(
        HttpMethod method,
        string relativePath,
        IReadOnlyDictionary<string, string>? queryParameters = null,
        HttpContent? requestContent = null
    )
    {
        EnsureTokenAvailable();

        Dictionary<string, string> parameters = new(queryParameters ?? new Dictionary<string, string>())
        {
            ["token"] = _token,
        };

        return await SendAsync(method, relativePath, parameters, requestContent);
    }

    private async Task<string> SendUnauthenticatedGetAsync(
        string relativePath,
        IReadOnlyDictionary<string, string>? queryParameters = null
    )
    {
        return await SendAsync(HttpMethod.Get, relativePath, queryParameters);
    }

    private async Task<string> SendAsync(
        HttpMethod method,
        string relativePath,
        IReadOnlyDictionary<string, string>? queryParameters = null,
        HttpContent? requestContent = null
    )
    {
        using var timeout = new CancellationTokenSource(GetRequestTimeout(method, relativePath));
        using var request = new HttpRequestMessage(method, BuildRelativeUri(relativePath, queryParameters));
        request.Content = requestContent;
        using var response = await _httpClient.SendAsync(request, timeout.Token);
        string content = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(content) ? response.ReasonPhrase : content
            );
        }

        return content;
    }

    private async Task<T?> ReadAuthenticatedJsonAsync<T>(
        HttpMethod method,
        string relativePath,
        IReadOnlyDictionary<string, string>? queryParameters = null
    )
    {
        string json = await SendAuthenticatedAsync(method, relativePath, queryParameters);
        return JsonSerializer.Deserialize<T>(
            json,
            new JsonSerializerOptions(SerializationHelpers.DefaultOptions)
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true,
            }
        );
    }

    private async Task<TResponse?> ReadAuthenticatedJsonWithBodyAsync<TResponse, TBody>(
        string relativePath,
        TBody body,
        IReadOnlyDictionary<string, string>? queryParameters = null
    )
    {
        using var content = JsonContent.Create(
            body,
            options: new JsonSerializerOptions(SerializationHelpers.DefaultOptions)
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true,
            }
        );
        string json = await SendAuthenticatedAsync(HttpMethod.Post, relativePath, queryParameters, content);
        return JsonSerializer.Deserialize<TResponse>(
            json,
            new JsonSerializerOptions(SerializationHelpers.DefaultOptions)
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true,
            }
        );
    }

    private async Task<AutomationPackageOperationResult> SendPackageOperationAsync(
        string relativePath,
        AutomationPackageActionRequest request
    )
    {
        return await ReadAuthenticatedJsonAsync<AutomationPackageOperationResult>(
                HttpMethod.Post,
                relativePath,
                BuildPackageQueryParameters(request)
            )
            ?? new AutomationPackageOperationResult
            {
                Status = "error",
                Message = "The background API returned an empty response.",
            };
    }

    private async Task<AutomationSourceOperationResult> SendSourceOperationAsync(
        string relativePath,
        AutomationSourceRequest request
    )
    {
        Dictionary<string, string> parameters = new()
        {
            ["manager"] = request.ManagerName,
            ["name"] = request.SourceName,
        };

        if (!string.IsNullOrWhiteSpace(request.SourceUrl))
        {
            parameters["url"] = request.SourceUrl;
        }

        return await ReadAuthenticatedJsonAsync<AutomationSourceOperationResult>(
                HttpMethod.Post,
                relativePath,
                parameters
            )
            ?? new AutomationSourceOperationResult
            {
                Status = "error",
                Message = "The background API returned an empty response.",
            };
    }

    private async Task<AutomationDesktopShortcutOperationResult> SendDesktopShortcutOperationAsync(
        string relativePath,
        AutomationDesktopShortcutRequest request
    )
    {
        Dictionary<string, string> parameters = new() { ["path"] = request.Path };
        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            parameters["status"] = request.Status;
        }

        return await ReadAuthenticatedJsonAsync<AutomationDesktopShortcutOperationResult>(
                HttpMethod.Post,
                relativePath,
                parameters
            )
            ?? new AutomationDesktopShortcutOperationResult
            {
                Status = "error",
                Message = "The background API returned an empty response.",
            };
    }

    private async Task<AutomationManagerMaintenanceActionResult> SendManagerMaintenanceActionAsync(
        string relativePath,
        AutomationManagerMaintenanceRequest request
    )
    {
        return await ReadAuthenticatedJsonWithBodyAsync<
                AutomationManagerMaintenanceActionResult,
                AutomationManagerMaintenanceRequest
            >(relativePath, request)
            ?? new AutomationManagerMaintenanceActionResult
            {
                Status = "error",
                Message = "The background API returned an empty response.",
            };
    }

    private async Task<BackgroundApiCommandResult> SendCommandAsync(
        string relativePath,
        IReadOnlyDictionary<string, string>? queryParameters = null
    )
    {
        return await ReadAuthenticatedJsonAsync<BackgroundApiCommandResult>(
                HttpMethod.Post,
                relativePath,
                queryParameters
            )
            ?? new BackgroundApiCommandResult
            {
                Status = "error",
                Message = "The background API returned an empty response.",
            };
    }

    private static Dictionary<string, string> BuildPackageQueryParameters(
        AutomationPackageActionRequest request
    )
    {
        Dictionary<string, string> parameters = new() { ["packageId"] = request.PackageId };

        if (!string.IsNullOrWhiteSpace(request.ManagerName))
        {
            parameters["manager"] = request.ManagerName;
        }

        if (!string.IsNullOrWhiteSpace(request.PackageSource))
        {
            parameters["packageSource"] = request.PackageSource;
        }

        if (!string.IsNullOrWhiteSpace(request.Version))
        {
            parameters["version"] = request.Version;
        }

        if (!string.IsNullOrWhiteSpace(request.Scope))
        {
            parameters["scope"] = request.Scope;
        }

        if (request.PreRelease.HasValue)
        {
            parameters["preRelease"] = request.PreRelease.Value ? "true" : "false";
        }

        if (request.Elevated.HasValue)
        {
            parameters["elevated"] = request.Elevated.Value ? "true" : "false";
        }

        if (request.Interactive.HasValue)
        {
            parameters["interactive"] = request.Interactive.Value ? "true" : "false";
        }

        if (request.SkipHash.HasValue)
        {
            parameters["skipHash"] = request.SkipHash.Value ? "true" : "false";
        }

        if (request.RemoveData.HasValue)
        {
            parameters["removeData"] = request.RemoveData.Value ? "true" : "false";
        }

        if (!string.IsNullOrWhiteSpace(request.Architecture))
        {
            parameters["architecture"] = request.Architecture;
        }

        if (!string.IsNullOrWhiteSpace(request.InstallLocation))
        {
            parameters["location"] = request.InstallLocation;
        }

        if (!string.IsNullOrWhiteSpace(request.OutputPath))
        {
            parameters["outputPath"] = request.OutputPath;
        }

        return parameters;
    }

    private static HttpClient CreateHttpClient(BackgroundApiTransportOptions options)
    {
        if (options.TransportKind == BackgroundApiTransportKind.NamedPipe)
        {
            var handler = new SocketsHttpHandler
            {
                UseProxy = false,
                ConnectCallback = async (_, cancellationToken) =>
                {
                    var pipeClient = new NamedPipeClientStream(
                        ".",
                        options.NamedPipeName,
                        PipeDirection.InOut,
                        PipeOptions.Asynchronous
                    );
                    await pipeClient.ConnectAsync(cancellationToken);
                    return pipeClient;
                },
            };

            return new HttpClient(handler)
            {
                BaseAddress = options.BaseAddress,
                Timeout = Timeout.InfiniteTimeSpan,
            };
        }

        return new HttpClient
        {
            BaseAddress = options.BaseAddress,
            Timeout = Timeout.InfiniteTimeSpan,
        };
    }

    private static TimeSpan GetRequestTimeout(HttpMethod method, string relativePath)
    {
        if (relativePath.Equals("/v3/status", StringComparison.OrdinalIgnoreCase))
        {
            return TimeSpan.FromSeconds(5);
        }

        if (relativePath.StartsWith("/v3/packages/", StringComparison.OrdinalIgnoreCase))
        {
            return method == HttpMethod.Post
                ? TimeSpan.FromMinutes(5)
                : TimeSpan.FromSeconds(30);
        }

        if (relativePath.StartsWith("/v3/bundles/install", StringComparison.OrdinalIgnoreCase))
        {
            return TimeSpan.FromMinutes(5);
        }

        if (
            relativePath.StartsWith("/v3/managers/maintenance/action", StringComparison.OrdinalIgnoreCase)
        )
        {
            return TimeSpan.FromMinutes(10);
        }

        if (
            relativePath.StartsWith("/v3/backups/github/sign-in/complete", StringComparison.OrdinalIgnoreCase)
        )
        {
            return TimeSpan.FromMinutes(5);
        }

        if (
            relativePath.StartsWith("/v3/backups/local/create", StringComparison.OrdinalIgnoreCase)
            || relativePath.StartsWith("/v3/backups/cloud/create", StringComparison.OrdinalIgnoreCase)
        )
        {
            return TimeSpan.FromMinutes(2);
        }

        if (relativePath.StartsWith("/v3/backups/", StringComparison.OrdinalIgnoreCase))
        {
            return method == HttpMethod.Post ? TimeSpan.FromMinutes(1) : TimeSpan.FromSeconds(30);
        }

        if (relativePath.StartsWith("/v3/bundles/", StringComparison.OrdinalIgnoreCase))
        {
            return method == HttpMethod.Post ? TimeSpan.FromMinutes(1) : TimeSpan.FromSeconds(15);
        }

        if (relativePath.StartsWith("/v3/sources/", StringComparison.OrdinalIgnoreCase))
        {
            return method == HttpMethod.Post
                ? TimeSpan.FromMinutes(2)
                : TimeSpan.FromSeconds(30);
        }

        if (
            relativePath.StartsWith("/v3/managers", StringComparison.OrdinalIgnoreCase)
            || relativePath.StartsWith("/v3/settings", StringComparison.OrdinalIgnoreCase)
            || relativePath.StartsWith("/v3/secure-settings", StringComparison.OrdinalIgnoreCase)
            || relativePath.StartsWith("/v3/desktop-shortcuts", StringComparison.OrdinalIgnoreCase)
            || relativePath.StartsWith("/v3/logs/", StringComparison.OrdinalIgnoreCase)
        )
        {
            return TimeSpan.FromSeconds(15);
        }

        return TimeSpan.FromSeconds(5);
    }

    private static bool IsConnectivityException(Exception exception)
    {
        return exception is HttpRequestException
            or IOException
            or TaskCanceledException
            or OperationCanceledException;
    }

    private void EnsureTokenAvailable()
    {
        if (string.IsNullOrWhiteSpace(_token))
        {
            throw new InvalidOperationException(
                "The background API token is not available. Start UniGetUI and try again."
            );
        }
    }

    private static string BuildRelativeUri(
        string relativePath,
        IReadOnlyDictionary<string, string>? queryParameters
    )
    {
        if (queryParameters is null || queryParameters.Count == 0)
        {
            return relativePath;
        }

        string query = string.Join(
            "&",
            queryParameters.Select(pair =>
                $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"
            )
        );

        return $"{relativePath}?{query}";
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}

public sealed class BackgroundApiUpdateEntry
{
    public string Name { get; set; } = "";
    public string Id { get; set; } = "";
    public string Version { get; set; } = "";
    public string NewVersion { get; set; } = "";
    public string Source { get; set; } = "";
    public string Manager { get; set; } = "";
    public string IconUrl { get; set; } = "";
}

public sealed class BackgroundApiCommandResult
{
    public string Status { get; set; } = "success";
    public string Command { get; set; } = "";
    public string? Message { get; set; }

    public static BackgroundApiCommandResult Success(string command)
    {
        return new BackgroundApiCommandResult { Command = command };
    }
}
