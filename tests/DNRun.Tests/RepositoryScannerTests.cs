using DNRun.Configuration;
using DNRun.Discovery;
using DNRun.Tests.Fixtures;

namespace DNRun.Tests;

public sealed class RepositoryScannerTests
{
    private static ScanResult Scan(TempRepo repo, DNRunConfig? config = null, string? from = null) =>
        RepositoryScanner.Scan(WorkspaceResolver.Resolve(from ?? repo.Root), config);

    [Fact]
    public void Finds_projects_directly_at_the_repository_root()
    {
        using var repo = TempRepo.Create()
            .WithSolution("XYZ.sln")
            .WithProject("XYZ.Web.csproj", sdk: "Microsoft.NET.Sdk.Web")
            .WithProject("XYZ.API.csproj", sdk: "Microsoft.NET.Sdk.Web");

        var result = Scan(repo);

        Assert.Equal(2, result.ProjectFiles.Count);
        Assert.False(result.UsedFallbackScan);
    }

    [Fact]
    public void Finds_projects_nested_under_src()
    {
        using var repo = TempRepo.Create()
            .WithSolution("XYZ.sln")
            .WithProject("src/XYZ.Web/XYZ.Web.csproj", sdk: "Microsoft.NET.Sdk.Web")
            .WithProject("src/Areas/Deep/XYZ.Nested/XYZ.Nested.csproj", outputType: "Exe");

        var result = Scan(repo);

        Assert.Equal(2, result.ProjectFiles.Count);
        Assert.Contains(result.ProjectFiles, p => p.EndsWith("XYZ.Nested.csproj", StringComparison.Ordinal));
    }

    [Fact]
    public void Prunes_generated_and_vendor_directories()
    {
        using var repo = TempRepo.Create()
            .WithSolution("XYZ.sln")
            .WithProject("src/XYZ.Web/XYZ.Web.csproj", sdk: "Microsoft.NET.Sdk.Web")
            .WithProject("src/XYZ.Web/bin/Debug/Ghost.csproj", outputType: "Exe")
            .WithProject("src/XYZ.Web/obj/Ghost2.csproj", outputType: "Exe")
            .WithProject("src/node_modules/pkg/Ghost3.csproj", outputType: "Exe")
            .WithProject("src/.hidden/Ghost4.csproj", outputType: "Exe");

        var result = Scan(repo);

        Assert.Single(result.ProjectFiles);
        Assert.EndsWith("XYZ.Web.csproj", result.ProjectFiles[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Honors_extra_ignored_directories_from_the_configuration()
    {
        using var repo = TempRepo.Create()
            .WithSolution("XYZ.sln")
            .WithProject("src/Keep/Keep.csproj", outputType: "Exe")
            .WithProject("src/samples/Sample.csproj", outputType: "Exe");

        var result = Scan(repo, new DNRunConfig { IgnoreDirectories = ["samples"] });

        Assert.Single(result.ProjectFiles);
        Assert.EndsWith("Keep.csproj", result.ProjectFiles[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Falls_back_to_a_full_scan_when_root_and_src_are_empty()
    {
        using var repo = TempRepo.Create()
            .WithSolution("XYZ.sln")
            .WithProject("source/Apps/XYZ.Web/XYZ.Web.csproj", sdk: "Microsoft.NET.Sdk.Web");

        var result = Scan(repo);

        Assert.True(result.UsedFallbackScan);
        Assert.Single(result.ProjectFiles);
    }

    [Fact]
    public void Scanning_is_identical_when_invoked_from_a_deep_subdirectory()
    {
        using var repo = TempRepo.Create()
            .WithSolution("XYZ.sln")
            .WithProject("src/XYZ.Web/XYZ.Web.csproj", sdk: "Microsoft.NET.Sdk.Web")
            .WithProject("src/XYZ.Domain/XYZ.Domain.csproj");

        var fromRoot = Scan(repo);
        var fromDeep = Scan(repo, from: repo.Path("src/XYZ.Domain"));

        Assert.Equal(fromRoot.ProjectFiles, fromDeep.ProjectFiles);
    }

    [Fact]
    public void Solution_only_projects_outside_the_scan_order_are_still_discovered()
    {
        using var repo = TempRepo.Create()
            .WithSolution("XYZ.sln", "src/XYZ.Web/XYZ.Web.csproj", "tools/Gen/Gen.csproj")
            .WithProject("src/XYZ.Web/XYZ.Web.csproj", sdk: "Microsoft.NET.Sdk.Web")
            .WithProject("tools/Gen/Gen.csproj", outputType: "Exe");

        var context = WorkspaceResolver.Resolve(repo.Root);
        var discovery = ProjectDiscovery.Discover(context, null);

        Assert.Equal(2, discovery.RunnableProjects.Count);
        Assert.Contains(discovery.RunnableProjects, p => p.Name == "Gen");
    }

    [Fact]
    public void Projects_missing_from_the_solution_are_offered_but_tagged()
    {
        using var repo = TempRepo.Create()
            .WithSolution("XYZ.sln", "src/XYZ.Web/XYZ.Web.csproj")
            .WithProject("src/XYZ.Web/XYZ.Web.csproj", sdk: "Microsoft.NET.Sdk.Web")
            .WithProject("src/XYZ.Extra/XYZ.Extra.csproj", outputType: "Exe");

        var discovery = ProjectDiscovery.Discover(WorkspaceResolver.Resolve(repo.Root), null);

        var extra = Assert.Single(discovery.AllProjects, p => p.Name == "XYZ.Extra");
        Assert.True(extra.IsRunnable);
        Assert.False(extra.InSolution);
    }

    [Fact]
    public void Solution_entries_missing_on_disk_are_ignored_silently()
    {
        using var repo = TempRepo.Create()
            .WithSolution("XYZ.sln", "src/XYZ.Web/XYZ.Web.csproj", "src/Deleted/Deleted.csproj")
            .WithProject("src/XYZ.Web/XYZ.Web.csproj", sdk: "Microsoft.NET.Sdk.Web");

        var discovery = ProjectDiscovery.Discover(WorkspaceResolver.Resolve(repo.Root), null);

        Assert.Single(discovery.AllProjects);
        Assert.Empty(discovery.Warnings);
    }

    [Fact]
    public void Solution_folders_are_not_treated_as_projects()
    {
        using var repo = TempRepo.Create().WithSolutionFolder("XYZ.sln", "Solution Items");

        var paths = SolutionReader.ReadProjectPaths(repo.Path("XYZ.sln"));

        Assert.Empty(paths);
    }

    [Fact]
    public void Reads_project_paths_from_a_slnx_solution()
    {
        using var repo = TempRepo.Create()
            .WithFile("XYZ.slnx", """
                <Solution>
                  <Folder Name="/src/">
                    <Project Path="src/XYZ.Web/XYZ.Web.csproj" />
                    <Project Path="src/XYZ.Domain/XYZ.Domain.csproj" />
                  </Folder>
                </Solution>
                """)
            .WithProject("src/XYZ.Web/XYZ.Web.csproj", sdk: "Microsoft.NET.Sdk.Web")
            .WithProject("src/XYZ.Domain/XYZ.Domain.csproj");

        var paths = SolutionReader.ReadProjectPaths(repo.Path("XYZ.slnx"));

        Assert.Equal(2, paths.Count);
        Assert.All(paths, p => Assert.True(File.Exists(p)));
    }

    [Fact]
    public void The_spec_reference_layout_yields_exactly_the_runnable_projects()
    {
        using var repo = TempRepo.Create()
            .WithSolution(
                "XYZ.sln",
                "src/XYZ.Web/XYZ.Web.csproj",
                "src/XYZ.API/XYZ.API.csproj",
                "src/XYZ.Windows/XYZ.Windows.csproj",
                "src/XYZ.Domain/XYZ.Domain.csproj")
            .WithProject("src/XYZ.Web/XYZ.Web.csproj", sdk: "Microsoft.NET.Sdk.Web")
            .WithProject("src/XYZ.API/XYZ.API.csproj", sdk: "Microsoft.NET.Sdk.Web")
            .WithProject("src/XYZ.Windows/XYZ.Windows.csproj", outputType: "WinExe")
            .WithProject("src/XYZ.Domain/XYZ.Domain.csproj");

        var discovery = ProjectDiscovery.Discover(WorkspaceResolver.Resolve(repo.Root), null);

        Assert.Equal(
            ["XYZ.Web", "XYZ.API", "XYZ.Windows"],
            discovery.RunnableProjects.Select(p => p.Name).ToArray());
    }
}
