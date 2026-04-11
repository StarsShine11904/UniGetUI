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
        IReadOnlyDictionary<string, string>? queryParameters = null
    )
    {
        EnsureTokenAvailable();

        Dictionary<string, string> parameters = new(queryParameters ?? new Dictionary<string, string>())
        {
            ["token"] = _token,
        };

        return await SendAsync(method, relativePath, parameters);
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
        IReadOnlyDictionary<string, string>? queryParameters = null
    )
    {
        using var timeout = new CancellationTokenSource(GetRequestTimeout(method, relativePath));
        using var request = new HttpRequestMessage(method, BuildRelativeUri(relativePath, queryParameters));
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

    private async Task<AutomationPackageOperationResult> SendPackageOperationAsync(
        string relativePath,
        AutomationPackageActionRequest request
    )
    {
        Dictionary<string, string> parameters = new()
        {
            ["packageId"] = request.PackageId,
        };

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

        return await ReadAuthenticatedJsonAsync<AutomationPackageOperationResult>(
                HttpMethod.Post,
                relativePath,
                parameters
            )
            ?? new AutomationPackageOperationResult
            {
                Status = "error",
                Message = "The background API returned an empty response.",
            };
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
