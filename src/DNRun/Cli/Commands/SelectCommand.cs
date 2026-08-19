using DNRun.Execution;

namespace DNRun.Cli.Commands;

/// <summary>
/// <c>dnrun select</c> (spec §8.2): force the prompt, replace the saved startup project, then
/// run the new selection. Identical to the default flow with the config lookup skipped.
/// </summary>
internal static class SelectCommand
{
    public static int Execute(DNRunSession session, IReadOnlyList<string> forwarded, IProcessRunner runner) =>
        RunCommand.Execute(session, forwarded, runner, forceSelection: true);
}
