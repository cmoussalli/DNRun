using DNRun.Discovery;
using DNRun.Model;
using DNRun.Tests.Fixtures;

namespace DNRun.Tests;

public sealed class ProjectAnalyzerTests
{
    [Theory]
    [InlineData("Microsoft.NET.Sdk", "Exe", true)]
    [InlineData("Microsoft.NET.Sdk", "WinExe", true)]
    [InlineData("Microsoft.NET.Sdk", "Library", false)]
    [InlineData("Microsoft.NET.Sdk", null, false)]
    [InlineData("Microsoft.NET.Sdk.Web", null, true)]
    [InlineData("Microsoft.NET.Sdk.Worker", null, true)]
    [InlineData("Microsoft.NET.Sdk.BlazorWebAssembly", null, true)]
    [InlineData("Microsoft.NET.Sdk.Razor", null, false)]
    public void Classifies_runnability_from_the_sdk_and_output_type(string sdk, string? outputType, bool expected)
    {
        using var repo = TempRepo.Create().WithProject("App/App.csproj", sdk: sdk, outputType: outputType);

        var project = ProjectAnalyzer.Analyze(repo.Path("App/App.csproj"), repo.Root);

        Assert.Equal(expected, project.IsRunnable);
    }

    [Fact]
    public void A_razor_class_library_that_declares_an_executable_output_is_still_runnable()
    {
        using var repo = TempRepo.Create()
            .WithProject("App/App.csproj", sdk: "Microsoft.NET.Sdk.Razor", outputType: "Exe");

        Assert.True(ProjectAnalyzer.Analyze(repo.Path("App/App.csproj"), repo.Root).IsRunnable);
    }

    [Fact]
    public void An_aspnetcore_reference_makes_a_plain_sdk_project_runnable()
    {
        using var repo = TempRepo.Create()
            .WithProject("Legacy/Legacy.csproj", packages: ["Microsoft.AspNetCore.App"]);

        Assert.True(ProjectAnalyzer.Analyze(repo.Path("Legacy/Legacy.csproj"), repo.Root).IsRunnable);
    }

    [Fact]
    public void The_last_declared_output_type_wins()
    {
        using var repo = TempRepo.Create().WithFile("App/App.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Library</OutputType>
              </PropertyGroup>
              <PropertyGroup>
                <OutputType>Exe</OutputType>
              </PropertyGroup>
            </Project>
            """);

        Assert.True(ProjectAnalyzer.Analyze(repo.Path("App/App.csproj"), repo.Root).IsRunnable);
    }

    [Theory]
    [InlineData("XYZ.Tests")]
    [InlineData("XYZ.Test")]
    [InlineData("XYZ.IntegrationTests")]
    [InlineData("XYZ.UnitTests")]
    public void Test_projects_are_excluded_by_name(string name)
    {
        using var repo = TempRepo.Create().WithProject($"tests/{name}/{name}.csproj", outputType: "Exe");

        var project = ProjectAnalyzer.Analyze(repo.Path($"tests/{name}/{name}.csproj"), repo.Root);

        Assert.False(project.IsRunnable);
        Assert.Equal(ProjectKind.Test, project.Kind);
    }

    [Theory]
    [InlineData("Microsoft.NET.Test.Sdk")]
    [InlineData("xunit.v3")]
    [InlineData("NUnit")]
    [InlineData("MSTest.TestFramework")]
    public void Test_projects_are_excluded_by_package_reference(string package)
    {
        // A modern test SDK emits OutputType=Exe, so without this filter every test project
        // would pollute the selection menu.
        using var repo = TempRepo.Create()
            .WithProject("Suite/Suite.csproj", outputType: "Exe", packages: [package]);

        Assert.False(ProjectAnalyzer.Analyze(repo.Path("Suite/Suite.csproj"), repo.Root).IsRunnable);
    }

    [Fact]
    public void Test_projects_are_excluded_by_the_IsTestProject_property()
    {
        using var repo = TempRepo.Create().WithProject(
            "Suite/Suite.csproj",
            outputType: "Exe",
            extraProperties: "<IsTestProject>true</IsTestProject>");

        Assert.False(ProjectAnalyzer.Analyze(repo.Path("Suite/Suite.csproj"), repo.Root).IsRunnable);
    }

    [Theory]
    [InlineData("XYZ.Web", nameof(ProjectKind.Web))]
    [InlineData("XYZ.API", nameof(ProjectKind.Api))]
    [InlineData("XYZ.Windows", nameof(ProjectKind.Windows))]
    [InlineData("XYZ.Mobile", nameof(ProjectKind.Mobile))]
    [InlineData("XYZ.Worker", nameof(ProjectKind.Worker))]
    [InlineData("XYZ.Cli", nameof(ProjectKind.Console))]
    public void Classifies_the_project_kind_from_the_name_suffix(string name, string expected)
    {
        using var repo = TempRepo.Create().WithProject($"src/{name}/{name}.csproj", outputType: "Exe");

        Assert.Equal(
            Enum.Parse<ProjectKind>(expected),
            ProjectAnalyzer.Analyze(repo.Path($"src/{name}/{name}.csproj"), repo.Root).Kind);
    }

    [Fact]
    public void Malformed_xml_warns_and_skips_instead_of_crashing()
    {
        using var repo = TempRepo.Create().WithFile("Broken/Broken.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\">");

        var project = ProjectAnalyzer.Analyze(repo.Path("Broken/Broken.csproj"), repo.Root);

        Assert.False(project.IsRunnable);
        Assert.NotNull(project.AnalysisWarning);
        Assert.Equal("Broken", project.Name);
    }

    [Fact]
    public void A_legacy_project_carrying_the_msbuild_namespace_is_still_understood()
    {
        using var repo = TempRepo.Create().WithFile("Old/Old.csproj", """
            <Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
              </PropertyGroup>
            </Project>
            """);

        Assert.True(ProjectAnalyzer.Analyze(repo.Path("Old/Old.csproj"), repo.Root).IsRunnable);
    }

    [Fact]
    public void An_sdk_version_suffix_does_not_change_the_sdk_identity()
    {
        using var repo = TempRepo.Create().WithProject("Web/Web.csproj", sdk: "Microsoft.NET.Sdk.Web/8.0.100");

        var project = ProjectAnalyzer.Analyze(repo.Path("Web/Web.csproj"), repo.Root);

        Assert.True(project.IsRunnable);
        Assert.Equal("Microsoft.NET.Sdk.Web", project.Sdk);
    }

    [Fact]
    public void The_relative_path_is_repository_rooted_and_forward_slashed()
    {
        using var repo = TempRepo.Create().WithProject("src/XYZ.Web/XYZ.Web.csproj", sdk: "Microsoft.NET.Sdk.Web");

        var project = ProjectAnalyzer.Analyze(repo.Path("src/XYZ.Web/XYZ.Web.csproj"), repo.Root);

        Assert.Equal("src/XYZ.Web/XYZ.Web.csproj", project.RelativePath);
    }

    [Fact]
    public void The_runnable_allowlist_overrides_the_heuristics()
    {
        // The escape hatch for OutputType inherited from Directory.Build.props, which DNRun
        // deliberately does not evaluate.
        using var repo = TempRepo.Create()
            .WithSolution("XYZ.sln")
            .WithProject("src/Hidden/Hidden.csproj");

        var context = WorkspaceResolver.Resolve(repo.Root);
        var config = new DNRun.Configuration.DNRunConfig { RunnableProjects = ["src/Hidden/Hidden.csproj"] };

        var discovery = ProjectDiscovery.Discover(context, config);

        Assert.Single(discovery.RunnableProjects);
        Assert.Equal("Hidden", discovery.RunnableProjects[0].Name);
    }
}
