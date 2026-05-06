using System.Net;
using System.Net.Http.Headers;
using UniGetUI.PolicySimulator.Core;

var parsedArgs = ArgumentParser.Parse(args);
var requestPath = parsedArgs.GetValueOrDefault("request") ?? throw new ArgumentException("Missing required --request <path> argument.");
var server = parsedArgs.GetValueOrDefault("server") ?? "http://127.0.0.1:8765";
var endpoint = parsedArgs.GetValueOrDefault("endpoint") ?? "/v1/package-operations/evaluate";
var asJson = parsedArgs.ContainsKey("json");

var fullRequestPath = PolicyPathResolver.ResolveExistingPath(requestPath);
var requestText = await File.ReadAllTextAsync(fullRequestPath);
var format = DocumentLoader.InferFormatFromPath(fullRequestPath);
var contentType = format == "yaml" ? "application/x-yaml" : "application/vnd.unigetui.package-request+json; version=1.0";

using var client = new HttpClient();
client.DefaultRequestHeaders.Accept.Add(MediaTypeWithQualityHeaderValue.Parse("application/vnd.unigetui.package-broker-response+json; version=1.0"));
client.DefaultRequestHeaders.Add("UniGetUI-Protocol-Version", "1.0");
using var content = new StringContent(requestText);
content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);

var response = await client.PostAsync(new Uri(new Uri(server), endpoint), content);
var responseText = await response.Content.ReadAsStringAsync();

if (asJson)
{
    Console.WriteLine(responseText);
}
else
{
    Console.WriteLine($"HTTP {(int)response.StatusCode} {response.StatusCode}");
    Console.WriteLine(responseText);
}

return response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Forbidden or HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity ? 0 : 1;

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