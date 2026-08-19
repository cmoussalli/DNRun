using DNRun.Cli;

namespace DNRun.Tests;

public sealed class ParsedArgsTests
{
    [Fact]
    public void No_arguments_means_run()
    {
        var parsed = ParsedArgs.Parse([]);

        Assert.Equal(Verb.Run, parsed.Verb);
        Assert.Empty(parsed.Forwarded);
        Assert.Null(parsed.Error);
    }

    [Theory]
    [InlineData("select", nameof(Verb.Select))]
    [InlineData("list", nameof(Verb.List))]
    [InlineData("ls", nameof(Verb.List))]
    [InlineData("config", nameof(Verb.Config))]
    [InlineData("reset", nameof(Verb.Reset))]
    [InlineData("help", nameof(Verb.Help))]
    [InlineData("--help", nameof(Verb.Help))]
    [InlineData("-h", nameof(Verb.Help))]
    [InlineData("version", nameof(Verb.Version))]
    [InlineData("--version", nameof(Verb.Version))]
    [InlineData("SELECT", nameof(Verb.Select))]
    public void Recognizes_the_verbs(string argument, string expected)
    {
        Assert.Equal(Enum.Parse<Verb>(expected), ParsedArgs.Parse([argument]).Verb);
    }

    [Fact]
    public void Everything_after_the_separator_is_forwarded()
    {
        var parsed = ParsedArgs.Parse(["--", "--urls", "http://localhost:5005"]);

        Assert.Equal(Verb.Run, parsed.Verb);
        Assert.Equal(["--urls", "http://localhost:5005"], parsed.Forwarded);
    }

    [Fact]
    public void A_verb_can_also_forward_arguments()
    {
        var parsed = ParsedArgs.Parse(["select", "--", "--verbose"]);

        Assert.Equal(Verb.Select, parsed.Verb);
        Assert.Equal(["--verbose"], parsed.Forwarded);
    }

    [Fact]
    public void An_unknown_command_is_an_error_rather_than_a_silent_run()
    {
        var parsed = ParsedArgs.Parse(["web"]);

        Assert.NotNull(parsed.Error);
        Assert.Contains("unknown command", parsed.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unknown_option_is_reported_as_an_option()
    {
        var parsed = ParsedArgs.Parse(["--watch"]);

        Assert.NotNull(parsed.Error);
        Assert.Contains("unknown option", parsed.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public void Stray_arguments_after_a_verb_are_rejected()
    {
        var parsed = ParsedArgs.Parse(["list", "everything"]);

        Assert.NotNull(parsed.Error);
    }
}
