<#
.SYNOPSIS
    MorphDB Development Environment Stop Script

.DESCRIPTION
    Stops all development services:
    - Docker containers (PostgreSQL, Redis)
    - Any running dotnet watch processes
    - Any running electron processes
#>

$ErrorActionPreference = "SilentlyContinue"

$ProjectRoot = Split-Path -Parent $PSScriptRoot

Write-Host ""
Write-Host "  Stopping MorphDB Development Environment..." -ForegroundColor Yellow
Write-Host ""

# Stop Docker containers
Write-Host "  → Stopping Docker containers..." -ForegroundColor Green
Push-Location $ProjectRoot
docker-compose down 2>$null
Pop-Location

# Kill dotnet watch processes for MorphDB.Service
Write-Host "  → Stopping .NET watch processes..." -ForegroundColor Green
Get-Process -Name "dotnet" -ErrorAction SilentlyContinue | Where-Object {
    $_.CommandLine -like "*MorphDB.Service*" -or $_.CommandLine -like "*watch*"
} | Stop-Process -Force -ErrorAction SilentlyContinue

# Kill Electron processes for morphdb-desk
Write-Host "  → Stopping Electron processes..." -ForegroundColor Green
Get-Process -Name "electron" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Get-Process -Name "morphdb-desk" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "  ✓ Development environment stopped" -ForegroundColor Green
Write-Host ""
