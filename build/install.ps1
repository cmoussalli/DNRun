<#
.SYNOPSIS
    Installs DNRun.exe to a directory on PATH.

.DESCRIPTION
    Copies ./artifacts/DNRun.exe to the install directory (C:\CMouss\DNRun by default) and adds
    that directory to the user PATH if it is not already there. DNRun is installed once and used
    from every repository - it is never copied into a project.

    Open a new terminal afterwards so the updated PATH is picked up.

.EXAMPLE
    ./build/install.ps1
    ./build/install.ps1 -InstallDir 'D:\Tools\DNRun'
#>
[CmdletBinding()]
param(
    [string]$InstallDir = 'C:\CMouss\DNRun',
    [switch]$SkipPath
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$source = Join-Path $repoRoot 'artifacts/DNRun.exe'

if (-not (Test-Path $source)) {
    throw "$source not found. Run ./build/publish.ps1 first."
}

if (-not (Test-Path $InstallDir)) {
    New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
}

$target = Join-Path $InstallDir 'DNRun.exe'

# A running DNRun.exe holds a lock on the file; say so plainly instead of failing cryptically.
try {
    Copy-Item $source $target -Force
}
catch [System.IO.IOException] {
    throw "Could not write $target - is DNRun.exe currently running? ($($_.Exception.Message))"
}

Write-Host "Installed $target" -ForegroundColor Green

if ($SkipPath) { return }

$userPath = [Environment]::GetEnvironmentVariable('PATH', 'User')
$entries = @($userPath -split ';' | Where-Object { $_ })

if ($entries -contains $InstallDir) {
    Write-Host "PATH already contains $InstallDir"
}
else {
    $updated = (@($entries) + $InstallDir) -join ';'
    [Environment]::SetEnvironmentVariable('PATH', $updated, 'User')
    Write-Host "Added $InstallDir to the user PATH." -ForegroundColor Green
    Write-Host 'Open a new terminal for the change to take effect.'
}

Write-Host ''
Write-Host 'Verify with:'
Write-Host '    cd <any .NET repository>'
Write-Host '    dnrun list'
