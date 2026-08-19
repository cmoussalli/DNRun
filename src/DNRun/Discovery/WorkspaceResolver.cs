using DNRun.Model;

namespace DNRun.Discovery;

/// <summary>
/// Turns the process working directory into a <see cref="RepositoryContext"/> (plan §4.1).
///
/// The single most important invariant in the application: discovery starts at
/// <see cref="Environment.CurrentDirectory"/>, never at the directory holding DNRun.exe
/// (spec §3, §14.2).
/// </summary>
internal static class WorkspaceResolver
{
    private const int MaxAncestorLevels = 32;

    /// <summary>Root markers, most authoritative first. The first match in a directory wins.</summary>
    private static readonly string[] MarkerOrder = ["dnrun.config.json", "*.slnx", "*.sln", ".git", "global.json"];

    public static RepositoryContext Resolve(string workingDirectory)
    {
        var start = PathUtils.Normalize(workingDirectory);

        string? root = null;
        string? marker = null;

        var current = new DirectoryInfo(start);
        for (var level = 0; level < MaxAncestorLevels && current is not null; level++)
        {
            marker = FindMarker(current.FullName);
            if (marker is not null)
            {
                root = PathUtils.Normalize(current.FullName);
                break;
            }

            current = current.Parent;
        }

        // No marker anywhere up the tree: DNRun still works in a flat folder holding a lone .csproj.
        root ??= start;

        var solutions = FindSolutions(root);
        return new RepositoryContext(
            WorkingDirectory: start,
            RepositoryRoot: root,
            SolutionPath: ChooseSolution(root, solutions),
            ScannedLocations: [])
        {
            AllSolutionPaths = solutions,
            RootMarker = marker,
        };
    }

    private static string? FindMarker(string directory)
    {
        foreach (var candidate in MarkerOrder)
        {
            if (candidate.StartsWith('*'))
            {
                if (SafeEnumerateFiles(directory, candidate).Any())
                {
                    return candidate;
                }
            }
            else if (candidate == ".git")
            {
                // A worktree or submodule has .git as a file, not a directory.
                var gitPath = Path.Combine(directory, ".git");
                if (Directory.Exists(gitPath) || File.Exists(gitPath))
                {
                    return ".git";
                }
            }
            else if (File.Exists(Path.Combine(directory, candidate)))
            {
                return candidate;
            }
        }

        return null;
    }

    private static IReadOnlyList<string> FindSolutions(string root)
    {
        var solutions = new List<string>();
        solutions.AddRange(SafeEnumerateFiles(root, "*.slnx"));
        solutions.AddRange(SafeEnumerateFiles(root, "*.sln"));
        return solutions.Select(PathUtils.Normalize).Distinct(PathUtils.PathComparer).ToArray();
    }

    /// <summary>
    /// Multiple solutions at the root: prefer one named after the root directory, else the
    /// alphabetically first. <c>dnrun list</c> reports the ambiguity.
    /// </summary>
    private static string? ChooseSolution(string root, IReadOnlyList<string> solutions)
    {
        if (solutions.Count == 0)
        {
            return null;
        }

        if (solutions.Count == 1)
        {
            return solutions[0];
        }

        var rootName = Path.GetFileName(root);

        // Name match first, then .slnx over .sln — a repository holding both has migrated to the
        // newer format, and the marker order above already treats .slnx as more authoritative.
        return solutions
            .OrderBy(s => string.Equals(Path.GetFileNameWithoutExtension(s), rootName, PathUtils.PathComparison) ? 0 : 1)
            .ThenBy(s => s.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .First();
    }

    private static IEnumerable<string> SafeEnumerateFiles(string directory, string pattern)
    {
        try
        {
            return Directory.EnumerateFiles(directory, pattern, new EnumerationOptions
            {
                MatchCasing = MatchCasing.CaseInsensitive,
                IgnoreInaccessible = true,
                RecurseSubdirectories = false,
            }).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
