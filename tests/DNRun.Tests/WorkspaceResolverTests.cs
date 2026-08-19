using DNRun.Discovery;
using DNRun.Tests.Fixtures;

namespace DNRun.Tests;

public sealed class WorkspaceResolverTests
{
    [Fact]
    public void Uses_the_given_directory_when_no_marker_exists()
    {
        using var repo = TempRepo.Create().WithProject("App.csproj", outputType: "Exe");

        var context = WorkspaceResolver.Resolve(repo.Root);

        Assert.Equal(repo.Root, context.RepositoryRoot, ignoreCase: true);
        Assert.Null(context.RootMarker);
    }

    [Fact]
    public void Walks_up_to_the_solution_when_invoked_from_a_subdirectory()
    {
        using var repo = TempRepo.Create()
            .WithSolution("XYZ.sln", "src/XYZ.Web/XYZ.Web.csproj")
            .WithProject("src/XYZ.Web/XYZ.Web.csproj", sdk: "Microsoft.NET.Sdk.Web")
            .WithProject("src/XYZ.Domain/XYZ.Domain.csproj");

        var context = WorkspaceResolver.Resolve(repo.Path("src/XYZ.Domain"));

        Assert.Equal(repo.Root, context.RepositoryRoot, ignoreCase: true);
        Assert.Equal("*.sln", context.RootMarker);
        Assert.Equal(repo.Path("XYZ.sln"), context.SolutionPath, ignoreCase: true);
    }

    [Fact]
    public void Config_file_outranks_a_solution_further_up()
    {
        using var repo = TempRepo.Create()
            .WithSolution("Outer.sln")
            .WithFile("inner/dnrun.config.json", "{}")
            .WithProject("inner/App/App.csproj", outputType: "Exe");

        var context = WorkspaceResolver.Resolve(repo.Path("inner/App"));

        Assert.Equal(repo.Path("inner"), context.RepositoryRoot, ignoreCase: true);
        Assert.Equal("dnrun.config.json", context.RootMarker);
    }

    [Fact]
    public void Git_directory_marks_the_root_when_there_is_no_solution()
    {
        using var repo = TempRepo.Create()
            .WithDirectory(".git")
            .WithProject("src/App/App.csproj", outputType: "Exe");

        var context = WorkspaceResolver.Resolve(repo.Path("src/App"));

        Assert.Equal(repo.Root, context.RepositoryRoot, ignoreCase: true);
        Assert.Equal(".git", context.RootMarker);
        Assert.Null(context.SolutionPath);
    }

    [Fact]
    public void Slnx_is_preferred_over_sln_as_a_root_marker()
    {
        using var repo = TempRepo.Create()
            .WithFile("XYZ.slnx", "<Solution><Project Path=\"src/App/App.csproj\" /></Solution>")
            .WithSolution("XYZ.sln", "src/App/App.csproj")
            .WithProject("src/App/App.csproj", outputType: "Exe");

        var context = WorkspaceResolver.Resolve(repo.Root);

        Assert.Equal("*.slnx", context.RootMarker);
        Assert.Equal(repo.Path("XYZ.slnx"), context.SolutionPath, ignoreCase: true);
    }

    [Fact]
    public void Multiple_solutions_prefer_the_one_named_after_the_root_directory()
    {
        using var repo = TempRepo.Create("Contoso");
        repo.WithSolution("Aardvark.sln").WithSolution(Path.GetFileName(repo.Root) + ".sln");

        var context = WorkspaceResolver.Resolve(repo.Root);

        Assert.Equal(2, context.AllSolutionPaths.Count);
        Assert.Equal(Path.GetFileName(repo.Root) + ".sln", Path.GetFileName(context.SolutionPath!));
    }

    [Fact]
    public void Multiple_unrelated_solutions_fall_back_to_alphabetical_order()
    {
        using var repo = TempRepo.Create().WithSolution("Zulu.sln").WithSolution("Alpha.sln");

        var context = WorkspaceResolver.Resolve(repo.Root);

        Assert.Equal("Alpha.sln", Path.GetFileName(context.SolutionPath!));
    }

    [Fact]
    public void Handles_paths_containing_spaces_and_non_ascii_characters()
    {
        using var repo = TempRepo.Create("prøve mappe");
        repo.WithSolution("Prøve Løsning.sln").WithProject("src/Æøå App/Æøå App.csproj", outputType: "Exe");

        var context = WorkspaceResolver.Resolve(repo.Path("src/Æøå App"));

        Assert.Equal(repo.Root, context.RepositoryRoot, ignoreCase: true);
        Assert.Equal("Prøve Løsning.sln", Path.GetFileName(context.SolutionPath!));
    }
}
