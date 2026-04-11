using System.Text.Json;
using UniGetUI.Core.Data;
using UniGetUI.Core.Logging;

namespace UniGetUI.Interface;

public enum AutomationCliExitCode
{
    Success = 0,
    Failed = -1,
    InvalidParameter = -1073741811,
    BackgroundApiUnavailable = -3,
    UnknownAutomationCommand = -4,
}

public static class AutomationCliCommandRunner
{
    public const string AutomationArgument = "--automation";

    public static async Task<int> RunAsync(
        IReadOnlyList<string> args,
        TextWriter output,
        TextWriter error
    )
    {
        int basePos = args.ToList().IndexOf(AutomationArgument);
        if (basePos < 0 || basePos + 1 >= args.Count)
        {
            return await WriteErrorAsync(
                output,
                "The automation command requires a subcommand.",
                AutomationCliExitCode.InvalidParameter
            );
        }

        string subcommand = args[basePos + 1].Trim().ToLowerInvariant();

        try
        {
            using var client = BackgroundApiClient.CreateForCli(args);
            return subcommand switch
            {
                "status" => await WriteJsonAsync(output, await client.GetStatusAsync()),
                "get-version" => await WriteJsonAsync(
                    output,
                    new
                    {
                        status = "success",
                        build = await client.GetVersionAsync(),
                    }
                ),
                "get-updates" => await WriteJsonAsync(
                    output,
                    new
                    {
                        status = "success",
                        updates = await client.ListUpgradablePackagesAsync(
                            GetOptionalArgument(args, "--manager")
                        ),
                    }
                ),
                "list-installed" => await WriteJsonAsync(
                    output,
                    new
                    {
                        status = "success",
                        packages = await client.ListInstalledPackagesAsync(
                            GetOptionalArgument(args, "--manager")
                        ),
                    }
                ),
                "search-packages" => await WriteJsonAsync(
                    output,
                    new
                    {
                        status = "success",
                        packages = await client.SearchPackagesAsync(
                            GetRequiredArgument(
                                args,
                                "--query",
                                "The search-packages automation command requires --query."
                            ),
                            GetOptionalArgument(args, "--manager"),
                            GetOptionalIntArgument(args, "--max-results")
                        ),
                    }
                ),
                "install-package" => await WriteJsonAsync(
                    output,
                    await client.InstallPackageAsync(BuildPackageActionRequest(args))
                ),
                "update-package" => await WriteJsonAsync(
                    output,
                    await client.UpdatePackageAsync(BuildPackageActionRequest(args))
                ),
                "uninstall-package" => await WriteJsonAsync(
                    output,
                    await client.UninstallPackageAsync(BuildPackageActionRequest(args))
                ),
                "open-window" => await WriteJsonAsync(output, await client.OpenWindowAsync()),
                "open-updates" => await WriteJsonAsync(output, await client.OpenUpdatesAsync()),
                "show-package" => await WriteJsonAsync(
                    output,
                    await client.ShowPackageAsync(
                        GetRequiredArgument(
                            args,
                            "--package-id",
                            "The show-package automation command requires --package-id."
                        ),
                        GetRequiredArgument(
                            args,
                            "--package-source",
                            "The show-package automation command requires --package-source."
                        )
                    )
                ),
                "update-all" => await WriteJsonAsync(output, await client.UpdateAllAsync()),
                "update-manager" => await WriteJsonAsync(
                    output,
                    await client.UpdateManagerAsync(
                        GetRequiredArgument(
                            args,
                            "--manager",
                            "The update-manager automation command requires --manager."
                        )
                    )
                ),
                _ => await WriteErrorAsync(
                    output,
                    $"Unknown automation command \"{subcommand}\".",
                    AutomationCliExitCode.UnknownAutomationCommand
                ),
            };
        }
        catch (InvalidOperationException ex)
        {
            return await WriteErrorAsync(output, ex.Message, AutomationCliExitCode.InvalidParameter);
        }
        catch (HttpRequestException ex)
        {
            return await WriteErrorAsync(
                output,
                ex.Message,
                AutomationCliExitCode.BackgroundApiUnavailable
            );
        }
        catch (IOException ex)
        {
            return await WriteErrorAsync(
                output,
                ex.Message,
                AutomationCliExitCode.BackgroundApiUnavailable
            );
        }
        catch (Exception ex)
        {
            Logger.Error(ex);
            return await WriteErrorAsync(output, ex.Message, AutomationCliExitCode.Failed);
        }
    }

    private static AutomationPackageActionRequest BuildPackageActionRequest(IReadOnlyList<string> args)
    {
        return new AutomationPackageActionRequest
        {
            PackageId = GetRequiredArgument(
                args,
                "--package-id",
                "This automation command requires --package-id."
            ),
            ManagerName = GetOptionalArgument(args, "--manager"),
            PackageSource = GetOptionalArgument(args, "--package-source"),
            Version = GetOptionalArgument(args, "--version"),
            Scope = GetOptionalArgument(args, "--scope"),
            PreRelease = args.Contains("--pre-release") ? true : null,
        };
    }

    private static string GetRequiredArgument(
        IReadOnlyList<string> arguments,
        string argumentName,
        string errorMessage
    )
    {
        int index = arguments.ToList().IndexOf(argumentName);
        if (index < 0 || index + 1 >= arguments.Count)
        {
            throw new InvalidOperationException(errorMessage);
        }

        return arguments[index + 1].Trim('"').Trim('\'');
    }

    private static string? GetOptionalArgument(
        IReadOnlyList<string> arguments,
        string argumentName
    )
    {
        int index = arguments.ToList().IndexOf(argumentName);
        if (index < 0 || index + 1 >= arguments.Count)
        {
            return null;
        }

        return arguments[index + 1].Trim('"').Trim('\'');
    }

    private static int? GetOptionalIntArgument(
        IReadOnlyList<string> arguments,
        string argumentName
    )
    {
        string? value = GetOptionalArgument(arguments, argumentName);
        if (value is null)
        {
            return null;
        }

        if (int.TryParse(value, out int result))
        {
            return result;
        }

        throw new InvalidOperationException(
            $"The value supplied to {argumentName} must be an integer."
        );
    }

    private static async Task<int> WriteJsonAsync<T>(TextWriter output, T value)
    {
        await output.WriteLineAsync(
            JsonSerializer.Serialize(
                value,
                new JsonSerializerOptions(SerializationHelpers.DefaultOptions)
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true,
                }
            )
        );
        return (int)AutomationCliExitCode.Success;
    }

    private static async Task<int> WriteErrorAsync(
        TextWriter output,
        string message,
        AutomationCliExitCode exitCode
    )
    {
        await output.WriteLineAsync(
            JsonSerializer.Serialize(
                new { status = "error", message },
                new JsonSerializerOptions(SerializationHelpers.DefaultOptions)
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true,
                }
            )
        );
        return (int)exitCode;
    }
}
