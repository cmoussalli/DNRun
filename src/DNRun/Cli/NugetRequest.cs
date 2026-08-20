using DNRun.Packaging;

namespace DNRun.Cli;

internal enum NugetAction
{
    /// <summary>Print the package project and the version it currently declares.</summary>
    Show,

    /// <summary>Write a new version.</summary>
    Set,

    /// <summary>List every packable project with its current version.</summary>
    List,

    /// <summary>Forget the saved package project.</summary>
    Reset,

    /// <summary>Print usage.</summary>
    Help,
}

/// <summary>
/// Parsing for <c>dnuget</c> (equivalently <c>dnrun nuget</c>). Kept apart from
/// <see cref="ParsedArgs"/> because this verb takes a value, which the other five do not, and
/// because the version must be validated before any project file is opened.
/// </summary>
internal sealed record NugetRequest(
    NugetAction Action,
    NuGetVersion? Version = null,
    bool ForceSelection = false,
    bool AllProjects = false,
    string? Error = null)
{
    public static NugetRequest Parse(IReadOnlyList<string> args)
    {
        var forceSelection = false;
        var allProjects = false;
        NugetAction? action = null;
        NuGetVersion? version = null;

        foreach (var argument in args)
        {
            switch (argument.ToLowerInvariant())
            {
                case "--help" or "-h" or "-?" or "/?" or "help":
                    return new NugetRequest(NugetAction.Help);

                case "--all" or "-a" or "all":
                    allProjects = true;
                    continue;

                case "--select" or "-s" or "select":
                    forceSelection = true;
                    continue;

                case "list" or "ls" or "--list":
                    action = NugetAction.List;
                    continue;

                case "reset" or "--reset":
                    action = NugetAction.Reset;
                    continue;
            }

            if (argument.StartsWith('-'))
            {
                return new NugetRequest(NugetAction.Help, Error: $"unknown option '{argument}'.");
            }

            if (version is not null)
            {
                return new NugetRequest(NugetAction.Help, Error: $"only one version can be given; got '{version}' and '{argument}'.");
            }

            if (!NuGetVersion.TryParse(argument, out var parsed, out var error))
            {
                return new NugetRequest(NugetAction.Help, Error: error);
            }

            version = parsed;
        }

        if (version is not null)
        {
            // A version alongside 'list' or 'reset' is a typo worth refusing rather than guessing at.
            if (action is not null)
            {
                return new NugetRequest(NugetAction.Help, Error: $"a version cannot be combined with '{action.Value.ToString().ToLowerInvariant()}'.");
            }

            return new NugetRequest(NugetAction.Set, version, forceSelection, allProjects);
        }

        if (allProjects && action is null or NugetAction.Show)
        {
            return new NugetRequest(NugetAction.List, null, forceSelection, AllProjects: true);
        }

        return new NugetRequest(action ?? NugetAction.Show, null, forceSelection, allProjects);
    }
}
