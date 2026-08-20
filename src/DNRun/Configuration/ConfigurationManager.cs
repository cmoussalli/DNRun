using System.Text.Json;
using DNRun.Discovery;
using DNRun.Model;

namespace DNRun.Configuration;

/// <summary>How a stored startup project stands relative to what is on disk (spec §7).</summary>
internal enum ConfigState
{
    /// <summary>No config file, or no startupProject in it.</summary>
    NotConfigured,

    /// <summary>The configured project exists and is runnable.</summary>
    Valid,

    /// <summary>The configured path no longer resolves to a discovered project.</summary>
    Missing,

    /// <summary>The configured project exists but is no longer classified as runnable.</summary>
    NotRunnable,
}

internal sealed record ConfigValidation(ConfigState State, ProjectInfo? Project, string? ConfiguredPath);

/// <summary>Reads, validates, and writes <c>dnrun.config.json</c> (plan §4.5).</summary>
internal static class ConfigurationManager
{
    public static string ConfigPath(string repositoryRoot) =>
        Path.Combine(repositoryRoot, DNRunConfig.FileName);

    /// <summary>
    /// Loads the config. Returns false only when a file exists but cannot be read or parsed —
    /// a malformed file is a warning, not a crash: the caller continues as if unconfigured and
    /// must not overwrite the file until the user makes a new selection.
    /// </summary>
    public static bool TryLoad(string repositoryRoot, out DNRunConfig? config, out string? error)
    {
        config = null;
        error = null;

        var path = ConfigPath(repositoryRoot);
        if (!File.Exists(path))
        {
            return true;
        }

        try
        {
            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
            {
                config = new DNRunConfig();
                return true;
            }

            config = JsonSerializer.Deserialize(json, DNRunConfigContext.Default.DNRunConfig) ?? new DNRunConfig();
            return true;
        }
        catch (JsonException ex)
        {
            error = $"{path} is not valid JSON ({ex.Message.TrimEnd()})";
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = $"{path} could not be read ({ex.Message})";
            return false;
        }
    }

    /// <summary>Matches the configured startup project against the discovered candidates.</summary>
    public static ConfigValidation Validate(
        DNRunConfig? config,
        string repositoryRoot,
        IReadOnlyList<ProjectInfo> discovered) =>
        Validate(config?.StartupProject, repositoryRoot, discovered, p => p.IsRunnable);

    /// <summary>Matches the configured package project against the discovered candidates.</summary>
    public static ConfigValidation ValidatePackageProject(
        DNRunConfig? config,
        string repositoryRoot,
        IReadOnlyList<ProjectInfo> discovered) =>
        Validate(config?.PackageProject, repositoryRoot, discovered, p => p.IsPackable);

    /// <summary>
    /// Shared validation for both saved selections. <paramref name="eligible"/> is what makes a
    /// project usable for the command at hand - runnable for <c>dnrun</c>, packable for
    /// <c>dnuget</c> - and a project that fails it yields <see cref="ConfigState.NotRunnable"/>.
    /// </summary>
    private static ConfigValidation Validate(
        string? configured,
        string repositoryRoot,
        IReadOnlyList<ProjectInfo> discovered,
        Func<ProjectInfo, bool> eligible)
    {
        if (string.IsNullOrWhiteSpace(configured))
        {
            return new ConfigValidation(ConfigState.NotConfigured, null, null);
        }

        var absolute = PathUtils.FromRepositoryRelative(repositoryRoot, configured);
        var match = discovered.FirstOrDefault(p => PathUtils.SamePath(p.AbsolutePath, absolute));

        if (match is null)
        {
            // Not among the discovered set — but it may simply sit outside the scanned locations.
            if (File.Exists(absolute))
            {
                var analyzed = ProjectAnalyzer.Analyze(absolute, repositoryRoot);
                return eligible(analyzed)
                    ? new ConfigValidation(ConfigState.Valid, analyzed, configured)
                    : new ConfigValidation(ConfigState.NotRunnable, analyzed, configured);
            }

            return new ConfigValidation(ConfigState.Missing, null, configured);
        }

        return eligible(match)
            ? new ConfigValidation(ConfigState.Valid, match, configured)
            : new ConfigValidation(ConfigState.NotRunnable, match, configured);
    }

    /// <summary>
    /// Writes the config atomically: temp file in the same directory, then an overwriting move,
    /// so an interrupted write cannot leave a truncated file behind.
    /// </summary>
    public static void Save(string repositoryRoot, DNRunConfig config)
    {
        if (config.StartupProject is not null)
        {
            config.StartupProject = config.StartupProject.Replace('\\', '/');
        }

        if (config.PackageProject is not null)
        {
            config.PackageProject = config.PackageProject.Replace('\\', '/');
        }

        var path = ConfigPath(repositoryRoot);
        var json = JsonSerializer.Serialize(config, DNRunConfigContext.Default.DNRunConfig);
        var temp = Path.Combine(repositoryRoot, $".dnrun.config.{Environment.ProcessId}.tmp");

        try
        {
            File.WriteAllText(temp, json + Environment.NewLine);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                TryDelete(temp);
            }
        }
    }

    public static void Delete(string repositoryRoot) => TryDelete(ConfigPath(repositoryRoot));

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: a leftover temp file is harmless.
        }
    }
}
