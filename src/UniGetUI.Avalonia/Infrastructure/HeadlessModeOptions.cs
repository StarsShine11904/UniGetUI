namespace UniGetUI.Avalonia.Infrastructure;

internal static class HeadlessModeOptions
{
    public const string HeadlessArgument = "--headless";
    public const string DaemonArgument = "--daemon";

    public static bool IsHeadless(IReadOnlyList<string> args)
    {
        return args.Contains(HeadlessArgument, StringComparer.OrdinalIgnoreCase)
            || args.Contains(DaemonArgument, StringComparer.OrdinalIgnoreCase);
    }
}
