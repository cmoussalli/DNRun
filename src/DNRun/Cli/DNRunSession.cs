using DNRun.Configuration;
using DNRun.Discovery;
using DNRun.Model;
using DNRun.Presentation;

namespace DNRun.Cli;

/// <summary>
/// The setup every command shares: resolve the workspace from the working directory, load the
/// config, and run one discovery pass.
/// </summary>
internal sealed class DNRunSession
{
    private DNRunSession(
        RepositoryContext context,
        DNRunConfig? config,
        bool configUnreadable,
        DiscoveryResult discovery)
    {
        Context = context;
        Config = config;
        ConfigUnreadable = configUnreadable;
        Discovery = discovery;
    }

    public RepositoryContext Context { get; }

    public DNRunConfig? Config { get; }

    /// <summary>True when a config file exists but could not be parsed; it must not be overwritten silently.</summary>
    public bool ConfigUnreadable { get; }

    public DiscoveryResult Discovery { get; private set; }

    public IReadOnlyList<ProjectInfo> Runnable => Discovery.RunnableProjects;

    public string RepositoryRoot => Context.RepositoryRoot;

    /// <summary>
    /// Builds the session for the given working directory — always the process CWD in production,
    /// never the directory holding DNRun.exe (spec §14.2).
    /// </summary>
    public static DNRunSession Create(string workingDirectory, bool quiet = false)
    {
        var context = WorkspaceResolver.Resolve(workingDirectory);

        var loaded = ConfigurationManager.TryLoad(context.RepositoryRoot, out var config, out var error);
        if (!loaded && !quiet)
        {
            // A malformed config is a warning, not a crash: continue as if unconfigured.
            Output.Warn(error!);
            Output.Warn("continuing without a saved startup project; run 'dnrun select' to rewrite it.");
            Output.Blank();
        }

        var discovery = ProjectDiscovery.Discover(context, config);

        if (!quiet)
        {
            foreach (var warning in discovery.Warnings)
            {
                Output.Warn(warning);
            }
        }

        return new DNRunSession(discovery.Context, config, configUnreadable: !loaded, discovery);
    }

    /// <summary>Persists a new startup project, preserving any other settings already in the file.</summary>
    public void SaveStartupProject(ProjectInfo project)
    {
        var config = Config ?? new DNRunConfig();
        config.StartupProject = project.RelativePath;

        try
        {
            ConfigurationManager.Save(RepositoryRoot, config);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Failing to remember the choice must not stop the app from starting.
            Output.Warn($"could not write {ConfigurationManager.ConfigPath(RepositoryRoot)} ({ex.Message})");
        }
    }

    /// <summary>Prints the locations that were searched — the body of the "nothing found" error (spec §6 Scenario A).</summary>
    public void PrintScannedLocations()
    {
        Output.Label("Scanned locations:");
        foreach (var location in Discovery.ScannedLocations)
        {
            Output.Line("  " + location);
        }
    }
}
