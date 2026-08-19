using DNRun.Configuration;
using DNRun.Model;
using DNRun.Presentation;

namespace DNRun.Cli.Commands;

/// <summary>
/// <c>dnrun list</c> (spec §8.3): show the solution, the runnable candidates with their paths,
/// and the current startup project. Never launches anything.
/// </summary>
internal static class ListCommand
{
    public static int Execute(DNRunSession session)
    {
        Output.Banner();
        Output.Blank();

        PrintSolution(session);
        PrintRoot(session);

        var runnable = session.Runnable;
        if (runnable.Count == 0)
        {
            Output.Error("no runnable .NET project was found.");
            Output.Blank();
            session.PrintScannedLocations();
        }
        else
        {
            Output.Label($"Runnable projects ({runnable.Count}):");
            Output.Blank();
            PrintProjects(runnable);
        }

        var others = session.Discovery.AllProjects.Where(p => !p.IsRunnable).ToArray();
        if (others.Length > 0)
        {
            Output.Blank();
            Output.Label($"Other projects ({others.Length}):");
            Output.Blank();
            foreach (var project in others)
            {
                var kind = project.Kind == ProjectKind.Unknown ? "library" : project.Kind.ToDisplayString().ToLowerInvariant();
                Output.Line($"  {project.Name}  {Output.Dim("(" + kind + ")")}");
                Output.Line($"      {Output.Dim(project.RelativePath)}");
            }
        }

        PrintStartupProject(session);
        return ExitCodes.Success;
    }

    private static void PrintSolution(DNRunSession session)
    {
        if (session.Context.SolutionPath is null)
        {
            return;
        }

        Output.Label("Solution:");
        Output.Line("  " + Path.GetFileName(session.Context.SolutionPath));

        if (session.Context.AllSolutionPaths.Count > 1)
        {
            var others = session.Context.AllSolutionPaths
                .Where(p => !PathUtils.SamePath(p, session.Context.SolutionPath))
                .Select(Path.GetFileName);

            Output.Line("  " + Output.Dim("also present: " + string.Join(", ", others)));
        }

        Output.Blank();
    }

    private static void PrintRoot(DNRunSession session)
    {
        Output.Label("Repository root:");
        Output.Line("  " + session.RepositoryRoot);
        Output.Blank();
    }

    private static void PrintProjects(IReadOnlyList<ProjectInfo> projects)
    {
        var width = projects.Count.ToString().Length;

        for (var i = 0; i < projects.Count; i++)
        {
            var project = projects[i];
            var number = (i + 1).ToString().PadLeft(width);
            var tags = new List<string>();

            if (project.Kind != ProjectKind.Unknown)
            {
                tags.Add(project.Kind.ToDisplayString());
            }

            if (!project.InSolution)
            {
                tags.Add("not in solution");
            }

            var suffix = tags.Count > 0 ? "  " + Output.Dim("(" + string.Join(", ", tags) + ")") : string.Empty;

            Output.Line($"  {Output.Cyan("[" + number + "]")} {project.Name}{suffix}");
            Output.Line($"      {Output.Dim(project.RelativePath)}");
        }
    }

    private static void PrintStartupProject(DNRunSession session)
    {
        Output.Blank();
        Output.Label("Current startup project:");

        var validation = ConfigurationManager.Validate(
            session.Config,
            session.RepositoryRoot,
            session.Discovery.AllProjects);

        var message = validation.State switch
        {
            ConfigState.Valid => "  " + validation.Project!.Name,
            ConfigState.Missing => $"  {validation.ConfiguredPath} " + Output.Dim("(missing — will be re-selected)"),
            ConfigState.NotRunnable => $"  {validation.ConfiguredPath} " + Output.Dim("(no longer runnable — will be re-selected)"),
            _ => "  " + Output.Dim("(none — will be chosen on the next run)"),
        };

        Output.Line(message);
    }
}
