using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using UniGetUI.Core.Data;
using UniGetUI.Core.IconEngine;
using UniGetUI.Core.Logging;
using UniGetUI.Core.SettingsEngine;
using UniGetUI.Core.Tools;
using UniGetUI.Interface.Enums;
using UniGetUI.PackageEngine;
using UniGetUI.PackageEngine.Interfaces;
using UniGetUI.PackageEngine.PackageLoader;

namespace UniGetUI.Interface
{
    internal static class ApiTokenHolder
    {
        public static string Token = "";
    }

    public class BackgroundApiRunner
    {
        public event EventHandler<EventArgs>? OnOpenWindow;
        public event EventHandler<EventArgs>? OnOpenUpdatesPage;
        public event EventHandler<KeyValuePair<string, string>>? OnShowSharedPackage;
        public event EventHandler<EventArgs>? OnUpgradeAll;
        public event EventHandler<string>? OnUpgradeAllForManager;
        public event EventHandler<string>? OnUpgradePackage;
        public Func<AutomationAppInfo>? AppInfoProvider;
        public Func<BackgroundApiCommandResult>? ShowAppHandler;
        public Func<AutomationAppNavigateRequest, BackgroundApiCommandResult>? NavigateAppHandler;
        public Func<BackgroundApiCommandResult>? QuitAppHandler;

        private IHost? _host;
        private BackgroundApiTransportOptions _transportOptions =
            BackgroundApiTransportOptions.LoadForServer();

        public BackgroundApiRunner() { }

        public static bool AuthenticateToken(string? token)
        {
            return token == ApiTokenHolder.Token;
        }

        public async Task Start()
        {
            _transportOptions = BackgroundApiTransportOptions.LoadForServer();
            ApiTokenHolder.Token = CoreTools.RandomString(64);
            Settings.SetValue(Settings.K.CurrentSessionToken, ApiTokenHolder.Token);
            Logger.Info("Generated a background API auth token for the current session");

            var builder = Host.CreateDefaultBuilder();
            builder.ConfigureServices(services => services.AddCors());
            builder.ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.UseKestrel(serverOptions => ConfigureTransport(serverOptions));
#if !DEBUG
                webBuilder.SuppressStatusMessages(true);
#endif
                webBuilder.Configure(app =>
                {
                    app.UseCors(policy =>
                        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()
                    );

                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet("/v3/status", V3_Status);
                        endpoints.MapGet("/v3/app", V3_GetAppInfo);
                        endpoints.MapPost("/v3/app/show", V3_ShowApp);
                        endpoints.MapPost("/v3/app/navigate", V3_NavigateApp);
                        endpoints.MapPost("/v3/app/quit", V3_QuitApp);
                        endpoints.MapGet("/v3/managers", V3_ListManagers);
                        endpoints.MapGet("/v3/managers/maintenance", V3_GetManagerMaintenance);
                        endpoints.MapPost("/v3/managers/maintenance/reload", V3_ReloadManager);
                        endpoints.MapPost(
                            "/v3/managers/maintenance/executable/set",
                            V3_SetManagerExecutable
                        );
                        endpoints.MapPost(
                            "/v3/managers/maintenance/executable/clear",
                            V3_ClearManagerExecutable
                        );
                        endpoints.MapPost("/v3/managers/maintenance/action", V3_RunManagerAction);
                        endpoints.MapGet("/v3/sources", V3_ListSources);
                        endpoints.MapPost("/v3/sources/add", V3_AddSource);
                        endpoints.MapPost("/v3/sources/remove", V3_RemoveSource);
                        endpoints.MapGet("/v3/settings", V3_ListSettings);
                        endpoints.MapGet("/v3/settings/item", V3_GetSetting);
                        endpoints.MapPost("/v3/settings/set", V3_SetSetting);
                        endpoints.MapPost("/v3/settings/clear", V3_ClearSetting);
                        endpoints.MapPost("/v3/settings/reset", V3_ResetSettings);
                        endpoints.MapGet("/v3/secure-settings", V3_ListSecureSettings);
                        endpoints.MapGet("/v3/secure-settings/item", V3_GetSecureSetting);
                        endpoints.MapPost("/v3/secure-settings/set", V3_SetSecureSetting);
                        endpoints.MapPost("/v3/managers/set-enabled", V3_SetManagerEnabled);
                        endpoints.MapPost(
                            "/v3/managers/set-update-notifications",
                            V3_SetManagerUpdateNotifications
                        );
                        endpoints.MapGet("/v3/desktop-shortcuts", V3_ListDesktopShortcuts);
                        endpoints.MapPost("/v3/desktop-shortcuts/set", V3_SetDesktopShortcut);
                        endpoints.MapPost("/v3/desktop-shortcuts/reset", V3_ResetDesktopShortcut);
                        endpoints.MapPost("/v3/desktop-shortcuts/reset-all", V3_ResetDesktopShortcuts);
                        endpoints.MapGet("/v3/logs/app", V3_GetAppLog);
                        endpoints.MapGet("/v3/logs/history", V3_GetOperationHistory);
                        endpoints.MapGet("/v3/logs/manager", V3_GetManagerLog);
                        endpoints.MapGet("/v3/backups/status", V3_GetBackupStatus);
                        endpoints.MapPost("/v3/backups/local/create", V3_CreateLocalBackup);
                        endpoints.MapPost(
                            "/v3/backups/github/sign-in/start",
                            V3_StartGitHubDeviceFlow
                        );
                        endpoints.MapPost(
                            "/v3/backups/github/sign-in/complete",
                            V3_CompleteGitHubDeviceFlow
                        );
                        endpoints.MapPost("/v3/backups/github/sign-out", V3_SignOutGitHub);
                        endpoints.MapGet("/v3/backups/cloud", V3_ListCloudBackups);
                        endpoints.MapPost("/v3/backups/cloud/create", V3_CreateCloudBackup);
                        endpoints.MapPost("/v3/backups/cloud/download", V3_DownloadCloudBackup);
                        endpoints.MapPost("/v3/backups/cloud/restore", V3_RestoreCloudBackup);
                        endpoints.MapGet("/v3/bundles", V3_GetBundle);
                        endpoints.MapPost("/v3/bundles/reset", V3_ResetBundle);
                        endpoints.MapPost("/v3/bundles/import", V3_ImportBundle);
                        endpoints.MapPost("/v3/bundles/export", V3_ExportBundle);
                        endpoints.MapPost("/v3/bundles/add", V3_AddBundlePackage);
                        endpoints.MapPost("/v3/bundles/remove", V3_RemoveBundlePackage);
                        endpoints.MapPost("/v3/bundles/install", V3_InstallBundle);
                        endpoints.MapGet("/v3/packages/search", V3_SearchPackages);
                        endpoints.MapGet("/v3/packages/installed", V3_ListInstalledPackages);
                        endpoints.MapGet("/v3/packages/updates", V3_ListUpgradablePackages);
                        endpoints.MapGet("/v3/packages/details", V3_GetPackageDetails);
                        endpoints.MapGet("/v3/packages/versions", V3_GetPackageVersions);
                        endpoints.MapGet("/v3/packages/ignored", V3_ListIgnoredUpdates);
                        endpoints.MapPost("/v3/packages/ignore", V3_IgnorePackage);
                        endpoints.MapPost("/v3/packages/unignore", V3_UnignorePackage);
                        endpoints.MapPost("/v3/packages/download", V3_DownloadPackage);
                        endpoints.MapPost("/v3/packages/install", V3_InstallPackage);
                        endpoints.MapPost("/v3/packages/reinstall", V3_ReinstallPackage);
                        endpoints.MapPost("/v3/packages/update", V3_UpdatePackage);
                        endpoints.MapPost("/v3/packages/uninstall", V3_UninstallPackage);
                        endpoints.MapPost(
                            "/v3/packages/uninstall-then-reinstall",
                            V3_UninstallThenReinstallPackage
                        );
                        // Share endpoints
                        endpoints.MapGet("/v2/show-package", V2_ShowPackage);
                        endpoints.MapGet("/is-running", API_IsRunning);
                        // Widgets v1 API
                        endpoints.MapGet(
                            "/widgets/v1/get_wingetui_version",
                            WIDGETS_V1_GetUniGetUIVersion
                        );
                        endpoints.MapGet("/widgets/v1/get_updates", WIDGETS_V1_GetUpdates);
                        endpoints.MapGet("/widgets/v1/open_wingetui", WIDGETS_V1_OpenUniGetUI);
                        endpoints.MapGet("/widgets/v1/view_on_wingetui", WIDGETS_V1_ViewOnUniGetUI);
                        endpoints.MapGet("/widgets/v1/update_package", WIDGETS_V1_UpdatePackage);
                        endpoints.MapGet(
                            "/widgets/v1/update_all_packages",
                            WIDGETS_V1_UpdateAllPackages
                        );
                        endpoints.MapGet(
                            "/widgets/v1/update_all_packages_for_source",
                            WIDGETS_V1_UpdateAllPackagesForSource
                        );
                        // Widgets v2 API
                        endpoints.MapGet(
                            "/widgets/v2/get_icon_for_package",
                            WIDGETS_V2_GetIconForPackage
                        );
                    });
                });
            });
            _host = builder.Build();
            await _host.StartAsync();
            _transportOptions.Persist();
            Logger.Info(
                _transportOptions.TransportKind == BackgroundApiTransportKind.NamedPipe
                    ? $"Api running on named pipe {_transportOptions.NamedPipeName}"
                    : $"Api running on {_transportOptions.BaseAddressString}"
            );
        }

        private void ConfigureTransport(KestrelServerOptions serverOptions)
        {
            if (_transportOptions.TransportKind == BackgroundApiTransportKind.NamedPipe)
            {
                serverOptions.ListenNamedPipe(
                    _transportOptions.NamedPipeName,
                    listenOptions =>
                    {
                        listenOptions.Protocols = HttpProtocols.Http1;
                    }
                );
            }
            else
            {
                serverOptions.ListenLocalhost(_transportOptions.TcpPort);
            }
        }

        private async Task V2_ShowPackage(HttpContext context)
        {
            var query = context.Request.Query;
            if (string.IsNullOrEmpty(query["pid"]) || string.IsNullOrEmpty(query["psource"]))
            {
                context.Response.StatusCode = 400;
                return;
            }

            string packageId = query["pid"].ToString();
            string packageSource = query["psource"].ToString();
            OnShowSharedPackage?.Invoke(
                null,
                new KeyValuePair<string, string>(packageId, packageSource)
            );

            await context.Response.WriteAsync("{\"status\": \"success\"}");
        }

        private async Task API_IsRunning(HttpContext context)
        {
            await context.Response.WriteAsync("{\"status\": \"success\"}");
        }

        private async Task V3_Status(HttpContext context)
        {
            await context.Response.WriteAsJsonAsync(
                new BackgroundApiStatus
                {
                    Running = true,
                    Transport = _transportOptions.TransportKind switch
                    {
                        BackgroundApiTransportKind.NamedPipe => "named-pipe",
                        _ => "tcp",
                    },
                    TcpPort = _transportOptions.TcpPort,
                    NamedPipeName = _transportOptions.NamedPipeName,
                    BaseAddress = _transportOptions.BaseAddressString,
                    Version = CoreData.VersionName,
                    BuildNumber = CoreData.BuildNumber,
                },
                new JsonSerializerOptions(SerializationHelpers.DefaultOptions)
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true,
                }
            );
        }

        private async Task V3_ListManagers(HttpContext context)
        {
            if (!AuthenticateToken(context.Request.Query["token"]))
            {
                context.Response.StatusCode = 401;
                return;
            }

            await context.Response.WriteAsJsonAsync(
                AutomationManagerSettingsApi.ListManagers(),
                new JsonSerializerOptions(SerializationHelpers.DefaultOptions)
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true,
                }
            );
        }

        private async Task V3_GetAppInfo(HttpContext context)
        {
            if (!AuthenticateToken(context.Request.Query["token"]))
            {
                context.Response.StatusCode = 401;
                return;
            }

            try
            {
                await context.Response.WriteAsJsonAsync(
                    AppInfoProvider?.Invoke()
                        ?? throw new InvalidOperationException(
                            "The application did not register an app-state provider."
                        ),
                    new JsonSerializerOptions(SerializationHelpers.DefaultOptions)
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        WriteIndented = true,
                    }
                );
            }
            catch (InvalidOperationException ex)
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync(ex.Message);
            }
        }

        private async Task V3_ShowApp(HttpContext context)
        {
            await HandleCommandAsync(
                context,
                () =>
                    ShowAppHandler?.Invoke()
                    ?? throw new InvalidOperationException(
                        "The current UniGetUI session cannot show a window."
                    )
            );
        }

        private async Task V3_NavigateApp(HttpContext context)
        {
            await HandleCommandAsync(
                context,
                () =>
                    NavigateAppHandler?.Invoke(BuildAppNavigateRequest(context.Request))
                    ?? throw new InvalidOperationException(
                        "The current UniGetUI session cannot navigate application pages."
                    )
            );
        }

        private async Task V3_QuitApp(HttpContext context)
        {
            await HandleCommandAsync(
                context,
                () =>
                    QuitAppHandler?.Invoke()
                    ?? throw new InvalidOperationException(
                        "The current UniGetUI session cannot be shut down through automation."
                    )
            );
        }

        private async Task V3_ListSources(HttpContext context)
        {
            if (!AuthenticateToken(context.Request.Query["token"]))
            {
                context.Response.StatusCode = 401;
                return;
            }

            try
            {
                await context.Response.WriteAsJsonAsync(
                    AutomationManagerSettingsApi.ListSources(context.Request.Query["manager"]),
                    new JsonSerializerOptions(SerializationHelpers.DefaultOptions)
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        WriteIndented = true,
                    }
                );
            }
            catch (InvalidOperationException ex)
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync(ex.Message);
            }
        }

        private async Task V3_GetManagerMaintenance(HttpContext context)
        {
            if (!AuthenticateToken(context.Request.Query["token"]))
            {
                context.Response.StatusCode = 401;
                return;
            }

            string managerName = context.Request.Query["manager"].ToString();
            if (string.IsNullOrWhiteSpace(managerName))
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync("The manager parameter is required.");
                return;
            }

            try
            {
                await context.Response.WriteAsJsonAsync(
                    AutomationManagerMaintenanceApi.GetMaintenanceInfo(managerName),
                    new JsonSerializerOptions(SerializationHelpers.DefaultOptions)
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        WriteIndented = true,
                    }
                );
            }
            catch (InvalidOperationException ex)
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync(ex.Message);
            }
        }

        private async Task V3_ReloadManager(HttpContext context)
        {
            await HandleManagerMaintenanceActionAsync<
                AutomationManagerMaintenanceRequest,
                AutomationManagerMaintenanceActionResult
            >(context, AutomationManagerMaintenanceApi.ReloadManagerAsync);
        }

        private async Task V3_SetManagerExecutable(HttpContext context)
        {
            await HandleManagerMaintenanceActionAsync<
                AutomationManagerMaintenanceRequest,
                AutomationManagerMaintenanceActionResult
            >(context, AutomationManagerMaintenanceApi.SetExecutablePathAsync);
        }

        private async Task V3_ClearManagerExecutable(HttpContext context)
        {
            await HandleManagerMaintenanceActionAsync<
                AutomationManagerMaintenanceRequest,
                AutomationManagerMaintenanceActionResult
            >(context, AutomationManagerMaintenanceApi.ClearExecutablePathAsync);
        }

        private async Task V3_RunManagerAction(HttpContext context)
        {
            await HandleManagerMaintenanceActionAsync<
                AutomationManagerMaintenanceRequest,
                AutomationManagerMaintenanceActionResult
            >(context, AutomationManagerMaintenanceApi.RunActionAsync);
        }

        private async Task V3_AddSource(HttpContext context)
        {
            await HandleSourceActionAsync(context, AutomationManagerSettingsApi.AddSourceAsync);
        }

        private async Task V3_RemoveSource(HttpContext context)
        {
            await HandleSourceActionAsync(context, AutomationManagerSettingsApi.RemoveSourceAsync);
        }

        private async Task V3_ListSettings(HttpContext context)
        {
            if (!AuthenticateToken(context.Request.Query["token"]))
            {
                context.Response.StatusCode = 401;
                return;
            }

            await context.Response.WriteAsJsonAsync(
                AutomationManagerSettingsApi.ListSettings(),
                new JsonSerializerOptions(SerializationHelpers.DefaultOptions)
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true,
                }
            );
        }

        private async Task V3_GetSetting(HttpContext context)
        {
            if (!AuthenticateToken(context.Request.Query["token"]))
            {
                context.Response.StatusCode = 401;
                return;
            }

            string key = context.Request.Query["key"].ToString();
            if (string.IsNullOrWhiteSpace(key))
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync("The key parameter is required.");
                return;
            }

            try
            {
                await context.Response.WriteAsJsonAsync(
                    AutomationManagerSettingsApi.GetSetting(key),
                    new JsonSerializerOptions(SerializationHelpers.DefaultOptions)
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        WriteIndented = true,
                    }
                );
            }
            catch (InvalidOperationException ex)
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync(ex.Message);
            }
        }

        private async Task V3_SetSetting(HttpContext context)
        {
            if (!AuthenticateToken(context.Request.Query["token"]))
            {
                context.Response.StatusCode = 401;
                return;
            }

            try
            {
                await context.Response.WriteAsJsonAsync(
                    AutomationManagerSettingsApi.SetSetting(
                        new AutomationSettingValueRequest
                        {
                            SettingKey = context.Request.Query["key"],
                            Enabled = bool.TryParse(context.Request.Query["enabled"], out bool enabled)
                                ? enabled
                                : null,
                            Value = context.Request.Query.TryGetValue("value", out var value)
                                ? value.ToString()
                                : null,
                        }
                    ),
                    new JsonSerializerOptions(SerializationHelpers.DefaultOptions)
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        WriteIndented = true,
                    }
                );
            }
            catch (InvalidOperationException ex)
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync(ex.Message);
            }
        }

        private async Task V3_ClearSetting(HttpContext context)
        {
            if (!AuthenticateToken(context.Request.Query["token"]))
            {
                context.Response.StatusCode = 401;
                return;
            }

            string key = context.Request.Query["key"].ToString();
            if (string.IsNullOrWhiteSpace(key))
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync("The key parameter is required.");
                return;
            }

            try
            {
                await context.Response.WriteAsJsonAsync(
                    AutomationManagerSettingsApi.ClearSetting(key),
                    new JsonSerializerOptions(SerializationHelpers.DefaultOptions)
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        WriteIndented = true,
                    }
                );
            }
            catch (InvalidOperationException ex)
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync(ex.Message);
            }
        }

        private async Task V3_ResetSettings(HttpContext context)
        {
            if (!AuthenticateToken(context.Request.Query["token"]))
            {
                context.Response.StatusCode = 401;
                return;
            }

            AutomationManagerSettingsApi.ResetSettingsPreservingSession();
            await context.Response.WriteAsJsonAsync(
                BackgroundApiCommandResult.Success("reset-settings"),
                new JsonSerializerOptions(SerializationHelpers.DefaultOptions)
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true,
                }
            );
        }

        private async Task V3_ListSecureSettings(HttpContext context)
        {
            if (!AuthenticateToken(context.Request.Query["token"]))
            {
                context.Response.StatusCode = 401;
                return;
            }

            await context.Response.WriteAsJsonAsync(
                AutomationSecureSettingsApi.ListSettings(context.Request.Query["user"]),
                new JsonSerializerOptions(SerializationHelpers.DefaultOptions)
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true,
                }
            );
        }

        private async Task V3_GetSecureSetting(HttpContext context)
        {
            if (!AuthenticateToken(context.Request.Query["token"]))
            {
                context.Response.StatusCode = 401;
                return;
            }

            try
            {
                await context.Response.WriteAsJsonAsync(
                    AutomationSecureSettingsApi.GetSetting(
                        context.Request.Query["key"],
                        context.Request.Query["user"]
                    ),
                    new JsonSerializerOptions(SerializationHelpers.DefaultOptions)
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        WriteIndented = true,
                    }
                );
            }
            catch (InvalidOperationException ex)
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync(ex.Message);
            }
        }

        private async Task V3_SetSecureSetting(HttpContext context)
        {
            if (!AuthenticateToken(context.Request.Query["token"]))
            {
                context.Response.StatusCode = 401;
                return;
            }

            if (!bool.TryParse(context.Request.Query["enabled"], out bool enabled))
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync("The enabled parameter must be either true or false.");
                return;
            }

            try
            {
                await context.Response.WriteAsJsonAsync(
                    await AutomationSecureSettingsApi.SetSettingAsync(
                        new AutomationSecureSettingRequest
                        {
                            SettingKey = context.Request.Query["key"],
                            UserName = context.Request.Query.TryGetValue("user", out var user)
                                ? user.ToString()
                                : null,
                            Enabled = enabled,
                        }
                    ),
                    new JsonSerializerOptions(SerializationHelpers.DefaultOptions)
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        WriteIndented = true,
                    }
                );
            }
            catch (InvalidOperationException ex)
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync(ex.Message);
            }
        }

        private async Task V3_SetManagerEnabled(HttpContext context)
        {
            if (!AuthenticateToken(context.Request.Query["token"]))
            {
                context.Response.StatusCode = 401;
                return;
            }

            if (!bool.TryParse(context.Request.Query["enabled"], out bool enabled))
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync("The enabled parameter must be either true or false.");
                return;
            }

            try
            {
                await context.Response.WriteAsJsonAsync(
                    await AutomationManagerSettingsApi.SetManagerEnabledAsync(
                        new AutomationManagerToggleRequest
                        {
                            ManagerName = context.Request.Query["manager"],
                            Enabled = enabled,
                        }
                    ),
                    new JsonSerializerOptions(SerializationHelpers.DefaultOptions)
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        WriteIndented = true,
                    }
                );
            }
            catch (InvalidOperationException ex)
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync(ex.Message);
            }
        }

        private async Task V3_SetManagerUpdateNotifications(HttpContext context)
        {
            if (!AuthenticateToken(context.Request.Query["token"]))
            {
                context.Response.StatusCode = 401;
                return;
            }

            if (!bool.TryParse(context.Request.Query["enabled"], out bool enabled))
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync("The enabled parameter must be either true or false.");
                return;
            }

            try
            {
                await context.Response.WriteAsJsonAsync(
                    AutomationManagerSettingsApi.SetManagerNotifications(
                        new AutomationManagerToggleRequest
                        {
                            ManagerName = context.Request.Query["manager"],
                            Enabled = enabled,
                        }
                    ),
                    new JsonSerializerOptions(SerializationHelpers.DefaultOptions)
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        WriteIndented = true,
                    }
                );
            }
            catch (InvalidOperationException ex)
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync(ex.Message);
            }
        }

        private async Task V3_ListDesktopShortcuts(HttpContext context)
        {
            if (!AuthenticateToken(context.Request.Query["token"]))
            {
                context.Response.StatusCode = 401;
                return;
            }

            await context.Response.WriteAsJsonAsync(
                AutomationDesktopShortcutsApi.ListShortcuts(),
                new JsonSerializerOptions(SerializationHelpers.DefaultOptions)
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true,
                }
            );
        }

        private async Task V3_SetDesktopShortcut(HttpContext context)
        {
            if (!AuthenticateToken(context.Request.Query["token"]))
            {
                context.Response.StatusCode = 401;
                return;
            }

            try
            {
                await context.Response.WriteAsJsonAsync(
                    AutomationDesktopShortcutsApi.SetShortcut(
                        new AutomationDesktopShortcutRequest
                        {
                            Path = context.Request.Query["path"],
                            Status = context.Request.Query.TryGetValue("status", out var status)
                                ? status.ToString()
                                : null,
                        }
                    ),
                    new JsonSerializerOptions(SerializationHelpers.DefaultOptions)
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        WriteIndented = true,
                    }
                );
            }
            catch (InvalidOperationException ex)
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync(ex.Message);
            }
        }

        private async Task V3_ResetDesktopShortcut(HttpContext context)
        {
            if (!AuthenticateToken(context.Request.Query["token"]))
            {
                context.Response.StatusCode = 401;
                return;
            }

            try
            {
                await context.Response.WriteAsJsonAsync(
                    AutomationDesktopShortcutsApi.ResetShortcut(
                        new AutomationDesktopShortcutRequest
                        {
                            Path = context.Request.Query["path"],
                        }
                    ),
                    new JsonSerializerOptions(SerializationHelpers.DefaultOptions)
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        WriteIndented = true,
                    }
                );
            }
            catch (InvalidOperationException ex)
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync(ex.Message);
            }
        }

        private async Task V3_ResetDesktopShortcuts(HttpContext context)
        {
            if (!AuthenticateToken(context.Request.Query["token"]))
            {
                context.Response.StatusCode = 401;
                return;
            }

            await context.Response.WriteAsJsonAsync(
                AutomationDesktopShortcutsApi.ResetAllShortcuts(),
                new JsonSerializerOptions(SerializationHelpers.DefaultOptions)
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true,
                }
            );
        }

        private async Task V3_GetAppLog(HttpContext context)
        {
            if (!AuthenticateToken(context.Request.Query["token"]))
            {
                context.Response.StatusCode = 401;
                return;
            }

            int level = int.TryParse(context.Request.Query["level"], out int parsedLevel)
                ? parsedLevel
                : 4;
            await context.Response.WriteAsJsonAsync(
                AutomationLogsApi.ListAppLog(level),
                new JsonSerializerOptions(SerializationHelpers.DefaultOptions)
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true,
                }
            );
        }

        private async Task V3_GetOperationHistory(HttpContext context)
        {
            if (!AuthenticateToken(context.Request.Query["token"]))
            {
                context.Response.StatusCode = 401;
                return;
            }

            await context.Response.WriteAsJsonAsync(
                AutomationLogsApi.ListOperationHistory(),
                new JsonSerializerOptions(SerializationHelpers.DefaultOptions)
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true,
                }
            );
        }

        private async Task V3_GetManagerLog(HttpContext context)
        {
            if (!AuthenticateToken(context.Request.Query["token"]))
            {
                context.Response.StatusCode = 401;
                return;
            }

            try
            {
                await context.Response.WriteAsJsonAsync(
                    AutomationLogsApi.ListManagerLogs(
                        context.Request.Query["manager"],
                        bool.TryParse(context.Request.Query["verbose"], out bool verbose) && verbose
                    ),
                    new JsonSerializerOptions(SerializationHelpers.DefaultOptions)
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        WriteIndented = true,
                    }
                );
            }
            catch (InvalidOperationException ex)
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync(ex.Message);
            }
        }

        private async Task V3_GetBackupStatus(HttpContext context)
        {
            if (!AuthenticateToken(context.Request.Query["token"]))
            {
                context.Response.StatusCode = 401;
                return;
            }

            await context.Response.WriteAsJsonAsync(
                await AutomationBackupApi.GetStatusAsync(),
                new JsonSerializerOptions(SerializationHelpers.DefaultOptions)
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true,
                }
            );
        }

        private async Task V3_CreateLocalBackup(HttpContext context)
        {
            if (!AuthenticateToken(context.Request.Query["token"]))
            {
                context.Response.StatusCode = 401;
                return;
            }

            try
            {
                await context.Response.WriteAsJsonAsync(
                    await AutomationBackupApi.CreateLocalBackupAsync(),
                    new JsonSerializerOptions(SerializationHelpers.DefaultOptions)
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        WriteIndented = true,
                    }
                );
            }
            catch (InvalidOperationException ex)
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync(ex.Message);
            }
        }

        private async Task V3_StartGitHubDeviceFlow(HttpContext context)
        {
            await HandleBackupActionAsync<
                AutomationGitHubDeviceFlowRequest,
                AutomationGitHubAuthResult
            >(context, AutomationBackupApi.StartGitHubDeviceFlowAsync);
        }

        private async Task V3_CompleteGitHubDeviceFlow(HttpContext context)
        {
            if (!AuthenticateToken(context.Request.Query["token"]))
            {
                context.Response.StatusCode = 401;
                return;
            }

            try
            {
                await context.Response.WriteAsJsonAsync(
                    await AutomationBackupApi.CompleteGitHubDeviceFlowAsync(),
                    new JsonSerializerOptions(SerializationHelpers.DefaultOptions)
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        WriteIndented = true,
                    }
                );
            }
            catch (InvalidOperationException ex)
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync(ex.Message);
            }
        }

        private async Task V3_SignOutGitHub(HttpContext context)
        {
            if (!AuthenticateToken(context.Request.Query["token"]))
            {
                context.Response.StatusCode = 401;
                return;
            }

            await context.Response.WriteAsJsonAsync(
                await AutomationBackupApi.SignOutGitHubAsync(),
                new JsonSerializerOptions(SerializationHelpers.DefaultOptions)
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true,
                }
            );
        }

        private async Task V3_ListCloudBackups(HttpContext context)
        {
            if (!AuthenticateToken(context.Request.Query["token"]))
            {
                context.Response.StatusCode = 401;
                return;
            }

            try
            {
                await context.Response.WriteAsJsonAsync(
                    await AutomationBackupApi.ListCloudBackupsAsync(),
                    new JsonSerializerOptions(SerializationHelpers.DefaultOptions)
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        WriteIndented = true,
                    }
                );
            }
            catch (InvalidOperationException ex)
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync(ex.Message);
            }
        }

        private async Task V3_CreateCloudBackup(HttpContext context)
        {
            if (!AuthenticateToken(context.Request.Query["token"]))
            {
                context.Response.StatusCode = 401;
                return;
            }

            try
            {
                await context.Response.WriteAsJsonAsync(
                    await AutomationBackupApi.CreateCloudBackupAsync(),
                    new JsonSerializerOptions(SerializationHelpers.DefaultOptions)
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        WriteIndented = true,
                    }
                );
            }
            catch (InvalidOperationException ex)
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync(ex.Message);
            }
        }

        private async Task V3_DownloadCloudBackup(HttpContext context)
        {
            await HandleBackupActionAsync<
                AutomationCloudBackupRequest,
                AutomationCloudBackupContentResult
            >(context, AutomationBackupApi.DownloadCloudBackupAsync);
        }

        private async Task V3_RestoreCloudBackup(HttpContext context)
        {
            await HandleBackupActionAsync<
                AutomationCloudBackupRequest,
                AutomationCloudBackupRestoreResult
            >(context, AutomationBackupApi.RestoreCloudBackupAsync);
        }

        private async Task V3_GetBundle(HttpContext context)
        {
            if (!AuthenticateToken(context.Request.Query["token"]))
            {
                context.Response.StatusCode = 401;
                return;
            }

            try
            {
                await context.Response.WriteAsJsonAsync(
                    await AutomationBundleApi.GetCurrentBundleAsync(),
                    new JsonSerializerOptions(SerializationHelpers.DefaultOptions)
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        WriteIndented = true,
                    }
                );
            }
            catch (InvalidOperationException ex)
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync(ex.Message);
            }
        }

        private async Task V3_ResetBundle(HttpContext context)
        {
            if (!AuthenticateToken(context.Request.Query["token"]))
            {
                context.Response.StatusCode = 401;
                return;
            }

            try
            {
                await context.Response.WriteAsJsonAsync(
                    AutomationBundleApi.ResetBundle(),
                    new JsonSerializerOptions(SerializationHelpers.DefaultOptions)
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        WriteIndented = true,
                    }
                );
            }
            catch (InvalidOperationException ex)
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync(ex.Message);
            }
        }

        private async Task V3_ImportBundle(HttpContext context)
        {
            await HandleBundleActionAsync<AutomationBundleImportRequest, AutomationBundleImportResult>(
                context,
                AutomationBundleApi.ImportBundleAsync
            );
        }

        private async Task V3_ExportBundle(HttpContext context)
        {
            await HandleBundleActionAsync<AutomationBundleExportRequest, AutomationBundleExportResult>(
                context,
                AutomationBundleApi.ExportBundleAsync
            );
        }

        private async Task V3_AddBundlePackage(HttpContext context)
        {
            await HandleBundleActionAsync<
                AutomationBundlePackageRequest,
                AutomationBundlePackageOperationResult
            >(context, AutomationBundleApi.AddPackageAsync);
        }

        private async Task V3_RemoveBundlePackage(HttpContext context)
        {
            await HandleBundleActionAsync<
                AutomationBundlePackageRequest,
                AutomationBundlePackageOperationResult
            >(context, AutomationBundleApi.RemovePackageAsync);
        }

        private async Task V3_InstallBundle(HttpContext context)
        {
            await HandleBundleActionAsync<
                AutomationBundleInstallRequest,
                AutomationBundleInstallResult
            >(context, AutomationBundleApi.InstallBundleAsync);
        }

        private async Task WIDGETS_V1_GetUniGetUIVersion(HttpContext context)
        {
            if (!AuthenticateToken(context.Request.Query["token"]))
            {
                context.Response.StatusCode = 401;
                return;
            }

            await context.Response.WriteAsync(CoreData.BuildNumber.ToString());
        }

        private async Task V3_SearchPackages(HttpContext context)
        {
            if (!AuthenticateToken(context.Request.Query["token"]))
            {
                context.Response.StatusCode = 401;
                return;
            }

            string query = context.Request.Query["query"].ToString();
            if (string.IsNullOrWhiteSpace(query))
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync("The query parameter is required.");
                return;
            }

            string? manager = context.Request.Query["manager"];
            int maxResults = 50;
            if (
                int.TryParse(context.Request.Query["maxResults"], out int parsedMaxResults)
                && parsedMaxResults > 0
            )
            {
                maxResults = parsedMaxResults;
            }

            try
            {
                await context.Response.WriteAsJsonAsync(
                    AutomationPackageApi.SearchPackages(query, manager, maxResults),
                    new JsonSerializerOptions(SerializationHelpers.DefaultOptions)
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        WriteIndented = true,
                    }
                );
            }
            catch (InvalidOperationException ex)
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync(ex.Message);
            }
        }

        private async Task V3_ListInstalledPackages(HttpContext context)
        {
            if (!AuthenticateToken(context.Request.Query["token"]))
            {
                context.Response.StatusCode = 401;
                return;
            }

            try
            {
                await context.Response.WriteAsJsonAsync(
                    AutomationPackageApi.ListInstalledPackages(context.Request.Query["manager"]),
                    new JsonSerializerOptions(SerializationHelpers.DefaultOptions)
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        WriteIndented = true,
                    }
                );
            }
            catch (InvalidOperationException ex)
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync(ex.Message);
            }
        }

        private async Task V3_ListUpgradablePackages(HttpContext context)
        {
            if (!AuthenticateToken(context.Request.Query["token"]))
            {
                context.Response.StatusCode = 401;
                return;
            }

            try
            {
                await context.Response.WriteAsJsonAsync(
                    AutomationPackageApi.ListUpgradablePackages(context.Request.Query["manager"]),
                    new JsonSerializerOptions(SerializationHelpers.DefaultOptions)
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        WriteIndented = true,
                    }
                );
            }
            catch (InvalidOperationException ex)
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync(ex.Message);
            }
        }

        private async Task V3_GetPackageDetails(HttpContext context)
        {
            if (!AuthenticateToken(context.Request.Query["token"]))
            {
                context.Response.StatusCode = 401;
                return;
            }

            string packageId = context.Request.Query["packageId"].ToString();
            if (string.IsNullOrWhiteSpace(packageId))
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync("The packageId parameter is required.");
                return;
            }

            try
            {
                await context.Response.WriteAsJsonAsync(
                    await AutomationPackageApi.GetPackageDetailsAsync(
                        BuildPackageActionRequest(context.Request)
                    ),
                    new JsonSerializerOptions(SerializationHelpers.DefaultOptions)
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        WriteIndented = true,
                    }
                );
            }
            catch (InvalidOperationException ex)
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync(ex.Message);
            }
        }

        private async Task V3_GetPackageVersions(HttpContext context)
        {
            if (!AuthenticateToken(context.Request.Query["token"]))
            {
                context.Response.StatusCode = 401;
                return;
            }

            string packageId = context.Request.Query["packageId"].ToString();
            if (string.IsNullOrWhiteSpace(packageId))
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync("The packageId parameter is required.");
                return;
            }

            try
            {
                await context.Response.WriteAsJsonAsync(
                    AutomationPackageApi.GetPackageVersions(BuildPackageActionRequest(context.Request)),
                    new JsonSerializerOptions(SerializationHelpers.DefaultOptions)
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        WriteIndented = true,
                    }
                );
            }
            catch (InvalidOperationException ex)
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync(ex.Message);
            }
        }

        private async Task V3_ListIgnoredUpdates(HttpContext context)
        {
            if (!AuthenticateToken(context.Request.Query["token"]))
            {
                context.Response.StatusCode = 401;
                return;
            }

            await context.Response.WriteAsJsonAsync(
                AutomationPackageApi.ListIgnoredUpdates(),
                new JsonSerializerOptions(SerializationHelpers.DefaultOptions)
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true,
                }
            );
        }

        private async Task V3_IgnorePackage(HttpContext context)
        {
            await HandleCommandActionAsync(context, AutomationPackageApi.IgnorePackageUpdateAsync);
        }

        private async Task V3_UnignorePackage(HttpContext context)
        {
            await HandleCommandActionAsync(context, AutomationPackageApi.RemoveIgnoredUpdateAsync);
        }

        private async Task V3_InstallPackage(HttpContext context)
        {
            await HandlePackageActionAsync(
                context,
                AutomationPackageApi.InstallPackageAsync
            );
        }

        private async Task V3_DownloadPackage(HttpContext context)
        {
            await HandlePackageActionAsync(
                context,
                AutomationPackageApi.DownloadPackageAsync
            );
        }

        private async Task V3_ReinstallPackage(HttpContext context)
        {
            await HandlePackageActionAsync(
                context,
                AutomationPackageApi.ReinstallPackageAsync
            );
        }

        private async Task V3_UpdatePackage(HttpContext context)
        {
            await HandlePackageActionAsync(
                context,
                AutomationPackageApi.UpdatePackageAsync
            );
        }

        private async Task V3_UninstallPackage(HttpContext context)
        {
            await HandlePackageActionAsync(
                context,
                AutomationPackageApi.UninstallPackageAsync
            );
        }

        private async Task V3_UninstallThenReinstallPackage(HttpContext context)
        {
            await HandlePackageActionAsync(
                context,
                AutomationPackageApi.UninstallThenReinstallPackageAsync
            );
        }

        private static async Task HandlePackageActionAsync(
            HttpContext context,
            Func<AutomationPackageActionRequest, Task<AutomationPackageOperationResult>> action
        )
        {
            if (!AuthenticateToken(context.Request.Query["token"]))
            {
                context.Response.StatusCode = 401;
                return;
            }

            string packageId = context.Request.Query["packageId"].ToString();
            if (string.IsNullOrWhiteSpace(packageId))
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync("The packageId parameter is required.");
                return;
            }

            try
            {
                var request = BuildPackageActionRequest(context.Request);

                await context.Response.WriteAsJsonAsync(
                    await action(request),
                    new JsonSerializerOptions(SerializationHelpers.DefaultOptions)
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        WriteIndented = true,
                    }
                );
            }
            catch (InvalidOperationException ex)
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync(ex.Message);
            }
        }

        private static async Task HandleCommandAsync(
            HttpContext context,
            Func<BackgroundApiCommandResult> action
        )
        {
            if (!AuthenticateToken(context.Request.Query["token"]))
            {
                context.Response.StatusCode = 401;
                return;
            }

            try
            {
                await context.Response.WriteAsJsonAsync(
                    action(),
                    new JsonSerializerOptions(SerializationHelpers.DefaultOptions)
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        WriteIndented = true,
                    }
                );
            }
            catch (InvalidOperationException ex)
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync(ex.Message);
            }
        }

        private static async Task HandleCommandActionAsync(
            HttpContext context,
            Func<AutomationPackageActionRequest, Task<BackgroundApiCommandResult>> action
        )
        {
            if (!AuthenticateToken(context.Request.Query["token"]))
            {
                context.Response.StatusCode = 401;
                return;
            }

            string packageId = context.Request.Query["packageId"].ToString();
            if (string.IsNullOrWhiteSpace(packageId))
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync("The packageId parameter is required.");
                return;
            }

            try
            {
                await context.Response.WriteAsJsonAsync(
                    await action(BuildPackageActionRequest(context.Request)),
                    new JsonSerializerOptions(SerializationHelpers.DefaultOptions)
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        WriteIndented = true,
                    }
                );
            }
            catch (InvalidOperationException ex)
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync(ex.Message);
            }
        }

        private static async Task HandleSourceActionAsync(
            HttpContext context,
            Func<AutomationSourceRequest, Task<AutomationSourceOperationResult>> action
        )
        {
            if (!AuthenticateToken(context.Request.Query["token"]))
            {
                context.Response.StatusCode = 401;
                return;
            }

            try
            {
                await context.Response.WriteAsJsonAsync(
                    await action(
                        new AutomationSourceRequest
                        {
                            ManagerName = context.Request.Query["manager"],
                            SourceName = context.Request.Query["name"],
                            SourceUrl = context.Request.Query.TryGetValue("url", out var url)
                                ? url.ToString()
                                : null,
                        }
                    ),
                    new JsonSerializerOptions(SerializationHelpers.DefaultOptions)
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        WriteIndented = true,
                    }
                );
            }
            catch (InvalidOperationException ex)
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync(ex.Message);
            }
        }

        private static async Task HandleBundleActionAsync<TRequest, TResult>(
            HttpContext context,
            Func<TRequest, Task<TResult>> action
        )
        {
            if (!AuthenticateToken(context.Request.Query["token"]))
            {
                context.Response.StatusCode = 401;
                return;
            }

            try
            {
                await context.Response.WriteAsJsonAsync(
                    await action(await ReadJsonBodyAsync<TRequest>(context)),
                    new JsonSerializerOptions(SerializationHelpers.DefaultOptions)
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        WriteIndented = true,
                    }
                );
            }
            catch (InvalidOperationException ex)
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync(ex.Message);
            }
        }

        private static async Task HandleBackupActionAsync<TRequest, TResult>(
            HttpContext context,
            Func<TRequest, Task<TResult>> action
        )
        {
            if (!AuthenticateToken(context.Request.Query["token"]))
            {
                context.Response.StatusCode = 401;
                return;
            }

            try
            {
                await context.Response.WriteAsJsonAsync(
                    await action(await ReadJsonBodyAsync<TRequest>(context)),
                    new JsonSerializerOptions(SerializationHelpers.DefaultOptions)
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        WriteIndented = true,
                    }
                );
            }
            catch (InvalidOperationException ex)
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync(ex.Message);
            }
        }

        private static async Task HandleManagerMaintenanceActionAsync<TRequest, TResult>(
            HttpContext context,
            Func<TRequest, Task<TResult>> action
        )
        {
            if (!AuthenticateToken(context.Request.Query["token"]))
            {
                context.Response.StatusCode = 401;
                return;
            }

            try
            {
                await context.Response.WriteAsJsonAsync(
                    await action(await ReadJsonBodyAsync<TRequest>(context)),
                    new JsonSerializerOptions(SerializationHelpers.DefaultOptions)
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        WriteIndented = true,
                    }
                );
            }
            catch (InvalidOperationException ex)
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync(ex.Message);
            }
        }

        private static async Task<TRequest> ReadJsonBodyAsync<TRequest>(HttpContext context)
        {
            var request = await context.Request.ReadFromJsonAsync<TRequest>(
                new JsonSerializerOptions(SerializationHelpers.DefaultOptions)
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true,
                }
            );
            return request
                ?? throw new InvalidOperationException("The request body is required.");
        }

        private static AutomationPackageActionRequest BuildPackageActionRequest(HttpRequest request)
        {
            return new AutomationPackageActionRequest
            {
                PackageId = request.Query["packageId"],
                ManagerName = request.Query["manager"],
                PackageSource = request.Query["packageSource"],
                Version = request.Query["version"],
                Scope = request.Query["scope"],
                PreRelease = bool.TryParse(request.Query["preRelease"], out bool preRelease)
                    ? preRelease
                    : null,
                Elevated = bool.TryParse(request.Query["elevated"], out bool elevated)
                    ? elevated
                    : null,
                Interactive = bool.TryParse(request.Query["interactive"], out bool interactive)
                    ? interactive
                    : null,
                SkipHash = bool.TryParse(request.Query["skipHash"], out bool skipHash)
                    ? skipHash
                    : null,
                RemoveData = bool.TryParse(request.Query["removeData"], out bool removeData)
                    ? removeData
                    : null,
                Architecture = request.Query["architecture"],
                InstallLocation = request.Query["location"],
                OutputPath = request.Query["outputPath"],
            };
        }

        private static AutomationAppNavigateRequest BuildAppNavigateRequest(HttpRequest request)
        {
            return new AutomationAppNavigateRequest
            {
                Page = request.Query["page"],
                ManagerName = request.Query.TryGetValue("manager", out var manager)
                    ? manager.ToString()
                    : null,
                HelpAttachment = request.Query.TryGetValue("helpAttachment", out var helpAttachment)
                    ? helpAttachment.ToString()
                    : null,
            };
        }

        private async Task WIDGETS_V1_GetUpdates(HttpContext context)
        {
            if (!AuthenticateToken(context.Request.Query["token"]))
            {
                context.Response.StatusCode = 401;
                return;
            }

            if (
                !UpgradablePackagesLoader.Instance.IsLoaded
                && !UpgradablePackagesLoader.Instance.IsLoading
            )
            {
                _ = UpgradablePackagesLoader.Instance.ReloadPackages();
            }

            while (UpgradablePackagesLoader.Instance.IsLoading)
            {
                await Task.Delay(100);
            }

            StringBuilder packages = new();
            foreach (IPackage package in UpgradablePackagesLoader.Instance.Packages)
            {
                if (package.Tag is PackageTag.OnQueue or PackageTag.BeingProcessed)
                    continue;

                string icon =
                    $"{_transportOptions.BaseAddressString}/widgets/v2/get_icon_for_package?packageId={Uri.EscapeDataString(package.Id)}&packageSource={Uri.EscapeDataString(package.Source.Name)}&token={ApiTokenHolder.Token}";
                packages.Append(
                    $"{package.Name.Replace('|', '-')}"
                        + $"|{package.Id}"
                        + $"|{package.VersionString}"
                        + $"|{package.NewVersionString}"
                        + $"|{package.Source.AsString_DisplayName}"
                        + $"|{package.Manager.Name}"
                        + $"|{icon}&&"
                );
            }

            string result = packages.ToString();
            if (result.Length > 2)
                result = result[..(result.Length - 2)];

            await context.Response.WriteAsync(result);
        }

        private async Task WIDGETS_V1_OpenUniGetUI(HttpContext context)
        {
            if (!AuthenticateToken(context.Request.Query["token"]))
            {
                context.Response.StatusCode = 401;
                return;
            }

            OnOpenWindow?.Invoke(null, EventArgs.Empty);
            context.Response.StatusCode = 200;
        }

        private async Task WIDGETS_V1_ViewOnUniGetUI(HttpContext context)
        {
            if (!AuthenticateToken(context.Request.Query["token"]))
            {
                context.Response.StatusCode = 401;
                return;
            }

            OnOpenUpdatesPage?.Invoke(null, EventArgs.Empty);
            context.Response.StatusCode = 200;
        }

        private async Task WIDGETS_V1_UpdatePackage(HttpContext context)
        {
            if (!AuthenticateToken(context.Request.Query["token"]))
            {
                context.Response.StatusCode = 401;
                return;
            }

            var id = context.Request.Query["id"];
            if (string.IsNullOrEmpty(id))
            {
                context.Response.StatusCode = 400;
                return;
            }

            string packageId = id.ToString();
            OnUpgradePackage?.Invoke(null, packageId);
            context.Response.StatusCode = 200;
        }

        private async Task WIDGETS_V1_UpdateAllPackages(HttpContext context)
        {
            if (!AuthenticateToken(context.Request.Query["token"]))
            {
                context.Response.StatusCode = 401;
                return;
            }

            Logger.Info("[WIDGETS] Updating all packages");
            OnUpgradeAll?.Invoke(null, EventArgs.Empty);
            context.Response.StatusCode = 200;
        }

        private async Task WIDGETS_V1_UpdateAllPackagesForSource(HttpContext context)
        {
            if (!AuthenticateToken(context.Request.Query["token"]))
            {
                context.Response.StatusCode = 401;
                return;
            }

            var source = context.Request.Query["source"];
            if (string.IsNullOrEmpty(source))
            {
                context.Response.StatusCode = 400;
                return;
            }

            string sourceName = source.ToString();
            Logger.Info($"[WIDGETS] Updating all packages for manager {sourceName}");
            OnUpgradeAllForManager?.Invoke(null, sourceName);
            context.Response.StatusCode = 200;
        }

        private async Task WIDGETS_V2_GetIconForPackage(HttpContext context)
        {
            if (!AuthenticateToken(context.Request.Query["token"]))
            {
                context.Response.StatusCode = 401;
                return;
            }

            var packageId = context.Request.Query["packageId"];
            var packageSource = context.Request.Query["packageSource"];
            if (string.IsNullOrEmpty(packageId) || string.IsNullOrEmpty(packageSource))
            {
                context.Response.StatusCode = 400;
                return;
            }

            string iconPath = Path.Join(
                CoreData.UniGetUIExecutableDirectory,
                "Assets",
                "Images",
                "package_color.png"
            );

            string resolvedPackageId = packageId.ToString();
            IPackage? package = UpgradablePackagesLoader.Instance.GetPackageForId(
                resolvedPackageId,
                packageSource
            );
            if (package != null)
            {
                var iconUrl = await Task.Run(package.GetIconUrl);
                if (iconUrl.ToString() != "ms-appx:///Assets/Images/package_color.png")
                {
                    string mimePath = Path.Join(
                        CoreData.UniGetUICacheDirectory_Icons,
                        package.Manager.Name,
                        package.Id,
                        "icon.mime"
                    );
                    iconPath = Path.Join(
                        CoreData.UniGetUICacheDirectory_Icons,
                        package.Manager.Name,
                        package.Id,
                        $"icon.{IconCacheEngine.MimeToExtension[await File.ReadAllTextAsync(mimePath)]}"
                    );
                }
            }
            else
            {
                Logger.Warn($"[API] Package id={packageId} with source={packageSource} not found!");
            }

            var bytes = await File.ReadAllBytesAsync(iconPath);
            var ext = Path.GetExtension(iconPath).TrimStart('.').ToLower();
            context.Response.ContentType = IconCacheEngine.ExtensionToMime.GetValueOrDefault(
                ext,
                "image/png"
            );
            await context.Response.Body.WriteAsync(bytes.AsMemory());
        }

        public async Task Stop()
        {
            try
            {
                ArgumentNullException.ThrowIfNull(_host);
                await _host.StopAsync();
                BackgroundApiTransportOptions.DeletePersistedMetadata();
                Logger.Info("Api was shut down");
            }
            catch (Exception ex)
            {
                Logger.Error(ex);
            }
        }
    }
}
