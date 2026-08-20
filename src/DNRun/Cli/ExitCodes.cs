namespace DNRun.Cli;

/// <summary>Process exit codes (plan §5). A launched app's own exit code is propagated verbatim.</summary>
internal static class ExitCodes
{
    public const int Success = 0;

    /// <summary>No project the command could act on was found: nothing runnable, or nothing packable.</summary>
    public const int NoRunnableProject = 1;

    /// <summary>Bad usage, ambiguous selection with no TTY, or an abandoned prompt.</summary>
    public const int UsageError = 2;

    /// <summary>`dotnet` is not on PATH, or the child process could not be started.</summary>
    public const int LaunchFailure = 3;

    /// <summary>The configuration file exists but is unreadable and unrecoverable.</summary>
    public const int ConfigError = 4;

    /// <summary>A project file could not be rewritten with the new package version.</summary>
    public const int VersionUpdateFailed = 5;
}
