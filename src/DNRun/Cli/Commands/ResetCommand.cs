using DNRun.Configuration;
using DNRun.Presentation;

namespace DNRun.Cli.Commands;

/// <summary>
/// <c>dnrun reset</c>: forget the saved startup project. The config file is removed entirely
/// when nothing else is left in it, so a reset repository looks untouched.
/// </summary>
internal static class ResetCommand
{
    public static int Execute(DNRunSession session)
    {
        Output.Banner();
        Output.Blank();

        var configPath = ConfigurationManager.ConfigPath(session.RepositoryRoot);

        if (!File.Exists(configPath))
        {
            Output.Line("Nothing to reset — no configuration at " + configPath);
            return ExitCodes.Success;
        }

        if (session.ConfigUnreadable)
        {
            ConfigurationManager.Delete(session.RepositoryRoot);
            Output.Line("Removed unreadable " + configPath);
            return ExitCodes.Success;
        }

        var config = session.Config ?? new DNRunConfig();
        var previous = config.StartupProject;
        config.StartupProject = null;

        try
        {
            if (config.IsEmpty)
            {
                ConfigurationManager.Delete(session.RepositoryRoot);
                Output.Line("Removed " + configPath);
            }
            else
            {
                // Other settings are the user's, not ours to discard.
                ConfigurationManager.Save(session.RepositoryRoot, config);
                Output.Line("Cleared the startup project in " + configPath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Output.Error($"could not update {configPath} ({ex.Message})");
            return ExitCodes.ConfigError;
        }

        if (previous is not null)
        {
            Output.Blank();
            Output.Line("Previous startup project: " + Output.Dim(previous));
        }

        return ExitCodes.Success;
    }
}
