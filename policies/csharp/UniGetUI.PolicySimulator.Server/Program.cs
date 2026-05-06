using System.Text.Json;
using UniGetUI.PolicySimulator.Core;

const string ProtocolVersion = "1.0";
const string RequestMediaType = "application/vnd.unigetui.package-request+json; version=1.0";
const string ResponseMediaType = "application/vnd.unigetui.package-broker-response+json; version=1.0";

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

app.MapGet("/health", Health);
app.MapGet("/v1/health", Health);
app.MapGet("/v1/capabilities", Capabilities);
app.MapPost("/requests", HandlePackageOperation);
app.MapPost("/v1/package-operations/evaluate", HandlePackageOperation);

Console.WriteLine($"UniGetUI C# policy server simulator listening on {url}");
Console.WriteLine($"Policy: {policyPath}");
await app.RunAsync();

IResult Health()
{
    return Results.Json(new
    {
        status = "ready",
        protocolVersion = ProtocolVersion,
        elevatedSimulation = true,
        policyPath,
        endpoints = new[] { "GET /v1/health", "GET /v1/capabilities", "POST /v1/package-operations/evaluate", "POST /requests" }
    });
}

IResult Capabilities()
{
    return Results.Json(new
    {
        protocolVersion = ProtocolVersion,
        transports = new[] { "http-loopback-simulator", "http-named-pipe" },
        requestMediaTypes = new[] { RequestMediaType, "application/json" },
        responseMediaTypes = new[] { ResponseMediaType },
        requestSchema = "https://aka.ms/unigetui/package-request.schema.1.0.json",
        responseSchema = "https://aka.ms/unigetui/package-broker-response.schema.1.0.json",
        supportedManagers = new[] { "Winget", "PowerShell" },
        supportedOperations = new[] { "install", "update", "uninstall" },
        maxRequestBodyBytes = 262144
    });
}

async Task<IResult> HandlePackageOperation(HttpRequest httpRequest)
{
    var auditId = CreateAuditId();
    using var reader = new StreamReader(httpRequest.Body);
    var body = await reader.ReadToEndAsync();
    if (string.IsNullOrWhiteSpace(body))
    {
        AddProtocolHeaders(httpRequest.HttpContext.Response, null, auditId, loadedPolicy.Value);
        return Results.Json(ToValidationFailureEnvelope(loadedPolicy.Value, auditId, "Request body is required."), contentType: ResponseMediaType, statusCode: StatusCodes.Status400BadRequest);
    }

    try
    {
        var format = DocumentLoader.InferFormatFromContentType(httpRequest.ContentType);
        var request = loader.LoadText<PackageRequest>(body, "HTTP request body", format, requestSchemaPath).Value;
        ValidateRequestHeaders(httpRequest, request);
        var response = broker.Evaluate(request);
        AddProtocolHeaders(httpRequest.HttpContext.Response, response.RequestId, auditId, loadedPolicy.Value);
        var statusCode = response.Decision == "allow" ? StatusCodes.Status200OK : StatusCodes.Status403Forbidden;
        return Results.Json(ToEnvelope(response, loadedPolicy.Value, auditId), contentType: ResponseMediaType, statusCode: statusCode);
    }
    catch (Exception exception) when (exception is PolicyValidationException or JsonException)
    {
        AddProtocolHeaders(httpRequest.HttpContext.Response, null, auditId, loadedPolicy.Value);
        return Results.Json(ToValidationFailureEnvelope(loadedPolicy.Value, auditId, exception.Message), contentType: ResponseMediaType, statusCode: StatusCodes.Status422UnprocessableEntity);
    }
}

static void ValidateRequestHeaders(HttpRequest httpRequest, PackageRequest request)
{
    if (httpRequest.Headers.TryGetValue("UniGetUI-Request-Id", out var requestIdHeader) && requestIdHeader.Count > 0 && requestIdHeader[0] != request.RequestId)
    {
        throw new PolicyValidationException("Header 'UniGetUI-Request-Id' must match request body field 'requestId'.");
    }
}

static void AddProtocolHeaders(HttpResponse response, string? requestId, string auditId, PolicyDocument policy)
{
    response.Headers["UniGetUI-Protocol-Version"] = ProtocolVersion;
    response.Headers["UniGetUI-Audit-Id"] = auditId;
    response.Headers["UniGetUI-Policy-Id"] = policy.Metadata.Id;
    response.Headers["UniGetUI-Policy-Revision"] = policy.Metadata.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture);
    if (!string.IsNullOrWhiteSpace(requestId))
    {
        response.Headers["UniGetUI-Request-Id"] = requestId;
    }
}

static object ToEnvelope(BrokerEvaluationResponse response, PolicyDocument policy, string auditId)
{
    var timestamp = DateTimeOffset.UtcNow;
    return new
    {
        responseVersion = "1.0.0",
        responseType = "packageBrokerResponse",
        broker = new
        {
            name = "UniGetUI C# policy server simulator",
            protocolVersion = ProtocolVersion,
            transport = "http-loopback-simulator",
            elevatedSimulation = true
        },
        auditId,
        receivedAt = timestamp,
        completedAt = timestamp,
        response.RequestId,
        response.Manager,
        response.Source,
        response.PackageId,
        response.Operation,
        response.Decision,
        response.RuleId,
        response.Reason,
        response.WouldExecute,
        policy = new
        {
            id = policy.Metadata.Id,
            revision = policy.Metadata.Revision,
            policyVersion = policy.PolicyVersion
        },
        execution = new
        {
            response.Mode,
            Command = response.Command,
            note = "The sample server returns the command that an elevated broker would run; it does not execute package managers."
        }
    };
}

static object ToValidationFailureEnvelope(PolicyDocument policy, string auditId, string reason)
{
    var timestamp = DateTimeOffset.UtcNow;
    return new
    {
        responseVersion = "1.0.0",
        responseType = "packageBrokerResponse",
        broker = new
        {
            name = "UniGetUI C# policy server simulator",
            protocolVersion = ProtocolVersion,
            transport = "http-loopback-simulator",
            elevatedSimulation = true
        },
        auditId,
        requestId = (string?)null,
        receivedAt = timestamp,
        completedAt = timestamp,
        manager = (string?)null,
        source = (string?)null,
        packageId = (string?)null,
        operation = (string?)null,
        decision = "deny",
        ruleId = "<validation-failure>",
        reason,
        wouldExecute = false,
        policy = new
        {
            id = policy.Metadata.Id,
            revision = policy.Metadata.Revision,
            policyVersion = policy.PolicyVersion
        },
        execution = new
        {
            mode = "simulated-elevated",
            command = Array.Empty<string>(),
            note = "The sample server validates and filters requests but never executes package managers."
        }
    };
}

static string CreateAuditId()
{
    return "audit-" + Guid.NewGuid().ToString("N");
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