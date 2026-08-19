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
}
