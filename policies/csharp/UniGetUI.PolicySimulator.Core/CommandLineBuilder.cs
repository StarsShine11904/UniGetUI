namespace UniGetUI.PolicySimulator.Core;

public static class CommandLineBuilder
{
    public static IReadOnlyList<string> Build(PackageRequest request)
    {
        return request.Manager.Name switch
        {
            "Winget" => BuildWinget(request),
            "PowerShell" => BuildPowerShell(request),
            _ => throw new InvalidOperationException($"Unsupported manager '{request.Manager.Name}'.")
        };
    }

    private static IReadOnlyList<string> BuildWinget(PackageRequest request)
    {
        var command = new List<string> { "winget.exe", request.Operation, "--id", request.Package.Id, "--exact" };
        AddPair(command, "--source", request.Source.Name);
        AddPair(command, "--scope", request.Options.Scope);
        AddPair(command, "--version", request.Options.Version);
        command.Add(request.Options.Interactive ? "--interactive" : "--silent");
        AddPair(command, "--architecture", request.Options.Architecture);
        if (request.Options.SkipHashCheck) command.Add("--ignore-security-hash");
        AddPair(command, "--location", request.Options.CustomInstallLocation);
        command.AddRange(request.Options.CustomParameters ?? []);
        return command;
    }

    private static IReadOnlyList<string> BuildPowerShell(PackageRequest request)
    {
        var verb = request.Operation switch
        {
            "install" => "Install-Module",
            "update" => "Update-Module",
            "uninstall" => "Uninstall-Module",
            _ => throw new InvalidOperationException($"Unsupported PowerShell operation '{request.Operation}'.")
        };

        var command = new List<string> { "pwsh.exe", "-NoProfile", "-Command", verb, "-Name", request.Package.Id };
        if (request.Options.Scope == "user") command.AddRange(["-Scope", "CurrentUser"]);
        if (request.Options.Scope == "machine") command.AddRange(["-Scope", "AllUsers"]);
        AddPair(command, "-RequiredVersion", request.Options.Version);
        if (request.Options.PreRelease) command.Add("-AllowPrerelease");
        if (request.Options.SkipHashCheck) command.Add("-SkipPublisherCheck");
        command.AddRange(request.Options.CustomParameters ?? []);
        return command;
    }

    private static void AddPair(List<string> command, string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        command.Add(name);
        command.Add(value);
    }
}