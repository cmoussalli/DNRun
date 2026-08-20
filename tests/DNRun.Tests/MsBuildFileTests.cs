using System.Text;
using DNRun.Packaging;
using DNRun.Tests.Fixtures;

namespace DNRun.Tests;

public sealed class MsBuildFileTests
{
    /// <summary>
    /// Multi-line comparisons below are written with LF, so the line endings of this source file
    /// must not decide whether they pass.
    /// </summary>
    private static string Lf(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static MsBuildFile Load(TempRepo repo, string relativePath)
    {
        Assert.True(MsBuildFile.TryLoad(repo.Path(relativePath), out var file, out var error), error);
        return file!;
    }

    [Fact]
    public void Reads_a_property_from_a_property_group()
    {
        using var repo = TempRepo.Create().WithFile("A/A.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <Version>1.2.3</Version>
              </PropertyGroup>
            </Project>
            """);

        Assert.Equal("1.2.3", Load(repo, "A/A.csproj").Read("Version"));
    }

    [Fact]
    public void A_version_element_inside_a_package_reference_is_not_a_project_version()
    {
        using var repo = TempRepo.Create().WithFile("A/A.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Newtonsoft.Json">
                  <Version>13.0.3</Version>
                </PackageReference>
              </ItemGroup>
            </Project>
            """);

        var file = Load(repo, "A/A.csproj");

        Assert.Null(file.Read("Version"));
        Assert.Empty(file.Find("Version"));
    }

    [Fact]
    public void Setting_a_version_leaves_a_package_reference_alone()
    {
        using var repo = TempRepo.Create().WithFile("A/A.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <Version>1.0.0</Version>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Newtonsoft.Json">
                  <Version>13.0.3</Version>
                </PackageReference>
              </ItemGroup>
            </Project>
            """);

        var file = Load(repo, "A/A.csproj");
        Assert.True(file.TrySet("Version", "2.0.0"));

        Assert.Contains("<Version>2.0.0</Version>", file.Text, StringComparison.Ordinal);
        Assert.Contains("<Version>13.0.3</Version>", file.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_last_unconditional_declaration_is_the_one_rewritten()
    {
        using var repo = TempRepo.Create().WithFile("A/A.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <Version>1.0.0</Version>
              </PropertyGroup>
              <PropertyGroup Condition="'$(CI)' == 'true'">
                <Version>9.9.9</Version>
              </PropertyGroup>
            </Project>
            """);

        var file = Load(repo, "A/A.csproj");
        Assert.Equal("1.0.0", file.Read("Version"));

        file.TrySet("Version", "2.0.0");

        Assert.Contains("<Version>2.0.0</Version>", file.Text, StringComparison.Ordinal);
        Assert.Contains("<Version>9.9.9</Version>", file.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Comments_indentation_and_entities_survive_an_edit()
    {
        const string original = """
            <Project Sdk="Microsoft.NET.Sdk">

              <PropertyGroup>
                <!-- keep these in step -->
                <Version>1.0.3</Version>
                <Description>Bits &amp; pieces</Description>
              </PropertyGroup>

            </Project>
            """;

        using var repo = TempRepo.Create().WithFile("A/A.csproj", original);
        var file = Load(repo, "A/A.csproj");

        file.TrySet("Version", "1.0.4");

        Assert.Equal(original.Replace("1.0.3", "1.0.4", StringComparison.Ordinal), file.Text);
    }

    [Fact]
    public void A_self_closing_property_becomes_a_normal_one()
    {
        using var repo = TempRepo.Create().WithFile("A/A.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <VersionSuffix />
              </PropertyGroup>
            </Project>
            """);

        var file = Load(repo, "A/A.csproj");
        Assert.True(file.TrySet("VersionSuffix", "beta.1"));

        Assert.Contains("<VersionSuffix>beta.1</VersionSuffix>", file.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_property_is_inserted_into_the_first_unconditional_group()
    {
        using var repo = TempRepo.Create().WithFile("A/A.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup Condition="'$(CI)' == 'true'">
                <ContinuousIntegrationBuild>true</ContinuousIntegrationBuild>
              </PropertyGroup>
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);

        var file = Load(repo, "A/A.csproj");
        Assert.True(file.SetOrInsert("Version", "1.2.14"));

        Assert.Contains("    <TargetFramework>net10.0</TargetFramework>\n    <Version>1.2.14</Version>", Lf(file.Text), StringComparison.Ordinal);
    }

    [Fact]
    public void A_project_with_no_property_group_gets_one()
    {
        using var repo = TempRepo.Create().WithFile("A/A.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <Compile Remove="Generated.cs" />
              </ItemGroup>
            </Project>
            """);

        var file = Load(repo, "A/A.csproj");
        Assert.True(file.SetOrInsert("Version", "1.2.14"));

        Assert.Contains("<PropertyGroup>", file.Text, StringComparison.Ordinal);
        Assert.Contains("<Version>1.2.14</Version>", file.Text, StringComparison.Ordinal);
        Assert.True(file.IsWellFormed);
    }

    [Fact]
    public void An_inserted_property_follows_the_indentation_already_in_use()
    {
        using var repo = TempRepo.Create().WithFile("A/A.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\">\n\t<PropertyGroup>\n\t\t<TargetFramework>net10.0</TargetFramework>\n\t</PropertyGroup>\n</Project>\n");

        var file = Load(repo, "A/A.csproj");
        file.SetOrInsert("Version", "1.2.14");

        Assert.Contains("\t\t<Version>1.2.14</Version>", file.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void An_inserted_property_uses_the_line_ending_of_the_file()
    {
        using var repo = TempRepo.Create().WithFile(
            "A/A.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\">\r\n  <PropertyGroup>\r\n    <TargetFramework>net10.0</TargetFramework>\r\n  </PropertyGroup>\r\n</Project>\r\n");

        var file = Load(repo, "A/A.csproj");
        file.SetOrInsert("Version", "1.2.14");

        Assert.Contains("    <Version>1.2.14</Version>\r\n", file.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("<Version>1.2.14</Version>\n  </PropertyGroup>", file.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Inserting_after_an_anchor_keeps_the_pair_together()
    {
        using var repo = TempRepo.Create().WithFile("A/A.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <VersionPrefix>1.2.14</VersionPrefix>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);

        var file = Load(repo, "A/A.csproj");
        Assert.True(file.TryInsertAfter("VersionPrefix", "VersionSuffix", "beta.1"));

        Assert.Contains(
            "    <VersionPrefix>1.2.14</VersionPrefix>\n    <VersionSuffix>beta.1</VersionSuffix>",
            Lf(file.Text),
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_value_with_markup_characters_is_escaped()
    {
        using var repo = TempRepo.Create().WithFile("A/A.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <Description>plain</Description>
              </PropertyGroup>
            </Project>
            """);

        var file = Load(repo, "A/A.csproj");
        file.TrySet("Description", "a < b & c");

        Assert.Contains("<Description>a &lt; b &amp; c</Description>", file.Text, StringComparison.Ordinal);
        Assert.True(file.IsWellFormed);
    }

    [Fact]
    public void Malformed_xml_yields_no_properties_rather_than_an_exception()
    {
        using var repo = TempRepo.Create().WithFile("A/A.csproj", "<Project><PropertyGroup><Version>1.0.0</Project>");

        var file = Load(repo, "A/A.csproj");

        Assert.False(file.IsWellFormed);
        Assert.Null(file.Read("Version"));
    }

    [Fact]
    public void Saving_preserves_a_byte_order_mark()
    {
        using var repo = TempRepo.Create();
        var path = repo.Path("A/A.csproj");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);

        File.WriteAllText(
            path,
            "<Project>\n  <PropertyGroup>\n    <Version>1.0.0</Version>\n  </PropertyGroup>\n</Project>\n",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        var file = Load(repo, "A/A.csproj");
        file.TrySet("Version", "2.0.0");
        Assert.True(file.Save(out var error), error);

        var bytes = File.ReadAllBytes(path);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
        Assert.Contains("<Version>2.0.0</Version>", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Fact]
    public void Saving_leaves_no_temp_file_behind()
    {
        using var repo = TempRepo.Create().WithFile("A/A.csproj", """
            <Project>
              <PropertyGroup>
                <Version>1.0.0</Version>
              </PropertyGroup>
            </Project>
            """);

        var file = Load(repo, "A/A.csproj");
        file.TrySet("Version", "2.0.0");
        Assert.True(file.Save(out _));

        Assert.Equal(["A.csproj"], Directory.GetFiles(repo.Path("A")).Select(f => System.IO.Path.GetFileName(f)!).ToArray());
    }
}
