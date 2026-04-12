using UniGetUI.Core.Logging;
using UniGetUI.Interface;
using UniGetUI.PackageEngine;

namespace UniGetUI.Avalonia.Infrastructure;

internal static class HeadlessDaemonHost
{
    public static async Task<int> RunAsync(string[] args)
    {
        BackgroundApiRunner? backgroundApi = null;
        using var shutdown = new CancellationTokenSource();
        void RequestShutdown()
        {
            if (!shutdown.IsCancellationRequested)
            {
                shutdown.Cancel();
            }
        }

        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            RequestShutdown();
        };
        Console.CancelKeyPress += cancelHandler;

        EventHandler processExitHandler = (_, _) => RequestShutdown();
        AppDomain.CurrentDomain.ProcessExit += processExitHandler;

        try
        {
            Logger.Info("Starting UniGetUI headless daemon");

            ProcessEnvironmentConfigurator.PrepareForCurrentPlatform();
            PEInterface.LoadLoaders();
            await Task.Run(PEInterface.LoadManagers);

            backgroundApi = new BackgroundApiRunner();
            backgroundApi.AppInfoProvider = () =>
                new AutomationAppInfo
                {
                    Headless = true,
                    WindowAvailable = false,
                    WindowVisible = false,
                    CanShowWindow = false,
                    CanNavigate = false,
                    CanQuit = true,
                    SupportedPages = AutomationAppPages.SupportedPages,
                };
            backgroundApi.ShowAppHandler = () =>
                throw new InvalidOperationException(
                    "The current UniGetUI session is running headless and has no window to show."
                );
            backgroundApi.NavigateAppHandler = _ =>
                throw new InvalidOperationException(
                    "The current UniGetUI session is running headless and cannot navigate UI pages."
                );
            backgroundApi.QuitAppHandler = () =>
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(150);
                    shutdown.Cancel();
                });
                return BackgroundApiCommandResult.Success("quit-app");
            };
            await backgroundApi.Start();

            Logger.Info("UniGetUI headless daemon is ready");
            await WaitForShutdownAsync(shutdown.Token);
            return 0;
        }
        catch (Exception ex)
        {
            Logger.Error("UniGetUI headless daemon failed to start");
            Logger.Error(ex);
            return ex.HResult != 0 ? ex.HResult : 1;
        }
        finally
        {
            AppDomain.CurrentDomain.ProcessExit -= processExitHandler;
            Console.CancelKeyPress -= cancelHandler;

            if (backgroundApi is not null)
            {
                await backgroundApi.Stop();
            }
        }
    }

    private static Task WaitForShutdownAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        cancellationToken.Register(() => completion.TrySetResult());
        return completion.Task;
    }
}
