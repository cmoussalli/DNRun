using DNRun.Discovery;
using DNRun.Model;
using DNRun.Packaging;
using DNRun.Tests.Fixtures;

namespace DNRun.Tests;

public sealed class VersionUpdaterTests
{
    private static ProjectInfo Analyze(TempRepo repo, string relativePath) =>
        ProjectAnalyzer.Analyze(repo.Path(relativePath), repo.Root);

    private static NuGetVersion Version(string text)
    {
        Assert.True(NuGetVersion.TryParse(text, out var version, out var error), error);
        return version!;
    }

    private static string Apply(TempRepo repo, string relativePath, string version)
    {
        var project = Analyze(repo, relativePath);
        var source = VersionUpdater.Resolve(project, repo.Root);
        var result = VersionUpdater.Apply(source, Version(version));

        Assert.True(result.Succeeded, result.Error);
        return File.ReadAllText(source.FilePath);
    }

    [Fact]
    public void A_declared_version_is_replaced_in_the_project_file()
    {
        using var repo = TempRepo.Create().WithFile("src/A/A.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <Version>1.0.0</Version>
              </PropertyGroup>
            </Project>
            """);

        Assert.Contains("<Version>1.2.14</Version>", Apply(repo, "src/A/A.csproj", "1.2.14"), StringComparison.Ordinal);
    }

    [Fact]
    public void PackageVersion_wins_over_Version_when_both_are_declared_and_both_are_updated()
    {
        using var repo = TempRepo.Create().WithFile("src/A/A.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <Version>1.0.0</Version>
                <PackageVersion>1.0.0-beta.3</PackageVersion>
              </PropertyGroup>
            </Project>
            """);

        Assert.Equal("1.0.0-beta.3", VersionUpdater.Resolve(Analyze(repo, "src/A/A.csproj"), repo.Root).CurrentVersion);

        var updated = Apply(repo, "src/A/A.csproj", "1.2.14");

        Assert.Contains("<Version>1.2.14</Version>", updated, StringComparison.Ordinal);
        Assert.Contains("<PackageVersion>1.2.14</PackageVersion>", updated, StringComparison.Ordinal);
    }

    [Fact]
    public void Properties_the_project_already_declares_are_kept_in_step()
    {
        using var repo = TempRepo.Create().WithFile("src/A/A.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <Version>1.0.0</Version>
                <InformationalVersion>1.0.0</InformationalVersion>
                <AssemblyVersion>1.0.0.0</AssemblyVersion>
                <FileVersion>1.0.0.0</FileVersion>
              </PropertyGroup>
            </Project>
            """);

        var updated = Apply(repo, "src/A/A.csproj", "2.0.0-rc.1+build.9");

        Assert.Contains("<Version>2.0.0-rc.1</Version>", updated, StringComparison.Ordinal);
        Assert.Contains("<InformationalVersion>2.0.0-rc.1+build.9</InformationalVersion>", updated, StringComparison.Ordinal);
        Assert.Contains("<AssemblyVersion>2.0.0.0</AssemblyVersion>", updated, StringComparison.Ordinal);
        Assert.Contains("<FileVersion>2.0.0.0</FileVersion>", updated, StringComparison.Ordinal);
    }

    [Fact]
    public void Assembly_metadata_the_project_does_not_declare_is_not_introduced()
    {
        using var repo = TempRepo.Create().WithFile("src/A/A.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <Version>1.0.0</Version>
              </PropertyGroup>
            </Project>
            """);

        var updated = Apply(repo, "src/A/A.csproj", "1.2.14");

        Assert.DoesNotContain("AssemblyVersion", updated, StringComparison.Ordinal);
        Assert.DoesNotContain("InformationalVersion", updated, StringComparison.Ordinal);
    }

    [Fact]
    public void A_split_prefix_and_suffix_are_written_apart()
    {
        using var repo = TempRepo.Create().WithFile("src/A/A.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <VersionPrefix>0.9.1</VersionPrefix>
              </PropertyGroup>
            </Project>
            """);

        var updated = Apply(repo, "src/A/A.csproj", "2.0.0-beta.1");

        Assert.Contains("<VersionPrefix>2.0.0</VersionPrefix>", updated, StringComparison.Ordinal);
        Assert.Contains("<VersionSuffix>beta.1</VersionSuffix>", updated, StringComparison.Ordinal);
        Assert.DoesNotContain("<Version>", updated, StringComparison.Ordinal);
    }

    [Fact]
    public void A_stale_suffix_is_cleared_by_a_stable_release()
    {
        using var repo = TempRepo.Create().WithFile("src/A/A.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <VersionPrefix>2.0.0</VersionPrefix>
                <VersionSuffix>beta.1</VersionSuffix>
              </PropertyGroup>
            </Project>
            """);

        var updated = Apply(repo, "src/A/A.csproj", "2.0.0");

        Assert.Contains("<VersionSuffix></VersionSuffix>", updated, StringComparison.Ordinal);

        // An emptied suffix must read back as a stable release, not as "2.0.0-".
        Assert.Equal("2.0.0", VersionUpdater.Resolve(Analyze(repo, "src/A/A.csproj"), repo.Root).CurrentVersion);
    }

    [Fact]
    public void A_project_that_declares_no_version_gets_one()
    {
        using var repo = TempRepo.Create().WithProject("src/A/A.csproj", extraProperties: "<IsPackable>true</IsPackable>");

        var source = VersionUpdater.Resolve(Analyze(repo, "src/A/A.csproj"), repo.Root);
        Assert.True(source.IsImplicit);
        Assert.True(source.IsProjectFile);

        Assert.Contains("<Version>1.2.14</Version>", Apply(repo, "src/A/A.csproj", "1.2.14"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_version_inherited_from_directory_build_props_is_updated_there()
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

        var project = Analyze(repo, "src/A/A.csproj");
        var source = VersionUpdater.Resolve(project, repo.Root);

        Assert.False(source.IsProjectFile);
        Assert.Equal("3.1.0", source.CurrentVersion);

        Assert.True(VersionUpdater.Apply(source, Version("3.2.0")).Succeeded);

        Assert.Contains("<Version>3.2.0</Version>", File.ReadAllText(repo.Path("Directory.Build.props")), StringComparison.Ordinal);
        Assert.DoesNotContain("<Version>", File.ReadAllText(repo.Path("src/A/A.csproj")), StringComparison.Ordinal);
    }

    [Fact]
    public void The_projects_own_version_wins_over_an_inherited_one()
    {
        using var repo = TempRepo.Create()
            .WithFile("Directory.Build.props", """
                <Project>
                  <PropertyGroup>
                    <Version>3.1.0</Version>
                  </PropertyGroup>
                </Project>
                """)
            .WithProject("src/A/A.csproj", extraProperties: "<Version>1.0.0</Version>");

        var source = VersionUpdater.Resolve(Analyze(repo, "src/A/A.csproj"), repo.Root);

        Assert.True(source.IsProjectFile);
        Assert.Equal("1.0.0", source.CurrentVersion);

        Assert.True(VersionUpdater.Apply(source, Version("1.0.1")).Succeeded);
        Assert.Contains("<Version>3.1.0</Version>", File.ReadAllText(repo.Path("Directory.Build.props")), StringComparison.Ordinal);
    }

    [Fact]
    public void The_nearest_props_file_wins()
    {
        using var repo = TempRepo.Create()
            .WithFile("Directory.Build.props", """
                <Project>
                  <PropertyGroup>
                    <Version>3.1.0</Version>
                  </PropertyGroup>
                </Project>
                """)
            .WithFile("src/Directory.Build.props", """
                <Project>
                  <PropertyGroup>
                    <Version>4.0.0</Version>
                  </PropertyGroup>
                </Project>
                """)
            .WithProject("src/A/A.csproj");

        var source = VersionUpdater.Resolve(Analyze(repo, "src/A/A.csproj"), repo.Root);

        Assert.Equal(repo.Path("src/Directory.Build.props"), source.FilePath);
        Assert.Equal("4.0.0", source.CurrentVersion);
    }

    [Fact]
    public void A_props_file_outside_the_repository_is_not_touched()
    {
        using var repo = TempRepo.Create().WithProject("src/A/A.csproj");

        // The walk stops at the repository root; anything above it belongs to another repository
        // (or to the temp directory itself) and must be left alone.
        Assert.All(
            VersionUpdater.PropsFilesAbove(repo.Path("src/A/A.csproj"), repo.Root),
            path => Assert.StartsWith(repo.Root, path, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Re_applying_the_same_version_changes_nothing()
    {
        using var repo = TempRepo.Create().WithFile("src/A/A.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <Version>1.2.14</Version>
              </PropertyGroup>
            </Project>
            """);

        var before = File.ReadAllText(repo.Path("src/A/A.csproj"));
        var source = VersionUpdater.Resolve(Analyze(repo, "src/A/A.csproj"), repo.Root);
        var result = VersionUpdater.Apply(source, Version("1.2.14"));

        Assert.True(result.Succeeded);
        Assert.Empty(result.Changes);
        Assert.Equal(before, File.ReadAllText(repo.Path("src/A/A.csproj")));
    }

    [Fact]
    public void Each_rewritten_property_is_reported()
    {
        using var repo = TempRepo.Create().WithFile("src/A/A.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <Version>1.0.0</Version>
                <FileVersion>1.0.0.0</FileVersion>
              </PropertyGroup>
            </Project>
            """);

        var source = VersionUpdater.Resolve(Analyze(repo, "src/A/A.csproj"), repo.Root);
        var result = VersionUpdater.Apply(source, Version("1.2.14"));

        Assert.Equal(["Version", "FileVersion"], result.Changes.Select(c => c.Property).ToArray());
        Assert.Equal("1.0.0", result.Changes[0].OldValue);
        Assert.Equal("1.2.14", result.Changes[0].NewValue);
    }

    [Fact]
    public void An_unparsable_project_fails_without_writing()
    {
        using var repo = TempRepo.Create().WithFile("src/A/A.csproj", "<Project><PropertyGroup>");

        var before = File.ReadAllText(repo.Path("src/A/A.csproj"));
        var source = new VersionSource(repo.Path("src/A/A.csproj"), IsProjectFile: true, CurrentVersion: null);
        var result = VersionUpdater.Apply(source, Version("1.2.14"));

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Error);
        Assert.Equal(before, File.ReadAllText(repo.Path("src/A/A.csproj")));
    }
}
