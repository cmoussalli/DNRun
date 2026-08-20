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

            case Verb.Nuget:
                return ExecuteNuget(parsed.Forwarded, workingDirectory);
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

    /// <summary>
    /// The version is validated before the repository is touched, so a typo costs nothing and
    /// '--help' never pays for a discovery pass.
    /// </summary>
    private static int ExecuteNuget(IReadOnlyList<string> args, string workingDirectory)
    {
        var request = NugetRequest.Parse(args);

        if (request.Error is not null)
        {
            Output.Error(request.Error);
            Output.Blank();
            PrintNugetUsage();
            return ExitCodes.UsageError;
        }

        if (request.Action == NugetAction.Help)
        {
            PrintNugetUsage();
            return ExitCodes.Success;
        }

        return NugetCommand.Execute(DNRunSession.Create(workingDirectory), request);
    }

    public static string Version =>
        typeof(CommandRouter).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? "1.1.0";

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
        Output.Line("  dnuget 1.2.14         Set the version of the NuGet package this repo publishes");
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

    private static void PrintNugetUsage()
    {
        Output.Banner();
        Output.Blank();
        Output.Line("Sets the version of the NuGet package this repository publishes, using the same");
        Output.Line("project discovery as 'dnrun'. 'dnuget' and 'dnrun nuget' are the same command.");
        Output.Blank();
        Output.Label("Usage:");
        Output.Line("  dnuget 1.2.14         Set the package version of the saved package project");
        Output.Line("  dnuget                Show the package project and the version it declares");
        Output.Line("  dnuget list           List every packable project with its current version");
        Output.Line("  dnuget select 1.2.14  Choose a different package project, save it, and set the version");
        Output.Line("  dnuget --all 1.2.14   Set the version on every packable project");
        Output.Line("  dnuget reset          Forget the saved package project");
        Output.Line("  dnuget --help         Show this help");
        Output.Blank();
        Output.Label("Versions:");
        Output.Line("  2 to 4 numbers, an optional prerelease label, an optional +metadata:");
        Output.Line("  1.2.14, 1.2.14.3, 1.3.0-beta.1, 2.0.0-rc.2+build.57. A leading 'v' is accepted.");
        Output.Blank();
        Output.Label("What gets written:");
        Output.Line("  The project's own <Version> (or <PackageVersion>, or <VersionPrefix>/<VersionSuffix>),");
        Output.Line("  plus <InformationalVersion>, <AssemblyVersion>, and <FileVersion> when the project");
        Output.Line("  already declares them. When the version comes from Directory.Build.props, that");
        Output.Line("  file is updated instead — and the projects sharing it are named first.");
    }
}
