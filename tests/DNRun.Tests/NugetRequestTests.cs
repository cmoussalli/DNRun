using DNRun.Cli;

namespace DNRun.Tests;

public sealed class NugetRequestTests
{
    [Fact]
    public void No_arguments_shows_the_current_version()
    {
        var request = NugetRequest.Parse([]);

        Assert.Equal(NugetAction.Show, request.Action);
        Assert.Null(request.Version);
        Assert.Null(request.Error);
    }

    [Fact]
    public void A_bare_version_sets_it()
    {
        var request = NugetRequest.Parse(["1.2.14"]);

        Assert.Equal(NugetAction.Set, request.Action);
        Assert.Equal("1.2.14", request.Version!.ToString());
        Assert.False(request.ForceSelection);
        Assert.False(request.AllProjects);
    }

    [Theory]
    [InlineData("list")]
    [InlineData("ls")]
    [InlineData("--list")]
    public void Listing_is_recognized(string argument) =>
        Assert.Equal(NugetAction.List, NugetRequest.Parse([argument]).Action);

    [Theory]
    [InlineData("reset")]
    [InlineData("--reset")]
    public void Resetting_is_recognized(string argument) =>
        Assert.Equal(NugetAction.Reset, NugetRequest.Parse([argument]).Action);

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    [InlineData("help")]
    [InlineData("/?")]
    public void Help_is_recognized(string argument) =>
        Assert.Equal(NugetAction.Help, NugetRequest.Parse([argument]).Action);

    [Theory]
    [InlineData("select")]
    [InlineData("--select")]
    [InlineData("-s")]
    public void Selection_can_be_forced_alongside_a_version(string argument)
    {
        var request = NugetRequest.Parse([argument, "1.2.14"]);

        Assert.Equal(NugetAction.Set, request.Action);
        Assert.True(request.ForceSelection);
        Assert.Equal("1.2.14", request.Version!.ToString());
    }

    [Fact]
    public void Selection_without_a_version_still_shows_the_result()
    {
        var request = NugetRequest.Parse(["select"]);

        Assert.Equal(NugetAction.Show, request.Action);
        Assert.True(request.ForceSelection);
    }

    [Fact]
    public void Every_project_can_be_versioned_at_once()
    {
        var request = NugetRequest.Parse(["--all", "1.2.14"]);

        Assert.Equal(NugetAction.Set, request.Action);
        Assert.True(request.AllProjects);
    }

    [Fact]
    public void All_without_a_version_is_a_listing()
    {
        var request = NugetRequest.Parse(["--all"]);

        Assert.Equal(NugetAction.List, request.Action);
    }

    [Fact]
    public void The_order_of_the_version_and_the_options_does_not_matter()
    {
        var request = NugetRequest.Parse(["1.2.14", "--all"]);

        Assert.Equal(NugetAction.Set, request.Action);
        Assert.True(request.AllProjects);
    }

    [Fact]
    public void An_invalid_version_is_refused_before_anything_is_opened()
    {
        var request = NugetRequest.Parse(["1.2.x"]);

        Assert.Equal(NugetAction.Help, request.Action);
        Assert.NotNull(request.Error);
        Assert.Contains("1.2.x", request.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_versions_are_refused_rather_than_one_being_picked()
    {
        Assert.NotNull(NugetRequest.Parse(["1.2.14", "1.2.15"]).Error);
    }

    [Fact]
    public void A_version_cannot_be_combined_with_a_listing()
    {
        Assert.NotNull(NugetRequest.Parse(["list", "1.2.14"]).Error);
    }

    [Fact]
    public void An_unknown_option_is_refused()
    {
        var request = NugetRequest.Parse(["--force"]);

        Assert.NotNull(request.Error);
        Assert.Contains("--force", request.Error!, StringComparison.Ordinal);
    }
}
