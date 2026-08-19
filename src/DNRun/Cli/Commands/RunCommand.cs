using DNRun.Configuration;
using DNRun.Execution;
using DNRun.Model;
using DNRun.Presentation;

namespace DNRun.Cli.Commands;

/// <summary>
/// The default flow (spec §8.1, §12): config → validate → run, otherwise discover and either
/// auto-run the only candidate or prompt. Also serves <c>dnrun select</c>, which is the same
/// flow with the prompt forced.
/// </summary>
internal static class RunCommand
{
    public static int Execute(
        DNRunSession session,
        IReadOnlyList<string> forwarded,
        IProcessRunner runner,
        bool forceSelection = false)
    {
        Output.Banner();
        Output.Blank();

        var project = forceSelection ? null : ResolveFromConfig(session);

        if (project is null)
        {
            Output.Line("Searching for .NET projects...");
            Output.Blank();

            if (session.Discovery.UsedFallbackScan && session.Discovery.AllProjects.Count > 0)
            {
                Output.Line(Output.Dim("No projects in ./ or ./src — performed a full scan."));
                Output.Blank();
            }

            project = SelectProject(session, forceSelection, out var failureCode);
            if (project is null)
            {
                return failureCode;
            }
        }

        var plan = new RunPlan(project, session.RepositoryRoot, "run", forwarded);

        Output.Label("Starting:");
        Output.Line("  " + plan.ToDisplayString(session.RepositoryRoot));
        Output.Blank();

        return runner.Run(plan);
    }

    /// <summary>Returns the saved startup project when it still checks out, else null after warning (spec §7).</summary>
    private static ProjectInfo? ResolveFromConfig(DNRunSession session)
    {
        var validation = ConfigurationManager.Validate(
            session.Config,
            session.RepositoryRoot,
            session.Discovery.AllProjects);

        switch (validation.State)
        {
            case ConfigState.Valid:
                Output.Label("Startup project:");
                Output.Line("  " + validation.Project!.Name);
                Output.Blank();
                return validation.Project;

            case ConfigState.Missing:
                Output.Warn($"the configured startup project '{validation.ConfiguredPath}' no longer exists.");
                Output.Blank();
                return null;

            case ConfigState.NotRunnable:
                Output.Warn($"the configured startup project '{validation.ConfiguredPath}' is no longer runnable.");
                Output.Blank();
                return null;

            default:
                return null;
        }
    }

    /// <summary>
    /// Scenario A / B / C from spec §6. Returns null when the run cannot proceed, with the exit
    /// code to use in <paramref name="failureCode"/>.
    /// </summary>
    private static ProjectInfo? SelectProject(DNRunSession session, bool forceSelection, out int failureCode)
    {
        failureCode = ExitCodes.Success;
        var candidates = session.Runnable;
        var listingLibraries = false;

        if (candidates.Count == 0)
        {
            // Zero runnable projects during an explicit selection means the heuristics may simply
            // be wrong (e.g. OutputType inherited from Directory.Build.props). Offer everything
            // rather than hard-blocking the user.
            if (forceSelection && session.Discovery.AllProjects.Count > 0)
            {
                candidates = session.Discovery.AllProjects;
                listingLibraries = true;
                Output.Warn("no project was classified as runnable; listing every discovered project.");
                Output.Blank();
            }
            else
            {
                return ReportNothingFound(session, out failureCode);
            }
        }

        // Scenario B — exactly one candidate, no interaction required.
        if (candidates.Count == 1 && !forceSelection)
        {
            Output.Label("Found runnable project:");
            Output.Line("  " + candidates[0].Name);
            Output.Blank();
            return candidates[0];
        }

        // Scenario C — several candidates.
        Output.Line(forceSelection ? "Available projects:" : "Multiple runnable projects found:");
        Output.Blank();
        ProjectPrompt.PrintNumberedList(candidates, withPaths: listingLibraries);
        Output.Blank();

        var result = ProjectPrompt.Select(
            candidates,
            forceSelection ? "Select the default project:" : "Select the project to run:");

        switch (result.Outcome)
        {
            case PromptOutcome.NonInteractive:
                Output.Error("multiple runnable projects found and no interactive terminal is attached.");
                Output.Line("Run 'dnrun select' from a terminal to choose a default startup project,");
                Output.Line($"or set \"startupProject\" in {ConfigurationManager.ConfigPath(session.RepositoryRoot)}.");
                failureCode = ExitCodes.UsageError;
                return null;

            case PromptOutcome.Abandoned:
                Output.Error("no project selected.");
                failureCode = ExitCodes.UsageError;
                return null;
        }

        var selected = result.Project!;

        Output.Label("Selected:");
        Output.Line("  " + selected.Name);
        Output.Blank();

        if (session.ConfigUnreadable)
        {
            Output.Line("Rewriting dnrun.config.json...");
        }
        else
        {
            Output.Line("Saving default project...");
        }

        session.SaveStartupProject(selected);
        Output.Blank();

        return selected;
    }

    /// <summary>Scenario A — nothing runnable anywhere (spec §6).</summary>
    private static ProjectInfo? ReportNothingFound(DNRunSession session, out int failureCode)
    {
        failureCode = ExitCodes.NoRunnableProject;

        Output.Error("no runnable .NET project was found.");
        Output.Blank();
        session.PrintScannedLocations();

        var others = session.Discovery.AllProjects;
        if (others.Count > 0)
        {
            Output.Blank();
            Output.Label($"Projects found, none classified as runnable ({others.Count}):");
            foreach (var project in others)
            {
                Output.Line($"  {project.Name}  {Output.Dim(project.RelativePath)}");
            }

            Output.Blank();
            Output.Line("If one of these should be runnable, choose it with 'dnrun select',");
            Output.Line("or list it under \"runnableProjects\" in dnrun.config.json.");
        }

        return null;
    }
}
