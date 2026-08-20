using DNRun.Cli;
using DNRun.Execution;
using DNRun.Presentation;

namespace DNRun;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            // The same executable answers to two names: DNRun.exe runs projects, dnuget versions
            // the package. The alias is normally a shim, but a renamed copy must work identically.
            var invokedName = Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? string.Empty);

            // Environment.CurrentDirectory, never AppContext.BaseDirectory: DNRun is installed
            // once outside every repository and must discover projects relative to wherever the
            // terminal (or Orca's run command) happens to be (spec §3, §14.2).
            return CommandRouter.Execute(
                ParsedArgs.NormalizeArgs(invokedName, args),
                new DotnetProcessRunner(),
                Environment.CurrentDirectory);
        }
        catch (Exception ex)
        {
            Output.Error(ex.Message);
            return ExitCodes.LaunchFailure;
        }
    }
}
