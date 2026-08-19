using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace DNRun.Discovery;

/// <summary>
/// Reads project paths out of a .sln or .slnx (spec §4.3, plan §4.3).
///
/// The solution is used as a filter and enrichment source, not as the discovery mechanism:
/// projects present on disk but absent from the solution are still offered, and projects listed
/// in the solution but missing on disk are ignored silently.
/// </summary>
internal static partial class SolutionReader
{
    [GeneratedRegex(
        "^Project\\(\"\\{[^}]+\\}\"\\)\\s*=\\s*\"([^\"]+)\"\\s*,\\s*\"([^\"]+)\"",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SolutionProjectLine();

    /// <summary>Absolute paths of the .csproj files referenced by the solution. Never throws.</summary>
    public static IReadOnlyList<string> ReadProjectPaths(string solutionPath)
    {
        try
        {
            var directory = Path.GetDirectoryName(PathUtils.Normalize(solutionPath)) ?? ".";

            var relativePaths = solutionPath.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase)
                ? ReadSlnx(solutionPath)
                : ReadSln(solutionPath);

            return relativePaths
                .Where(p => p.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                .Select(p => PathUtils.Normalize(Path.Combine(directory, p.Replace('\\', Path.DirectorySeparatorChar))))
                .Distinct(PathUtils.PathComparer)
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return [];
        }
    }

    private static IEnumerable<string> ReadSln(string solutionPath)
    {
        foreach (var line in File.ReadLines(solutionPath))
        {
            if (!line.StartsWith("Project(", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var match = SolutionProjectLine().Match(line);
            if (!match.Success)
            {
                continue;
            }

            var name = match.Groups[1].Value;
            var path = match.Groups[2].Value;

            // A solution folder repeats its name in the path slot; it is not a project.
            if (string.Equals(name, path, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            yield return path;
        }
    }

    private static IEnumerable<string> ReadSlnx(string solutionPath)
    {
        var document = XDocument.Load(solutionPath);
        return document
            .Descendants()
            .Where(e => string.Equals(e.Name.LocalName, "Project", StringComparison.OrdinalIgnoreCase))
            .Select(e => e.Attribute("Path")?.Value)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!)
            .ToArray();
    }
}
