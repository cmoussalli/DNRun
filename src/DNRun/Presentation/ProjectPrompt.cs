using DNRun.Model;

namespace DNRun.Presentation;

internal enum PromptOutcome
{
    Selected,

    /// <summary>stdin is not a terminal — the caller must print the list and exit rather than block.</summary>
    NonInteractive,

    /// <summary>End of input, or too many invalid answers.</summary>
    Abandoned,
}

internal sealed record PromptResult(PromptOutcome Outcome, ProjectInfo? Project);

/// <summary>Where the prompt reads answers from. Exists so the read loop is testable without a TTY.</summary>
internal interface IPromptInput
{
    bool IsInteractive { get; }

    string? ReadLine();
}

internal sealed class ConsolePromptInput : IPromptInput
{
    public static readonly ConsolePromptInput Instance = new();

    public bool IsInteractive => !Console.IsInputRedirected;

    public string? ReadLine() => Console.ReadLine();
}

/// <summary>Interactive numeric selection (spec §6 Scenario C, plan §4.7).</summary>
internal static class ProjectPrompt
{
    private const int MaxAttempts = 3;

    public static PromptResult Select(
        IReadOnlyList<ProjectInfo> projects,
        string question,
        IPromptInput? input = null)
    {
        input ??= ConsolePromptInput.Instance;

        if (projects.Count == 0)
        {
            return new PromptResult(PromptOutcome.Abandoned, null);
        }

        if (projects.Count == 1)
        {
            return new PromptResult(PromptOutcome.Selected, projects[0]);
        }

        // Orca's run command may attach no TTY. A hung prompt with no visible cursor is the worst
        // possible failure mode, so refuse to read before the first read rather than after.
        if (!input.IsInteractive)
        {
            return new PromptResult(PromptOutcome.NonInteractive, null);
        }

        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            Output.Line(question);
            Console.Write("> ");
            var answer = input.ReadLine();

            if (answer is null)
            {
                Output.Blank();
                return new PromptResult(PromptOutcome.Abandoned, null);
            }

            answer = answer.Trim();

            // Bare Enter takes the first entry — the common case is "yes, the obvious one".
            if (answer.Length == 0)
            {
                Output.Blank();
                return new PromptResult(PromptOutcome.Selected, projects[0]);
            }

            if (answer is "q" or "quit" or "exit")
            {
                return new PromptResult(PromptOutcome.Abandoned, null);
            }

            var match = Resolve(projects, answer, out var error);
            if (match is not null)
            {
                // Separates the typed answer from what follows, matching the spec's transcript.
                Output.Blank();
                return new PromptResult(PromptOutcome.Selected, match);
            }

            Output.Blank();
            Output.Error(error!);
            Output.Blank();
        }

        return new PromptResult(PromptOutcome.Abandoned, null);
    }

    /// <summary>Accepts an index, a full name, or any unambiguous name fragment ("web" → XYZ.Web).</summary>
    public static ProjectInfo? Resolve(IReadOnlyList<ProjectInfo> projects, string answer, out string? error)
    {
        error = null;

        if (int.TryParse(answer, out var index))
        {
            if (index >= 1 && index <= projects.Count)
            {
                return projects[index - 1];
            }

            error = $"'{answer}' is not one of [1..{projects.Count}].";
            return null;
        }

        var exact = projects
            .Where(p => string.Equals(p.Name, answer, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (exact.Length == 1)
        {
            return exact[0];
        }

        var fuzzy = projects
            .Where(p => p.Name.EndsWith("." + answer, StringComparison.OrdinalIgnoreCase)
                        || p.Name.StartsWith(answer, StringComparison.OrdinalIgnoreCase)
                        || p.Name.Contains(answer, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (fuzzy.Length == 1)
        {
            return fuzzy[0];
        }

        error = fuzzy.Length > 1
            ? $"'{answer}' matches {string.Join(", ", fuzzy.Select(p => p.Name))}. Be more specific."
            : $"'{answer}' did not match any listed project.";

        return null;
    }

    /// <summary>The numbered menu shared by the run and select flows.</summary>
    public static void PrintNumberedList(IReadOnlyList<ProjectInfo> projects, bool withPaths = false)
    {
        var width = projects.Count.ToString().Length;

        for (var i = 0; i < projects.Count; i++)
        {
            var number = (i + 1).ToString().PadLeft(width);
            var project = projects[i];
            var kind = project.Kind == ProjectKind.Unknown ? string.Empty : "  " + Output.Dim(project.Kind.ToDisplayString());

            Output.Line($"  {Output.Cyan("[" + number + "]")} {project.Name}{kind}");

            if (withPaths)
            {
                Output.Line($"      {Output.Dim(project.RelativePath)}");
            }
        }
    }
}
