using Avalonia.Threading;
using UniGetUI.Avalonia.Models;
using UniGetUI.Avalonia.Views;
using UniGetUI.Core.Data;
using UniGetUI.Core.IconEngine;
using UniGetUI.Core.Logging;
using UniGetUI.Core.SettingsEngine;
using UniGetUI.Core.SettingsEngine.SecureSettings;
using UniGetUI.Core.Tools;
using UniGetUI.Interface;
using UniGetUI.Interface.Telemetry;
using UniGetUI.PackageEngine;
using UniGetUI.PackageEngine.Classes.Manager.Classes;
using UniGetUI.PackageEngine.Interfaces;

namespace UniGetUI.Avalonia.Infrastructure;

internal static class AvaloniaBootstrapper
{
    private static bool _hasStarted;
    private static BackgroundApiRunner? _backgroundApi;

    public static async Task InitializeAsync()
    {
        if (_hasStarted)
        {
            return;
        }

        _hasStarted = true;
        Logger.Info("Starting Avalonia shell bootstrap");

        await Task.WhenAll(
            InitializeSharedServicesAsync(),
            InitializePackageEngineAsync()
        );

        Logger.Info("Avalonia shell bootstrap completed");
    }

    private static Task InitializeSharedServicesAsync()
    {
        CoreTools.ReloadLanguageEngineInstance();
        ProcessEnvironmentConfigurator.ApplyProxySettingsToProcess();
        _ = Task.Run(AvaloniaAutoUpdater.UpdateCheckLoopAsync)
            .ContinueWith(
                t => Logger.Error(t.Exception!),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
        _ = Task.Run(InitializeBackgroundApiAsync)
            .ContinueWith(
                t => Logger.Error(t.Exception!),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
        TelemetryHandler.Configure(
            Secrets.GetOpenSearchUsername(),
            Secrets.GetOpenSearchPassword());
        _ = TelemetryHandler.InitializeAsync()
            .ContinueWith(
                t => Logger.Error(t.Exception!),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
        _ = Task.Run(LoadElevatorAsync)
            .ContinueWith(
                t => Logger.Error(t.Exception!),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
        _ = Task.Run(IconDatabase.Instance.LoadFromCacheAsync)
            .ContinueWith(
                t => Logger.Error(t.Exception!),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
        _ = Task.Run(IconDatabase.Instance.LoadIconAndScreenshotsDatabaseAsync)
            .ContinueWith(
                t => Logger.Error(t.Exception!),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
        return Task.CompletedTask;
    }

    private static async Task InitializePackageEngineAsync()
    {
        // LoadLoaders is called synchronously in App.axaml.cs before MainWindow creation
        await Task.Run(PEInterface.LoadManagers);
    }

    private static async Task InitializeBackgroundApiAsync()
    {
        try
        {
            if (Settings.Get(Settings.K.DisableApi))
                return;

            _backgroundApi = new BackgroundApiRunner();
            _backgroundApi.AppInfoProvider = () =>
                Dispatcher.UIThread.InvokeAsync(GetAppInfo).GetAwaiter().GetResult();
            _backgroundApi.ShowAppHandler = () =>
                Dispatcher.UIThread.InvokeAsync(ShowApp).GetAwaiter().GetResult();
            _backgroundApi.NavigateAppHandler = request =>
                Dispatcher.UIThread.InvokeAsync(() => NavigateApp(request)).GetAwaiter().GetResult();
            _backgroundApi.QuitAppHandler = () =>
                Dispatcher.UIThread.InvokeAsync(QuitApp).GetAwaiter().GetResult();

            _backgroundApi.OnOpenWindow += (_, _) =>
                Dispatcher.UIThread.Post(() => MainWindow.Instance?.ShowFromTray());

            _backgroundApi.OnOpenUpdatesPage += (_, _) =>
                Dispatcher.UIThread.Post(() =>
                {
                    MainWindow.Instance?.Navigate(PageType.Updates);
                    MainWindow.Instance?.ShowFromTray();
                });

            _backgroundApi.OnShowSharedPackage += (_, pkg) =>
                Dispatcher.UIThread.Post(() =>
                {
                    Logger.Info($"BackgroundApi: ShowSharedPackage {pkg.Key}/{pkg.Value}");
                    MainWindow.Instance?.ShowFromTray();
                    MainWindow.Instance?.OpenSharedPackage(pkg.Key, pkg.Value);
                });

            _backgroundApi.OnUpgradeAll += (_, _) =>
                Dispatcher.UIThread.Post(() => _ = AvaloniaPackageOperationHelper.UpdateAllAsync());

            _backgroundApi.OnUpgradeAllForManager += (_, managerName) =>
                Dispatcher.UIThread.Post(() =>
                    _ = AvaloniaPackageOperationHelper.UpdateAllForManagerAsync(managerName));

            _backgroundApi.OnUpgradePackage += (_, packageId) =>
                Dispatcher.UIThread.Post(() =>
                    _ = AvaloniaPackageOperationHelper.UpdateForIdAsync(packageId));

            await _backgroundApi.Start();
        }
        catch (Exception ex)
        {
            Logger.Error("Could not initialize Background API:");
            Logger.Error(ex);
        }
    }

    public static void StopBackgroundApi() => _backgroundApi?.Stop();

    private static AutomationAppInfo GetAppInfo()
    {
        MainWindow? window = MainWindow.Instance;
        return new AutomationAppInfo
        {
            Headless = false,
            WindowAvailable = window is not null,
            WindowVisible = window?.IsVisible ?? false,
            CanShowWindow = window is not null,
            CanNavigate = window is not null,
            CanQuit = true,
            CurrentPage = window is null ? "" : AutomationAppPages.ToPageName(window.CurrentPage.ToString()),
            SupportedPages = AutomationAppPages.SupportedPages,
        };
    }

    private static BackgroundApiCommandResult ShowApp()
    {
        MainWindow window = MainWindow.Instance
            ?? throw new InvalidOperationException("The application window is not available.");
        window.ShowFromTray();
        return BackgroundApiCommandResult.Success("show-app");
    }

    private static BackgroundApiCommandResult NavigateApp(AutomationAppNavigateRequest request)
    {
        MainWindow window = MainWindow.Instance
            ?? throw new InvalidOperationException("The application window is not available.");
        string page = AutomationAppPages.NormalizePageName(request.Page);
        var manager = ResolveManager(request.ManagerName);

        switch (page)
        {
            case "discover":
                window.Navigate(PageType.Discover);
                break;
            case "updates":
                window.Navigate(PageType.Updates);
                break;
            case "installed":
                window.Navigate(PageType.Installed);
                break;
            case "bundles":
                window.Navigate(PageType.Bundles);
                break;
            case "settings":
                window.Navigate(PageType.Settings);
                break;
            case "managers":
                window.OpenManagerSettings(manager);
                break;
            case "own-log":
                window.Navigate(PageType.OwnLog);
                break;
            case "manager-log":
                window.OpenManagerLogs(manager);
                break;
            case "operation-history":
                window.Navigate(PageType.OperationHistory);
                break;
            case "help":
                window.ShowHelp(request.HelpAttachment ?? "");
                break;
            case "release-notes":
                window.Navigate(PageType.ReleaseNotes);
                break;
            case "about":
                window.Navigate(PageType.About);
                break;
            default:
                throw new InvalidOperationException(
                    $"Unsupported app page \"{request.Page}\"."
                );
        }

        window.ShowFromTray();
        return BackgroundApiCommandResult.Success("navigate-app");
    }

    private static BackgroundApiCommandResult QuitApp()
    {
        MainWindow window = MainWindow.Instance
            ?? throw new InvalidOperationException("The application window is not available.");
        _ = Task.Run(async () =>
        {
            await Task.Delay(150);
            await Dispatcher.UIThread.InvokeAsync(window.QuitApplication);
        });
        return BackgroundApiCommandResult.Success("quit-app");
    }

    private static IPackageManager? ResolveManager(string? managerName)
    {
        if (string.IsNullOrWhiteSpace(managerName))
        {
            return null;
        }

        return PEInterface.Managers.FirstOrDefault(manager =>
            manager.Name.Equals(managerName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"Unknown manager \"{managerName}\"."
            );
    }

    private static async Task LoadElevatorAsync()
    {
        try
        {
            if (Settings.Get(Settings.K.ProhibitElevation))
            {
                Logger.Warn("UniGetUI Elevator has been disabled since elevation is prohibited!");
                return;
            }

            if (SecureSettings.Get(SecureSettings.K.ForceUserGSudo))
            {
                var res = await CoreTools.WhichAsync("gsudo.exe");
                if (res.Item1)
                {
                    CoreData.ElevatorPath = res.Item2;
                    Logger.Warn($"Using user GSudo (forced by user) at {CoreData.ElevatorPath}");
                    return;
                }
            }

#if DEBUG
            Logger.Warn($"Using system GSudo since UniGetUI Elevator is not available in DEBUG builds");
            CoreData.ElevatorPath = (await CoreTools.WhichAsync("gsudo.exe")).Item2;
#else
            CoreData.ElevatorPath = Path.Join(
                CoreData.UniGetUIExecutableDirectory,
                "Assets",
                "Utilities",
                "UniGetUI Elevator.exe"
            );
            Logger.Debug($"Using built-in UniGetUI Elevator at {CoreData.ElevatorPath}");
#endif
        }
        catch (Exception ex)
        {
            Logger.Error("Elevator/GSudo failed to be loaded!");
            Logger.Error(ex);
        }
    }

    /// <summary>
    /// Checks all ready package managers for missing dependencies.
    /// Returns the list of dependencies whose installation was not skipped by the user.
    /// </summary>
    public static async Task<IReadOnlyList<ManagerDependency>> GetMissingDependenciesAsync()
    {
        var missing = new List<ManagerDependency>();

        foreach (var manager in PEInterface.Managers)
        {
            if (!manager.IsReady()) continue;

            foreach (var dep in manager.Dependencies)
            {
                bool isInstalled = true;
                try
                {
                    isInstalled = await dep.IsInstalled();
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error checking dependency {dep.Name}: {ex.Message}");
                }

                if (!isInstalled)
                {
                    if (Settings.GetDictionaryItem<string, string>(
                            Settings.K.DependencyManagement, dep.Name) == "skipped")
                    {
                        Logger.Info($"Dependency {dep.Name} skipped by user preference.");
                    }
                    else
                    {
                        Logger.Warn(
                            $"Dependency {dep.Name} not found for manager {manager.Name}.");
                        missing.Add(dep);
                    }
                }
                else
                {
                    Logger.Info($"Dependency {dep.Name} for {manager.Name} is present.");
                }
            }
        }

        return missing;
    }
}
