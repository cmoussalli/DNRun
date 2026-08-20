using DNRun.Model;

namespace DNRun.Packaging;

/// <summary>One property that a version bump rewrote, for the summary printed afterwards.</summary>
internal sealed record VersionChange(string Property, string? OldValue, string NewValue);

/// <summary>
/// The file a project's package version actually comes from. Usually the .csproj, but repositories
/// that version every package together declare it once in a Directory.Build.props, and writing a
/// &lt;Version&gt; into the .csproj there would silently opt that one project out of the shared
/// version instead of bumping it.
/// </summary>
internal sealed record VersionSource(string FilePath, bool IsProjectFile, string? CurrentVersion)
{
    /// <summary>True when no version property exists anywhere; the bump will add one to the .csproj.</summary>
    public bool IsImplicit => CurrentVersion is null;
}

internal sealed record VersionUpdateResult(
    bool Succeeded,
    string FilePath,
    IReadOnlyList<VersionChange> Changes,
    string? Error);

/// <summary>
/// Reads and rewrites the version properties of a packable project (plan: dnuget).
///
/// Only properties that already exist are kept in sync — AssemblyVersion, FileVersion, and
/// InformationalVersion are never introduced, because adding them changes what the build produces
/// beyond the package version the user asked to change.
/// </summary>
internal static class VersionUpdater
{
    /// <summary>Properties that carry the package version itself, most specific first.</summary>
    private static readonly string[] PackageVersionProperties = ["PackageVersion", "Version"];

    /// <summary>Properties updated only when the project already declares them.</summary>
    private static readonly string[] InformationalProperties = ["InformationalVersion"];

    private static readonly string[] NumericProperties = ["AssemblyVersion", "FileVersion"];

    public const string PropsFileName = "Directory.Build.props";

    /// <summary>Finds where <paramref name="project"/>'s version is declared and what it currently is.</summary>
    public static VersionSource Resolve(ProjectInfo project, string repositoryRoot)
    {
        if (MsBuildFile.TryLoad(project.AbsolutePath, out var file, out _) && file is not null)
        {
            var declared = ReadVersion(file);
            if (declared is not null)
            {
                return new VersionSource(project.AbsolutePath, IsProjectFile: true, declared);
            }
        }

        foreach (var props in PropsFilesAbove(project.AbsolutePath, repositoryRoot))
        {
            if (!MsBuildFile.TryLoad(props, out var propsFile, out _) || propsFile is null)
            {
                continue;
            }

            var inherited = ReadVersion(propsFile);
            if (inherited is not null)
            {
                return new VersionSource(props, IsProjectFile: false, inherited);
            }
        }

        // Nothing declares a version: MSBuild's own default applies and the bump writes the first one.
        return new VersionSource(project.AbsolutePath, IsProjectFile: true, null);
    }

    /// <summary>The effective version string, or null when the project relies on MSBuild's 1.0.0 default.</summary>
    public static string? ReadVersion(MsBuildFile file) =>
        file.Read("PackageVersion")
        ?? file.Read("Version")
        ?? NuGetVersion.Combine(file.Read("VersionPrefix"), file.Read("VersionSuffix"));

    /// <summary>
    /// Writes <paramref name="version"/> into <paramref name="source"/>, updating whichever
    /// version properties the file already declares and adding <c>&lt;Version&gt;</c> when it
    /// declares none.
    /// </summary>
    public static VersionUpdateResult Apply(VersionSource source, NuGetVersion version)
    {
        if (!MsBuildFile.TryLoad(source.FilePath, out var file, out var loadError) || file is null)
        {
            return new VersionUpdateResult(false, source.FilePath, [], loadError);
        }

        if (!file.IsWellFormed)
        {
            return new VersionUpdateResult(false, source.FilePath, [], $"{source.FilePath} is not valid XML.");
        }

        var changes = new List<VersionChange>();
        var wrotePackageVersion = false;

        foreach (var property in PackageVersionProperties)
        {
            if (TryUpdate(file, property, version.WithoutMetadata, changes))
            {
                wrotePackageVersion = true;
            }
        }

        // VersionPrefix/VersionSuffix are the split form of the same value; keep them consistent
        // rather than leaving a stale prefix behind a freshly written Version.
        if (file.FindEffective("VersionPrefix") is not null)
        {
            TryUpdate(file, "VersionPrefix", version.Prefix, changes);
            wrotePackageVersion = true;

            if (file.FindEffective("VersionSuffix") is not null)
            {
                TryUpdate(file, "VersionSuffix", version.Suffix ?? string.Empty, changes);
            }
            else if (version.Suffix is not null)
            {
                file.TryInsertAfter("VersionPrefix", "VersionSuffix", version.Suffix);
                changes.Add(new VersionChange("VersionSuffix", null, version.Suffix));
            }
        }

        if (!wrotePackageVersion)
        {
            if (!file.SetOrInsert("Version", version.WithoutMetadata))
            {
                return new VersionUpdateResult(
                    false,
                    source.FilePath,
                    [],
                    $"could not add a <Version> property to {source.FilePath}.");
            }

            changes.Add(new VersionChange("Version", null, version.WithoutMetadata));
        }

        foreach (var property in InformationalProperties)
        {
            TryUpdate(file, property, version.ToString(), changes);
        }

        foreach (var property in NumericProperties)
        {
            TryUpdate(file, property, version.ToAssemblyVersion(), changes);
        }

        if (changes.Count == 0)
        {
            return new VersionUpdateResult(true, source.FilePath, changes, null);
        }

        return file.Save(out var saveError)
            ? new VersionUpdateResult(true, source.FilePath, changes, null)
            : new VersionUpdateResult(false, source.FilePath, [], saveError);
    }

    /// <summary>Directory.Build.props files between the project and the repository root, nearest first.</summary>
    public static IEnumerable<string> PropsFilesAbove(string projectPath, string repositoryRoot)
    {
        var root = PathUtils.Normalize(repositoryRoot);
        var directory = Path.GetDirectoryName(PathUtils.Normalize(projectPath));

        // 32 levels mirrors the workspace resolver's guard against a pathological tree.
        for (var depth = 0; directory is not null && depth < 32; depth++)
        {
            var candidate = Path.Combine(directory, PropsFileName);
            if (File.Exists(candidate))
            {
                yield return candidate;
            }

            if (string.Equals(directory, root, PathUtils.PathComparison))
            {
                yield break;
            }

            directory = Path.GetDirectoryName(directory);
        }
    }

    private static bool TryUpdate(MsBuildFile file, string property, string value, List<VersionChange> changes)
    {
        var existing = file.FindEffective(property);
        if (existing is null)
        {
            return false;
        }

        var old = existing.Value.Trim();
        if (!file.TrySet(property, value))
        {
            return false;
        }

        if (!string.Equals(old, value, StringComparison.Ordinal))
        {
            changes.Add(new VersionChange(property, old.Length == 0 ? null : old, value));
        }

        return true;
    }
}
