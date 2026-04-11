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

        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            shutdown.Cancel();
        };

        AppDomain.CurrentDomain.ProcessExit += (_, _) => shutdown.Cancel();

        try
        {
            Logger.Info("Starting UniGetUI headless daemon");

            ProcessEnvironmentConfigurator.PrepareForCurrentPlatform();
            PEInterface.LoadLoaders();
            await Task.Run(PEInterface.LoadManagers);

            backgroundApi = new BackgroundApiRunner();
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
