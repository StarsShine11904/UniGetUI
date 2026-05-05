using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using YamlDotNet.Serialization;

namespace UniGetUI.PolicySimulator.Core;

public sealed class DocumentLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public LoadedDocument<T> LoadFile<T>(string path, string? schemaPath = null)
    {
        var fullPath = Path.GetFullPath(path);
        var text = File.ReadAllText(fullPath);
        return LoadText<T>(text, fullPath, InferFormatFromPath(fullPath), schemaPath);
    }

    public LoadedDocument<T> LoadText<T>(string text, string documentName, string format, string? schemaPath = null)
    {
        var canonicalJson = ConvertToCanonicalJson(text, format);
        if (!string.IsNullOrWhiteSpace(schemaPath))
        {
            ValidateJsonSchema(canonicalJson, schemaPath, documentName);
        }

        var value = JsonSerializer.Deserialize<T>(canonicalJson, JsonOptions)
            ?? throw new PolicyValidationException($"Document '{documentName}' did not deserialize to {typeof(T).Name}.");

        return new LoadedDocument<T>(documentName, format, canonicalJson, value);
    }

    public static string InferFormatFromPath(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return extension switch
        {
            ".json" => "json",
            ".yaml" or ".yml" => "yaml",
            _ => throw new PolicyValidationException($"Unsupported document extension '{extension}'. Use .json, .yaml, or .yml.")
        };
    }

    public static string InferFormatFromContentType(string? contentType)
    {
        if (contentType?.Contains("yaml", StringComparison.OrdinalIgnoreCase) == true ||
            contentType?.Contains("yml", StringComparison.OrdinalIgnoreCase) == true)
        {
            return "yaml";
        }

        return "json";
    }

    private static string ConvertToCanonicalJson(string text, string format)
    {
        if (format.Equals("json", StringComparison.OrdinalIgnoreCase))
        {
            using var document = JsonDocument.Parse(text);
            return JsonSerializer.Serialize(document.RootElement, JsonOptions);
        }

        if (!format.Equals("yaml", StringComparison.OrdinalIgnoreCase))
        {
            throw new PolicyValidationException($"Unsupported document format '{format}'.");
        }

        var deserializer = new DeserializerBuilder()
            .WithAttemptingUnquotedStringTypeDeserialization()
            .Build();
        var yamlObject = deserializer.Deserialize(new StringReader(text));
        var normalized = NormalizeYamlObject(yamlObject);
        return JsonSerializer.Serialize(normalized, JsonOptions);
    }

    private static object? NormalizeYamlObject(object? value)
    {
        return value switch
        {
            null => null,
            IDictionary<object, object> dictionary => dictionary.ToDictionary(
                item => Convert.ToString(item.Key, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
                item => NormalizeYamlObject(item.Value)),
            IEnumerable<object> list => list.Select(NormalizeYamlObject).ToList(),
            _ => value
        };
    }

    private static void ValidateJsonSchema(string canonicalJson, string schemaPath, string documentName)
    {
        var schemaText = File.ReadAllText(schemaPath);
        var schema = JsonSchema.FromText(schemaText);
        var instance = JsonNode.Parse(canonicalJson)
            ?? throw new PolicyValidationException($"Document '{documentName}' could not be parsed as JSON.");

        var results = schema.Evaluate(instance, new EvaluationOptions { OutputFormat = OutputFormat.List });
        if (results.IsValid)
        {
            return;
        }

        var details = results.Details
            .Where(detail => detail.HasErrors)
            .SelectMany(detail => detail.Errors?.Select(error => $"{detail.InstanceLocation}: {error.Key} {error.Value}") ?? [])
            .ToList();

        var message = details.Count == 0 ? "schema validation failed" : string.Join("; ", details);
        throw new PolicyValidationException($"Document '{documentName}' failed schema validation: {message}");
    }
}