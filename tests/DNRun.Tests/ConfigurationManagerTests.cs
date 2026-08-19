using DNRun.Configuration;
using DNRun.Discovery;
using DNRun.Tests.Fixtures;

namespace DNRun.Tests;

public sealed class ConfigurationManagerTests
{
    private static TempRepo SpecRepo() => TempRepo.Create()
        .WithSolution("XYZ.sln", "src/XYZ.Web/XYZ.Web.csproj", "src/XYZ.Domain/XYZ.Domain.csproj")
        .WithProject("src/XYZ.Web/XYZ.Web.csproj", sdk: "Microsoft.NET.Sdk.Web")
        .WithProject("src/XYZ.Domain/XYZ.Domain.csproj");

    private static DiscoveryResult Discover(TempRepo repo, DNRunConfig? config = null) =>
        ProjectDiscovery.Discover(WorkspaceResolver.Resolve(repo.Root), config);

    [Fact]
    public void No_file_means_not_configured()
    {
        using var repo = SpecRepo();

        Assert.True(ConfigurationManager.TryLoad(repo.Root, out var config, out var error));
        Assert.Null(config);
        Assert.Null(error);

        var validation = ConfigurationManager.Validate(config, repo.Root, Discover(repo).AllProjects);
        Assert.Equal(ConfigState.NotConfigured, validation.State);
    }

    [Fact]
    public void A_configured_runnable_project_validates()
    {
        using var repo = SpecRepo().WithConfig("""{ "startupProject": "src/XYZ.Web/XYZ.Web.csproj" }""");

        Assert.True(ConfigurationManager.TryLoad(repo.Root, out var config, out _));
        var validation = ConfigurationManager.Validate(config, repo.Root, Discover(repo, config).AllProjects);

        Assert.Equal(ConfigState.Valid, validation.State);
        Assert.Equal("XYZ.Web", validation.Project!.Name);
    }

    [Fact]
    public void A_deleted_project_reports_as_missing()
    {
        using var repo = SpecRepo().WithConfig("""{ "startupProject": "src/Gone/Gone.csproj" }""");

        ConfigurationManager.TryLoad(repo.Root, out var config, out _);
        var validation = ConfigurationManager.Validate(config, repo.Root, Discover(repo, config).AllProjects);

        Assert.Equal(ConfigState.Missing, validation.State);
        Assert.Equal("src/Gone/Gone.csproj", validation.ConfiguredPath);
    }

    [Fact]
    public void A_class_library_reports_as_not_runnable()
    {
        using var repo = SpecRepo().WithConfig("""{ "startupProject": "src/XYZ.Domain/XYZ.Domain.csproj" }""");

        ConfigurationManager.TryLoad(repo.Root, out var config, out _);
        var validation = ConfigurationManager.Validate(config, repo.Root, Discover(repo, config).AllProjects);

        Assert.Equal(ConfigState.NotRunnable, validation.State);
    }

    [Fact]
    public void A_malformed_file_is_a_recoverable_warning()
    {
        using var repo = SpecRepo().WithConfig("{ this is not json");

        var loaded = ConfigurationManager.TryLoad(repo.Root, out var config, out var error);

        Assert.False(loaded);
        Assert.Null(config);
        Assert.NotNull(error);

        // Discovery still works without it — the user is never hard-blocked by a bad config.
        Assert.Single(Discover(repo).RunnableProjects);
    }

    [Fact]
    public void Windows_separators_are_normalized_on_save()
    {
        using var repo = SpecRepo();

        ConfigurationManager.Save(repo.Root, new DNRunConfig
        {
            StartupProject = @"src\XYZ.Web\XYZ.Web.csproj",
        });

        var json = File.ReadAllText(repo.Path("dnrun.config.json"));
        Assert.Contains("src/XYZ.Web/XYZ.Web.csproj", json, StringComparison.Ordinal);
        Assert.DoesNotContain(@"\\", json, StringComparison.Ordinal);
    }

    [Fact]
    public void A_saved_config_round_trips()
    {
        using var repo = SpecRepo();

        ConfigurationManager.Save(repo.Root, new DNRunConfig
        {
            StartupProject = "src/XYZ.Web/XYZ.Web.csproj",
            IgnoreDirectories = ["samples"],
            RunnableProjects = ["src/Odd/Odd.csproj"],
        });

        Assert.True(ConfigurationManager.TryLoad(repo.Root, out var config, out _));

        Assert.Equal("src/XYZ.Web/XYZ.Web.csproj", config!.StartupProject);
        Assert.Equal(["samples"], config.IgnoreDirectories!);
        Assert.Equal(["src/Odd/Odd.csproj"], config.RunnableProjects!);
        Assert.Equal(1, config.Version);
    }

    [Fact]
    public void Only_real_settings_are_written_to_the_file()
    {
        using var repo = SpecRepo();

        ConfigurationManager.Save(repo.Root, new DNRunConfig { StartupProject = "src/XYZ.Web/XYZ.Web.csproj" });

        var json = File.ReadAllText(repo.Path("dnrun.config.json"));

        Assert.Contains("\"version\"", json, StringComparison.Ordinal);
        Assert.Contains("\"startupProject\"", json, StringComparison.Ordinal);

        // Computed and unset members must not leak into a file the user reads and edits.
        Assert.DoesNotContain("isEmpty", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ignoreDirectories", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("runnableProjects", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Saving_leaves_no_temporary_files_behind()
    {
        using var repo = SpecRepo();

        ConfigurationManager.Save(repo.Root, new DNRunConfig { StartupProject = "src/XYZ.Web/XYZ.Web.csproj" });

        Assert.Empty(Directory.GetFiles(repo.Root, "*.tmp"));
        Assert.True(File.Exists(repo.Path("dnrun.config.json")));
    }

    [Fact]
    public void A_configured_project_outside_the_scanned_locations_still_validates()
    {
        using var repo = SpecRepo()
            .WithProject("tools/Gen/Gen.csproj", outputType: "Exe")
            .WithConfig("""{ "startupProject": "tools/Gen/Gen.csproj" }""");

        ConfigurationManager.TryLoad(repo.Root, out var config, out _);
        var validation = ConfigurationManager.Validate(config, repo.Root, Discover(repo, config).AllProjects);

        Assert.Equal(ConfigState.Valid, validation.State);
        Assert.Equal("Gen", validation.Project!.Name);
    }

    [Fact]
    public void An_empty_file_is_treated_as_an_empty_configuration()
    {
        using var repo = SpecRepo().WithConfig("");

        Assert.True(ConfigurationManager.TryLoad(repo.Root, out var config, out _));
        Assert.NotNull(config);
        Assert.True(config!.IsEmpty);
    }
}
