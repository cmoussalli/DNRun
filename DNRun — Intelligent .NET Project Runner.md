# DNRun — Intelligent .NET Project Runner

## 1. Project Overview

**DNRun** is a lightweight Windows command-line application designed to simplify running .NET projects, especially when working across multiple repositories and multi-project solutions in **Orca IDE**.

The application is installed once and made globally accessible through the Windows `PATH` environment variable. This allows the user to execute the `dnrun` command from any project directory without copying the executable or any DNRun-related files into each project.

DNRun automatically detects the current project context based on the process **current working directory**, scans the repository for .NET projects, determines which project should be executed, and runs it using the appropriate .NET command.

The primary objective is to replace repeatedly configured commands such as:

```text
dotnet run --project ./src/XYZ.Web/XYZ.Web.csproj
```

with a single reusable command:

```text
dnrun
```

---

# 2. Problem Statement

In Orca IDE, each project may require a manually configured run command. For a typical .NET solution, this can require specifying a command similar to:

```text
dotnet run --project path/to/project.csproj
```

For a multi-project repository, the user may have to configure a different command for each workspace.

Example repository:

```text
XYZ/
│
├── XYZ.sln
│
└── src/
    ├── XYZ.Web/
    │   └── XYZ.Web.csproj
    │
    ├── XYZ.API/
    │   └── XYZ.API.csproj
    │
    ├── XYZ.Windows/
    │   └── XYZ.Windows.csproj
    │
    └── XYZ.Domain/
        └── XYZ.Domain.csproj
```

The project that should be executed may change depending on the solution. Additionally, the executable project is not always located directly in the repository root.

DNRun solves this problem by automatically discovering projects from the current workspace and remembering the user's selected startup project.

---

# 3. Core Concept

DNRun works similarly to globally installed command-line applications such as the `claude` command.

The DNRun executable is located outside the user's projects:

```text
C:\CMouss\DNRun\DNRun.exe
```

Its installation directory is registered in the Windows `PATH` environment variable.

The user can therefore execute:

```text
dnrun
```

from any terminal or from Orca's Run Command configuration.

For example:

```text
C:\Projects\XYZ> dnrun
```

Although `DNRun.exe` is installed elsewhere, it receives the current working directory:

```text
C:\Projects\XYZ
```

DNRun must use this directory as the project discovery starting point.

**DNRun must never assume that the project repository is located beside the DNRun executable.**

---

# 4. Functional Requirements

## 4.1 Current Working Directory Detection

When executed, DNRun must identify the current working directory.

Example:

```text
C:\Projects\XYZ> dnrun
```

DNRun should detect:

```text
Working Directory:
C:\Projects\XYZ
```

This directory becomes the starting point for project discovery.

---

## 4.2 Repository and Solution Discovery

DNRun should search for the .NET solution and project files using a predictable search strategy.

### First Priority: Current Working Directory

DNRun should first inspect the current directory.

Example:

```text
XYZ/
├── XYZ.sln
├── XYZ.Web.csproj
└── XYZ.API.csproj
```

It should detect:

- Solution files: `*.sln`
- Project files: `*.csproj`

---

### Second Priority: `src` Directory

If the expected project structure is not directly available at the repository root, DNRun should search for a `src` directory.

Example:

```text
XYZ/
├── XYZ.sln
│
└── src/
    ├── XYZ.Web/
    │   └── XYZ.Web.csproj
    │
    ├── XYZ.API/
    │   └── XYZ.API.csproj
    │
    └── XYZ.Domain/
        └── XYZ.Domain.csproj
```

DNRun should recursively discover project files within:

```text
./src
```

The preferred scanning strategy is therefore:

```text
1. Current working directory
2. Current working directory/src
```

The `src` directory should support nested project structures.

---

## 4.3 Solution File Awareness

When a `.sln` file is available, DNRun should use the solution as contextual information.

Example:

```text
XYZ.sln
```

DNRun may use the solution to identify which `.csproj` files belong to the repository.

However, DNRun should not blindly attempt to execute every project in the solution because many projects are class libraries or non-runnable projects.

Examples:

```text
XYZ.Domain.csproj
XYZ.Infrastructure.csproj
XYZ.Application.csproj
XYZ.Web.csproj
XYZ.API.csproj
```

Only projects that can reasonably be executed should be presented as startup candidates.

---

# 5. Project Discovery and Runnable Project Detection

DNRun should recursively scan for `.csproj` files according to the configured search rules.

It should exclude generated and irrelevant directories such as:

```text
bin
obj
node_modules
.git
```

Additional ignored directories may be configurable.

For each `.csproj`, DNRun should inspect its properties to determine whether it is a runnable application.

Potential indicators include:

```xml
<OutputType>Exe</OutputType>
```

or:

```xml
<OutputType>WinExe</OutputType>
```

ASP.NET Core web applications should also be recognized as runnable projects.

DNRun should distinguish between:

### Runnable applications

```text
XYZ.Web.csproj
XYZ.API.csproj
XYZ.Windows.csproj
```

### Class libraries

```text
XYZ.Domain.csproj
XYZ.Application.csproj
XYZ.Infrastructure.csproj
```

Class libraries should normally not be selected as startup projects.

---

# 6. Automatic Project Selection

DNRun should behave differently depending on how many runnable projects are discovered.

## Scenario A — No Runnable Projects

If no runnable projects are found, DNRun should display an informative error.

Example:

```text
No runnable .NET project was found.

Scanned locations:
- C:\Projects\XYZ
- C:\Projects\XYZ\src
```

DNRun should exit with a non-zero exit code.

---

## Scenario B — Exactly One Runnable Project

If exactly one runnable project is found, DNRun should execute it automatically.

Example:

```text
Found runnable project:

XYZ.Web
```

DNRun then runs:

```text
dotnet run --project "C:\Projects\XYZ\src\XYZ.Web\XYZ.Web.csproj"
```

No user interaction is required.

---

## Scenario C — Multiple Runnable Projects

If multiple runnable projects are found and no project has previously been selected, DNRun should display an interactive prompt.

Example:

```text
Multiple runnable projects were found:

[1] XYZ.Web
[2] XYZ.API
[3] XYZ.Windows

Select a project to run:
>
```

After the user selects a project:

```text
> 1
```

DNRun should execute the selected project and save the selection for future executions.

---

# 7. Persistent Startup Project Selection

After the user selects a project, DNRun should create a small configuration file inside the repository or solution root.

Example:

```text
XYZ/
├── XYZ.sln
├── dnrun.config.json
│
└── src/
```

Example configuration:

```json
{
  "startupProject": "src/XYZ.Web/XYZ.Web.csproj"
}
```

The stored path should preferably be relative to the repository root so the repository can be moved to another location without invalidating the configuration.

On subsequent executions:

```text
dnrun
```

DNRun should read the configuration:

```text
startupProject:
src/XYZ.Web/XYZ.Web.csproj
```

and automatically execute that project.

The user should not be prompted again unless:

- The configuration file is missing.
- The configured project no longer exists.
- The configured project is no longer runnable.
- The user explicitly requests a different project.

---

# 8. Command-Line Interface

The primary command is:

```text
dnrun
```

## 8.1 Default Command

```text
dnrun
```

Behavior:

1. Detect current working directory.
2. Locate the repository root.
3. Search for an existing DNRun configuration.
4. Validate the configured startup project.
5. Automatically run the configured project.
6. If no configuration exists, discover runnable projects.
7. Automatically run the only project or prompt the user when multiple projects exist.

---

## 8.2 Select Command

```text
dnrun select
```

This command should force project selection.

Example:

```text
C:\Projects\XYZ> dnrun select

Available runnable projects:

[1] XYZ.Web
[2] XYZ.API
[3] XYZ.Windows

Select the default project:
>
```

The new selection replaces the previously saved startup project.

---

## 8.3 List Command

```text
dnrun list
```

Example output:

```text
Solution:
XYZ.sln

Runnable projects:

[1] XYZ.Web
    src/XYZ.Web/XYZ.Web.csproj

[2] XYZ.API
    src/XYZ.API/XYZ.API.csproj

[3] XYZ.Windows
    src/XYZ.Windows/XYZ.Windows.csproj

Current startup project:
XYZ.Web
```

This command does not start any project.

---

# 9. Project Naming Convention Support

The project discovery system should preserve project names and optionally support semantic classification.

For example:

```text
XYZ.Web.csproj
XYZ.API.csproj
XYZ.Windows.csproj
XYZ.Mobile.csproj
```

DNRun should recognize the naming suffixes:

```text
.Web
.API
.Windows
.Mobile
```

This creates a foundation for future specialized commands such as:

```text
dnrun web
```

```text
dnrun api
```

```text
dnrun windows
```

For example:

```text
dnrun web
```

could prioritize:

```text
*.Web.csproj
```

while:

```text
dnrun api
```

could prioritize:

```text
*.API.csproj
```

The initial implementation should focus on the generic interactive selection mechanism, with semantic project commands treated as an extension.

---

# 10. Orca IDE Integration

DNRun is intended to integrate with Orca through its Run Command configuration.

Instead of configuring a project-specific command such as:

```text
dotnet run --project ./src/XYZ.Web/XYZ.Web.csproj
```

the user should configure:

```text
dnrun
```

Because DNRun is globally accessible through Windows `PATH`, Orca should be able to invoke the command regardless of the current project repository.

The essential requirement is that Orca launches the command with the project workspace as its working directory.

For example:

```text
Orca Workspace:
C:\Projects\XYZ
```

When Orca executes:

```text
dnrun
```

the process must receive:

```text
Current Working Directory:
C:\Projects\XYZ
```

DNRun then performs project discovery relative to that location.

This means the DNRun executable does **not** need to be copied into:

```text
C:\Projects\XYZ
```

or:

```text
C:\Projects\AnotherProject
```

It is installed once and used across all repositories.

---

# 11. Execution Strategy

After determining the target project, DNRun should execute:

```text
dotnet run --project "<project-path>"
```

Example:

```text
dotnet run --project "C:\Projects\XYZ\src\XYZ.Web\XYZ.Web.csproj"
```

The process should preferably inherit:

- Standard input.
- Standard output.
- Standard error.
- Environment variables.
- Current terminal session.

This allows the .NET application to behave as if the user directly executed `dotnet run`.

For example:

```text
C:\Projects\XYZ> dnrun
```

Should effectively behave as:

```text
C:\Projects\XYZ> dotnet run --project "src\XYZ.Web\XYZ.Web.csproj"
```

The application output should remain visible in Orca's terminal or command output.

---

# 12. Suggested Discovery Algorithm

The recommended algorithm is:

```text
START
│
├── Get Current Working Directory
│
├── Locate Repository/Solution Root
│
├── Check for DNRun Configuration
│   │
│   ├── Valid Startup Project Found?
│   │       │
│   │       └── YES → Run Configured Project
│   │
│   └── NO
│
├── Scan Current Root
│   │
│   └── Search for .sln and .csproj files
│
├── Scan ./src
│   │
│   └── Recursively search for .csproj files
│
├── Filter Non-Runnable Projects
│
├── Count Runnable Projects
│   │
│   ├── 0 → Display Error
│   │
│   ├── 1 → Run Automatically
│   │
│   └── More Than 1
│          │
│          ├── Display Interactive Selection
│          │
│          ├── User Selects Project
│          │
│          ├── Save Configuration
│          │
│          └── Run Selected Project
│
END
```

---

# 13. Example User Experience

## First Execution

Repository:

```text
XYZ/
├── XYZ.sln
└── src/
    ├── XYZ.Web/
    │   └── XYZ.Web.csproj
    ├── XYZ.API/
    │   └── XYZ.API.csproj
    └── XYZ.Domain/
        └── XYZ.Domain.csproj
```

Command:

```text
C:\Projects\XYZ> dnrun
```

Output:

```text
DNRun — Intelligent .NET Project Runner

Searching for .NET projects...

Multiple runnable projects found:

[1] XYZ.Web
[2] XYZ.API

Select the project to run:
> 1

Selected:
XYZ.Web

Saving default project...

Starting:
dotnet run --project src/XYZ.Web/XYZ.Web.csproj
```

DNRun creates:

```text
dnrun.config.json
```

with:

```json
{
  "startupProject": "src/XYZ.Web/XYZ.Web.csproj"
}
```

---

## Subsequent Execution

Command:

```text
C:\Projects\XYZ> dnrun
```

Output:

```text
DNRun — Intelligent .NET Project Runner

Startup project:
XYZ.Web

Starting application...
```

The application runs immediately without requiring user interaction.

---

# 14. Technical Architecture

The application should consist of the following logical components.

## 14.1 Command Parser

Responsible for parsing commands such as:

```text
dnrun
dnrun select
dnrun list
dnrun web
dnrun api
```

---

## 14.2 Working Directory Resolver

Responsible for obtaining:

```text
Environment.CurrentDirectory
```

This is the most important context for project discovery.

DNRun should not use the executable installation path as the repository location.

---

## 14.3 Repository Scanner

Responsible for:

- Finding `.sln` files.
- Finding `.csproj` files.
- Scanning the current root.
- Scanning the `src` directory.
- Ignoring generated directories.
- Returning discovered project metadata.

---

## 14.4 Project Analyzer

Responsible for determining:

- Project name.
- Project path.
- Project type.
- Whether the project is runnable.
- Project naming classification.

Example output:

```text
Name: XYZ.Web
Path: src/XYZ.Web/XYZ.Web.csproj
Runnable: True
Type: Web
```

---

## 14.5 Configuration Manager

Responsible for:

- Reading the saved startup project.
- Validating the configured project.
- Saving a new project selection.
- Handling missing or invalid configuration.

---

## 14.6 Project Runner

Responsible for launching:

```text
dotnet run --project <project-path>
```

It should pass the terminal input and output through to the child process.

---

# 15. Future Enhancements

The architecture should allow future expansion without changing the core usage model.

Potential features include:

## Project Type Commands

```text
dnrun api
dnrun web
dnrun mobile
dnrun windows
```

## Build Without Running

```text
dnrun build
```

## Clean Project

```text
dnrun clean
```

## Solution Selection

```text
dnrun solution select
```

Useful when a repository contains multiple `.sln` files.

## Configuration Inspection

```text
dnrun config
```

## Reset Configuration

```text
dnrun reset
```

## Explicit Project Selection

```text
dnrun run XYZ.API
```

## Project Profiles

Future support could allow:

```text
dnrun web
```

to run with web-specific configuration, or:

```text
dnrun api
```

to run with API-specific environment settings.

---

# 16. Initial Technology Recommendation

The most appropriate implementation is a .NET console application written in C#.

Suggested target:

```text
.NET 10 or later
```

The project should produce a self-contained or globally deployable Windows executable.

Possible installation:

```text
C:\CMouss\DNRun\DNRun.exe
```

Windows PATH:

```text
C:\CMouss\DNRun
```

After installation:

```text
C:\Any\Project> dnrun
```

should work from any directory.

---

# 17. Primary Design Principles

DNRun should follow these principles:

### Global Installation

Install once:

```text
C:\CMouss\DNRun
```

Use everywhere:

```text
dnrun
```

### Working Directory Based

The current directory determines which repository is scanned.

### Zero Project-Specific Installation

Do not copy DNRun into every repository.

### Minimal Configuration

The user should select the startup project only once.

### Smart Discovery

Support both:

```text
repository-root/*.csproj
```

and:

```text
repository-root/src/**/*.csproj
```

### Predictable Behavior

Scanning order should remain explicit:

```text
1. Current root
2. src directory
```

### Remember User Intent

Once the user selects a startup project, DNRun should automatically use it in future executions.

---

# 18. Final Project Goal

The final user experience should be as simple as:

```text
C:\Projects\XYZ> dnrun
```

Orca IDE should be configured with the same generic command:

```text
dnrun
```

DNRun then automatically determines:

```text
Current repository
        ↓
Solution structure
        ↓
Available runnable projects
        ↓
Previously selected startup project
        ↓
dotnet run
```

The result is a reusable, intelligent .NET application launcher that eliminates the need to manually configure `dotnet run --project <path>` for every project or repository while supporting multi-project solutions and the common `src`-based repository structure.

---

# 19. Package Versioning — `dnuget`

Implemented after the runner itself, and built on the same discovery pass: the problem is the same
shape. Publishing a package means opening a `.csproj`, finding `<Version>`, remembering that
`<InformationalVersion>` and `<FileVersion>` sit next to it, and getting all of them right — in
whichever project of the repository happens to be the packaged one.

```text
C:\Projects\XYZ> dnuget 1.2.14
```

`dnuget` is `DNRun.exe` under a second name: the installer writes a `dnuget.cmd` shim beside the
executable, and `dnrun nuget <version>` is the same command spelled out. A copy of the executable
renamed `dnuget.exe` behaves identically, because the entry point normalizes its own invocation
name before parsing.

## 19.1 Packable Project Detection

The `dnrun` counterpart of §5, with a different question asked of the same parsed `.csproj`:

- `IsPackable=false`, and test projects, are excluded outright.
- A project that *asks* to be packaged — `IsPackable=true`, `PackageId`,
  `GeneratePackageOnBuild`, or `PackAsTool` — is an explicit candidate.
- When the repository has explicit candidates, only those are offered. Otherwise every remaining
  project is offered, libraries first: a repository that publishes nothing explicitly is exactly
  where guessing is wrong, so the menu appears instead.

Selection, prompting, and persistence follow §6 and §7 unchanged, storing `packageProject` in
`dnrun.config.json`. It is deliberately separate from `startupProject`: the application you run
and the library you publish are rarely the same project.

## 19.2 Version Source Resolution

The file to edit is not always the project file:

1. The `.csproj`, when it declares `PackageVersion`, `Version`, or `VersionPrefix`.
2. Otherwise the nearest `Directory.Build.props` between the project and the repository root that
   declares one — the shared-version layout every multi-package repository uses. Writing a
   `<Version>` into the `.csproj` there would silently opt that project *out* of the shared
   version rather than bumping it, so the props file is updated and the projects sharing it are
   named first.
3. Otherwise the `.csproj`, which gains a `<Version>`.

## 19.3 Properties Written

| Declared property | Written |
|---|---|
| `PackageVersion`, `Version` | Full version without `+metadata` |
| `VersionPrefix` / `VersionSuffix` | Split; a stable release empties the suffix |
| `InformationalVersion` | Full version including `+metadata` |
| `AssemblyVersion`, `FileVersion` | Numeric, zero-filled to four parts |

Only properties the project already declares are updated. Introducing `AssemblyVersion` where
there was none would change what the build produces, which is more than the user asked for.

## 19.4 Editing Strategy

Version values are spliced into the original file text. The document is parsed — with line info —
only to *locate* properties, because that is the part a regex gets wrong: a `<Version>` inside a
`<PackageReference>` is not the project's version, and a declaration under a conditional
`PropertyGroup` may not apply at all. Everything else about the file survives byte for byte:
comments, indentation and tabs, blank lines, CRLF, and a BOM. Writes go through a temp file in the
same directory, as §14.5 does for the config.

Versions are validated before any file is opened — two to four numbers, optional `-prerelease`,
optional `+metadata`, with a leading `v` forgiven — so a typo costs nothing and never reaches a
project file.

## 19.5 Command Surface

```text
dnuget <version>          Set the package version
dnuget                    Show the package project and its declared version
dnuget list               Every packable project with its current version
dnuget select [version]   Choose a different package project, save it
dnuget --all <version>    Version every packable project; shared files written once
dnuget reset              Forget the saved package project
```

Exit codes follow §5 of the plan, with one addition: `5` when a project file could not be
rewritten.
