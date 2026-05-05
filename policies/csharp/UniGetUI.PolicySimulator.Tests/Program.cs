using System.Text.Json;
using System.Text.Json.Serialization;
using UniGetUI.PolicySimulator.Core;

var parsedArgs = ArgumentParser.Parse(args);
var policyRoot = parsedArgs.TryGetValue("policy-root", out var policyRootArgument)
    ? PolicyPathResolver.ResolveExistingPath(policyRootArgument)
    : PolicyPathResolver.FindPoliciesRoot();
var samplesRoot = Path.Combine(policyRoot, "samples");
var policySchemaPath = Path.Combine(policyRoot, "schemas", "unigetui.package-policy.schema.1.0.json");
var requestSchemaPath = Path.Combine(policyRoot, "schemas", "unigetui.package-request.schema.1.0.json");
var scenarioRoot = Path.Combine(samplesRoot, "scenarios");
var loader = new DocumentLoader();
var evaluator = new PolicyEvaluator();
var failures = new List<string>();
var passed = 0;
var commandChecksPassed = 0;
var commandChecksTotal = 0;

foreach (var manifestPath in Directory.EnumerateFiles(scenarioRoot, "*.scenarios.json").OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
{
    var manifest = JsonSerializer.Deserialize<ScenarioManifest>(await File.ReadAllTextAsync(manifestPath), new JsonSerializerOptions(JsonSerializerDefaults.Web))
        ?? throw new InvalidOperationException($"Could not parse scenario manifest '{manifestPath}'.");

    foreach (var scenario in manifest.Scenarios)
    {
        var policyPath = Path.Combine(samplesRoot, scenario.Policy);
        var requestPath = Path.Combine(samplesRoot, scenario.Request);
        var response = EvaluateScenario(loader, evaluator, policyPath, requestPath, policySchemaPath, requestSchemaPath);
        var decisionPassed = response.Decision == scenario.ExpectedDecision;
        var rulePassed = string.IsNullOrWhiteSpace(scenario.ExpectedRuleId) || response.RuleId == scenario.ExpectedRuleId;
        if (decisionPassed && rulePassed)
        {
            passed++;
            continue;
        }

        failures.Add($"{scenario.Id}: expected {scenario.ExpectedDecision}/{scenario.ExpectedRuleId}, got {response.Decision}/{response.RuleId}. Reason: {response.Reason}");
    }
}

commandChecksPassed = RunCommandConstructionChecks(loader, samplesRoot, requestSchemaPath, failures, out commandChecksTotal);

foreach (var failure in failures)
{
    Console.Error.WriteLine(failure);
}

Console.WriteLine($"Scenario checks passed: {passed}");
Console.WriteLine($"Command construction checks passed: {commandChecksPassed} of {commandChecksTotal}");
return failures.Count == 0 ? 0 : 1;

static BrokerEvaluationResponse EvaluateScenario(DocumentLoader loader, PolicyEvaluator evaluator, string policyPath, string requestPath, string policySchemaPath, string requestSchemaPath)
{
    try
    {
        var policy = loader.LoadFile<PolicyDocument>(policyPath, policySchemaPath).Value;
        var request = loader.LoadFile<PackageRequest>(requestPath, requestSchemaPath).Value;
        var decision = evaluator.Evaluate(policy, request);
        var command = decision.Decision == "allow" ? CommandLineBuilder.Build(request) : [];
        return new BrokerEvaluationResponse(request.RequestId, request.Manager.Name, request.Source.Name, request.Package.Id, request.Operation, decision.Decision, decision.RuleId, decision.Reason, decision.Decision == "allow", command, "simulated-elevated");
    }
    catch (Exception exception) when (exception is PolicyValidationException or JsonException)
    {
        return new BrokerEvaluationResponse("<validation-failure>", null, null, null, null, "deny", "<validation-failure>", exception.Message, false, [], "simulated-elevated");
    }
}

static int RunCommandConstructionChecks(DocumentLoader loader, string samplesRoot, string requestSchemaPath, List<string> failures, out int total)
{
    var checks = new[]
    {
        new CommandCheck(
            "WinGet install",
            Path.Combine(samplesRoot, "requests", "winget-vscode-install.request.json"),
            ["winget.exe", "install", "--id", "Microsoft.VisualStudioCode", "--exact", "--source", "winget", "--scope", "machine", "--silent", "--architecture", "x64"]),
        new CommandCheck(
            "WinGet update maps to upgrade",
            Path.Combine(samplesRoot, "requests", "winget-vscode-update-in-range.request.json"),
            ["winget.exe", "upgrade", "--id", "Microsoft.VisualStudioCode", "--exact", "--source", "winget", "--scope", "machine", "--version", "1.96.0", "--silent", "--architecture", "x64"]),
        new CommandCheck(
            "WinGet uninstall",
            Path.Combine(samplesRoot, "requests", "winget-git-uninstall.request.json"),
            ["winget.exe", "uninstall", "--id", "Git.Git", "--exact", "--source", "winget", "--scope", "machine", "--silent", "--architecture", "x64"]),
        new CommandCheck(
            "PowerShell install",
            Path.Combine(samplesRoot, "requests", "powershell-pester-currentuser.request.json"),
            ["pwsh.exe", "-NoProfile", "-Command", "Install-Module", "-Name", "Pester", "-Scope", "CurrentUser"]),
        new CommandCheck(
            "PowerShell versioned install",
            Path.Combine(samplesRoot, "requests", "powershell-pester-version-allowed.request.json"),
            ["pwsh.exe", "-NoProfile", "-Command", "Install-Module", "-Name", "Pester", "-Scope", "CurrentUser", "-RequiredVersion", "5.5.0"]),
        new CommandCheck(
            "PowerShell update",
            Path.Combine(samplesRoot, "requests", "powershell-pester-update-currentuser.request.json"),
            ["pwsh.exe", "-NoProfile", "-Command", "Update-Module", "-Name", "Pester", "-RequiredVersion", "5.5.0"])
    };

    total = checks.Length;
    var passed = 0;
    foreach (var check in checks)
    {
        var request = loader.LoadFile<PackageRequest>(check.RequestPath, requestSchemaPath).Value;
        var command = CommandLineBuilder.Build(request);
        if (!command.SequenceEqual(check.ExpectedCommand))
        {
            failures.Add($"{check.Name} command mismatch: {string.Join(' ', command)}");
            continue;
        }

        passed++;
    }

    return passed;
}

internal sealed record CommandCheck(string Name, string RequestPath, IReadOnlyList<string> ExpectedCommand);

internal sealed class ScenarioManifest
{
    [JsonPropertyName("scenarios")]
    public List<Scenario> Scenarios { get; set; } = [];
}

internal sealed class Scenario
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("policy")]
    public string Policy { get; set; } = "";

    [JsonPropertyName("request")]
    public string Request { get; set; } = "";

    [JsonPropertyName("expectedDecision")]
    public string ExpectedDecision { get; set; } = "deny";

    [JsonPropertyName("expectedRuleId")]
    public string? ExpectedRuleId { get; set; }
}

internal static class ArgumentParser
{
    public static Dictionary<string, string> Parse(string[] args)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Length; index++)
        {
            var current = args[index];
            if (!current.StartsWith("--", StringComparison.Ordinal)) continue;
            var key = current[2..];
            if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                result[key] = "true";
                continue;
            }

            result[key] = args[++index];
        }

        return result;
    }
}