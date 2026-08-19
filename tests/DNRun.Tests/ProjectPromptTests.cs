using DNRun.Discovery;
using DNRun.Model;
using DNRun.Presentation;
using DNRun.Tests.Fixtures;

namespace DNRun.Tests;

public sealed class ProjectPromptTests
{
    private sealed class ScriptedInput(params string?[] answers) : IPromptInput
    {
        private int _index;

        public bool IsInteractive => true;

        public int Reads { get; private set; }

        public string? ReadLine()
        {
            Reads++;
            return _index < answers.Length ? answers[_index++] : null;
        }
    }

    private sealed class RedirectedInput : IPromptInput
    {
        public bool IsInteractive => false;

        public string? ReadLine() => throw new InvalidOperationException("must never read without a terminal");
    }

    private static IReadOnlyList<ProjectInfo> Candidates(TempRepo repo) =>
        ProjectDiscovery.Discover(WorkspaceResolver.Resolve(repo.Root), null).RunnableProjects;

    private static TempRepo ThreeProjects() => TempRepo.Create()
        .WithSolution("XYZ.sln")
        .WithProject("src/XYZ.Web/XYZ.Web.csproj", sdk: "Microsoft.NET.Sdk.Web")
        .WithProject("src/XYZ.API/XYZ.API.csproj", sdk: "Microsoft.NET.Sdk.Web")
        .WithProject("src/XYZ.Windows/XYZ.Windows.csproj", outputType: "WinExe");

    private static PromptResult Select(TempRepo repo, IPromptInput input) =>
        ProjectPrompt.Select(Candidates(repo), "Select the project to run:", input);

    [Fact]
    public void An_index_selects_the_matching_entry()
    {
        using var repo = ThreeProjects();

        var result = Select(repo, new ScriptedInput("2"));

        Assert.Equal(PromptOutcome.Selected, result.Outcome);
        Assert.Equal("XYZ.API", result.Project!.Name);
    }

    [Fact]
    public void Bare_enter_takes_the_first_entry()
    {
        using var repo = ThreeProjects();

        var result = Select(repo, new ScriptedInput(""));

        Assert.Equal("XYZ.Web", result.Project!.Name);
    }

    [Fact]
    public void A_name_suffix_is_enough_when_it_is_unambiguous()
    {
        using var repo = ThreeProjects();

        Assert.Equal("XYZ.Windows", Select(repo, new ScriptedInput("windows")).Project!.Name);
        Assert.Equal("XYZ.API", Select(repo, new ScriptedInput("api")).Project!.Name);
    }

    [Fact]
    public void An_out_of_range_index_re_prompts_rather_than_failing()
    {
        using var repo = ThreeProjects();
        var input = new ScriptedInput("9", "1");

        var result = Select(repo, input);

        Assert.Equal("XYZ.Web", result.Project!.Name);
        Assert.Equal(2, input.Reads);
    }

    [Fact]
    public void An_ambiguous_answer_re_prompts()
    {
        using var repo = ThreeProjects();
        var input = new ScriptedInput("XYZ", "3");

        var result = Select(repo, input);

        Assert.Equal("XYZ.Windows", result.Project!.Name);
        Assert.Equal(2, input.Reads);
    }

    [Fact]
    public void Three_invalid_answers_abandon_the_prompt()
    {
        using var repo = ThreeProjects();
        var input = new ScriptedInput("nope", "still-nope", "nope-again", "1");

        var result = Select(repo, input);

        Assert.Equal(PromptOutcome.Abandoned, result.Outcome);
        Assert.Equal(3, input.Reads);
    }

    [Fact]
    public void End_of_input_abandons_the_prompt()
    {
        using var repo = ThreeProjects();

        Assert.Equal(PromptOutcome.Abandoned, Select(repo, new ScriptedInput([null])).Outcome);
    }

    [Theory]
    [InlineData("q")]
    [InlineData("quit")]
    [InlineData("exit")]
    public void Quitting_abandons_the_prompt(string answer)
    {
        using var repo = ThreeProjects();

        Assert.Equal(PromptOutcome.Abandoned, Select(repo, new ScriptedInput(answer)).Outcome);
    }

    [Fact]
    public void A_single_candidate_needs_no_answer_at_all()
    {
        using var repo = TempRepo.Create()
            .WithSolution("XYZ.sln")
            .WithProject("src/XYZ.Web/XYZ.Web.csproj", sdk: "Microsoft.NET.Sdk.Web");

        var input = new ScriptedInput();
        var result = Select(repo, input);

        Assert.Equal(PromptOutcome.Selected, result.Outcome);
        Assert.Equal(0, input.Reads);
    }

    [Fact]
    public void Without_a_terminal_the_prompt_never_reads()
    {
        using var repo = ThreeProjects();

        var result = Select(repo, new RedirectedInput());

        Assert.Equal(PromptOutcome.NonInteractive, result.Outcome);
        Assert.Null(result.Project);
    }
}
