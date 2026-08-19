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
            // Environment.CurrentDirectory, never AppContext.BaseDirectory: DNRun is installed
            // once outside every repository and must discover projects relative to wherever the
            // terminal (or Orca's run command) happens to be (spec §3, §14.2).
            return CommandRouter.Execute(args, new DotnetProcessRunner(), Environment.CurrentDirectory);
        }
        catch (Exception ex)
        {
            Output.Error(ex.Message);
            return ExitCodes.LaunchFailure;
        }
    }
}
