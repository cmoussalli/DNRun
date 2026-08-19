namespace DNRun.Model;

/// <summary>
/// Semantic classification derived from the project name or SDK (spec §9).
/// Feeds the <c>dnrun web</c> / <c>dnrun api</c> verbs and menu ordering.
/// </summary>
internal enum ProjectKind
{
    Unknown = 0,
    Web,
    Api,
    Windows,
    Mobile,
    Worker,
    Console,
    Library,
    Test,
}

internal static class ProjectKindExtensions
{
    /// <summary>
    /// Menu ordering priority (spec §4.4): Web, Api, Worker, Windows, Mobile, Console, then the rest.
    /// Lower sorts first.
    /// </summary>
    public static int SortPriority(this ProjectKind kind) => kind switch
    {
        ProjectKind.Web => 0,
        ProjectKind.Api => 1,
        ProjectKind.Worker => 2,
        ProjectKind.Windows => 3,
        ProjectKind.Mobile => 4,
        ProjectKind.Console => 5,
        ProjectKind.Unknown => 6,
        ProjectKind.Library => 7,
        ProjectKind.Test => 8,
        _ => 9,
    };

    public static string ToDisplayString(this ProjectKind kind) => kind switch
    {
        ProjectKind.Api => "API",
        _ => kind.ToString(),
    };
}
