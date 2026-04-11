using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Platform;
using Avalonia.Styling;
using UniGetUI.Avalonia.Infrastructure;
using UniGetUI.Avalonia.Views;
using UniGetUI.PackageEngine;
using CoreSettings = global::UniGetUI.Core.SettingsEngine.Settings;

namespace UniGetUI.Avalonia;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
#if AVALONIA_DIAGNOSTICS_ENABLED
        this.AttachDeveloperTools();
#endif

        string platform = OperatingSystem.IsWindows() ? "Windows"
            : OperatingSystem.IsMacOS() ? "macOS"
            : "Linux";

        Styles.Add(new StyleInclude(new Uri("avares://UniGetUI.Avalonia/"))
        {
            Source = new Uri($"avares://UniGetUI.Avalonia/Assets/Styles/Styles.{platform}.axaml")
        });
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (OperatingSystem.IsMacOS())
            {
                ProcessEnvironmentConfigurator.PrepareForCurrentPlatform();
                using var stream = AssetLoader.Open(new Uri("avares://UniGetUI.Avalonia/Assets/icon.png"));
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                MacOsNotificationBridge.SetDockIcon(ms.ToArray());
            }
            else
            {
                ProcessEnvironmentConfigurator.ApplyProxySettingsToProcess();
            }
            PEInterface.LoadLoaders();
            ApplyTheme(CoreSettings.GetValue(CoreSettings.K.PreferredTheme));
            var mainWindow = new MainWindow();
            desktop.MainWindow = mainWindow;
            _ = AvaloniaBootstrapper.InitializeAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }

    public static void ApplyTheme(string value)
    {
        Current!.RequestedThemeVariant = value switch
        {
            "light" => ThemeVariant.Light,
            "dark" => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
    }

}
