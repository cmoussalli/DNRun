namespace DNRun.Model;

/// <summary>
/// A discovered .csproj plus everything DNRun decided about it.
/// <paramref name="RelativePath"/> is computed once at discovery so config writes and
/// display never re-derive it inconsistently.
/// </summary>
internal sealed record ProjectInfo(
    string Name,
    string AbsolutePath,
    string RelativePath,
    bool IsRunnable,
    ProjectKind Kind,
    string? Sdk,
    string? OutputType)
{
    /// <summary>True when the project is listed in the repository's solution file.</summary>
    public bool InSolution { get; init; } = true;

    /// <summary>Set when the .csproj could not be parsed; the project is then reported but not runnable.</summary>
    public string? AnalysisWarning { get; init; }

    /// <summary>
    /// False when the project opts out of packaging with <c>IsPackable=false</c>, or is a test
    /// project (which modern test SDKs opt out for you). Everything else can be packed.
    /// </summary>
    public bool IsPackable { get; init; } = true;

    /// <summary>
    /// True when the project asks to be packaged — <c>IsPackable=true</c>, <c>PackageId</c>,
    /// <c>GeneratePackageOnBuild</c>, or <c>PackAsTool</c>. These are the projects 'dnuget' offers
    /// first; the rest are only offered when no project opts in explicitly.
    /// </summary>
    public bool PackagesExplicitly { get; init; }

    /// <summary>The <c>PackageId</c>, when the project overrides the assembly name for its package.</summary>
    public string? PackageId { get; init; }

    /// <summary>
    /// The version declared in the .csproj itself, or null when it inherits one (from
    /// Directory.Build.props) or relies on MSBuild's 1.0.0 default.
    /// </summary>
    public string? DeclaredVersion { get; init; }
}
