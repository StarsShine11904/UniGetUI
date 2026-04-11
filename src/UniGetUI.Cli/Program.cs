using System.Text;
using UniGetUI.Interface;

namespace UniGetUI.Cli;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        Console.InputEncoding = Encoding.UTF8;
        Console.OutputEncoding = Encoding.UTF8;

        if (args.Contains("--help", StringComparer.OrdinalIgnoreCase))
        {
            await Console.Out.WriteLineAsync(
                "Usage: unigetui-cli [--automation] <command> [options]\n"
                    + "Examples:\n"
                    + "  unigetui-cli status\n"
                    + "  unigetui-cli list-installed --manager \".NET Tool\"\n"
                    + "  unigetui-cli search-packages --manager \".NET Tool\" --query dotnetsay\n"
                    + "  unigetui-cli install-package --manager \".NET Tool\" --package-id dotnetsay --version 2.1.4 --scope Global"
            );
            return 0;
        }

        string[] effectiveArgs = args.Contains(AutomationCliCommandRunner.AutomationArgument)
            ? args
            : [AutomationCliCommandRunner.AutomationArgument, .. args];

        return await AutomationCliCommandRunner.RunAsync(
            effectiveArgs,
            Console.Out,
            Console.Error
        );
    }
}
