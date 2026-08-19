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

**New here?** [USER_GUIDE.md](USER_GUIDE.md) walks through installing it, everyday use, Orca
setup, and troubleshooting. The rest of this file is the short version plus development notes.

---

## Install

```powershell
irm https://raw.githubusercontent.com/cmoussalli/DNRun/main/install.ps1 | iex
```

That downloads `DNRun.exe` from the latest release into `%LOCALAPPDATA%\Programs\DNRun`, verifies its
SHA256, and adds that directory to your user PATH. If no release binary is available it builds from
source instead, which needs the .NET 10 SDK.

Open a new terminal, then from any .NET repository:

```powershell
dnrun list
```

`iex` cannot forward parameters, so pass options through a script block:

```powershell
$dnrun = 'https://raw.githubusercontent.com/cmoussalli/DNRun/main/install.ps1'
& ([scriptblock]::Create((irm $dnrun))) -InstallDir 'D:\Tools\DNRun'
& ([scriptblock]::Create((irm $dnrun))) -Version 'v1.0.0'   # a specific release
& ([scriptblock]::Create((irm $dnrun))) -FromSource -NoAot  # build locally, no C++ build tools
& ([scriptblock]::Create((irm $dnrun))) -Uninstall          # remove the exe and the PATH entry
```

`-SkipPath` leaves PATH alone; `-Ref` picks the branch or tag to build from source. Re-running the
one-liner upgrades in place.

### From a clone

```powershell
./build/publish.ps1      # runs the tests, then publishes artifacts/DNRun.exe (Native AOT)
./build/install.ps1      # copies it to C:\CMouss\DNRun and adds that to the user PATH
```

`publish.ps1` needs the Visual Studio C++ build tools for the Native AOT link step. Without them,
publish framework-dependent instead — a much smaller exe that needs the installed .NET runtime:

```powershell
./build/publish.ps1 -NoAot
```

### Publishing a release

Push a tag and `.github/workflows/release.yml` tests, AOT-publishes, and attaches `DNRun.exe` plus
its checksum to the GitHub release the installer downloads:

```powershell
git tag v1.0.0
git push origin v1.0.0
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

Arguments reach your app through the standard `dotnet run` separator:

```powershell
dnrun -- --urls http://localhost:5005
```

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
  "ignoreDirectories": ["samples", "legacy"],
  "runnableProjects": ["src/Odd/Odd.csproj"]
}
```

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
dotnet test                    # 110 tests, no network or process launching required
./build/publish.ps1            # tests + AOT publish
```

Layout: `src/DNRun` holds the application (`Discovery`, `Configuration`, `Execution`,
`Presentation`, `Cli`), `tests/DNRun.Tests` holds the suite, which builds throwaway repository
trees on disk through the `TempRepo` fixture. No NuGet dependencies outside the test project.

Design notes and the decisions behind them are in `IMPLEMENTATION_PLAN.md`; the behavioural
specification is `DNRun — Intelligent .NET Project Runner.md`.
