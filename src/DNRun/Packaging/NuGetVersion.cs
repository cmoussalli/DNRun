using System.Text.RegularExpressions;

namespace DNRun.Packaging;

/// <summary>
/// A NuGet package version split into the parts MSBuild keeps in separate properties:
/// <c>VersionPrefix</c> (the numeric release), <c>VersionSuffix</c> (the prerelease label), and
/// the build metadata NuGet ignores when ordering.
///
/// Validated rather than merely accepted: writing "1.2.14-" or "v1.2.14" into a .csproj produces
/// a package that fails to restore, and the failure surfaces minutes later in a pack or push.
/// </summary>
internal sealed partial record NuGetVersion(string Prefix, string? Suffix, string? Metadata)
{
    /// <summary>2 to 4 numeric parts, an optional -prerelease, an optional +metadata (SemVer 2.0 as NuGet reads it).</summary>
    [GeneratedRegex(
        @"^(?<prefix>\d{1,9}(?:\.\d{1,9}){1,3})(?:-(?<suffix>[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+(?<meta>[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$",
        RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();

    public override string ToString() =>
        Prefix
        + (Suffix is null ? string.Empty : "-" + Suffix)
        + (Metadata is null ? string.Empty : "+" + Metadata);

    /// <summary>The full version without build metadata — what belongs in a Version property.</summary>
    public string WithoutMetadata => Prefix + (Suffix is null ? string.Empty : "-" + Suffix);

    /// <summary>
    /// AssemblyVersion and FileVersion accept only four numbers, so the prerelease label is
    /// dropped and missing parts are zero-filled: 1.2.14-beta.1 becomes 1.2.14.0.
    /// </summary>
    public string ToAssemblyVersion()
    {
        var parts = Prefix.Split('.').ToList();
        while (parts.Count < 4)
        {
            parts.Add("0");
        }

        return string.Join('.', parts.Take(4));
    }

    public static bool TryParse(string? text, out NuGetVersion? version, out string? error)
    {
        version = null;
        error = null;

        var trimmed = text?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            error = "no version was given.";
            return false;
        }

        // A leading 'v' is what everyone types out of git-tag habit; accept it rather than
        // rejecting a version the user clearly meant.
        if ((trimmed[0] == 'v' || trimmed[0] == 'V') && trimmed.Length > 1 && char.IsDigit(trimmed[1]))
        {
            trimmed = trimmed[1..];
        }

        var match = VersionPattern().Match(trimmed);
        if (!match.Success)
        {
            error = $"'{text!.Trim()}' is not a valid NuGet version. Expected something like 1.2.14, 1.2.14.3, or 1.3.0-beta.1.";
            return false;
        }

        version = new NuGetVersion(
            match.Groups["prefix"].Value,
            match.Groups["suffix"].Success ? match.Groups["suffix"].Value : null,
            match.Groups["meta"].Success ? match.Groups["meta"].Value : null);

        return true;
    }

    /// <summary>Rebuilds the display form from the prefix/suffix pair MSBuild may store separately.</summary>
    public static string? Combine(string? prefix, string? suffix)
    {
        if (string.IsNullOrWhiteSpace(prefix))
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(suffix) ? prefix.Trim() : prefix.Trim() + "-" + suffix.Trim();
    }
}
