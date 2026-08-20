using DNRun.Configuration;
using DNRun.Model;
using DNRun.Packaging;
using DNRun.Presentation;

namespace DNRun.Cli.Commands;

/// <summary>
/// <c>dnuget [version]</c> — the packaging counterpart of <c>dnrun</c>. Discovery works exactly as
/// it does for running: find the projects in this repository, work out which ones produce a NuGet
/// package, ask once when there is more than one, and remember the answer in dnrun.config.json.
/// The only difference is what happens next — a project file is rewritten instead of launched.
/// </summary>
internal static class NugetCommand
{
    public static int Execute(DNRunSession session, NugetRequest request)
    {
        Output.Banner();
        Output.Blank();

        return request.Action switch
        {
            NugetAction.List => List(session),
            NugetAction.Reset => Reset(session),
            NugetAction.Set when request.AllProjects => SetAll(session, request.Version!),
            NugetAction.Set => Set(session, request),
            _ => Show(session, request),
        };
    }

    /// <summary>No version given: report what would be published today, and from which file.</summary>
    private static int Show(DNRunSession session, NugetRequest request)
    {
        var project = Resolve(session, request.ForceSelection, out var failureCode);
        if (project is null)
        {
            return failureCode;
        }

        var source = VersionUpdater.Resolve(project, session.RepositoryRoot);

        PrintProject(project);
        Output.Label("Package version:");
        Output.Line("  " + (source.CurrentVersion ?? Output.Dim("1.0.0 (MSBuild's default — no version property is declared)")));
        Output.Line("  " + Output.Dim(PathUtils.ToRepositoryRelative(session.RepositoryRoot, source.FilePath)));
        Output.Blank();
        Output.Line("Set a new version with:  " + Output.Cyan("dnuget 1.2.14"));

        return ExitCodes.Success;
    }

    private static int Set(DNRunSession session, NugetRequest request)
    {
        var project = Resolve(session, request.ForceSelection, out var failureCode);
        if (project is null)
        {
            return failureCode;
        }

        PrintProject(project);

        var source = VersionUpdater.Resolve(project, session.RepositoryRoot);
        WarnAboutSharedSource(session, project, source);

        return Write(session, source, request.Version!, project.Name) ? ExitCodes.Success : ExitCodes.VersionUpdateFailed;
    }

    /// <summary>
    /// <c>--all</c>: version every packable project in one go. Projects sharing a
    /// Directory.Build.props are grouped so the shared file is rewritten once, not once per project.
    /// </summary>
    private static int SetAll(DNRunSession session, NuGetVersion version)
    {
        var projects = session.Packable;
        if (projects.Count == 0)
        {
            return ReportNothingFound(session);
        }

        Output.Label($"Package projects ({projects.Count}):");
        foreach (var project in projects)
        {
            Output.Line("  " + project.Name + "  " + Output.Dim(project.RelativePath));
        }

        Output.Blank();

        var failed = false;
        var seen = new HashSet<string>(PathUtils.PathComparer);

        foreach (var project in projects)
        {
            var source = VersionUpdater.Resolve(project, session.RepositoryRoot);
            if (!seen.Add(PathUtils.Normalize(source.FilePath)))
            {
                continue;
            }

            failed |= !Write(session, source, version, project.Name);
        }

        return failed ? ExitCodes.VersionUpdateFailed : ExitCodes.Success;
    }

    private static int List(DNRunSession session)
    {
        var projects = session.Packable;
        if (projects.Count == 0)
        {
            return ReportNothingFound(session);
        }

        Output.Label($"Packable projects ({projects.Count}):");
        Output.Blank();

        foreach (var project in projects)
        {
            var source = VersionUpdater.Resolve(project, session.RepositoryRoot);
            var version = source.CurrentVersion ?? "1.0.0";
            var tags = new List<string>();

            if (project.PackageId is not null && !string.Equals(project.PackageId, project.Name, StringComparison.Ordinal))
            {
                tags.Add("id: " + project.PackageId);
            }

            if (!source.IsProjectFile)
            {
                tags.Add("from " + Path.GetFileName(source.FilePath));
            }
            else if (source.IsImplicit)
            {
                tags.Add("not declared");
            }

            var suffix = tags.Count > 0 ? "  " + Output.Dim("(" + string.Join(", ", tags) + ")") : string.Empty;

            Output.Line($"  {project.Name}  {Output.Cyan(version)}{suffix}");
            Output.Line($"      {Output.Dim(project.RelativePath)}");
        }

        PrintSavedProject(session);
        return ExitCodes.Success;
    }

    private static int Reset(DNRunSession session)
    {
        if (session.Config?.PackageProject is null)
        {
            Output.Line("No package project was saved.");
            return ExitCodes.Success;
        }

        var previous = session.Config.PackageProject;
        session.ClearPackageProject();

        Output.Line($"Forgot the package project ({previous}).");
        Output.Line("The next 'dnuget' will ask again.");
        return ExitCodes.Success;
    }

    /// <summary>Writes one file and prints exactly which properties moved.</summary>
    private static bool Write(DNRunSession session, VersionSource source, NuGetVersion version, string projectName)
    {
        var relative = PathUtils.ToRepositoryRelative(session.RepositoryRoot, source.FilePath);
        var result = VersionUpdater.Apply(source, version);

        if (!result.Succeeded)
        {
            Output.Error(result.Error ?? $"{relative} could not be updated.");
            return false;
        }

        if (result.Changes.Count == 0)
        {
            Output.Line($"{projectName} is already at {Output.Cyan(version.WithoutMetadata)}.");
            Output.Line("  " + Output.Dim(relative));
            Output.Blank();
            return true;
        }

        Output.Label($"Updated {relative}:");
        var width = result.Changes.Max(c => c.Property.Length);

        foreach (var change in result.Changes)
        {
            var from = change.OldValue is null ? Output.Dim("(added)") : change.OldValue;
            Output.Line($"  {change.Property.PadRight(width)}  {from} {Output.Dim("->")} {Output.Cyan(change.NewValue)}");
        }

        Output.Blank();
        Output.Line($"{projectName} will now publish as {Output.Cyan(version.WithoutMetadata)}.");
        Output.Blank();
        return true;
    }

    /// <summary>
    /// The saved package project when it still checks out, otherwise the discovery + prompt flow
    /// (spec §6 Scenario A/B/C, applied to packable projects).
    /// </summary>
    private static ProjectInfo? Resolve(DNRunSession session, bool forceSelection, out int failureCode)
    {
        failureCode = ExitCodes.Success;

        if (!forceSelection)
        {
            var validation = ConfigurationManager.ValidatePackageProject(
                session.Config,
                session.RepositoryRoot,
                session.Discovery.AllProjects);

            switch (validation.State)
            {
                case ConfigState.Valid:
                    return validation.Project;

                case ConfigState.Missing:
                    Output.Warn($"the configured package project '{validation.ConfiguredPath}' no longer exists.");
                    Output.Blank();
                    break;

                case ConfigState.NotRunnable:
                    Output.Warn($"the configured package project '{validation.ConfiguredPath}' is no longer packable.");
                    Output.Blank();
                    break;
            }
        }

        var candidates = session.Packable;
        if (candidates.Count == 0)
        {
            failureCode = ReportNothingFound(session);
            return null;
        }

        if (candidates.Count == 1 && !forceSelection)
        {
            return candidates[0];
        }

        Output.Line(forceSelection ? "Packable projects:" : "Multiple packable projects found:");
        Output.Blank();
        PrintNumberedList(session, candidates);
        Output.Blank();

        var result = ProjectPrompt.Select(candidates, "Select the project to version:");

        switch (result.Outcome)
        {
            case PromptOutcome.NonInteractive:
                Output.Error("multiple packable projects found and no interactive terminal is attached.");
                Output.Line("Run 'dnuget select' from a terminal to choose one,");
                Output.Line($"or set \"packageProject\" in {ConfigurationManager.ConfigPath(session.RepositoryRoot)}.");
                failureCode = ExitCodes.UsageError;
                return null;

            case PromptOutcome.Abandoned:
                Output.Error("no project selected.");
                failureCode = ExitCodes.UsageError;
                return null;
        }

        session.SavePackageProject(result.Project!);
        return result.Project;
    }

    /// <summary>The numbered menu, with each project's current version so the choice is informed.</summary>
    private static void PrintNumberedList(DNRunSession session, IReadOnlyList<ProjectInfo> projects)
    {
        var width = projects.Count.ToString().Length;

        for (var i = 0; i < projects.Count; i++)
        {
            var project = projects[i];
            var number = (i + 1).ToString().PadLeft(width);
            var version = VersionUpdater.Resolve(project, session.RepositoryRoot).CurrentVersion;

            Output.Line($"  {Output.Cyan("[" + number + "]")} {project.Name}"
                + (version is null ? string.Empty : "  " + Output.Dim(version)));
            Output.Line($"      {Output.Dim(project.RelativePath)}");
        }
    }

    private static void PrintProject(ProjectInfo project)
    {
        Output.Label("Package project:");
        var packageId = project.PackageId is null || string.Equals(project.PackageId, project.Name, StringComparison.Ordinal)
            ? string.Empty
            : "  " + Output.Dim("(" + project.PackageId + ")");

        Output.Line("  " + project.Name + packageId);
        Output.Blank();
    }

    /// <summary>
    /// A version declared in Directory.Build.props belongs to every project under it, so bumping
    /// one project there quietly bumps the others. Say so instead of surprising the user at
    /// pack time.
    /// </summary>
    private static void WarnAboutSharedSource(DNRunSession session, ProjectInfo project, VersionSource source)
    {
        if (source.IsProjectFile)
        {
            return;
        }

        var sharing = session.Packable
            .Where(p => !PathUtils.SamePath(p.AbsolutePath, project.AbsolutePath))
            .Where(p => PathUtils.SamePath(VersionUpdater.Resolve(p, session.RepositoryRoot).FilePath, source.FilePath))
            .ToArray();

        var relative = PathUtils.ToRepositoryRelative(session.RepositoryRoot, source.FilePath);

        Output.Line($"The version is declared in {relative}, so it is updated there.");

        if (sharing.Length > 0)
        {
            Output.Line(Output.Dim($"  {sharing.Length} other packable project{(sharing.Length == 1 ? string.Empty : "s")} inherit{(sharing.Length == 1 ? "s" : string.Empty)} it: "
                + string.Join(", ", sharing.Select(p => p.Name))));
        }

        Output.Blank();
    }

    private static void PrintSavedProject(DNRunSession session)
    {
        Output.Blank();
        Output.Label("Current package project:");

        var validation = ConfigurationManager.ValidatePackageProject(
            session.Config,
            session.RepositoryRoot,
            session.Discovery.AllProjects);

        var message = validation.State switch
        {
            ConfigState.Valid => "  " + validation.Project!.Name,
            ConfigState.Missing => $"  {validation.ConfiguredPath} " + Output.Dim("(missing — will be re-selected)"),
            ConfigState.NotRunnable => $"  {validation.ConfiguredPath} " + Output.Dim("(no longer packable — will be re-selected)"),
            _ => "  " + Output.Dim("(none — will be chosen on the next 'dnuget')"),
        };

        Output.Line(message);
    }

    private static int ReportNothingFound(DNRunSession session)
    {
        Output.Error("no packable .NET project was found.");
        Output.Blank();
        session.PrintScannedLocations();

        var excluded = session.Discovery.AllProjects.Where(p => !p.IsPackable).ToArray();
        if (excluded.Length > 0)
        {
            Output.Blank();
            Output.Label($"Projects found, none packable ({excluded.Length}):");
            foreach (var project in excluded)
            {
                Output.Line($"  {project.Name}  {Output.Dim(project.RelativePath)}");
            }

            Output.Blank();
            Output.Line("Projects with <IsPackable>false</IsPackable>, and test projects, are never offered.");
        }

        return ExitCodes.NoRunnableProject;
    }
}
