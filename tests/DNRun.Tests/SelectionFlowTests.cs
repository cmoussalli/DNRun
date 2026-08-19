using System.Runtime.CompilerServices;
using DNRun.Cli;
using DNRun.Execution;
using DNRun.Tests.Fixtures;

namespace DNRun.Tests;

/// <summary>
/// End-to-end flow tests through <see cref="CommandRouter"/>. The process runner is faked, so
/// these assert the composed command line rather than launching anything.
/// </summary>
public sealed class SelectionFlowTests
{
    [ModuleInitializer]
    internal static void DisableColor() => Environment.SetEnvironmentVariable("NO_COLOR", "1");

    private sealed class FakeProcessRunner(int exitCode = 0) : IProcessRunner
    {
        public RunPlan? Plan { get; private set; }

        public int Run(RunPlan plan)
        {
            Plan = plan;
            return exitCode;
        }
    }

    private static (int ExitCode, FakeProcessRunner Runner, string Output) Invoke(
        TempRepo repo,
        string[] args,
        string? workingDirectory = null,
        int childExitCode = 0)
    {
        var runner = new FakeProcessRunner(childExitCode);
        var writer = new StringWriter();
        var originalOut = Console.Out;
        var originalError = Console.Error;

        try
        {
            Console.SetOut(writer);
            Console.SetError(writer);
            var exitCode = CommandRouter.Execute(args, runner, workingDirectory ?? repo.Root);
            return (exitCode, runner, writer.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    private static TempRepo SpecRepo() => TempRepo.Create()
        .WithSolution(
            "XYZ.sln",
            "src/XYZ.Web/XYZ.Web.csproj",
            "src/XYZ.API/XYZ.API.csproj",
            "src/XYZ.Domain/XYZ.Domain.csproj")
        .WithProject("src/XYZ.Web/XYZ.Web.csproj", sdk: "Microsoft.NET.Sdk.Web")
        .WithProject("src/XYZ.API/XYZ.API.csproj", sdk: "Microsoft.NET.Sdk.Web")
        .WithProject("src/XYZ.Domain/XYZ.Domain.csproj");

    [Fact]
    public void A_single_runnable_project_runs_without_interaction()
    {
        using var repo = TempRepo.Create()
            .WithSolution("XYZ.sln", "src/XYZ.Web/XYZ.Web.csproj")
            .WithProject("src/XYZ.Web/XYZ.Web.csproj", sdk: "Microsoft.NET.Sdk.Web")
            .WithProject("src/XYZ.Domain/XYZ.Domain.csproj");

        var (exitCode, runner, output) = Invoke(repo, []);

        Assert.Equal(0, exitCode);
        Assert.NotNull(runner.Plan);
        Assert.Equal("XYZ.Web", runner.Plan!.Project.Name);
        Assert.Contains("Found runnable project:", output, StringComparison.Ordinal);

        // Nothing was ambiguous, so nothing needed remembering.
        Assert.False(File.Exists(repo.Path("dnrun.config.json")));
    }

    [Fact]
    public void The_composed_command_line_matches_dotnet_run()
    {
        using var repo = TempRepo.Create()
            .WithSolution("XYZ.sln")
            .WithProject("src/XYZ.Web/XYZ.Web.csproj", sdk: "Microsoft.NET.Sdk.Web");

        var (_, runner, _) = Invoke(repo, []);

        Assert.Equal(
            ["run", "--project", repo.Path("src/XYZ.Web/XYZ.Web.csproj")],
            runner.Plan!.BuildArguments().ToArray());
        Assert.Equal(repo.Root, runner.Plan.WorkingDirectory, ignoreCase: true);
    }

    [Fact]
    public void Arguments_after_the_separator_are_forwarded_to_the_application()
    {
        using var repo = TempRepo.Create()
            .WithSolution("XYZ.sln")
            .WithProject("src/XYZ.Web/XYZ.Web.csproj", sdk: "Microsoft.NET.Sdk.Web");

        var (_, runner, _) = Invoke(repo, ["--", "--urls", "http://localhost:5005"]);

        Assert.Equal(
            ["run", "--project", repo.Path("src/XYZ.Web/XYZ.Web.csproj"), "--", "--urls", "http://localhost:5005"],
            runner.Plan!.BuildArguments().ToArray());
    }

    [Fact]
    public void The_child_exit_code_is_propagated()
    {
        using var repo = TempRepo.Create()
            .WithSolution("XYZ.sln")
            .WithProject("src/XYZ.Web/XYZ.Web.csproj", sdk: "Microsoft.NET.Sdk.Web");

        var (exitCode, _, _) = Invoke(repo, [], childExitCode: 42);

        Assert.Equal(42, exitCode);
    }

    [Fact]
    public void Several_candidates_without_a_terminal_exit_two_instead_of_blocking()
    {
        // stdin is redirected under the test host, which is exactly the Orca-run-command case.
        using var repo = SpecRepo();

        var (exitCode, runner, output) = Invoke(repo, []);

        Assert.Equal(ExitCodes.UsageError, exitCode);
        Assert.Null(runner.Plan);
        Assert.Contains("XYZ.Web", output, StringComparison.Ordinal);
        Assert.Contains("XYZ.API", output, StringComparison.Ordinal);
        Assert.Contains("dnrun select", output, StringComparison.Ordinal);
    }

    [Fact]
    public void A_saved_startup_project_runs_immediately()
    {
        using var repo = SpecRepo().WithConfig("""{ "startupProject": "src/XYZ.API/XYZ.API.csproj" }""");

        var (exitCode, runner, output) = Invoke(repo, []);

        Assert.Equal(0, exitCode);
        Assert.Equal("XYZ.API", runner.Plan!.Project.Name);
        Assert.Contains("Startup project:", output, StringComparison.Ordinal);
        Assert.DoesNotContain("Multiple runnable projects", output, StringComparison.Ordinal);
    }

    [Fact]
    public void A_saved_project_is_honoured_from_a_deep_subdirectory()
    {
        using var repo = SpecRepo().WithConfig("""{ "startupProject": "src/XYZ.API/XYZ.API.csproj" }""");

        var (exitCode, runner, _) = Invoke(repo, [], workingDirectory: repo.Path("src/XYZ.Domain"));

        Assert.Equal(0, exitCode);
        Assert.Equal("XYZ.API", runner.Plan!.Project.Name);
    }

    [Fact]
    public void A_stale_configuration_warns_and_falls_back_to_discovery()
    {
        using var repo = TempRepo.Create()
            .WithSolution("XYZ.sln")
            .WithProject("src/XYZ.Web/XYZ.Web.csproj", sdk: "Microsoft.NET.Sdk.Web")
            .WithConfig("""{ "startupProject": "src/Gone/Gone.csproj" }""");

        var (exitCode, runner, output) = Invoke(repo, []);

        Assert.Equal(0, exitCode);
        Assert.Equal("XYZ.Web", runner.Plan!.Project.Name);
        Assert.Contains("no longer exists", output, StringComparison.Ordinal);
    }

    [Fact]
    public void A_configured_class_library_warns_and_falls_back_to_discovery()
    {
        using var repo = TempRepo.Create()
            .WithSolution("XYZ.sln")
            .WithProject("src/XYZ.Web/XYZ.Web.csproj", sdk: "Microsoft.NET.Sdk.Web")
            .WithProject("src/XYZ.Domain/XYZ.Domain.csproj")
            .WithConfig("""{ "startupProject": "src/XYZ.Domain/XYZ.Domain.csproj" }""");

        var (exitCode, runner, output) = Invoke(repo, []);

        Assert.Equal(0, exitCode);
        Assert.Equal("XYZ.Web", runner.Plan!.Project.Name);
        Assert.Contains("no longer runnable", output, StringComparison.Ordinal);
    }

    [Fact]
    public void A_corrupt_configuration_warns_and_still_runs()
    {
        using var repo = TempRepo.Create()
            .WithSolution("XYZ.sln")
            .WithProject("src/XYZ.Web/XYZ.Web.csproj", sdk: "Microsoft.NET.Sdk.Web")
            .WithConfig("{ not json");

        var (exitCode, runner, output) = Invoke(repo, []);

        Assert.Equal(0, exitCode);
        Assert.Equal("XYZ.Web", runner.Plan!.Project.Name);
        Assert.Contains("not valid JSON", output, StringComparison.Ordinal);

        // The unreadable file is left alone until the user makes a new selection.
        Assert.Equal("{ not json", File.ReadAllText(repo.Path("dnrun.config.json")));
    }

    [Fact]
    public void No_runnable_project_reports_the_scanned_locations_and_exits_one()
    {
        using var repo = TempRepo.Create()
            .WithSolution("XYZ.sln")
            .WithProject("src/XYZ.Domain/XYZ.Domain.csproj");

        var (exitCode, runner, output) = Invoke(repo, []);

        Assert.Equal(ExitCodes.NoRunnableProject, exitCode);
        Assert.Null(runner.Plan);
        Assert.Contains("Scanned locations:", output, StringComparison.Ordinal);
        Assert.Contains(repo.Root, output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("XYZ.Domain", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Select_saves_and_runs_when_only_one_project_qualifies()
    {
        using var repo = TempRepo.Create()
            .WithSolution("XYZ.sln")
            .WithProject("src/XYZ.Web/XYZ.Web.csproj", sdk: "Microsoft.NET.Sdk.Web")
            .WithProject("src/XYZ.Domain/XYZ.Domain.csproj");

        var (exitCode, runner, output) = Invoke(repo, ["select"]);

        Assert.Equal(0, exitCode);
        Assert.Equal("XYZ.Web", runner.Plan!.Project.Name);
        Assert.Contains("Saving default project", output, StringComparison.Ordinal);
        Assert.Contains("src/XYZ.Web/XYZ.Web.csproj", File.ReadAllText(repo.Path("dnrun.config.json")), StringComparison.Ordinal);
    }

    [Fact]
    public void Select_without_a_terminal_and_several_candidates_exits_two()
    {
        using var repo = SpecRepo();

        var (exitCode, runner, _) = Invoke(repo, ["select"]);

        Assert.Equal(ExitCodes.UsageError, exitCode);
        Assert.Null(runner.Plan);
        Assert.False(File.Exists(repo.Path("dnrun.config.json")));
    }

    [Fact]
    public void List_shows_runnable_projects_libraries_and_the_current_selection_without_launching()
    {
        using var repo = SpecRepo().WithConfig("""{ "startupProject": "src/XYZ.Web/XYZ.Web.csproj" }""");

        var (exitCode, runner, output) = Invoke(repo, ["list"]);

        Assert.Equal(0, exitCode);
        Assert.Null(runner.Plan);
        Assert.Contains("XYZ.sln", output, StringComparison.Ordinal);
        Assert.Contains("[1] XYZ.Web", output, StringComparison.Ordinal);
        Assert.Contains("[2] XYZ.API", output, StringComparison.Ordinal);
        Assert.Contains("src/XYZ.API/XYZ.API.csproj", output, StringComparison.Ordinal);
        Assert.Contains("Other projects", output, StringComparison.Ordinal);
        Assert.Contains("XYZ.Domain", output, StringComparison.Ordinal);
        Assert.Contains("Current startup project:", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Config_reports_the_resolved_root_and_marker()
    {
        using var repo = SpecRepo().WithConfig("""{ "startupProject": "src/XYZ.Web/XYZ.Web.csproj" }""");

        var (exitCode, _, output) = Invoke(repo, ["config"], workingDirectory: repo.Path("src/XYZ.Domain"));

        Assert.Equal(0, exitCode);
        Assert.Contains(repo.Root, output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("dnrun.config.json", output, StringComparison.Ordinal);
        Assert.Contains("startupProject:", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Reset_removes_a_configuration_that_holds_nothing_else()
    {
        using var repo = SpecRepo().WithConfig("""{ "startupProject": "src/XYZ.Web/XYZ.Web.csproj" }""");

        var (exitCode, _, _) = Invoke(repo, ["reset"]);

        Assert.Equal(0, exitCode);
        Assert.False(File.Exists(repo.Path("dnrun.config.json")));
    }

    [Fact]
    public void Reset_keeps_settings_that_are_not_the_startup_project()
    {
        using var repo = SpecRepo().WithConfig(
            """{ "startupProject": "src/XYZ.Web/XYZ.Web.csproj", "ignoreDirectories": ["samples"] }""");

        var (exitCode, _, _) = Invoke(repo, ["reset"]);

        Assert.Equal(0, exitCode);
        Assert.True(ConfigurationHasIgnoreOnly(repo));
    }

    private static bool ConfigurationHasIgnoreOnly(TempRepo repo)
    {
        var json = File.ReadAllText(repo.Path("dnrun.config.json"));
        return json.Contains("samples", StringComparison.Ordinal)
               && !json.Contains("startupProject", StringComparison.Ordinal);
    }

    [Fact]
    public void An_unknown_command_never_falls_through_to_running_something()
    {
        using var repo = SpecRepo();

        var (exitCode, runner, output) = Invoke(repo, ["deploy"]);

        Assert.Equal(ExitCodes.UsageError, exitCode);
        Assert.Null(runner.Plan);
        Assert.Contains("unknown command", output, StringComparison.Ordinal);
        Assert.Contains("Usage:", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Help_and_version_do_not_touch_the_repository()
    {
        using var repo = SpecRepo();

        var (helpCode, helpRunner, helpOutput) = Invoke(repo, ["--help"]);
        var (versionCode, _, versionOutput) = Invoke(repo, ["version"]);

        Assert.Equal(0, helpCode);
        Assert.Null(helpRunner.Plan);
        Assert.Contains("dnrun select", helpOutput, StringComparison.Ordinal);
        Assert.Equal(0, versionCode);
        Assert.Contains("dnrun 1.0.0", versionOutput, StringComparison.Ordinal);
    }
}
