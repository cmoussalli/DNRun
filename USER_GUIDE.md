# DNRun — User Guide

DNRun runs the right .NET project for whatever repository you are standing in. You install it
once, and from then on a single command replaces every hand-configured
`dotnet run --project ./src/Something/Something.csproj`.

```text
C:\Projects\XYZ> dnrun
```

It also versions the package that repository publishes, so a release is one line rather than a
hunt through `.csproj` files:

```text
C:\Projects\XYZ> dnuget 1.2.14
```

This guide covers installing it, using it day to day, and what to do when it does something you
did not expect.

> Looking for the design decisions behind the tool? Those live in `IMPLEMENTATION_PLAN.md`.
> This guide is only about using it.

---

## Contents

1. [Install](#1-install)
2. [Your first run](#2-your-first-run)
3. [Everyday use](#3-everyday-use)
4. [Command reference](#4-command-reference)
5. [Passing arguments to your app](#5-passing-arguments-to-your-app)
6. [Versioning your NuGet package](#6-versioning-your-nuget-package)
7. [The configuration file](#7-the-configuration-file)
8. [Using DNRun with Orca IDE](#8-using-dnrun-with-orca-ide)
9. [How DNRun decides what to run](#9-how-dnrun-decides-what-to-run)
10. [Troubleshooting](#10-troubleshooting)
11. [Exit codes](#11-exit-codes)
12. [FAQ](#12-faq)

---

## 1. Install

Open Windows Terminal and run one line:

```powershell
irm https://raw.githubusercontent.com/cmoussalli/DNRun/main/install.ps1 | iex
```

That downloads `DNRun.exe` from the latest release into `%LOCALAPPDATA%\Programs\DNRun`, checks its
SHA256 against the one published with the release, writes the one-line `dnuget.cmd` beside it, and
adds the directory to your user PATH. No administrator rights and no clone are involved. When the repository has no release binary yet, the
same script downloads the sources and builds them, which needs the [.NET 10
SDK](https://dotnet.microsoft.com/download).

**Open a new terminal.** PATH changes only reach terminals started afterwards.

Confirm it worked from any .NET repository:

```powershell
dnrun version     # dnrun 1.1.1
dnrun list        # the projects you can run
dnuget list       # the packages you can version
```

### Install options

`iex` runs a script but cannot pass arguments to it, so options go through a script block:

```powershell
$dnrun = 'https://raw.githubusercontent.com/cmoussalli/DNRun/main/install.ps1'
& ([scriptblock]::Create((irm $dnrun))) -InstallDir 'D:\Tools\DNRun'
```

| Option | Effect |
|---|---|
| `-InstallDir 'D:\Tools\DNRun'` | Install somewhere other than `%LOCALAPPDATA%\Programs\DNRun`. |
| `-Version 'v1.0.0'` | Install a specific release instead of the latest. |
| `-FromSource` | Ignore releases and build from the repository sources (.NET 10 SDK required). |
| `-Ref 'develop'` | Branch or tag to build from with `-FromSource`. |
| `-NoAot` | With `-FromSource`, build framework-dependent instead of Native AOT. |
| `-SkipPath` | Copy the exe but leave PATH untouched (you manage PATH yourself). |
| `-Uninstall` | Remove the exe and drop the install directory from PATH. |

The released build is a self-contained ~7 MB Native AOT executable that needs no .NET runtime
installed. The `-NoAot` build is a ~150 KB executable that uses the .NET runtime you already have —
it starts about 40 ms slower and is otherwise identical, and it does not need the Visual Studio C++
build tools that the AOT link step requires.

### Installing from a clone

If you have the sources checked out already:

```powershell
./build/publish.ps1      # runs the tests, then builds artifacts/DNRun.exe
./build/install.ps1      # copies it to C:\CMouss\DNRun and adds that to your user PATH
```

`./build/publish.ps1 -SkipTests` skips the test run; `-NoAot` builds without Native AOT.

### Upgrading

Re-run the one-liner — it overwrites the installed executable in place. If the copy fails with a
file-in-use error, a `dnrun` process is still running; close it and try again.

---

## 2. Your first run

`cd` into a .NET repository and type `dnrun`. What happens next depends on how many runnable
projects it finds.

### If there is exactly one

DNRun runs it. No questions asked, nothing saved.

```text
DNRun — Intelligent .NET Project Runner

Searching for .NET projects...

Found runnable project:
  Demo.Cli

Starting:
  dotnet run --project src/Demo.Cli/Demo.Cli.csproj

hello from Demo.Cli
```

### If there are several

DNRun asks once, then remembers.

```text
DNRun — Intelligent .NET Project Runner

Searching for .NET projects...

Multiple runnable projects found:

  [1] XYZ.Web      Web
  [2] XYZ.API      API
  [3] XYZ.Windows  Windows

Select the project to run:
> 1

Selected:
  XYZ.Web

Saving default project...

Starting:
  dotnet run --project src/XYZ.Web/XYZ.Web.csproj
```

At the `>` prompt you can type:

- a **number** — `2`
- a **name or any unambiguous part of one** — `api`, `Web`, `XYZ.Windows`
- **nothing, just Enter** — takes `[1]`
- `q`, `quit`, or `exit` — cancel without running anything

Three unrecognised answers in a row and DNRun gives up rather than looping at you.

### If there are none

```text
error: no runnable .NET project was found.

Scanned locations:
  C:\Projects\XYZ
  C:\Projects\XYZ\src

Projects found, none classified as runnable (1):
  XYZ.Domain  src/XYZ.Domain/XYZ.Domain.csproj

If one of these should be runnable, choose it with 'dnrun select',
or list it under "runnableProjects" in dnrun.config.json.
```

See [Troubleshooting](#10-troubleshooting) if a project you expected is in that second list.

---

## 3. Everyday use

After that first answer, the everyday case is one word:

```text
C:\Projects\XYZ> dnrun

DNRun — Intelligent .NET Project Runner

Startup project:
  XYZ.Web

Starting:
  dotnet run --project src/XYZ.Web/XYZ.Web.csproj
```

No prompt, no menu. DNRun only asks again when:

- there is no saved choice yet,
- the saved project has been deleted or renamed,
- the saved project is no longer runnable (someone turned it into a class library), or
- you run `dnrun select` on purpose.

**You do not have to be at the repository root.** DNRun walks up from wherever you are until it
finds the root, so this works identically:

```text
C:\Projects\XYZ\src\XYZ.Domain> dnrun
```

**Ctrl+C** stops the running application the way it always does. DNRun stays out of the way and
passes your app's exit code back to the shell.

---

## 4. Command reference

### `dnrun`

Run the saved startup project. With none saved, discover projects and either run the only
candidate or ask you to choose.

### `dnrun select`

Choose a different startup project, save it, and run it. This is how you switch from the web app
to the API without editing any files.

```text
C:\Projects\XYZ> dnrun select

Available projects:

  [1] XYZ.Web      Web
  [2] XYZ.API      API
  [3] XYZ.Windows  Windows

Select the default project:
> 2
```

If exactly one project is runnable, `select` picks it and saves it without asking.

### `dnrun list`

Show what DNRun can see. Starts nothing — safe to run any time.

```text
Solution:
  XYZ.sln

Repository root:
  C:\Projects\XYZ

Runnable projects (3):

  [1] XYZ.Web  (Web)
      src/XYZ.Web/XYZ.Web.csproj
  [2] XYZ.API  (API)
      src/XYZ.API/XYZ.API.csproj
  [3] XYZ.Windows  (Windows)
      src/XYZ.Windows/XYZ.Windows.csproj

Other projects (1):

  XYZ.Domain  (library)
      src/XYZ.Domain/XYZ.Domain.csproj

Current startup project:
  XYZ.Web
```

`dnrun ls` is a shorter alias. The **Other projects** section is everything DNRun found but will
not offer to run — class libraries, Razor class libraries, and test projects.

### `dnrun config`

Show how DNRun resolved your situation: working directory, repository root and *why* it picked
that root, the solution, the config file path, and the effective settings.

This is the first command to run when DNRun chooses a root or a project that surprises you.

### `dnrun reset`

Forget the saved startup project. If `dnrun.config.json` holds nothing else, the file is deleted
outright so the repository looks untouched; if you have other settings in it, only
`startupProject` is cleared.

### `dnrun --help`, `dnrun version`

Usage text and version. `help`, `-h`, and `-?` all work; so does `--version`.

### `dnuget <version>`

Set the version of every NuGet package the repository publishes - one version, no question
asked. See
[Versioning your NuGet package](#6-versioning-your-nuget-package) for the whole story; the short
version is `dnuget 1.2.14`.

`dnuget` and `dnrun nuget` are the same command - `dnuget.cmd` is a one-line shim the installer
writes next to `DNRun.exe`.

---

## 5. Passing arguments to your app

Anything after `--` goes to your application, exactly as with `dotnet run`:

```powershell
dnrun -- --urls http://localhost:5005
dnrun -- --environment Staging --seed
```

becomes

```text
dotnet run --project src/XYZ.Web/XYZ.Web.csproj -- --urls http://localhost:5005
```

The `--` is required. `dnrun --urls ...` is rejected as an unknown option rather than guessed at,
because silently launching the wrong thing is worse than refusing.

Your application is started with the **repository root** as its working directory, so relative
paths it opens behave the same no matter which subdirectory you invoked `dnrun` from.

---

## 6. Versioning your NuGet package

If the repository publishes packages, `dnuget` sets their version without you opening a `.csproj`:

```powershell
dnuget 1.2.14
```

```text
DNRun - Intelligent .NET Project Runner

Package project:
  XYZ.Core

Updated src/XYZ.Core/XYZ.Core.csproj:
  Version               1.0.3 -> 1.2.14
  InformationalVersion  1.0.3 -> 1.2.14
  AssemblyVersion       1.0.3.0 -> 1.2.14.0

XYZ.Core will now publish as 1.2.14.
```

Nothing is built or pushed. The next `dotnet pack` or `dotnet publish` picks the new version up.

### The commands

| Command | What it does |
|---|---|
| `dnuget <version>` | Set that version on every packable project. Never asks. |
| `dnuget` | Show the packable projects and the versions they declare today. Writes nothing. |
| `dnuget list` | Every packable project with its current version. |
| `dnuget select` | Choose one package project and save it. Asks. |
| `dnuget select <version>` | Choose one project and version only that one. Asks. |
| `dnuget --all <version>` | The default, said explicitly. |
| `dnuget reset` | Forget the project chosen by `dnuget select`. |
| `dnuget --help` | Usage. |

### Which versions are accepted

Two to four numbers, an optional prerelease label, an optional build metadata suffix:

```text
1.2.14        1.2          1.2.14.3
1.3.0-beta.1  2.0.0-rc.2   2.0.0-rc.2+build.57
```

A leading `v` is accepted, so `dnuget v1.2.14` works if that is the habit from tagging. Anything
that would not restore - `1.2.x`, `next`, `1.2.14-` - is refused before any file is opened, so a
typo costs nothing.

### Which projects it changes

All of them. This is the one place `dnuget` deliberately differs from `dnrun`: running is about a
single application, so `dnrun` asks which one; publishing is about the release, so the version you
pass is written to every packable project and nothing is asked.

The same discovery as `dnrun`, filtered for packaging instead of running:

- Projects with `<IsPackable>false</IsPackable>`, and test projects, are never touched.
- Projects that ask to be packaged - `IsPackable`, `PackageId`, `GeneratePackageOnBuild`, or
  `PackAsTool` - are the only ones taken when the repository has any.
- Otherwise every remaining project is taken, libraries first.

`dnuget list` shows exactly which projects that is, before you set anything. Projects sharing a
`Directory.Build.props` have it rewritten once, not once per project.

### Versioning one project on its own

When one package has to move apart from the rest:

```powershell
dnuget select 1.2.14
```

You are shown the packable projects with their current versions, you pick one, and only that
project is versioned. The choice is saved as `packageProject` in `dnrun.config.json`, which is what
plain `dnuget` reports afterwards - it does not narrow what `dnuget <version>` writes. `dnuget
reset` forgets it.

`packageProject` is separate from `startupProject`: the app you run and the library you publish are
usually different projects, and `dnuget` never changes which project `dnrun` runs.

### Which properties it writes

Whichever of these the project already declares:

| Property | Written as |
|---|---|
| `PackageVersion`, `Version` | The version, without any `+metadata`. |
| `VersionPrefix` / `VersionSuffix` | Split: `2.0.0` and `beta.1`. A stable release empties the suffix. |
| `InformationalVersion` | The full version, `+metadata` included. |
| `AssemblyVersion`, `FileVersion` | Numbers only, zero-filled: `2.0.0-rc.1` becomes `2.0.0.0`. |

Properties the project does not declare are left out - `dnuget` never adds `AssemblyVersion` to a
project that had none, because that changes what the build produces. The one exception is a project
that declares no version at all, which gets a `<Version>`.

### When the version lives in `Directory.Build.props`

Repositories that publish several packages together usually declare the version once, in a
`Directory.Build.props` above the projects. `dnuget` follows it there and updates that file - once,
however many projects inherit it. On the single-project paths (`dnuget select`, or a repository
with exactly one packable project) it says so before writing:

```text
The version is declared in Directory.Build.props, so it is updated there.
  2 other packable projects inherit it: XYZ.Client, XYZ.Abstractions
```

Writing a `<Version>` into the `.csproj` instead would quietly opt that one project *out* of the
shared version, which is never what a version bump means.

### What the edit looks like

Only the version values change. Comments, indentation (tabs included), blank lines, CRLF endings,
and a byte-order mark all survive, so the diff is one line per property. A `<Version>` element
inside a `<PackageReference>` is never mistaken for the project's own version. The file is written
through a temp file in the same directory, so an interrupted run cannot leave a truncated project
behind.

---

## 7. The configuration file

DNRun writes `dnrun.config.json` at the repository root the first time you choose between
several projects:

```json
{
  "version": 1,
  "startupProject": "src/XYZ.Web/XYZ.Web.csproj"
}
```

The path is relative to the repository root and uses forward slashes, so cloning or moving the
repository does not invalidate it.

You can edit the file by hand. Three optional settings are available:

```json
{
  "startupProject": "src/XYZ.Web/XYZ.Web.csproj",
  "packageProject": "src/XYZ.Core/XYZ.Core.csproj",
  "ignoreDirectories": ["samples", "legacy"],
  "runnableProjects": ["src/Odd/Odd.csproj"]
}
```

| Setting | What it does |
|---|---|
| `startupProject` | The project `dnrun` runs. Repository-relative path. |
| `packageProject` | The project `dnuget select` last chose. Repository-relative path. `dnuget <version>` ignores it and versions every packable project. |
| `ignoreDirectories` | Extra directory names to skip while scanning, on top of the built-in list. |
| `runnableProjects` | Force these projects into the candidate list even if DNRun classified them as libraries. Accepts a repository-relative path or a bare project name. |

A malformed config is a warning, not a failure: DNRun tells you what is wrong, continues as if
unconfigured, and leaves your file alone until you make a new selection.

### Should I commit it?

Your call — DNRun does not touch `.gitignore` either way.

- **Commit it** when the whole team runs the same project. New clones work immediately.
- **Ignore it** when everyone works on a different part of the solution.

---

## 8. Using DNRun with Orca IDE

Set the run command for every workspace to the same thing:

```text
dnrun
```

That is the entire integration. Because DNRun resolves everything from the working directory,
one command serves every repository — no per-project run configuration, and nothing copied into
your projects.

The only requirement is that Orca launches the command **with the project workspace as the
working directory**.

### One-time setup per repository

If a repository has several runnable projects, record your choice **from a normal terminal** the
first time:

```powershell
cd C:\Projects\XYZ
dnrun select
```

Then Orca's plain `dnrun` runs your choice without ever needing to ask.

This matters because a run command may not attach an interactive terminal. Rather than hang on a
prompt you cannot see or answer, DNRun prints the candidates and exits with code `2`:

```text
error: multiple runnable projects found and no interactive terminal is attached.
Run 'dnrun select' from a terminal to choose a default startup project,
or set "startupProject" in C:\Projects\XYZ\dnrun.config.json.
```

If that appears in your output pane, run `dnrun select` once and it will not come back.

### Output and colours

DNRun never redirects your application's streams, so console output, ANSI colours, spinners, and
interactive prompts from your app behave exactly as they would under a hand-typed `dotnet run`.
If DNRun's own colours render as escape codes in your output pane, set `NO_COLOR=1` in the
environment.

---

## 9. How DNRun decides what to run

Useful background when the answer surprises you.

### Finding the repository root

DNRun starts at your current directory and walks **upward** (at most 32 levels), stopping at the
first directory containing one of these, in priority order:

1. `dnrun.config.json`
2. `*.slnx`
3. `*.sln`
4. `.git`
5. `global.json`

If none is found anywhere, your current directory is the root. `dnrun config` reports which
marker was used.

### Finding project files

Scanned in a fixed, predictable order:

1. `<root>/*.csproj` — top level only
2. `<root>/src/**/*.csproj` — recursively, nested project folders included
3. Only if both come up empty: the whole tree, depth-limited — this rescues layouts like
   `source/`, `apps/`, or `Backend/`, and says so when it happens

These directories are skipped during the walk, along with any directory whose name starts with a
dot, and any junction or symlink:

`bin` · `obj` · `node_modules` · `.git` · `.vs` · `.idea` · `.vscode` · `packages` · `artifacts` ·
`TestResults` · `.nuke` · `dist` · `.svn`

A `.sln` or `.slnx` at the root is read as *context*, not as the source of truth. Projects it
lists that the scan order missed are picked up as well; projects on disk it does not list are
still offered, tagged `not in solution` in `dnrun list`; entries pointing at deleted files are
ignored.

### Deciding what is runnable

**Runnable** when any of these hold:

- `OutputType` is `Exe` or `WinExe`
- the SDK is `Microsoft.NET.Sdk.Web`, `Microsoft.NET.Sdk.Worker`, or
  `Microsoft.NET.Sdk.BlazorWebAssembly`
- it is a plain-SDK project with no `OutputType` that references `Microsoft.AspNetCore.*`

**Never offered**, whatever the above says:

- test projects — detected by `IsTestProject`, by a reference to `Microsoft.NET.Test.Sdk`,
  `xunit*`, `NUnit*`, `MSTest*`, or `Microsoft.Testing.Platform*`, or by a name ending in
  `.Tests`, `.Test`, `.IntegrationTests`, `.UnitTests`, `.FunctionalTests`, or `.AcceptanceTests`.
  Modern test SDKs build as `Exe`, so without this filter every test project would clutter the
  menu.
- Razor class libraries (`Microsoft.NET.Sdk.Razor` without an executable `OutputType`)
- anything with `OutputType` set to `Library`

**Important limitation.** DNRun reads `.csproj` files directly and does not evaluate MSBuild. A
project whose `OutputType` is set in a shared `Directory.Build.props` therefore looks like a
library. The fix is the `runnableProjects` setting — see
[the config file](#7-the-configuration-file) and [Troubleshooting](#10-troubleshooting).

### Menu order

Web, then API, then Worker, Windows, Mobile, Console, then anything else — alphabetically within
each group. The order is stable, so `[1]` means the same project every time.

---

## 10. Troubleshooting

### `dnrun` is not recognised as a command

The PATH change has not reached this terminal. Open a new one. If it still fails, check that
`C:\CMouss\DNRun\DNRun.exe` exists and that `C:\CMouss\DNRun` appears in:

```powershell
[Environment]::GetEnvironmentVariable('PATH', 'User')
```

### It found no runnable project, but my app is right there

Run `dnrun list` and look at the **Other projects** section.

- **Your app is listed there** — it was classified as a library. The usual cause is `OutputType`
  living in `Directory.Build.props` instead of the `.csproj`. Add it to the config:

  ```json
  { "runnableProjects": ["src/MyApp/MyApp.csproj"] }
  ```

  Or just run `dnrun select` — when nothing is classified as runnable, it lists every project it
  found so you can pick one anyway.

- **Your app is not listed at all** — it is outside the scanned locations. Check `dnrun config`
  to see which root DNRun resolved; the project may sit above that root, or inside a directory
  on the skip list.

### It picked the wrong repository root

`dnrun config` shows the root and the marker that identified it. A stray `.git` folder or a
solution file in a subdirectory is the usual culprit. Dropping a `dnrun.config.json` in the
directory you want as the root settles it permanently — that marker outranks all the others.

### It runs the wrong project

```powershell
dnrun select
```

Pick the right one; the new choice replaces the old.

### It keeps asking me every time

The selection is not being saved. Check that `dnrun.config.json` can be written at the
repository root — DNRun warns if the write fails, but carries on and runs your app anyway, so
the warning is easy to miss above the application output.

### `dnuget` is not recognised, but `dnrun` is

`dnuget.cmd` is missing from the install directory - most likely because DNRun was installed
before the command existed. Re-run the installer; it rewrites the shim every time.
`dnrun nuget 1.2.14` works in the meantime, and is exactly the same command.

### `dnuget` versioned more projects than I expected

That is the design: one version for the whole repository. Run `dnuget list` to see exactly which
projects count as packable before you set anything, and use `dnuget select 1.2.14` when you really
do want a single project versioned on its own.

If a project is being versioned that should not be, mark it `<IsPackable>false</IsPackable>`.
If one you want is missing from `dnuget list`, it is already opting out that way, or it looks like
a test project.

### `dnuget` changed `Directory.Build.props` instead of my `.csproj`

That is deliberate, and it is announced before the write: the version was declared there, so that
is where a bump belongs - every project under that file shares it. To version one project on its
own, give it its own `<Version>` in its `.csproj`; `dnuget` then edits that from the next run on.

### It hangs, or exits with code 2, under Orca

No interactive terminal is attached, so DNRun refuses to prompt. Run `dnrun select` once from a
normal terminal. See [Orca integration](#8-using-dnrun-with-orca-ide).

### `'dotnet' could not be started`

The .NET SDK is not on PATH for this terminal. Verify with `dotnet --version`.

### `Unable to proceed with project … The current OutputType is 'Library'`

That message is from `dotnet run`, not DNRun — you selected a class library (`dnrun select`
lets you, deliberately, in case DNRun's classification was wrong). Run `dnrun select` again and
pick an actual application, or `dnrun reset` to start fresh.

### My config was rejected as invalid JSON

DNRun prints the parse error and carries on as if unconfigured, without touching the file. Fix
the JSON, or run `dnrun reset` to delete it and `dnrun select` to write a fresh one.

---

## 11. Exit codes

Useful in scripts and CI.

| Code | Meaning |
|---|---|
| *your app's own code* | The application ran. Its exit code is passed straight through, unchanged. |
| `0` | Success — including for `list`, `config`, `reset`, and every `dnuget` command. |
| `1` | No project the command could act on: nothing runnable for `dnrun`, nothing packable for `dnuget`. |
| `2` | Bad usage, or several candidates with no terminal available to ask on. |
| `3` | `dotnet` is not on PATH, or the process could not be started. |
| `4` | The configuration file exists but is unusable. |
| `5` | `dnuget` could not rewrite a project file - read-only, locked, or not valid XML. |

Codes `1`–`5` are only ever returned by DNRun itself, before your application starts.

---

## 12. FAQ

**Do I need to copy DNRun into each project?**
No. That is the whole point. It is installed once, outside your repositories, and works out where
it is being run from every time.

**Does it need to be run from the repository root?**
No. It walks up to find the root, so any subdirectory works.

**Does it work without a solution file?**
Yes. A `.sln` or `.slnx` improves what DNRun knows, but a repository with only `.csproj` files
works fine.

**What about repositories with several solutions?**
DNRun prefers the solution named after the repository directory, then `.slnx` over `.sln`, then
alphabetical order. `dnrun list` shows the others under `also present:` so you know a choice was
made.

**Can I run something other than `dotnet run` — build, watch, test?**
Not in this version. `dnrun build`, `dnrun clean`, `dnrun watch`, and shortcuts like
`dnrun web` / `dnrun api` are planned; today DNRun runs projects.

**Can I run two projects at once — an API and a front end?**
No. DNRun starts one project. Start each in its own terminal.

**Does it modify my projects?**
Only `dnrun.config.json` at the repository root, and only when you make a selection. Nothing
else on disk is touched — no `.gitignore` edits, no `.csproj` changes.

**How do I uninstall it?**

```powershell
& ([scriptblock]::Create((irm https://raw.githubusercontent.com/cmoussalli/DNRun/main/install.ps1))) -Uninstall
```

That deletes the executable and removes the install directory from your user PATH — add
`-InstallDir` if you installed elsewhere. By hand: delete the install directory and drop it from
PATH. Either way, `dnrun.config.json` files in your repositories are left alone; delete them
yourself if you want them gone.

**How do I turn off the colours?**
Set `NO_COLOR` to anything in your environment. Colours are also suppressed automatically when
output is redirected to a file or pipe.
