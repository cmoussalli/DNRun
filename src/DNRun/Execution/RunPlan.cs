using DNRun.Model;

namespace DNRun.Execution;

/// <summary>A fully resolved command line, ready to hand to the process runner.</summary>
internal sealed record RunPlan(
    ProjectInfo Project,
    string WorkingDirectory,
    string Verb,
    IReadOnlyList<string> ForwardedArguments)
{
    /// <summary>Arguments passed to <c>dotnet</c>, in order.</summary>
    public IReadOnlyList<string> BuildArguments()
    {
        var arguments = new List<string> { Verb, "--project", Project.AbsolutePath };

        if (ForwardedArguments.Count > 0)
        {
            // `dotnet run` needs the separator so the app's own switches are not parsed by the SDK.
            if (Verb == "run")
            {
                arguments.Add("--");
            }

            arguments.AddRange(ForwardedArguments);
        }

        return arguments;
    }

    /// <summary>The equivalent command line, quoted for display only.</summary>
    public string ToDisplayString(string? relativeTo = null)
    {
        var project = relativeTo is null
            ? Project.AbsolutePath
            : PathUtils.ToRepositoryRelative(relativeTo, Project.AbsolutePath);

        var parts = new List<string> { "dotnet", Verb, "--project", Quote(project) };

        if (ForwardedArguments.Count > 0)
        {
            if (Verb == "run")
            {
                parts.Add("--");
            }

            parts.AddRange(ForwardedArguments.Select(Quote));
        }

        return string.Join(' ', parts);
    }

    private static string Quote(string value) =>
        value.Contains(' ') ? string.Concat("\"", value, "\"") : value;
}
