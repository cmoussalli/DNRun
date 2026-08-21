using DNRun.Cli;
using DNRun.Configuration;
using DNRun.Execution;
using DNRun.Tests.Fixtures;

namespace DNRun.Tests;

/// <summary>
/// End-to-end flow tests for <c>dnuget</c> through <see cref="CommandRouter"/>. Nothing is
/// launched, so the assertions are about which file changed and what the terminal said.
/// </summary>
[Collection(ConsoleCapture.Name)]
public sealed class NugetFlowTests
{
    private sealed class UnusedProcessRunner : IProcessRunner
    {
        public int Run(RunPlan plan) => throw new InvalidOperationException("dnuget must never launch a process.");
    }

    private static (int ExitCode, string Output) Invoke(TempRepo repo, params string[] args)
    {
        var writer = new StringWriter();
        var originalOut = Console.Out;
        var originalError = Console.Error;

        try
        {
            Console.SetOut(writer);
            Console.SetError(writer);
            var exitCode = CommandRouter.Execute(args, new UnusedProcessRunner(), repo.Root);
            return (exitCode, writer.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    private static string Csproj(string relativePath, TempRepo repo) => File.ReadAllText(repo.Path(relativePath));

    private static TempRepo SinglePackageRepo() => TempRepo.Create()
        .WithFile("src/Acme.Core/Acme.Core.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <PackageId>Acme.Core</PackageId>
                <Version>1.0.3</Version>
              </PropertyGroup>
            </Project>
            """)
        .WithProject("src/Acme.Web/Acme.Web.csproj", sdk: "Microsoft.NET.Sdk.Web", extraProperties: "<IsPackable>false</IsPackable>")
        .WithProject("tests/Acme.Tests/Acme.Tests.csproj", outputType: "Exe", packages: ["xunit.v3"]);

    [Fact]
    public void The_only_packable_project_is_versioned_without_interaction()
    {
        using var repo = SinglePackageRepo();

        var (exitCode, output) = Invoke(repo, "nuget", "1.2.14");

        Assert.Equal(0, exitCode);
        Assert.Contains("Acme.Core", output, StringComparison.Ordinal);
        Assert.Contains("<Version>1.2.14</Version>", Csproj("src/Acme.Core/Acme.Core.csproj", repo), StringComparison.Ordinal);
    }

    [Fact]
    public void The_dnuget_alias_is_the_same_command()
    {
        using var repo = SinglePackageRepo();

        var (exitCode, _) = Invoke(repo, ParsedArgs.NormalizeArgs("dnuget", ["1.2.14"]));

        Assert.Equal(0, exitCode);
        Assert.Contains("<Version>1.2.14</Version>", Csproj("src/Acme.Core/Acme.Core.csproj", repo), StringComparison.Ordinal);
    }

    [Fact]
    public void A_project_that_opts_out_of_packaging_is_never_chosen()
    {
        using var repo = SinglePackageRepo();

        var (_, output) = Invoke(repo, "nuget", "list");

        Assert.Contains("Acme.Core", output, StringComparison.Ordinal);
        Assert.DoesNotContain("Acme.Web", output, StringComparison.Ordinal);
        Assert.DoesNotContain("Acme.Tests", output, StringComparison.Ordinal);
    }

    [Fact]
    public void A_saved_package_project_does_not_narrow_a_version()
    {
        using var repo = TempRepo.Create()
            .WithProject("src/A/A.csproj", extraProperties: "<PackageId>A</PackageId>")
            .WithProject("src/B/B.csproj", extraProperties: "<PackageId>B</PackageId>")
            .WithConfig("""{ "packageProject": "src/B/B.csproj" }""");

        var (exitCode, output) = Invoke(repo, "nuget", "1.2.14");

        Assert.Equal(0, exitCode);
        Assert.Contains("<Version>1.2.14</Version>", Csproj("src/A/A.csproj", repo), StringComparison.Ordinal);
        Assert.Contains("<Version>1.2.14</Version>", Csproj("src/B/B.csproj", repo), StringComparison.Ordinal);
        Assert.DoesNotContain("Select", output, StringComparison.Ordinal);
    }

    [Fact]
    public void The_startup_project_and_the_package_project_are_independent()
    {
        using var repo = TempRepo.Create()
            .WithProject("src/A.Web/A.Web.csproj", sdk: "Microsoft.NET.Sdk.Web")
            .WithProject("src/A.Core/A.Core.csproj", extraProperties: "<PackageId>A.Core</PackageId>")
            .WithConfig("""{ "startupProject": "src/A.Web/A.Web.csproj", "packageProject": "src/A.Core/A.Core.csproj" }""");

        Assert.Equal(0, Invoke(repo, "nuget", "1.2.14").ExitCode);

        Assert.True(ConfigurationManager.TryLoad(repo.Root, out var config, out _));
        Assert.Equal("src/A.Web/A.Web.csproj", config!.StartupProject);
        Assert.Equal("src/A.Core/A.Core.csproj", config.PackageProject);
    }

    [Fact]
    public void Several_candidates_are_all_versioned_without_asking()
    {
        using var repo = TempRepo.Create()
            .WithProject("src/A/A.csproj", extraProperties: "<PackageId>A</PackageId>")
            .WithProject("src/B/B.csproj", extraProperties: "<PackageId>B</PackageId>");

        var (exitCode, output) = Invoke(repo, "nuget", "1.2.14");

        Assert.Equal(0, exitCode);
        Assert.Contains("<Version>1.2.14</Version>", Csproj("src/A/A.csproj", repo), StringComparison.Ordinal);
        Assert.Contains("<Version>1.2.14</Version>", Csproj("src/B/B.csproj", repo), StringComparison.Ordinal);
        Assert.DoesNotContain("Select", output, StringComparison.Ordinal);
        Assert.DoesNotContain("interactive terminal", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Choosing_one_project_without_a_terminal_refuses_rather_than_guessing()
    {
        using var repo = TempRepo.Create()
            .WithProject("src/A/A.csproj", extraProperties: "<PackageId>A</PackageId>")
            .WithProject("src/B/B.csproj", extraProperties: "<PackageId>B</PackageId>");

        var (exitCode, output) = Invoke(repo, "nuget", "select", "1.2.14");

        Assert.Equal(ExitCodes.UsageError, exitCode);
        Assert.Contains("interactive terminal", output, StringComparison.Ordinal);
        Assert.DoesNotContain("<Version>", Csproj("src/A/A.csproj", repo), StringComparison.Ordinal);
        Assert.DoesNotContain("<Version>", Csproj("src/B/B.csproj", repo), StringComparison.Ordinal);
    }

    [Fact]
    public void All_and_select_together_are_refused_before_the_repository_is_touched()
    {
        using var repo = TempRepo.Create()
            .WithProject("src/A/A.csproj", extraProperties: "<PackageId>A</PackageId>");

        var (exitCode, output) = Invoke(repo, "nuget", "--all", "--select", "1.2.14");

        Assert.Equal(ExitCodes.UsageError, exitCode);
        Assert.Contains("opposite things", output, StringComparison.Ordinal);
        Assert.DoesNotContain("<Version>", Csproj("src/A/A.csproj", repo), StringComparison.Ordinal);
    }

    [Fact]
    public void A_repository_that_publishes_nothing_reports_it()
    {
        using var repo = TempRepo.Create()
            .WithProject("tests/A.Tests/A.Tests.csproj", outputType: "Exe", packages: ["xunit.v3"]);

        var (exitCode, output) = Invoke(repo, "nuget", "1.2.14");

        Assert.Equal(ExitCodes.NoRunnableProject, exitCode);
        Assert.Contains("no packable .NET project was found", output, StringComparison.Ordinal);
    }

    [Fact]
    public void A_version_reaches_every_package_and_writes_a_shared_props_file_once()
    {
        using var repo = TempRepo.Create()
            .WithFile("Directory.Build.props", """
                <Project>
                  <PropertyGroup>
                    <Version>3.1.0</Version>
                  </PropertyGroup>
                </Project>
                """)
            .WithProject("src/A/A.csproj", extraProperties: "<PackageId>A</PackageId>")
            .WithProject("src/B/B.csproj", extraProperties: "<PackageId>B</PackageId>");

        var (exitCode, output) = Invoke(repo, "nuget", "3.2.0");

        Assert.Equal(0, exitCode);
        Assert.Contains("<Version>3.2.0</Version>", Csproj("Directory.Build.props", repo), StringComparison.Ordinal);

        // One shared file, one rewrite - not one per project.
        Assert.Equal(1, output.Split("Updated Directory.Build.props").Length - 1);
    }

    [Fact]
    public void The_all_flag_is_the_default_said_explicitly()
    {
        using var repo = TempRepo.Create()
            .WithProject("src/A/A.csproj", extraProperties: "<PackageId>A</PackageId>")
            .WithProject("src/B/B.csproj", extraProperties: "<PackageId>B</PackageId>");

        var (exitCode, _) = Invoke(repo, "nuget", "--all", "1.2.14");

        Assert.Equal(0, exitCode);
        Assert.Contains("<Version>1.2.14</Version>", Csproj("src/A/A.csproj", repo), StringComparison.Ordinal);
        Assert.Contains("<Version>1.2.14</Version>", Csproj("src/B/B.csproj", repo), StringComparison.Ordinal);
    }

    [Fact]
    public void A_single_project_says_where_an_inherited_version_is_written()
    {
        using var repo = TempRepo.Create()
            .WithFile("Directory.Build.props", """
                <Project>
                  <PropertyGroup>
                    <Version>3.1.0</Version>
                  </PropertyGroup>
                </Project>
                """)
            .WithProject("src/A/A.csproj", extraProperties: "<PackageId>A</PackageId>");

        var (exitCode, output) = Invoke(repo, "nuget", "3.2.0");

        Assert.Equal(0, exitCode);
        Assert.Contains("declared in Directory.Build.props", output, StringComparison.Ordinal);
        Assert.Contains("<Version>3.2.0</Version>", Csproj("Directory.Build.props", repo), StringComparison.Ordinal);
    }

    [Fact]
    public void Showing_several_candidates_lists_them_instead_of_asking()
    {
        using var repo = TempRepo.Create()
            .WithProject("src/A/A.csproj", extraProperties: "<PackageId>A</PackageId>")
            .WithProject("src/B/B.csproj", extraProperties: "<PackageId>B</PackageId>");

        var (exitCode, output) = Invoke(repo, "nuget");

        Assert.Equal(0, exitCode);
        Assert.Contains("Packable projects (2)", output, StringComparison.Ordinal);
        Assert.DoesNotContain("Select", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Showing_the_version_writes_nothing()
    {
        using var repo = SinglePackageRepo();
        var before = Csproj("src/Acme.Core/Acme.Core.csproj", repo);

        var (exitCode, output) = Invoke(repo, "nuget");

        Assert.Equal(0, exitCode);
        Assert.Contains("1.0.3", output, StringComparison.Ordinal);
        Assert.Equal(before, Csproj("src/Acme.Core/Acme.Core.csproj", repo));
    }

    [Fact]
    public void A_bad_version_never_reaches_the_repository()
    {
        using var repo = SinglePackageRepo();
        var before = Csproj("src/Acme.Core/Acme.Core.csproj", repo);

        var (exitCode, output) = Invoke(repo, "nuget", "1.2.x");

        Assert.Equal(ExitCodes.UsageError, exitCode);
        Assert.Contains("not a valid NuGet version", output, StringComparison.Ordinal);
        Assert.Equal(before, Csproj("src/Acme.Core/Acme.Core.csproj", repo));
    }

    [Fact]
    public void Reset_forgets_only_the_package_project()
    {
        using var repo = TempRepo.Create()
            .WithProject("src/A.Web/A.Web.csproj", sdk: "Microsoft.NET.Sdk.Web")
            .WithProject("src/A.Core/A.Core.csproj", extraProperties: "<PackageId>A.Core</PackageId>")
            .WithConfig("""{ "startupProject": "src/A.Web/A.Web.csproj", "packageProject": "src/A.Core/A.Core.csproj" }""");

        Assert.Equal(0, Invoke(repo, "nuget", "reset").ExitCode);

        Assert.True(ConfigurationManager.TryLoad(repo.Root, out var config, out _));
        Assert.Null(config!.PackageProject);
        Assert.Equal("src/A.Web/A.Web.csproj", config.StartupProject);
    }

    [Fact]
    public void Help_never_touches_the_repository()
    {
        using var repo = TempRepo.Create();

        var (exitCode, output) = Invoke(repo, "nuget", "--help");

        Assert.Equal(0, exitCode);
        Assert.Contains("dnuget 1.2.14", output, StringComparison.Ordinal);
        Assert.False(File.Exists(repo.Path("dnrun.config.json")));
    }
}
