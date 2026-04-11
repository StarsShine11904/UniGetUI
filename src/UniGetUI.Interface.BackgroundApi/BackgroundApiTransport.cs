using System.Text.Json;
using UniGetUI.Core.Data;
using UniGetUI.Core.Logging;

namespace UniGetUI.Interface;

public enum BackgroundApiTransportKind
{
    Tcp,
    NamedPipe,
}

public sealed record BackgroundApiTransportOptions(
    BackgroundApiTransportKind TransportKind,
    int TcpPort,
    string NamedPipeName
)
{
    public const int DefaultTcpPort = 7058;
    public const string DefaultNamedPipeName = "UniGetUI.BackgroundApi";

    public const string TransportArgument = "--background-api-transport";
    public const string TcpPortArgument = "--background-api-port";
    public const string NamedPipeArgument = "--background-api-pipe-name";

    public const string CliTransportArgument = "--transport";
    public const string CliTcpPortArgument = "--tcp-port";
    public const string CliNamedPipeArgument = "--pipe-name";

    public const string TransportEnvironmentVariable = "UNIGETUI_API_TRANSPORT";
    public const string TcpPortEnvironmentVariable = "UNIGETUI_API_PORT";
    public const string NamedPipeEnvironmentVariable = "UNIGETUI_API_PIPE_NAME";

    private const string EndpointMetadataFileName = "BackgroundApiEndpoint.json";

    public Uri BaseAddress =>
        TransportKind == BackgroundApiTransportKind.NamedPipe
            ? new Uri("http://localhost/")
            : new Uri($"http://localhost:{TcpPort}/");

    public string BaseAddressString => BaseAddress.ToString().TrimEnd('/');

    public static BackgroundApiTransportOptions Default { get; } = new(
        BackgroundApiTransportKind.Tcp,
        DefaultTcpPort,
        DefaultNamedPipeName
    );

    public static string EndpointMetadataPath =>
        Path.Join(CoreData.UniGetUIUserConfigurationDirectory, EndpointMetadataFileName);

    public static BackgroundApiTransportOptions LoadForServer(IReadOnlyList<string>? args = null)
    {
        args ??= Environment.GetCommandLineArgs();
        return Parse(
            args,
            includeCliAliases: false,
            fallback: Default
        );
    }

    public static BackgroundApiTransportOptions LoadForClient(IReadOnlyList<string>? args = null)
    {
        args ??= Environment.GetCommandLineArgs();

        if (HasExplicitClientOverride(args))
        {
            return Parse(
                args,
                includeCliAliases: true,
                fallback: TryLoadPersisted() ?? Default
            );
        }

        return TryLoadPersisted() ?? Default;
    }

    public void Persist()
    {
        var metadata = new BackgroundApiStatus
        {
            Running = true,
            Transport = TransportKind switch
            {
                BackgroundApiTransportKind.NamedPipe => "named-pipe",
                _ => "tcp",
            },
            TcpPort = TcpPort,
            NamedPipeName = NamedPipeName,
            BaseAddress = BaseAddressString,
            Version = CoreData.VersionName,
            BuildNumber = CoreData.BuildNumber,
        };

        Directory.CreateDirectory(Path.GetDirectoryName(EndpointMetadataPath)!);
        File.WriteAllText(
            EndpointMetadataPath,
            JsonSerializer.Serialize(
                metadata,
                new JsonSerializerOptions(SerializationHelpers.DefaultOptions)
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true,
                }
            )
        );
    }

    public static void DeletePersistedMetadata()
    {
        try
        {
            if (File.Exists(EndpointMetadataPath))
            {
                File.Delete(EndpointMetadataPath);
            }
        }
        catch (Exception ex)
        {
            Logger.Warn("Could not delete background API endpoint metadata");
            Logger.Warn(ex);
        }
    }

    private static BackgroundApiTransportOptions? TryLoadPersisted()
    {
        try
        {
            if (!File.Exists(EndpointMetadataPath))
            {
                return null;
            }

            var json = File.ReadAllText(EndpointMetadataPath);
            var metadata = JsonSerializer.Deserialize<BackgroundApiStatus>(
                json,
                new JsonSerializerOptions(SerializationHelpers.DefaultOptions)
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true,
                }
            );

            if (metadata is null)
            {
                return null;
            }

            return new BackgroundApiTransportOptions(
                ParseTransport(metadata.Transport, Default.TransportKind),
                metadata.TcpPort > 0 ? metadata.TcpPort : DefaultTcpPort,
                string.IsNullOrWhiteSpace(metadata.NamedPipeName)
                    ? DefaultNamedPipeName
                    : metadata.NamedPipeName
            );
        }
        catch (Exception ex)
        {
            Logger.Warn("Could not load persisted background API endpoint metadata");
            Logger.Warn(ex);
            return null;
        }
    }

    private static BackgroundApiTransportOptions Parse(
        IReadOnlyList<string> args,
        bool includeCliAliases,
        BackgroundApiTransportOptions fallback
    )
    {
        string? transportValue = GetArgumentValue(
            args,
            includeCliAliases
                ? [CliTransportArgument, TransportArgument]
                : [TransportArgument]
        );
        transportValue ??= Environment.GetEnvironmentVariable(TransportEnvironmentVariable);

        string? portValue = GetArgumentValue(
            args,
            includeCliAliases ? [CliTcpPortArgument, TcpPortArgument] : [TcpPortArgument]
        );
        portValue ??= Environment.GetEnvironmentVariable(TcpPortEnvironmentVariable);

        string? pipeValue = GetArgumentValue(
            args,
            includeCliAliases ? [CliNamedPipeArgument, NamedPipeArgument] : [NamedPipeArgument]
        );
        pipeValue ??= Environment.GetEnvironmentVariable(NamedPipeEnvironmentVariable);

        var transport = ParseTransport(transportValue, fallback.TransportKind);
        int tcpPort = ParseTcpPort(portValue, fallback.TcpPort);
        string pipeName = ParseNamedPipeName(pipeValue, fallback.NamedPipeName);

        if (transport == BackgroundApiTransportKind.NamedPipe && !OperatingSystem.IsWindows())
        {
            Logger.Warn(
                "Named pipe background API transport is only supported on Windows. Falling back to TCP."
            );
            transport = BackgroundApiTransportKind.Tcp;
        }

        return new BackgroundApiTransportOptions(transport, tcpPort, pipeName);
    }

    private static bool HasExplicitClientOverride(IReadOnlyList<string> args)
    {
        return args.Contains(CliTransportArgument)
            || args.Contains(CliTcpPortArgument)
            || args.Contains(CliNamedPipeArgument)
            || args.Contains(TransportArgument)
            || args.Contains(TcpPortArgument)
            || args.Contains(NamedPipeArgument)
            || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(TransportEnvironmentVariable))
            || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(TcpPortEnvironmentVariable))
            || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(NamedPipeEnvironmentVariable));
    }

    private static BackgroundApiTransportKind ParseTransport(
        string? value,
        BackgroundApiTransportKind fallback
    )
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "tcp" => BackgroundApiTransportKind.Tcp,
            "named-pipe" or "namedpipe" or "pipe" => BackgroundApiTransportKind.NamedPipe,
            _ =>
                LogInvalidTransport(value, fallback),
        };
    }

    private static BackgroundApiTransportKind LogInvalidTransport(
        string value,
        BackgroundApiTransportKind fallback
    )
    {
        Logger.Warn(
            $"Invalid background API transport \"{value}\". Falling back to {fallback}."
        );
        return fallback;
    }

    private static int ParseTcpPort(string? value, int fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        if (int.TryParse(value, out int port) && port is > 0 and <= 65535)
        {
            return port;
        }

        Logger.Warn($"Invalid background API TCP port \"{value}\". Falling back to {fallback}.");
        return fallback;
    }

    private static string ParseNamedPipeName(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        string pipeName = value.Trim();
        if (pipeName.Length > 0)
        {
            return pipeName;
        }

        Logger.Warn(
            $"Invalid background API named pipe name \"{value}\". Falling back to {fallback}."
        );
        return fallback;
    }

    private static string? GetArgumentValue(
        IReadOnlyList<string> args,
        IReadOnlyList<string> argumentNames
    )
    {
        for (int i = 0; i < args.Count; i++)
        {
            if (!argumentNames.Contains(args[i]) || i + 1 >= args.Count)
            {
                continue;
            }

            return args[i + 1].Trim('"').Trim('\'');
        }

        return null;
    }
}

public sealed class BackgroundApiStatus
{
    public bool Running { get; set; }
    public string Transport { get; set; } = "tcp";
    public int TcpPort { get; set; }
    public string NamedPipeName { get; set; } = BackgroundApiTransportOptions.DefaultNamedPipeName;
    public string BaseAddress { get; set; } = "http://localhost:7058";
    public string Version { get; set; } = CoreData.VersionName;
    public int BuildNumber { get; set; } = CoreData.BuildNumber;
}
