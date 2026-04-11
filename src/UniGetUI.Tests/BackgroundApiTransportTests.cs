using UniGetUI.Core.Data;
using UniGetUI.Interface;

namespace UniGetUI.Tests;

public sealed class BackgroundApiTransportTests : IDisposable
{
    private readonly string _dataDirectory = Path.Join(
        Path.GetTempPath(),
        "UniGetUI.Tests",
        Guid.NewGuid().ToString("N")
    );

    public BackgroundApiTransportTests()
    {
        CoreData.TEST_DataDirectoryOverride = _dataDirectory;
        Directory.CreateDirectory(_dataDirectory);
    }

    [Fact]
    public void LoadForServerParsesNamedPipeOverrides()
    {
        var options = BackgroundApiTransportOptions.LoadForServer(
            [
                "UniGetUI.exe",
                BackgroundApiTransportOptions.TransportArgument,
                "named-pipe",
                BackgroundApiTransportOptions.NamedPipeArgument,
                "Contoso.Pipe",
                BackgroundApiTransportOptions.TcpPortArgument,
                "7258",
            ]
        );

        Assert.Equal(BackgroundApiTransportKind.NamedPipe, options.TransportKind);
        Assert.Equal("Contoso.Pipe", options.NamedPipeName);
        Assert.Equal(7258, options.TcpPort);
    }

    [Fact]
    public void LoadForClientUsesPersistedEndpointMetadataWhenNoOverridesExist()
    {
        var persisted = new BackgroundApiTransportOptions(
            BackgroundApiTransportKind.NamedPipe,
            7058,
            "Persisted.Pipe"
        );
        persisted.Persist();

        var options = BackgroundApiTransportOptions.LoadForClient(["UniGetUI.exe"]);

        Assert.Equal(BackgroundApiTransportKind.NamedPipe, options.TransportKind);
        Assert.Equal("Persisted.Pipe", options.NamedPipeName);
    }

    [Fact]
    public void ParseUpdatesPayloadParsesWidgetResponse()
    {
        var updates = BackgroundApiClient.ParseUpdatesPayload(
            "PowerShell|Microsoft.PowerShell|7.4.0|7.4.1|winget|WinGet|http://localhost/icon&&"
        );

        var update = Assert.Single(updates);
        Assert.Equal("PowerShell", update.Name);
        Assert.Equal("Microsoft.PowerShell", update.Id);
        Assert.Equal("7.4.0", update.Version);
        Assert.Equal("7.4.1", update.NewVersion);
        Assert.Equal("winget", update.Source);
        Assert.Equal("WinGet", update.Manager);
        Assert.Equal("http://localhost/icon", update.IconUrl);
    }

    public void Dispose()
    {
        BackgroundApiTransportOptions.DeletePersistedMetadata();
        CoreData.TEST_DataDirectoryOverride = null;

        if (Directory.Exists(_dataDirectory))
        {
            Directory.Delete(_dataDirectory, recursive: true);
        }
    }
}
