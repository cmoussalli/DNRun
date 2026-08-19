namespace DNRun;

/// <summary>Path normalization used everywhere paths are stored, compared, or displayed.</summary>
internal static class PathUtils
{
    /// <summary>Case-insensitive on Windows, which is the only platform DNRun targets.</summary>
    public static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    public static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    /// <summary>Absolute path with a canonical form and no trailing separator.</summary>
    public static string Normalize(string path)
    {
        var full = Path.GetFullPath(path);
        if (full.Length > 3 && (full.EndsWith(Path.DirectorySeparatorChar) || full.EndsWith(Path.AltDirectorySeparatorChar)))
        {
            full = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        return full;
    }

    /// <summary>
    /// Path of <paramref name="absolutePath"/> relative to <paramref name="root"/>, forward-slashed.
    /// Falls back to the absolute path when the target lives outside the root (or on another drive).
    /// </summary>
    public static string ToRepositoryRelative(string root, string absolutePath)
    {
        try
        {
            var relative = Path.GetRelativePath(Normalize(root), Normalize(absolutePath));
            if (Path.IsPathRooted(relative))
            {
                return Normalize(absolutePath).Replace('\\', '/');
            }

            return relative.Replace('\\', '/');
        }
        catch (ArgumentException)
        {
            return Normalize(absolutePath).Replace('\\', '/');
        }
    }

    /// <summary>Resolves a repository-relative (or absolute) configured path back to an absolute path.</summary>
    public static string FromRepositoryRelative(string root, string relativePath)
    {
        var native = relativePath.Replace('/', Path.DirectorySeparatorChar);
        return Path.IsPathRooted(native) ? Normalize(native) : Normalize(Path.Combine(root, native));
    }

    public static bool SamePath(string a, string b) =>
        string.Equals(Normalize(a), Normalize(b), PathComparison);
}
