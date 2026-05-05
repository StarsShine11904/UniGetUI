using System.Text.Json;
using UniGetUI.PolicySimulator.Core;

var parsedArgs = ArgumentParser.Parse(args);
var policiesRoot = PolicyPathResolver.FindPoliciesRoot();
var policyPath = PolicyPathResolver.ResolveExistingPath(parsedArgs.GetValueOrDefault("policy") ?? throw new ArgumentException("Missing required --policy <path> argument."));
var policySchemaPath = parsedArgs.TryGetValue("policy-schema", out var policySchemaArgument) ? PolicyPathResolver.ResolveExistingPath(policySchemaArgument) : Path.Combine(policiesRoot, "schemas", "unigetui.package-policy.schema.1.0.json");
var requestSchemaPath = parsedArgs.TryGetValue("request-schema", out var requestSchemaArgument) ? PolicyPathResolver.ResolveExistingPath(requestSchemaArgument) : Path.Combine(policiesRoot, "schemas", "unigetui.package-request.schema.1.0.json");
var url = parsedArgs.GetValueOrDefault("url") ?? "http://127.0.0.1:8765";

var loader = new DocumentLoader();
var loadedPolicy = loader.LoadFile<PolicyDocument>(policyPath, policySchemaPath);
PolicyEvaluator.ValidatePolicyShape(loadedPolicy.Value);
var broker = new BrokerSimulator(loadedPolicy.Value);

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(url);

var app = builder.Build();

app.MapGet("/health", () => Results.Json(new
{
    status = "ready",
    elevatedSimulation = true,
    policyPath,
    endpoints = new[] { "GET /health", "POST /requests" }
}));

app.MapPost("/requests", async (HttpRequest httpRequest) =>
{
    using var reader = new StreamReader(httpRequest.Body);
    var body = await reader.ReadToEndAsync();
    if (string.IsNullOrWhiteSpace(body))
    {
        return Results.BadRequest(new
        {
            decision = "deny",
            ruleId = "<validation-failure>",
            reason = "Request body is required.",
            wouldExecute = false
        });
    }

    try
    {
        var format = DocumentLoader.InferFormatFromContentType(httpRequest.ContentType);
        var request = loader.LoadText<PackageRequest>(body, "HTTP request body", format, requestSchemaPath).Value;
        var response = broker.Evaluate(request);
        return response.Decision == "allow" ? Results.Ok(ToEnvelope(response)) : Results.Json(ToEnvelope(response), statusCode: StatusCodes.Status403Forbidden);
    }
    catch (Exception exception) when (exception is PolicyValidationException or JsonException)
    {
        return Results.Json(new
        {
            server = "UniGetUI C# policy server simulator",
            elevatedSimulation = true,
            receivedAt = DateTimeOffset.UtcNow,
            decision = "deny",
            ruleId = "<validation-failure>",
            reason = exception.Message,
            wouldExecute = false,
            execution = new
            {
                mode = "simulated-elevated",
                command = Array.Empty<string>(),
                note = "The sample server validates and filters requests but never executes package managers."
            }
        }, statusCode: StatusCodes.Status403Forbidden);
    }
});

Console.WriteLine($"UniGetUI C# policy server simulator listening on {url}");
Console.WriteLine($"Policy: {policyPath}");
await app.RunAsync();

static object ToEnvelope(BrokerEvaluationResponse response)
{
    return new
    {
        server = "UniGetUI C# policy server simulator",
        elevatedSimulation = true,
        receivedAt = DateTimeOffset.UtcNow,
        response.RequestId,
        response.Manager,
        response.Source,
        response.PackageId,
        response.Operation,
        response.Decision,
        response.RuleId,
        response.Reason,
        response.WouldExecute,
        execution = new
        {
            response.Mode,
            Command = response.Command,
            note = "The sample server returns the command that an elevated broker would run; it does not execute package managers."
        }
    };
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