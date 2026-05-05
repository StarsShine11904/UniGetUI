namespace UniGetUI.PolicySimulator.Core;

public static class PolicyPathResolver
{
    public static string ResolveExistingPath(string path)
    {
        if (Path.IsPathRooted(path))
        {
            return Path.GetFullPath(path);
        }

        foreach (var basePath in GetCandidateBasePaths())
        {
            foreach (var ancestor in EnumerateAncestors(basePath))
            {
                var candidate = Path.GetFullPath(Path.Combine(ancestor, path));
                if (File.Exists(candidate) || Directory.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return Path.GetFullPath(path);
    }

    public static string FindPoliciesRoot()
    {
        foreach (var basePath in GetCandidateBasePaths())
        {
            foreach (var ancestor in EnumerateAncestors(basePath))
            {
                var policySchemaPath = Path.Combine(ancestor, "schemas", "unigetui.package-policy.schema.1.0.json");
                var requestSchemaPath = Path.Combine(ancestor, "schemas", "unigetui.package-request.schema.1.0.json");
                if (File.Exists(policySchemaPath) && File.Exists(requestSchemaPath))
                {
                    return ancestor;
                }
            }
        }

        throw new DirectoryNotFoundException("Could not locate the policies root containing the schema files.");
    }

    private static IEnumerable<string> GetCandidateBasePaths()
    {
        yield return Directory.GetCurrentDirectory();
        yield return AppContext.BaseDirectory;
    }

    private static IEnumerable<string> EnumerateAncestors(string path)
    {
        var directory = new DirectoryInfo(Path.GetFullPath(path));
        while (directory is not null)
        {
            yield return directory.FullName;
            directory = directory.Parent;
        }
    }
}