namespace DNRun.Cli;

internal enum Verb
{
    Run,
    Select,
    List,
    Config,
    Reset,
    Help,
    Version,
}

/// <summary>
/// Hand-rolled parsing over args[0] (plan D4): five verbs and one passthrough do not justify a
/// command-line library and the trimming story that comes with it.
/// </summary>
internal sealed record ParsedArgs(Verb Verb, IReadOnlyList<string> Forwarded, string? Error = null)
{
    public static ParsedArgs Parse(string[] args)
    {
        if (args.Length == 0)
        {
            return new ParsedArgs(Verb.Run, []);
        }

        var first = args[0];

        // `dnrun -- --urls http://localhost:5005` forwards everything after the separator.
        if (first == "--")
        {
            return new ParsedArgs(Verb.Run, args[1..]);
        }

        var verb = first.ToLowerInvariant() switch
        {
            "select" => Verb.Select,
            "list" or "ls" => Verb.List,
            "config" => Verb.Config,
            "reset" => Verb.Reset,
            "help" or "--help" or "-h" or "-?" or "/?" => Verb.Help,
            "version" or "--version" => Verb.Version,
            _ => (Verb?)null,
        } ?? Verb.Run;

        if (verb == Verb.Run)
        {
            // An unrecognized first argument must never silently fall through to the default run:
            // launching the wrong application is worse than refusing.
            return new ParsedArgs(Verb.Run, [], BuildUnknownArgumentError(first));
        }

        var rest = args[1..];
        if (rest.Length == 0)
        {
            return new ParsedArgs(verb, []);
        }

        if (rest[0] == "--")
        {
            return new ParsedArgs(verb, rest[1..]);
        }

        return new ParsedArgs(verb, [], $"'{string.Join(' ', rest)}' is not valid after '{first}'.");
    }

    private static string BuildUnknownArgumentError(string argument) =>
        argument.StartsWith('-')
            ? $"unknown option '{argument}'."
            : $"unknown command '{argument}'. Pass arguments to the application after '--', e.g. dnrun -- {argument}";
}
