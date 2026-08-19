using DNRun.Configuration;
using DNRun.Model;

namespace DNRun.Discovery;

/// <summary>Everything DNRun learned about the repository in one pass.</summary>
internal sealed record DiscoveryResult(
    RepositoryContext Context,
    IReadOnlyList<ProjectInfo> AllProjects,
    IReadOnlyList<ProjectInfo> RunnableProjects,
    IReadOnlyList<string> ScannedLocations,
    bool UsedFallbackScan,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Composes scanning, solution reading, and analysis into the single discovery pass every
/// command runs. Ordering is deterministic so that <c>[1]</c> means the same project on every
/// invocation.
/// </summary>
internal static class ProjectDiscovery
{
    public static DiscoveryResult Discover(RepositoryContext context, DNRunConfig? config)
    {
        var scan = RepositoryScanner.Scan(context, config);
        var warnings = new List<string>();

        var solutionProjects = context.SolutionPath is null
            ? []
            : SolutionReader.ReadProjectPaths(context.SolutionPath);

        var solutionSet = new HashSet<string>(solutionProjects, PathUtils.PathComparer);

        // The solution is also a discovery source: a project it references that the scan order
        // never reached (say tools/Foo/Foo.csproj) still belongs to this repository.
        var candidatePaths = scan.ProjectFiles
            .Concat(solutionProjects.Where(File.Exists))
            .Select(PathUtils.Normalize)
            .Distinct(PathUtils.PathComparer)
            .ToArray();

        var allowlist = BuildAllowlist(context.RepositoryRoot, config);

        var projects = new List<ProjectInfo>(candidatePaths.Length);
        foreach (var path in candidatePaths)
        {
            var project = ProjectAnalyzer.Analyze(path, context.RepositoryRoot);

            if (project.AnalysisWarning is not null)
            {
                warnings.Add($"{project.RelativePath} {project.AnalysisWarning}; skipped");
            }

            // Explicit allowlist overrides the heuristics (plan R2's escape hatch).
            if (!project.IsRunnable
                && (allowlist.Contains(project.AbsolutePath) || allowlist.Contains(project.Name)))
            {
                project = project with { IsRunnable = true };
            }

            projects.Add(project with { InSolution = solutionSet.Count == 0 || solutionSet.Contains(project.AbsolutePath) });
        }

        var ordered = projects.OrderBy(Rank).ThenBy(p => p.Name, StringComparer.Ordinal).ToArray();
        var runnable = ordered.Where(p => p.IsRunnable).ToArray();

        return new DiscoveryResult(
            context with { ScannedLocations = scan.ScannedLocations },
            ordered,
            runnable,
            scan.ScannedLocations,
            scan.UsedFallbackScan,
            warnings);
    }

    /// <summary>Kind priority first (plan §4.4), then ordinal name — see the ThenBy at the call site.</summary>
    private static int Rank(ProjectInfo project) => project.Kind.SortPriority();

    private static HashSet<string> BuildAllowlist(string repositoryRoot, DNRunConfig? config)
    {
        var allowlist = new HashSet<string>(PathUtils.PathComparer);
        foreach (var entry in config?.RunnableProjects ?? [])
        {
            if (string.IsNullOrWhiteSpace(entry))
            {
                continue;
            }

            // Accept either a repository-relative path or a bare project name.
            allowlist.Add(entry.Trim());
            allowlist.Add(PathUtils.FromRepositoryRelative(repositoryRoot, entry.Trim()));
        }

        return allowlist;
    }
}
