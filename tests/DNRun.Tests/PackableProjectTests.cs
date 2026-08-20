using DNRun.Cli;
using DNRun.Discovery;
using DNRun.Tests.Fixtures;

namespace DNRun.Tests;

public sealed class PackableProjectTests
{
    [Fact]
    public void A_plain_library_is_packable_but_does_not_ask_to_be()
    {
        using var repo = TempRepo.Create().WithProject("src/A/A.csproj");

        var project = ProjectAnalyzer.Analyze(repo.Path("src/A/A.csproj"), repo.Root);

        Assert.True(project.IsPackable);
        Assert.False(project.PackagesExplicitly);
    }

    [Theory]
    [InlineData("<IsPackable>true</IsPackable>")]
    [InlineData("<PackageId>Acme.Core</PackageId>")]
    [InlineData("<GeneratePackageOnBuild>true</GeneratePackageOnBuild>")]
    [InlineData("<PackAsTool>true</PackAsTool>")]
    public void Opting_in_is_recognized(string property)
    {
        using var repo = TempRepo.Create().WithProject("src/A/A.csproj", extraProperties: property);

        var project = ProjectAnalyzer.Analyze(repo.Path("src/A/A.csproj"), repo.Root);

        Assert.True(project.IsPackable);
        Assert.True(project.PackagesExplicitly);
    }

    [Fact]
    public void Opting_out_is_final_even_with_a_package_id()
    {
        using var repo = TempRepo.Create()
            .WithFile("src/A/A.csproj", """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <PackageId>Acme.Core</PackageId>
                    <IsPackable>false</IsPackable>
                  </PropertyGroup>
                </Project>
                """);

        var project = ProjectAnalyzer.Analyze(repo.Path("src/A/A.csproj"), repo.Root);

        Assert.False(project.IsPackable);
        Assert.False(project.PackagesExplicitly);
    }

    [Fact]
    public void A_test_project_is_not_packable()
    {
        using var repo = TempRepo.Create()
            .WithProject("tests/A.Tests/A.Tests.csproj", outputType: "Exe", packages: ["xunit.v3"]);

        Assert.False(ProjectAnalyzer.Analyze(repo.Path("tests/A.Tests/A.Tests.csproj"), repo.Root).IsPackable);
    }

    [Fact]
    public void A_test_project_that_insists_on_being_packable_is_believed()
    {
        using var repo = TempRepo.Create()
            .WithProject(
                "tests/A.Tests/A.Tests.csproj",
                outputType: "Exe",
                packages: ["xunit.v3"],
                extraProperties: "<IsPackable>true</IsPackable>");

        Assert.True(ProjectAnalyzer.Analyze(repo.Path("tests/A.Tests/A.Tests.csproj"), repo.Root).IsPackable);
    }

    [Fact]
    public void The_declared_version_is_read_from_whichever_property_holds_it()
    {
        using var repo = TempRepo.Create()
            .WithProject("src/A/A.csproj", extraProperties: "<VersionPrefix>1.2.14</VersionPrefix>")
            .WithProject("src/B/B.csproj", extraProperties: "<PackageVersion>2.0.0-rc.1</PackageVersion>");

        Assert.Equal("1.2.14", ProjectAnalyzer.Analyze(repo.Path("src/A/A.csproj"), repo.Root).DeclaredVersion);
        Assert.Equal("2.0.0-rc.1", ProjectAnalyzer.Analyze(repo.Path("src/B/B.csproj"), repo.Root).DeclaredVersion);
    }

    [Fact]
    public void Projects_that_ask_to_be_packaged_crowd_out_the_ones_that_do_not()
    {
        using var repo = TempRepo.Create()
            .WithProject("src/A.Core/A.Core.csproj")
            .WithProject("src/A.Client/A.Client.csproj", extraProperties: "<PackageId>A.Client</PackageId>");

        var session = DNRunSession.Create(repo.Root, quiet: true);

        Assert.Equal(["A.Client"], session.Packable.Select(p => p.Name).ToArray());
    }

    [Fact]
    public void When_nothing_opts_in_every_candidate_is_offered_libraries_first()
    {
        using var repo = TempRepo.Create()
            .WithProject("src/A.Web/A.Web.csproj", sdk: "Microsoft.NET.Sdk.Web")
            .WithProject("src/A.Core/A.Core.csproj", outputType: "Library")
            .WithProject("tests/A.Tests/A.Tests.csproj", outputType: "Exe", packages: ["xunit.v3"]);

        var session = DNRunSession.Create(repo.Root, quiet: true);

        Assert.Equal(["A.Core", "A.Web"], session.Packable.Select(p => p.Name).ToArray());
    }
}
