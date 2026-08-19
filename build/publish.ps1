<#
.SYNOPSIS
    Publishes DNRun.exe into ./artifacts.

.DESCRIPTION
    Runs the test suite, then publishes a Native AOT, single-file win-x64 executable with no
    runtime prerequisite. Native AOT needs the Visual Studio C++ build tools; pass -NoAot to fall
    back to a framework-dependent single file (~150 KB, requires the installed .NET runtime and
    costs roughly 40 ms of startup).

.EXAMPLE
    ./build/publish.ps1
    ./build/publish.ps1 -NoAot -SkipTests
#>
[CmdletBinding()]
param(
    [switch]$NoAot,
    [switch]$SkipTests,
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'src/DNRun/DNRun.csproj'
$output = Join-Path $repoRoot 'artifacts'

if (-not $SkipTests) {
    Write-Host 'Running tests...' -ForegroundColor Cyan
    dotnet test (Join-Path $repoRoot 'tests/DNRun.Tests/DNRun.Tests.csproj') --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "Tests failed with exit code $LASTEXITCODE." }
}

if (-not $NoAot) {
    # The ILCompiler targets shell out to vswhere.exe to locate the MSVC linker, but the VS
    # installer directory is not on PATH by default outside a Developer Command Prompt.
    if (-not (Get-Command vswhere.exe -ErrorAction SilentlyContinue)) {
        $installerDir = @(
            "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer",
            "$env:ProgramFiles\Microsoft Visual Studio\Installer"
        ) | Where-Object { Test-Path (Join-Path $_ 'vswhere.exe') } | Select-Object -First 1

        if ($installerDir) {
            $env:PATH = "$installerDir;$env:PATH"
        }
        else {
            Write-Warning 'vswhere.exe was not found. If the native link step fails, install the Visual Studio C++ build tools or re-run with -NoAot.'
        }
    }
}

if (Test-Path $output) { Remove-Item $output -Recurse -Force }
New-Item -ItemType Directory -Path $output | Out-Null

$publishArgs = @(
    'publish', $project,
    '-c', $Configuration,
    '-r', 'win-x64',
    '-o', $output,
    '--nologo'
)

if ($NoAot) {
    Write-Host 'Publishing (framework-dependent single file)...' -ForegroundColor Cyan
    $publishArgs += @('-p:PublishAot=false', '-p:PublishSingleFile=true', '-p:SelfContained=false')
}
else {
    Write-Host 'Publishing (Native AOT)...' -ForegroundColor Cyan
    $publishArgs += '-p:PublishAot=true'
}

dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw "Publish failed with exit code $LASTEXITCODE." }

$exe = Join-Path $output 'DNRun.exe'
if (-not (Test-Path $exe)) { throw "Expected $exe to exist after publish." }

$sizeMb = [math]::Round((Get-Item $exe).Length / 1MB, 2)
Write-Host ''
Write-Host "Published $exe ($sizeMb MB)" -ForegroundColor Green
Write-Host "Install it with: ./build/install.ps1"
