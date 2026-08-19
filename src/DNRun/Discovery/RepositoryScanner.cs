using DNRun.Configuration;
using DNRun.Model;

namespace DNRun.Discovery;

/// <summary>Result of a repository scan, including which locations were actually walked.</summary>
internal sealed record ScanResult(
    IReadOnlyList<string> ProjectFiles,
    IReadOnlyList<string> ScannedLocations,
    bool UsedFallbackScan);

/// <summary>
/// Finds candidate .csproj files under the repository root (spec §4.2, plan §4.2).
///
/// Scan order is explicit and predictable: the root itself, then ./src recursively, and only if
/// both come up empty a depth-limited full scan. Directories are pruned during the walk rather
/// than filtered afterwards — an unpruned recursive scan of a repo with a populated node_modules
/// takes seconds.
/// </summary>
internal static class RepositoryScanner
{
    public const int FallbackScanDepth = 6;
    private const int SrcScanDepth = 12;

    public static readonly string[] DefaultIgnoredDirectories =
    [
        "bin", "obj", "node_modules", ".git", ".vs", ".idea", ".vscode",
        "packages", "artifacts", "TestResults", ".nuke", "dist", ".svn",
    ];

    public static ScanResult Scan(RepositoryContext context, DNRunConfig? config)
    {
        var ignored = BuildIgnoreSet(config);
        var root = context.RepositoryRoot;

        var scanned = new List<string> { root };
        var found = new List<string>();

        // 1. Repository root, top level only.
        found.AddRange(EnumerateProjects(root));

        // 2. ./src, recursively.
        var srcDirectory = Path.Combine(root, "src");
        if (Directory.Exists(srcDirectory))
        {
            scanned.Add(srcDirectory);
            Walk(srcDirectory, SrcScanDepth, ignored, found);
        }

        // 3. Extension beyond the spec: nothing in ./ or ./src, so sweep the tree before giving up.
        //    Costs nothing on the happy path and rescues layouts like source/, apps/, or Backend/.
        var usedFallback = false;
        if (found.Count == 0)
        {
            usedFallback = true;
            scanned.Add(Path.Combine(root, "**"));
            Walk(root, FallbackScanDepth, ignored, found, skipTopLevelFiles: true);
        }

        var projectFiles = found
            .Select(PathUtils.Normalize)
            .Distinct(PathUtils.PathComparer)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new ScanResult(projectFiles, scanned, usedFallback);
    }

    private static HashSet<string> BuildIgnoreSet(DNRunConfig? config)
    {
        var ignored = new HashSet<string>(DefaultIgnoredDirectories, StringComparer.OrdinalIgnoreCase);
        foreach (var extra in config?.IgnoreDirectories ?? [])
        {
            if (!string.IsNullOrWhiteSpace(extra))
            {
                ignored.Add(extra.Trim().Trim('/', '\\'));
            }
        }

        return ignored;
    }

    private static void Walk(
        string directory,
        int depthRemaining,
        HashSet<string> ignored,
        List<string> results,
        bool skipTopLevelFiles = false)
    {
        if (!skipTopLevelFiles)
        {
            results.AddRange(EnumerateProjects(directory));
        }

        if (depthRemaining <= 0)
        {
            return;
        }

        foreach (var child in EnumerateDirectories(directory))
        {
            var name = Path.GetFileName(child);
            if (ignored.Contains(name) || name.StartsWith('.'))
            {
                continue;
            }

            // Never follow junctions or symlinks: they are the one way this walk can cycle.
            if (IsReparsePoint(child))
            {
                continue;
            }

            Walk(child, depthRemaining - 1, ignored, results);
        }
    }

    private static IEnumerable<string> EnumerateProjects(string directory)
    {
        try
        {
            return Directory.EnumerateFiles(directory, "*.csproj", new EnumerationOptions
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

    private static IEnumerable<string> EnumerateDirectories(string directory)
    {
        try
        {
            return Directory.EnumerateDirectories(directory, "*", new EnumerationOptions
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = false,
            }).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static bool IsReparsePoint(string directory)
    {
        try
        {
            return new DirectoryInfo(directory).Attributes.HasFlag(FileAttributes.ReparsePoint);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }
}
