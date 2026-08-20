using DNRun.Packaging;

namespace DNRun.Tests;

public sealed class NuGetVersionTests
{
    [Theory]
    [InlineData("1.2.14")]
    [InlineData("1.2")]
    [InlineData("1.2.14.3")]
    [InlineData("0.0.1")]
    [InlineData("1.3.0-beta.1")]
    [InlineData("2.0.0-rc-2")]
    [InlineData("2.0.0-rc.2+build.57")]
    [InlineData("10.20.30")]
    public void Accepts_the_versions_nuget_accepts(string text)
    {
        Assert.True(NuGetVersion.TryParse(text, out var version, out var error));
        Assert.Null(error);
        Assert.Equal(text, version!.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1")]
    [InlineData("1.2.x")]
    [InlineData("1.2.14-")]
    [InlineData("1.2.14.3.4")]
    [InlineData("next")]
    [InlineData("1.2.14 beta")]
    [InlineData("-1.2.14")]
    public void Rejects_what_would_not_restore(string text)
    {
        Assert.False(NuGetVersion.TryParse(text, out var version, out var error));
        Assert.Null(version);
        Assert.NotNull(error);
    }

    [Fact]
    public void Null_is_rejected_rather_than_thrown_on()
    {
        Assert.False(NuGetVersion.TryParse(null, out _, out var error));
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData("v1.2.14", "1.2.14")]
    [InlineData("V2.0.0-beta.1", "2.0.0-beta.1")]
    [InlineData("  1.2.14  ", "1.2.14")]
    public void A_leading_v_and_surrounding_space_are_forgiven(string text, string expected)
    {
        Assert.True(NuGetVersion.TryParse(text, out var version, out _));
        Assert.Equal(expected, version!.ToString());
    }

    [Fact]
    public void The_prerelease_label_and_metadata_are_kept_apart()
    {
        Assert.True(NuGetVersion.TryParse("2.0.0-rc.2+build.57", out var version, out _));

        Assert.Equal("2.0.0", version!.Prefix);
        Assert.Equal("rc.2", version.Suffix);
        Assert.Equal("build.57", version.Metadata);
        Assert.Equal("2.0.0-rc.2", version.WithoutMetadata);
    }

    [Theory]
    [InlineData("1.2.14", "1.2.14.0")]
    [InlineData("1.2", "1.2.0.0")]
    [InlineData("1.2.14.3", "1.2.14.3")]
    [InlineData("2.0.0-beta.1", "2.0.0.0")]
    public void Assembly_versions_drop_the_label_and_are_zero_filled(string text, string expected)
    {
        Assert.True(NuGetVersion.TryParse(text, out var version, out _));
        Assert.Equal(expected, version!.ToAssemblyVersion());
    }

    [Theory]
    [InlineData("1.2.14", null, "1.2.14")]
    [InlineData("1.2.14", "beta.1", "1.2.14-beta.1")]
    [InlineData("1.2.14", "  ", "1.2.14")]
    [InlineData(null, "beta.1", null)]
    public void Combines_a_split_prefix_and_suffix(string? prefix, string? suffix, string? expected)
    {
        Assert.Equal(expected, NuGetVersion.Combine(prefix, suffix));
    }
}
