using System.Xml.Linq;
using DNRun.Model;

namespace DNRun.Discovery;

/// <summary>
/// Decides whether a .csproj is a runnable application and classifies it (spec §5, plan §4.4).
///
/// Known limitation, documented rather than solved: properties inherited from
/// Directory.Build.props are not evaluated — that would require MSBuild evaluation and with it
/// a dependency DNRun deliberately does not take. The escape hatch is the
/// <c>runnableProjects</c> allowlist in dnrun.config.json.
/// </summary>
internal static class ProjectAnalyzer
{
    private static readonly string[] TestPackagePrefixes =
        ["Microsoft.NET.Test.Sdk", "xunit", "nunit", "mstest", "Microsoft.Testing.Platform"];

    private static readonly string[] TestNameSuffixes =
        [".Tests", ".Test", ".IntegrationTests", ".UnitTests", ".FunctionalTests", ".AcceptanceTests"];

    public static ProjectInfo Analyze(string csprojPath, string repositoryRoot)
    {
        var absolute = PathUtils.Normalize(csprojPath);
        var name = Path.GetFileNameWithoutExtension(absolute);
        var relative = PathUtils.ToRepositoryRelative(repositoryRoot, absolute);

        XDocument document;
        try
        {
            document = XDocument.Load(absolute, LoadOptions.None);
        }
        catch (Exception ex) when (ex is System.Xml.XmlException or IOException or UnauthorizedAccessException)
        {
            // A project we cannot parse is reported, never crashed on, and never offered as runnable.
            return new ProjectInfo(name, absolute, relative, IsRunnable: false, ProjectKind.Unknown, null, null)
            {
                AnalysisWarning = "could not be parsed (" + FirstLine(ex.Message) + ")",
                IsPackable = false,
            };
        }

        var root = document.Root;
        if (root is null)
        {
            return new ProjectInfo(name, absolute, relative, IsRunnable: false, ProjectKind.Unknown, null, null)
            {
                AnalysisWarning = "is empty",
                IsPackable = false,
            };
        }

        var sdk = ReadSdk(root);
        var outputType = LastPropertyValue(root, "OutputType");
        var isTestProperty = ReadBool(LastPropertyValue(root, "IsTestProject"));
        var packageReferences = ReadPackageReferences(root);

        var isTest = isTestProperty == true
            || (sdk is not null && sdk.StartsWith("MSTest.Sdk", StringComparison.OrdinalIgnoreCase))
            || packageReferences.Any(IsTestPackage)
            || TestNameSuffixes.Any(s => name.EndsWith(s, StringComparison.OrdinalIgnoreCase));

        var kind = Classify(name, sdk, outputType, isTest);
        var runnable = !isTest && IsRunnableProject(sdk, outputType, packageReferences);

        // Packaging facts (spec: dnuget). `dotnet pack` happily packs anything that does not opt
        // out, so "packable" is the absence of a refusal, while "packages explicitly" is the
        // stronger signal used to decide which projects to offer first.
        var isPackableProperty = ReadBool(LastPropertyValue(root, "IsPackable"));
        var packageId = LastPropertyValue(root, "PackageId");
        var packable = isPackableProperty ?? !isTest;
        var explicitlyPackages = packable
            && (isPackableProperty == true
                || packageId is not null
                || ReadBool(LastPropertyValue(root, "GeneratePackageOnBuild")) == true
                || ReadBool(LastPropertyValue(root, "PackAsTool")) == true);

        var version = LastPropertyValue(root, "PackageVersion")
            ?? LastPropertyValue(root, "Version")
            ?? Packaging.NuGetVersion.Combine(
                LastPropertyValue(root, "VersionPrefix"),
                LastPropertyValue(root, "VersionSuffix"));

        return new ProjectInfo(name, absolute, relative, runnable, kind, sdk, outputType)
        {
            IsPackable = packable,
            PackagesExplicitly = explicitlyPackages,
            PackageId = packageId,
            DeclaredVersion = version,
        };
    }

    private static bool IsRunnableProject(string? sdk, string? outputType, IReadOnlyList<string> packageReferences)
    {
        // An explicit OutputType is the strongest signal in either direction.
        if (Is(outputType, "Exe") || Is(outputType, "WinExe"))
        {
            return true;
        }

        if (Is(outputType, "Library") || Is(outputType, "Module"))
        {
            return false;
        }

        // A Razor class library is a library despite living under a web-flavoured SDK.
        if (Is(sdk, "Microsoft.NET.Sdk.Razor"))
        {
            return false;
        }

        // Web, Worker, and standalone Blazor WebAssembly apps default to an executable output
        // without ever declaring OutputType.
        if (Is(sdk, "Microsoft.NET.Sdk.Web")
            || Is(sdk, "Microsoft.NET.Sdk.Worker")
            || Is(sdk, "Microsoft.NET.Sdk.BlazorWebAssembly"))
        {
            return true;
        }

        // Legacy web host: plain SDK, no OutputType, but hosting ASP.NET Core.
        if (Is(sdk, "Microsoft.NET.Sdk")
            && outputType is null
            && packageReferences.Any(p => p.StartsWith("Microsoft.AspNetCore", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return false;
    }

    private static ProjectKind Classify(string name, string? sdk, string? outputType, bool isTest)
    {
        if (isTest)
        {
            return ProjectKind.Test;
        }

        var lastSegment = name.Contains('.') ? name[(name.LastIndexOf('.') + 1)..] : name;
        var fromName = lastSegment.ToLowerInvariant() switch
        {
            "web" or "webapp" or "site" or "blazor" => ProjectKind.Web,
            "api" or "apis" or "webapi" or "rest" => ProjectKind.Api,
            "windows" or "desktop" or "wpf" or "winforms" or "winui" => ProjectKind.Windows,
            "mobile" or "maui" or "android" or "ios" => ProjectKind.Mobile,
            "worker" or "service" or "services" or "jobs" or "daemon" => ProjectKind.Worker,
            "cli" or "console" or "tool" or "tools" => ProjectKind.Console,
            _ => ProjectKind.Unknown,
        };

        if (fromName != ProjectKind.Unknown)
        {
            return fromName;
        }

        if (Is(sdk, "Microsoft.NET.Sdk.Web") || Is(sdk, "Microsoft.NET.Sdk.BlazorWebAssembly"))
        {
            return ProjectKind.Web;
        }

        if (Is(sdk, "Microsoft.NET.Sdk.Worker"))
        {
            return ProjectKind.Worker;
        }

        if (Is(outputType, "WinExe"))
        {
            return ProjectKind.Windows;
        }

        if (Is(outputType, "Exe"))
        {
            return ProjectKind.Console;
        }

        if (Is(outputType, "Library") || Is(sdk, "Microsoft.NET.Sdk.Razor"))
        {
            return ProjectKind.Library;
        }

        return ProjectKind.Unknown;
    }

    private static string? ReadSdk(XElement root)
    {
        var attribute = root.Attribute("Sdk")?.Value;
        if (!string.IsNullOrWhiteSpace(attribute))
        {
            // "Microsoft.NET.Sdk.Web/8.0.0" — the version suffix is not part of the identity.
            return attribute.Split('/')[0].Trim();
        }

        var element = Descendants(root, "Sdk").FirstOrDefault()?.Attribute("Name")?.Value;
        return string.IsNullOrWhiteSpace(element) ? null : element.Trim();
    }

    /// <summary>Last declaration wins, mirroring MSBuild's evaluation of repeated properties.</summary>
    private static string? LastPropertyValue(XElement root, string propertyName)
    {
        string? value = null;
        foreach (var group in Descendants(root, "PropertyGroup"))
        {
            foreach (var property in Descendants(group, propertyName))
            {
                var text = property.Value.Trim();
                if (text.Length > 0)
                {
                    value = text;
                }
            }
        }

        return value;
    }

    private static IReadOnlyList<string> ReadPackageReferences(XElement root) =>
        Descendants(root, "PackageReference")
            .Select(e => e.Attribute("Include")?.Value ?? e.Attribute("Update")?.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!.Trim())
            .ToArray();

    private static bool IsTestPackage(string package) =>
        TestPackagePrefixes.Any(p => package.StartsWith(p, StringComparison.OrdinalIgnoreCase));

    /// <summary>Matches by local name so legacy projects carrying the 2003 MSBuild namespace still parse.</summary>
    private static IEnumerable<XElement> Descendants(XElement root, string localName) =>
        root.Descendants().Where(e => string.Equals(e.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase));

    private static bool Is(string? value, string expected) =>
        value is not null && string.Equals(value.Trim(), expected, StringComparison.OrdinalIgnoreCase);

    private static bool? ReadBool(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "true" => true,
        "false" => false,
        _ => null,
    };

    private static string FirstLine(string text)
    {
        var index = text.IndexOfAny(['\r', '\n']);
        return (index < 0 ? text : text[..index]).Trim();
    }
}
