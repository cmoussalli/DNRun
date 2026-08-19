using System.Reflection;
using DNRun.Cli.Commands;
using DNRun.Execution;
using DNRun.Presentation;

namespace DNRun.Cli;

/// <summary>Maps a parsed verb onto a command, building the session only when one is needed.</summary>
internal static class CommandRouter
{
    public static int Execute(string[] args, IProcessRunner runner, string workingDirectory)
    {
        var parsed = ParsedArgs.Parse(args);

        if (parsed.Error is not null)
        {
            Output.Error(parsed.Error);
            Output.Blank();
            PrintUsage();
            return ExitCodes.UsageError;
        }

        switch (parsed.Verb)
        {
            case Verb.Help:
                PrintUsage();
                return ExitCodes.Success;

            case Verb.Version:
                Console.WriteLine("dnrun " + Version);
                return ExitCodes.Success;
        }

        var session = DNRunSession.Create(workingDirectory);

        return parsed.Verb switch
        {
            Verb.Run => RunCommand.Execute(session, parsed.Forwarded, runner),
            Verb.Select => SelectCommand.Execute(session, parsed.Forwarded, runner),
            Verb.List => ListCommand.Execute(session),
            Verb.Config => ConfigCommand.Execute(session),
            Verb.Reset => ResetCommand.Execute(session),
            _ => ExitCodes.UsageError,
        };
    }

    public static string Version =>
        typeof(CommandRouter).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? "1.0.0";

    private static void PrintUsage()
    {
        Output.Banner();
        Output.Blank();
        Output.Line("Runs the right .NET project for the current directory, so a single generic");
        Output.Line("command replaces 'dotnet run --project <path>' in every repository.");
        Output.Blank();
        Output.Label("Usage:");
        Output.Line("  dnrun                 Run the saved startup project, or discover and choose one");
        Output.Line("  dnrun select          Choose a different startup project, save it, and run it");
        Output.Line("  dnrun list            Show the solution, runnable projects, and current selection");
        Output.Line("  dnrun config          Show the resolved root, config file, and effective settings");
        Output.Line("  dnrun reset           Forget the saved startup project");
        Output.Line("  dnrun --help          Show this help");
        Output.Line("  dnrun version         Show the version");
        Output.Blank();
        Output.Label("Passing arguments to the application:");
        Output.Line("  dnrun -- --urls http://localhost:5005");
        Output.Blank();
        Output.Label("Configuration:");
        Output.Line("  dnrun.config.json at the repository root records the chosen startup project.");
        Output.Line("  Discovery starts from the current working directory, never from DNRun.exe's");
        Output.Line("  own location, so one installation serves every repository.");
    }
}
