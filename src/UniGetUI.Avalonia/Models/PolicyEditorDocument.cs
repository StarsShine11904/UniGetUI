using System.Text.Json.Serialization;

namespace UniGetUI.Avalonia.Models;

public static class PolicyEditorConstants
{
    public const string PolicyKind = "devolutions.uniget.policy";
    public const string SchemaVersion = "1.0";
    public const string InstallOperation = "install";
    public const string UpdateOperation = "update";

    public static readonly IReadOnlyList<string> DefaultOperations =
    [
        InstallOperation,
        UpdateOperation,
    ];
}

public sealed class PolicyEditorDocument
{
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = PolicyEditorConstants.PolicyKind;

    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; set; } = PolicyEditorConstants.SchemaVersion;

    [JsonPropertyName("policyId")]
    public string PolicyId { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("generatedAtUtc")]
    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("generatedBy")]
    public PolicyGeneratedBy GeneratedBy { get; set; } = new();

    [JsonPropertyName("defaults")]
    public PolicyDefaults Defaults { get; set; } = new();

    [JsonPropertyName("trustedCallers")]
    public List<PolicyTrustedCallerRule> TrustedCallers { get; set; } = [];

    [JsonPropertyName("packages")]
    public List<PolicyPackageRule> Packages { get; set; } = [];
}

public sealed class PolicyGeneratedBy
{
    [JsonPropertyName("product")]
    public string Product { get; set; } = "UniGetUI.Avalonia";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "POC";
}

public sealed class PolicyDefaults
{
    [JsonPropertyName("allowOperations")]
    public List<string> AllowOperations { get; set; } = [.. PolicyEditorConstants.DefaultOperations];

    [JsonPropertyName("allowAnyVersion")]
    public bool AllowAnyVersion { get; set; } = true;

    [JsonPropertyName("caseSensitiveId")]
    public bool CaseSensitiveId { get; set; }
}

public sealed class PolicyTrustedCallerRule
{
    [JsonPropertyName("pathEquals")]
    public string PathEquals { get; set; } = string.Empty;

    [JsonPropertyName("signatureRequired")]
    public bool SignatureRequired { get; set; }
}

public sealed class PolicyPackageRule
{
    [JsonPropertyName("manager")]
    public string Manager { get; set; } = string.Empty;

    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("source")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Source { get; set; }

    [JsonPropertyName("allowOperations")]
    public List<string> AllowOperations { get; set; } = [.. PolicyEditorConstants.DefaultOperations];

    [JsonIgnore]
    public string OperationsSummary => string.Join(", ", AllowOperations.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase));
}
