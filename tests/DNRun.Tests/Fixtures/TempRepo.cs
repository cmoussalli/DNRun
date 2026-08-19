using System.Text;

namespace DNRun.Tests.Fixtures;

/// <summary>
/// Materializes a throwaway repository tree under the test temp directory and deletes it on
/// dispose. Every discovery test is written against a real filesystem because that is exactly
/// where the risk in this application lives.
/// </summary>
internal sealed class TempRepo : IDisposable
{
    private TempRepo(string root) => Root = root;

    public string Root { get; }

    public static TempRepo Create(string? name = null)
    {
        var root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "dnrun-tests",
            (name ?? "repo") + "-" + Guid.NewGuid().ToString("N")[..8]);

        Directory.CreateDirectory(root);
        return new TempRepo(root);
    }

    public string Path(string relativePath) =>
        System.IO.Path.GetFullPath(System.IO.Path.Combine(Root, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar)));

    public TempRepo WithDirectory(string relativePath)
    {
        Directory.CreateDirectory(Path(relativePath));
        return this;
    }

    public TempRepo WithFile(string relativePath, string content)
    {
        var full = Path(relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return this;
    }

    /// <summary>A .csproj with the given SDK, OutputType, and package references.</summary>
    public TempRepo WithProject(
        string relativePath,
        string sdk = "Microsoft.NET.Sdk",
        string? outputType = null,
        string[]? packages = null,
        string? extraProperties = null)
    {
        var xml = new StringBuilder();
        xml.AppendLine($"<Project Sdk=\"{sdk}\">");
        xml.AppendLine("  <PropertyGroup>");
        xml.AppendLine("    <TargetFramework>net10.0</TargetFramework>");

        if (outputType is not null)
        {
            xml.AppendLine($"    <OutputType>{outputType}</OutputType>");
        }

        if (extraProperties is not null)
        {
            xml.AppendLine("    " + extraProperties);
        }

        xml.AppendLine("  </PropertyGroup>");

        if (packages is { Length: > 0 })
        {
            xml.AppendLine("  <ItemGroup>");
            foreach (var package in packages)
            {
                xml.AppendLine($"    <PackageReference Include=\"{package}\" Version=\"1.0.0\" />");
            }

            xml.AppendLine("  </ItemGroup>");
        }

        xml.AppendLine("</Project>");
        return WithFile(relativePath, xml.ToString());
    }

    /// <summary>A .sln listing the given repository-relative project paths.</summary>
    public TempRepo WithSolution(string relativePath, params string[] projectRelativePaths)
    {
        var sln = new StringBuilder();
        sln.AppendLine("Microsoft Visual Studio Solution File, Format Version 12.00");

        foreach (var project in projectRelativePaths)
        {
            var name = System.IO.Path.GetFileNameWithoutExtension(project);
            var windowsPath = project.Replace('/', '\\');
            sln.AppendLine(
                $"Project(\"{{9A19103F-16F7-4668-BE54-9A1E7A4F7556}}\") = \"{name}\", \"{windowsPath}\", \"{{{Guid.NewGuid()}}}\"");
            sln.AppendLine("EndProject");
        }

        return WithFile(relativePath, sln.ToString());
    }

    public TempRepo WithSolutionFolder(string relativePath, string folderName)
    {
        var sln = new StringBuilder();
        sln.AppendLine("Microsoft Visual Studio Solution File, Format Version 12.00");
        sln.AppendLine(
            $"Project(\"{{2150E333-8FDC-42A3-9474-1A3956D46DE8}}\") = \"{folderName}\", \"{folderName}\", \"{{{Guid.NewGuid()}}}\"");
        sln.AppendLine("EndProject");
        return WithFile(relativePath, sln.ToString());
    }

    public TempRepo WithConfig(string json) => WithFile("dnrun.config.json", json);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover temp tree is not worth failing a test over.
        }
    }
}
