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

    /// <summary>Update the version of the NuGet package this repository publishes.</summary>
    Nuget,
}

/// <summary>
/// Hand-rolled parsing over args[0] (plan D4): a handful of verbs and one passthrough do not
/// justify a command-line library and the trimming story that comes with it.
/// <paramref name="Forwarded"/> carries whatever follows the verb: the application's own arguments
/// for a run, and the version and options for <see cref="Verb.Nuget"/>.
/// </summary>
internal sealed record ParsedArgs(Verb Verb, IReadOnlyList<string> Forwarded, string? Error = null)
{
    /// <summary>The executable name that means "this is a dnuget invocation", however it was installed.</summary>
    public const string NugetAlias = "dnuget";

    /// <summary>
    /// Turns an invocation of the <c>dnuget</c> alias into the equivalent <c>dnrun nuget</c>
    /// arguments. The alias is normally a one-line shim next to DNRun.exe, but a plain copy of the
    /// executable renamed to dnuget.exe works too, and both must behave the same.
    /// </summary>
    public static string[] NormalizeArgs(string? invokedName, string[] args) =>
        string.Equals(invokedName, NugetAlias, StringComparison.OrdinalIgnoreCase)
            ? ["nuget", .. args]
            : args;

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
            "nuget" or "package" or "pack" => Verb.Nuget,
            _ => (Verb?)null,
        } ?? Verb.Run;

        if (verb == Verb.Run)
        {
            // An unrecognized first argument must never silently fall through to the default run:
            // launching the wrong application is worse than refusing.
            return new ParsedArgs(Verb.Run, [], BuildUnknownArgumentError(first));
        }

        var rest = args[1..];

        // 'nuget' is the one verb that takes a value of its own; NugetRequest parses the rest.
        if (verb == Verb.Nuget)
        {
            return new ParsedArgs(verb, rest);
        }

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
