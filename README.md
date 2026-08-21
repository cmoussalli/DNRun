# DNRun — Intelligent .NET Project Runner

Install it in one line — Windows Terminal, no admin rights, no clone:

```powershell
irm https://raw.githubusercontent.com/cmoussalli/DNRun/main/install.ps1 | iex
```

One global command that runs the right .NET project for whatever repository you happen to be in.

```text
C:\Projects\XYZ> dnrun
```

instead of

```text
C:\Projects\XYZ> dotnet run --project ./src/XYZ.Web/XYZ.Web.csproj
```

DNRun discovers projects from the **current working directory**, works out which ones are
actually runnable, asks once when there is more than one, and remembers the answer in
`dnrun.config.json` at the repository root. It is installed once, outside your projects, and
never copied into them.

The same discovery drives `dnuget`, which sets the version of the NuGet packages the repository
publishes. Running is about one application, so `dnrun` asks which one; publishing is about the
release, so `dnuget` never asks - one version applies to every packable project in the repository:

```text
C:\Projects\XYZ> dnuget 1.2.14
```

instead of opening the `.csproj` and editing `<Version>` (and `<InformationalVersion>`, and
`<FileVersion>`) by hand.

**New here?** [USER_GUIDE.md](USER_GUIDE.md) walks through installing it, everyday use, Orca
setup, and troubleshooting. The rest of this file is the short version plus development notes.

---

## Install

```powershell
irm https://raw.githubusercontent.com/cmoussalli/DNRun/main/install.ps1 | iex
```

That downloads `DNRun.exe` from the latest release into `%LOCALAPPDATA%\Programs\DNRun`, verifies its
SHA256, writes a one-line `dnuget.cmd` beside it, and adds that directory to your user PATH. If no
release binary is available it builds from source instead, which needs the .NET 10 SDK.

Open a new terminal, then from any .NET repository:

```powershell
dnrun list
```

`iex` cannot forward parameters, so pass options through a script block:



### From a clone

```powershell
./build/publish.ps1      # runs the tests, then publishes artifacts/DNRun.exe (Native AOT)
./build/install.ps1      # copies it to C:\CMouss\DNRun and adds that to the user PATH
```
---

## Commands

| Command | What it does |
|---|---|
| `dnrun` | Run the saved startup project. With none saved: run the only runnable project, or ask. |
| `dnrun select` | Choose a different startup project, save it, and run it. |
| `dnrun list` | Show the solution, the runnable projects with paths, and the current selection. Launches nothing. |
| `dnrun config` | Show the resolved repository root, config file, and effective settings. |
| `dnrun reset` | Forget the saved startup project. |
| `dnrun -- <args>` | Run, forwarding `<args>` to the application. |
| `dnrun --help`, `dnrun version` | Usage and version. |
| `dnuget <version>` | Set that version on **every** packable project. Never asks. |
| `dnuget` | Show the packable projects and the versions they currently declare. |
| `dnuget list` | List every packable project with its current version. |
| `dnuget select <version>` | Version one chosen project instead of all of them. |
| `dnuget --all <version>` | The default, said explicitly. |
| `dnuget reset` | Forget the project chosen by `dnuget select`. |

`dnuget` and `dnrun nuget` are the same command: `dnuget.cmd` is a shim the installer writes next
to `DNRun.exe`.



The application is started with the **repository root** as its working directory, so a run behaves
the same whether you invoked `dnrun` from the root or from `src/XYZ.Domain/`. Relative paths your
app opens resolve against the root, exactly as they would for `dotnet run` typed there.

### Exit codes

| Code | Meaning |
|---|---|
| *child's own* | The application ran; its exit code is propagated verbatim. |
| `1` | No runnable .NET project was found. |
| `2` | Bad usage, or several candidates with no terminal attached to ask on. |
| `3` | `dotnet` is not on PATH, or the process could not be started. |
| `4` | The configuration file exists but is unusable. |
| `5` | A project file could not be rewritten with the new package version. |

---

## How discovery works

**Repository root.** DNRun walks up from the working directory (at most 32 levels) and stops at
the first directory containing, in priority order: `dnrun.config.json`, `*.slnx`, `*.sln`,
`.git`, `global.json`. Nothing found anywhere means the working directory itself is the root. This
is why `dnrun` behaves the same whether you run it from the repository root or from
`src/XYZ.Domain/`.

**Project files.** Scanned in a fixed, predictable order:

1. `<root>/*.csproj` — top level only
2. `<root>/src/**/*.csproj` — recursive
3. Only if both come up empty: `<root>/**/*.csproj`, depth-limited, which rescues layouts like
   `source/`, `apps/`, or `Backend/`

`bin`, `obj`, `node_modules`, `.git`, `.vs`, `.idea`, `.vscode`, `packages`, `artifacts`,
`TestResults`, `.nuke`, `dist`, and `.svn` are pruned during the walk, as are dot-directories,
junctions, and symlinks. Add your own with `ignoreDirectories` in the config.

**Solution awareness.** A `.sln` or `.slnx` at the root is read as context, not as the discovery
mechanism: projects it lists that are outside the scan order are picked up too, projects on disk
that it does not list are still offered (tagged `not in solution` in `dnrun list`), and entries
pointing at deleted files are ignored.

**Runnable or not.** A project is runnable when `OutputType` is `Exe` or `WinExe`, when its SDK is
`Microsoft.NET.Sdk.Web`, `Microsoft.NET.Sdk.Worker`, or `Microsoft.NET.Sdk.BlazorWebAssembly`, or
when a plain-SDK project with no `OutputType` references `Microsoft.AspNetCore.*`. Class
libraries, Razor class libraries, and test projects are excluded — modern test SDKs emit
`OutputType=Exe`, so without that filter every `*.Tests` project would clutter the menu.

---

## Versioning the package (`dnuget`)

```text
C:\Projects\XYZ> dnuget 1.2.14

DNRun - Intelligent .NET Project Runner

Package project:
  XYZ.Core

Updated src/XYZ.Core/XYZ.Core.csproj:
  Version               1.0.3 -> 1.2.14
  InformationalVersion  1.0.3 -> 1.2.14
  AssemblyVersion       1.0.3.0 -> 1.2.14.0

XYZ.Core will now publish as 1.2.14.
```

**Which projects.** All of them. The same discovery pass as `dnrun`, filtered differently: a
project is packable unless it says `<IsPackable>false</IsPackable>` or is a test project. Projects
that *ask* to be packaged - `IsPackable`, `PackageId`, `GeneratePackageOnBuild`, or `PackAsTool` -
are the only ones taken when any of them exists; otherwise every remaining project is taken,
libraries first. A repository releases as one thing, so the version you pass is written to every
one of them and no question is asked. Projects sharing a `Directory.Build.props` have it rewritten
once, not once per project.

For the rare repository where one package moves apart from the others, `dnuget select 1.2.14` asks
which project and versions only that one, saving the choice as `packageProject`. That is the only
command that asks, and `dnuget reset` forgets the answer.

**Which properties.** Whichever the project already declares - `PackageVersion`, `Version`, or
`VersionPrefix`/`VersionSuffix` - plus `InformationalVersion`, `AssemblyVersion`, and `FileVersion`
when they are present. `AssemblyVersion` and `FileVersion` take only numbers, so `2.0.0-rc.1`
becomes `2.0.0.0` there. Properties the project does not declare are never introduced, except that
a project declaring no version at all gets a `<Version>`.

**Which file.** The `.csproj`, unless the version is inherited from a `Directory.Build.props`
between the project and the repository root - then that file is rewritten instead, and the other
packable projects sharing it are named before the change is made. Writing `<Version>` into the
`.csproj` there would silently opt one project out of the shared version instead of bumping it.

**Versions** are 2 to 4 numbers, an optional `-prerelease`, and an optional `+metadata`:
`1.2.14`, `1.2.14.3`, `1.3.0-beta.1`, `2.0.0-rc.2+build.57`. A leading `v` is accepted. Anything
else is refused before a file is opened, so a typo costs nothing.

Edits are surgical: comments, indentation, tabs, CRLF, and a BOM all survive, and a
`<Version>` element inside a `<PackageReference>` is never mistaken for the project's own. The
write goes through a temp file in the same directory, so an interruption cannot truncate a project.

---

## Configuration

`dnrun.config.json`, written at the repository root the first time you choose between several
projects:

```json
{
  "version": 1,
  "startupProject": "src/XYZ.Web/XYZ.Web.csproj"
}
```

Paths are repository-relative and forward-slashed, so moving or cloning the repository elsewhere
does not invalidate them. Two optional settings:

```json
{
  "startupProject": "src/XYZ.Web/XYZ.Web.csproj",
  "packageProject": "src/XYZ.Core/XYZ.Core.csproj",
  "ignoreDirectories": ["samples", "legacy"],
  "runnableProjects": ["src/Odd/Odd.csproj"]
}
```

`packageProject` is the project `dnuget select` last chose. `dnuget <version>` ignores it and
versions every packable project.

`runnableProjects` is an escape hatch. DNRun reads `.csproj` files directly and does **not**
evaluate MSBuild, so a project whose `OutputType` is set in a `Directory.Build.props` looks like a
library. Listing it here forces it into the candidate set. (`dnrun select` also offers every
discovered project when nothing at all is classified as runnable, so a wrong guess never blocks
you.)

You are re-prompted only when the config is missing, the configured project no longer exists, it
is no longer runnable, or you run `dnrun select`.

**Version control:** DNRun does not touch `.gitignore`. Commit `dnrun.config.json` if the team
shares one startup project; ignore it if everyone wants their own.

---

## Orca IDE integration

Set the run command for every workspace to the same thing:

```text
dnrun
```

The only requirement is that Orca launches it with the project workspace as the working
directory. DNRun does the rest — and because output streams are never redirected, the
application's console output, colours, and interactive prompts behave exactly as they would under
a hand-typed `dotnet run`.

If Orca runs commands without attaching a terminal, `dnrun` cannot show an interactive menu. It
prints the candidates and exits with code `2` rather than hanging on a read no one can see. Run
`dnrun select` once from a normal terminal to record the choice.

---

## Development

```powershell
dotnet test                    # 221 tests, no network or process launching required
./build/publish.ps1            # tests + AOT publish
```

