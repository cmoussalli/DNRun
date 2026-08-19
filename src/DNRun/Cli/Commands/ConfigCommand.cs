using DNRun.Configuration;
using DNRun.Discovery;
using DNRun.Presentation;

namespace DNRun.Cli.Commands;

/// <summary>
/// <c>dnrun config</c>: what DNRun resolved and what it will act on. The first thing to run
/// when DNRun picks a root or a project that surprises you.
/// </summary>
internal static class ConfigCommand
{
    public static int Execute(DNRunSession session)
    {
        Output.Banner();
        Output.Blank();

        Output.Label("Working directory:");
        Output.Line("  " + session.Context.WorkingDirectory);
        Output.Blank();

        Output.Label("Repository root:");
        Output.Line("  " + session.RepositoryRoot);
        Output.Line("  " + Output.Dim("identified by: " + (session.Context.RootMarker ?? "nothing — fell back to the working directory")));
        Output.Blank();

        Output.Label("Solution:");
        Output.Line("  " + (session.Context.SolutionPath ?? Output.Dim("(none)")));
        Output.Blank();

        var configPath = ConfigurationManager.ConfigPath(session.RepositoryRoot);
        Output.Label("Configuration file:");
        Output.Line("  " + configPath);

        if (!File.Exists(configPath))
        {
            Output.Line("  " + Output.Dim("(does not exist yet — written on the first selection)"));
            Output.Blank();
            return ExitCodes.Success;
        }

        if (session.ConfigUnreadable)
        {
            Output.Line("  " + Output.Dim("(unreadable — see the warning above)"));
            Output.Blank();
            return ExitCodes.ConfigError;
        }

        Output.Blank();
        Output.Label("Effective settings:");
        Output.Line("  startupProject:    " + (session.Config?.StartupProject ?? Output.Dim("(none)")));
        Output.Line("  ignoreDirectories: " + FormatList(session.Config?.IgnoreDirectories, "(defaults only)"));
        Output.Line("  runnableProjects:  " + FormatList(session.Config?.RunnableProjects, "(none)"));
        Output.Blank();

        Output.Label("Always-ignored directories:");
        Output.Line("  " + Output.Dim(string.Join(", ", RepositoryScanner.DefaultIgnoredDirectories)));

        return ExitCodes.Success;
    }

    private static string FormatList(string[]? values, string emptyText) =>
        values is null || values.Length == 0 ? Output.Dim(emptyText) : string.Join(", ", values);
}
