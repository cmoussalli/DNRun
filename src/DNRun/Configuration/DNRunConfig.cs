using System.Text.Json.Serialization;

namespace DNRun.Configuration;

/// <summary>
/// Contents of <c>dnrun.config.json</c> at the repository root (spec §7).
/// Paths are stored repository-relative and forward-slashed so the repo stays portable.
/// </summary>
internal sealed class DNRunConfig
{
    public const string FileName = "dnrun.config.json";

    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("startupProject")]
    public string? StartupProject { get; set; }

    /// <summary>
    /// The project whose package version <c>dnuget</c> updates. Kept separate from
    /// <see cref="StartupProject"/> because the app you run and the library you publish are
    /// rarely the same project.
    /// </summary>
    [JsonPropertyName("packageProject")]
    public string? PackageProject { get; set; }

    /// <summary>Additional directory names to prune during scanning, on top of the built-in list.</summary>
    [JsonPropertyName("ignoreDirectories")]
    public string[]? IgnoreDirectories { get; set; }

    /// <summary>
    /// Escape hatch for projects the analyzer misclassifies — e.g. when OutputType is inherited
    /// from Directory.Build.props, which DNRun does not evaluate (plan §4.4, R2).
    /// Repository-relative paths; listed projects are always treated as runnable.
    /// </summary>
    [JsonPropertyName("runnableProjects")]
    public string[]? RunnableProjects { get; set; }

    [JsonIgnore]
    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(StartupProject)
        && string.IsNullOrWhiteSpace(PackageProject)
        && (IgnoreDirectories is null || IgnoreDirectories.Length == 0)
        && (RunnableProjects is null || RunnableProjects.Length == 0);
}
