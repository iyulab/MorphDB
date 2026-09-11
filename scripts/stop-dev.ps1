<#
.SYNOPSIS
    MorphDB Development Environment Stop Script

.DESCRIPTION
    Stops what start-dev.ps1 started, and only that.

    A headless run (start-dev.ps1 -Headless) records the service's process id, the compose project
    and the ports in .dev-state.json. This script stops that one process by id, brings that one
    compose project down, and deletes the record. It never stops a process by name or by matching
    its command line: a pattern such as "*MorphDB.Service*" or "*watch*" also matches the MSBuild
    node servers a concurrent `dotnet build` is using, and stopping those corrupts the build that
    is running -- which is how a build server was killed once.

    An interactive run (Windows Terminal tabs) leaves no record; its tabs are stopped with Ctrl+C,
    and this script only brings the default compose project down.

.PARAMETER Project
    The compose project to bring down when there is no .dev-state.json (default: the directory
    name, which is what `docker compose` uses when -p is not given).
#>

param(
    [string]$Project
)

$ErrorActionPreference = "SilentlyContinue"

$ProjectRoot = Split-Path -Parent $PSScriptRoot
$StateFile = Join-Path $ProjectRoot ".dev-state.json"

Write-Host ""
Write-Host "  Stopping MorphDB Development Environment..." -ForegroundColor Yellow
Write-Host ""

if (Test-Path $StateFile) {
    $state = Get-Content $StateFile -Raw | ConvertFrom-Json

    # Stop the recorded process, by id, and only if it is still the process that was recorded --
    # ids are reused, so a stale record must not stop whatever holds the id now.
    $process = Get-Process -Id $state.pid -ErrorAction SilentlyContinue
    if ($process -and $process.ProcessName -eq "dotnet") {
        Write-Host "  -> Stopping MorphDB.Service (PID $($state.pid))..." -ForegroundColor Green
        Stop-Process -Id $state.pid -Force -ErrorAction SilentlyContinue
        $process.WaitForExit(10000) | Out-Null
    } else {
        Write-Host "  -> MorphDB.Service (PID $($state.pid)) is not running" -ForegroundColor Gray
    }

    Write-Host "  -> Bringing compose project '$($state.project)' down..." -ForegroundColor Green
    Push-Location $ProjectRoot
    docker compose -p $state.project down 2>$null
    Pop-Location

    Remove-Item $StateFile -Force -ErrorAction SilentlyContinue
} else {
    if (-not $Project) { $Project = Split-Path -Leaf $ProjectRoot }
    Write-Host "  -> No .dev-state.json: bringing compose project '$Project' down" -ForegroundColor Green
    Push-Location $ProjectRoot
    docker compose -p $Project down 2>$null
    Pop-Location
    Write-Host "     Service and Desk tabs started by start-dev.ps1 are stopped with Ctrl+C in each tab." -ForegroundColor Gray
}

Write-Host ""
Write-Host "  Development environment stopped" -ForegroundColor Green
Write-Host ""
