using System.Text.Json.Serialization;

namespace UniGetUI.PolicySimulator.Core;

public sealed class PolicyDocument
{
    [JsonPropertyName("policyVersion")]
    public string PolicyVersion { get; set; } = "";

    [JsonPropertyName("policyType")]
    public string PolicyType { get; set; } = "";

    [JsonPropertyName("metadata")]
    public PolicyMetadata Metadata { get; set; } = new();

    [JsonPropertyName("enforcement")]
    public PolicyEnforcement Enforcement { get; set; } = new();

    [JsonPropertyName("rules")]
    public List<PolicyRule> Rules { get; set; } = [];
}

public sealed class PolicyMetadata
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("publisher")]
    public string Publisher { get; set; } = "";

    [JsonPropertyName("revision")]
    public int Revision { get; set; }

    [JsonPropertyName("publishedAt")]
    public string PublishedAt { get; set; } = "";
}

public sealed class PolicyEnforcement
{
    [JsonPropertyName("defaultDecision")]
    public string DefaultDecision { get; set; } = "deny";

    [JsonPropertyName("failureDecision")]
    public string FailureDecision { get; set; } = "deny";

    [JsonPropertyName("rulePrecedence")]
    public string RulePrecedence { get; set; } = "priorityThenDeny";
}

public sealed class PolicyRule
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("enabled")]
    public bool? Enabled { get; set; }

    [JsonPropertyName("priority")]
    public int Priority { get; set; }

    [JsonPropertyName("decision")]
    public string Decision { get; set; } = "deny";

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    [JsonPropertyName("match")]
    public PolicyMatch Match { get; set; } = new();

    [JsonPropertyName("constraints")]
    public PolicyConstraints? Constraints { get; set; }
}

public sealed class PolicyMatch
{
    [JsonPropertyName("operations")]
    public List<string>? Operations { get; set; }

    [JsonPropertyName("managers")]
    public List<string>? Managers { get; set; }

    [JsonPropertyName("sources")]
    public List<string>? Sources { get; set; }

    [JsonPropertyName("packageIdentifiers")]
    public List<string>? PackageIdentifiers { get; set; }

    [JsonPropertyName("packageNames")]
    public List<string>? PackageNames { get; set; }

    [JsonPropertyName("versions")]
    public List<string>? Versions { get; set; }

    [JsonPropertyName("versionRange")]
    public VersionRange? VersionRange { get; set; }

    [JsonPropertyName("scopes")]
    public List<string>? Scopes { get; set; }

    [JsonPropertyName("architectures")]
    public List<string>? Architectures { get; set; }

    [JsonPropertyName("elevation")]
    public List<string>? Elevation { get; set; }

    [JsonPropertyName("runAsAdministrator")]
    public List<bool>? RunAsAdministrator { get; set; }

    [JsonPropertyName("interactive")]
    public List<bool>? Interactive { get; set; }

    [JsonPropertyName("skipHashCheck")]
    public List<bool>? SkipHashCheck { get; set; }

    [JsonPropertyName("preRelease")]
    public List<bool>? PreRelease { get; set; }

    [JsonPropertyName("hasCustomParameters")]
    public List<bool>? HasCustomParameters { get; set; }

    [JsonPropertyName("hasCustomInstallLocation")]
    public List<bool>? HasCustomInstallLocation { get; set; }

    [JsonPropertyName("hasPrePostCommands")]
    public List<bool>? HasPrePostCommands { get; set; }

    [JsonPropertyName("hasKillBeforeOperation")]
    public List<bool>? HasKillBeforeOperation { get; set; }
}

public sealed class VersionRange
{
    [JsonPropertyName("minVersion")]
    public string? MinVersion { get; set; }

    [JsonPropertyName("maxVersion")]
    public string? MaxVersion { get; set; }

    [JsonPropertyName("includePrerelease")]
    public bool IncludePrerelease { get; set; }
}

public sealed class PolicyConstraints
{
    [JsonPropertyName("allowInteractive")]
    public bool? AllowInteractive { get; set; }

    [JsonPropertyName("allowRunAsAdministrator")]
    public bool? AllowRunAsAdministrator { get; set; }

    [JsonPropertyName("allowSkipHashCheck")]
    public bool? AllowSkipHashCheck { get; set; }

    [JsonPropertyName("allowPreRelease")]
    public bool? AllowPreRelease { get; set; }

    [JsonPropertyName("allowCustomInstallLocation")]
    public bool? AllowCustomInstallLocation { get; set; }

    [JsonPropertyName("allowedInstallLocationPatterns")]
    public List<string>? AllowedInstallLocationPatterns { get; set; }

    [JsonPropertyName("allowCustomParameters")]
    public bool? AllowCustomParameters { get; set; }

    [JsonPropertyName("allowedCustomParameters")]
    public List<string>? AllowedCustomParameters { get; set; }

    [JsonPropertyName("allowedCustomParameterPatterns")]
    public List<string>? AllowedCustomParameterPatterns { get; set; }

    [JsonPropertyName("deniedCustomParameters")]
    public List<string>? DeniedCustomParameters { get; set; }

    [JsonPropertyName("allowPrePostCommands")]
    public bool? AllowPrePostCommands { get; set; }

    [JsonPropertyName("allowKillBeforeOperation")]
    public bool? AllowKillBeforeOperation { get; set; }
}

public sealed class PackageRequest
{
    [JsonPropertyName("requestVersion")]
    public string RequestVersion { get; set; } = "";

    [JsonPropertyName("requestType")]
    public string RequestType { get; set; } = "";

    [JsonPropertyName("requestId")]
    public string RequestId { get; set; } = "";

    [JsonPropertyName("createdAt")]
    public string CreatedAt { get; set; } = "";

    [JsonPropertyName("operation")]
    public string Operation { get; set; } = "";

    [JsonPropertyName("manager")]
    public RequestManager Manager { get; set; } = new();

    [JsonPropertyName("source")]
    public RequestSource Source { get; set; } = new();

    [JsonPropertyName("package")]
    public RequestPackage Package { get; set; } = new();

    [JsonPropertyName("options")]
    public RequestOptions Options { get; set; } = new();

    [JsonPropertyName("broker")]
    public BrokerContext Broker { get; set; } = new();
}

public sealed class RequestManager
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = "";

    [JsonPropertyName("executableFriendlyName")]
    public string ExecutableFriendlyName { get; set; } = "";
}

public sealed class RequestSource
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("isVirtualManager")]
    public bool? IsVirtualManager { get; set; }
}

public sealed class RequestPackage
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("newVersion")]
    public string? NewVersion { get; set; }
}

public sealed class RequestOptions
{
    [JsonPropertyName("scope")]
    public string? Scope { get; set; }

    [JsonPropertyName("architecture")]
    public string? Architecture { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("interactive")]
    public bool Interactive { get; set; }

    [JsonPropertyName("runAsAdministrator")]
    public bool RunAsAdministrator { get; set; }

    [JsonPropertyName("skipHashCheck")]
    public bool SkipHashCheck { get; set; }

    [JsonPropertyName("preRelease")]
    public bool PreRelease { get; set; }

    [JsonPropertyName("customInstallLocation")]
    public string? CustomInstallLocation { get; set; }

    [JsonPropertyName("customParameters")]
    public List<string>? CustomParameters { get; set; }

    [JsonPropertyName("preOperationCommand")]
    public string? PreOperationCommand { get; set; }

    [JsonPropertyName("postOperationCommand")]
    public string? PostOperationCommand { get; set; }

    [JsonPropertyName("killBeforeOperation")]
    public List<string>? KillBeforeOperation { get; set; }
}

public sealed class BrokerContext
{
    [JsonPropertyName("requestedElevation")]
    public string RequestedElevation { get; set; } = "";

    [JsonPropertyName("effectiveUser")]
    public string EffectiveUser { get; set; } = "";

    [JsonPropertyName("clientVersion")]
    public string? ClientVersion { get; set; }
}

public sealed record LoadedDocument<T>(string Path, string Format, string CanonicalJson, T Value);
public sealed record MatchedRule(string Id, int Priority, string Decision, string? Reason);
public sealed record PolicyDecision(string Decision, string RuleId, int? Priority, string Reason, IReadOnlyList<MatchedRule> MatchedRules);
public sealed record BrokerEvaluationResponse(string RequestId, string? Manager, string? Source, string? PackageId, string? Operation, string Decision, string RuleId, string Reason, bool WouldExecute, IReadOnlyList<string> Command, string Mode);

public sealed class PolicyValidationException(string message) : Exception(message);