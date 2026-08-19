namespace DNRun.Model;

/// <summary>
/// The resolved workspace: where DNRun was invoked, which directory it treats as the
/// repository root, and which solution (if any) provides context.
/// </summary>
internal sealed record RepositoryContext(
    string WorkingDirectory,
    string RepositoryRoot,
    string? SolutionPath,
    IReadOnlyList<string> ScannedLocations)
{
    /// <summary>All solutions found at the root; more than one means <see cref="SolutionPath"/> was a guess.</summary>
    public IReadOnlyList<string> AllSolutionPaths { get; init; } = [];

    /// <summary>Which marker identified the root ("dnrun.config.json", "*.sln", ".git", …), or null when none did.</summary>
    public string? RootMarker { get; init; }

    public string ConfigPath => Path.Combine(RepositoryRoot, "dnrun.config.json");
}
