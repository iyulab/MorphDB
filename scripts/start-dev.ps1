<#
.SYNOPSIS
    MorphDB Development Environment Startup Script

.DESCRIPTION
    Starts all development services with hot reload:
    - Docker containers (PostgreSQL, Redis)
    - MorphDB.Service (.NET with hot reload)
    - MorphDB Desk (Electron with hot reload)

.NOTES
    Requires: Docker Desktop, .NET 10 SDK, Node.js 20+, Windows Terminal
#>

param(
    [switch]$SkipDocker,
    [switch]$SkipApi,
    [switch]$SkipDesk,
    [switch]$Help
)

$ErrorActionPreference = "Stop"

# Configuration
$ProjectRoot = Split-Path -Parent $PSScriptRoot
$ServicePath = Join-Path $ProjectRoot "src\MorphDB.Service"
$DeskPath = Join-Path $ProjectRoot "desk"

# Colors for output
function Write-Header { param($Message) Write-Host "`n=== $Message ===" -ForegroundColor Cyan }
function Write-Step { param($Message) Write-Host "  -> $Message" -ForegroundColor Green }
function Write-Info { param($Message) Write-Host "     $Message" -ForegroundColor Gray }
function Write-Warn { param($Message) Write-Host "  !! $Message" -ForegroundColor Yellow }
function Write-Err { param($Message) Write-Host "  XX $Message" -ForegroundColor Red }

if ($Help) {
    Write-Host @"

MorphDB Development Startup Script
===================================

Usage: .\start-dev.ps1 [options]

Options:
  -SkipDocker    Skip starting Docker containers
  -SkipApi       Skip starting MorphDB.Service
  -SkipDesk      Skip starting MorphDB Desk (Electron)
  -Help          Show this help message

Examples:
  .\start-dev.ps1                    # Start everything
  .\start-dev.ps1 -SkipDesk          # Start only backend services
  .\start-dev.ps1 -SkipDocker        # Skip Docker (use existing containers)

"@
    exit 0
}

Write-Host ""
Write-Host "  MorphDB Development Environment" -ForegroundColor Magenta
Write-Host "  ================================" -ForegroundColor Magenta

# =============================================================================
# Prerequisites Check
# =============================================================================
Write-Header "Checking Prerequisites"

# Check Docker
if (-not $SkipDocker) {
    $docker = Get-Command docker -ErrorAction SilentlyContinue
    if (-not $docker) {
        Write-Err "Docker is not installed or not in PATH"
        exit 1
    }

    $dockerRunning = docker info 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Err "Docker Desktop is not running. Please start Docker Desktop first."
        exit 1
    }
    Write-Step "Docker Desktop is running"
}

# Check .NET SDK
if (-not $SkipApi) {
    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $dotnet) {
        Write-Err ".NET SDK is not installed"
        exit 1
    }
    Write-Step ".NET SDK $(dotnet --version)"
}

# Check Node.js
if (-not $SkipDesk) {
    $node = Get-Command node -ErrorAction SilentlyContinue
    if (-not $node) {
        Write-Err "Node.js is not installed"
        exit 1
    }
    Write-Step "Node.js $(node --version)"

    if (-not (Test-Path (Join-Path $DeskPath "node_modules"))) {
        Write-Warn "Installing npm dependencies..."
        Push-Location $DeskPath
        npm install
        Pop-Location
    }
}

# Check Windows Terminal
$wt = Get-Command wt -ErrorAction SilentlyContinue
if (-not $wt) {
    Write-Err "Windows Terminal (wt) not found"
    exit 1
}
Write-Step "Windows Terminal available"

# Pre-build API to speed up startup
if (-not $SkipApi) {
    Write-Header "Building MorphDB.Service"
    Push-Location $ServicePath
    Write-Step "Running dotnet build..."
    $buildResult = dotnet build --configuration Debug --verbosity quiet 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Err "Build failed!"
        Write-Host $buildResult -ForegroundColor Red
        Pop-Location
        exit 1
    }
    Write-Step "Build completed"
    Pop-Location
}

# =============================================================================
# Start Docker Services
# =============================================================================
if (-not $SkipDocker) {
    Write-Header "Starting Docker Services"

    Push-Location $ProjectRoot

    Write-Step "Stopping existing containers..."
    docker-compose down 2>$null

    Write-Step "Starting PostgreSQL and Redis..."
    docker-compose up -d postgres redis

    if ($LASTEXITCODE -ne 0) {
        Write-Err "Failed to start Docker containers"
        Pop-Location
        exit 1
    }

    # Wait for PostgreSQL
    Write-Step "Waiting for PostgreSQL..."
    $maxAttempts = 30
    for ($i = 0; $i -lt $maxAttempts; $i++) {
        $health = docker inspect --format='{{.State.Health.Status}}' morphdb-postgres 2>$null
        if ($health -eq "healthy") {
            Write-Step "PostgreSQL is healthy"
            break
        }
        Start-Sleep -Seconds 1
    }

    Pop-Location
    Write-Info "PostgreSQL: localhost:5432 (morph/morph)"
    Write-Info "Redis: localhost:6379"
}

# =============================================================================
# Start Services in Windows Terminal Tabs
# =============================================================================
Write-Header "Launching Windows Terminal"

# Create temporary script files for each tab
$tempDir = Join-Path $env:TEMP "morphdb-dev"
New-Item -ItemType Directory -Force -Path $tempDir | Out-Null

# Script for API tab
$apiScript = Join-Path $tempDir "api.ps1"
@"
`$Host.UI.RawUI.WindowTitle = 'MorphDB API'
Set-Location '$ServicePath'
Write-Host ''
Write-Host '  MorphDB.Service - Hot Reload' -ForegroundColor Green
Write-Host '  URL: http://localhost:5400' -ForegroundColor Gray
Write-Host '  Swagger: http://localhost:5400/swagger' -ForegroundColor Gray
Write-Host ''
`$env:ASPNETCORE_ENVIRONMENT = 'Development'
`$env:ASPNETCORE_URLS = 'http://localhost:5400'
dotnet watch run --no-hot-reload
"@ | Out-File -FilePath $apiScript -Encoding UTF8

# Script for Desk tab
$deskScript = Join-Path $tempDir "desk.ps1"
@"
`$Host.UI.RawUI.WindowTitle = 'MorphDB Desk'
Set-Location '$DeskPath'
Write-Host ''
Write-Host '  MorphDB Desk - Hot Reload' -ForegroundColor Cyan
Write-Host ''
npm run dev
"@ | Out-File -FilePath $deskScript -Encoding UTF8

# Script for Docker logs tab
$dockerScript = Join-Path $tempDir "docker.ps1"
@"
`$Host.UI.RawUI.WindowTitle = 'Docker Logs'
Set-Location '$ProjectRoot'
Write-Host ''
Write-Host '  Docker Logs (Ctrl+C to stop)' -ForegroundColor Yellow
Write-Host ''
docker-compose logs -f postgres redis
"@ | Out-File -FilePath $dockerScript -Encoding UTF8

# Build Windows Terminal command
$tabs = @()

if (-not $SkipApi) {
    $tabs += "new-tab --title `"API`" --tabColor `"#512BD4`" pwsh -NoExit -File `"$apiScript`""
}

if (-not $SkipDesk) {
    $tabs += "new-tab --title `"Desk`" --tabColor `"#61DAFB`" pwsh -NoExit -File `"$deskScript`""
}

if (-not $SkipDocker) {
    $tabs += "new-tab --title `"Docker`" --tabColor `"#336791`" pwsh -NoExit -File `"$dockerScript`""
}

if ($tabs.Count -eq 0) {
    Write-Warn "No services to start"
    exit 0
}

# Start first tab, then add others
$firstTab = $tabs[0] -replace "^new-tab ", ""
$wtCommand = $firstTab

for ($i = 1; $i -lt $tabs.Count; $i++) {
    $wtCommand += " ; $($tabs[$i])"
}

Write-Step "Starting $($tabs.Count) tabs..."
Start-Process wt -ArgumentList $wtCommand

# =============================================================================
# Wait for API and Bootstrap
# =============================================================================
if (-not $SkipApi) {
    Write-Header "Waiting for API to Start"

    $apiUrl = "http://localhost:5400"
    $maxWaitSeconds = 120
    $waited = 0

    Write-Host "  Waiting for $apiUrl to be ready" -NoNewline

    while ($waited -lt $maxWaitSeconds) {
        try {
            $response = Invoke-WebRequest -Uri "$apiUrl/health/live" -TimeoutSec 3 -ErrorAction Stop
            if ($response.StatusCode -eq 200) {
                Write-Host " OK" -ForegroundColor Green
                break
            }
        } catch {
            # Still starting - check if connection refused vs other error
        }
        Start-Sleep -Seconds 3
        $waited += 3
        Write-Host "." -NoNewline
    }

    if ($waited -ge $maxWaitSeconds) {
        Write-Host ""
        Write-Warn "API did not start within $maxWaitSeconds seconds"
        Write-Info "Check the API tab for errors"
        Write-Info "You can manually bootstrap later: POST $apiUrl/api/dev/bootstrap"
    } else {
        # API is ready, create bootstrap key
        Write-Header "Creating Development API Key"

        try {
            $bootstrapResponse = Invoke-RestMethod -Uri "$apiUrl/api/dev/bootstrap" -Method POST -ErrorAction Stop

            Write-Host ""
            Write-Host "  ╔════════════════════════════════════════════════════════════╗" -ForegroundColor Green
            Write-Host "  ║           Development API Key Created                      ║" -ForegroundColor Green
            Write-Host "  ╚════════════════════════════════════════════════════════════╝" -ForegroundColor Green
            Write-Host ""
            Write-Host "  API Key : " -NoNewline; Write-Host $bootstrapResponse.apiKey -ForegroundColor Cyan
            Write-Host ""
            Write-Host "  Use this API Key in the MorphDB Desk connection dialog." -ForegroundColor Gray
            Write-Host "  (Project ID is automatically detected from the API Key)" -ForegroundColor Gray
            Write-Host ""

            # Save to a file for convenience
            $credFile = Join-Path $ProjectRoot ".dev-credentials"
            @"
# MorphDB Development Credentials
# Generated: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")
# WARNING: Do not commit this file!

PROJECT_ID=$($bootstrapResponse.projectId)
API_KEY=$($bootstrapResponse.apiKey)
API_URL=http://localhost:5400
"@ | Out-File -FilePath $credFile -Encoding UTF8

            Write-Info "Credentials saved to .dev-credentials"

        } catch {
            Write-Warn "Failed to create bootstrap key: $_"
            Write-Info "You can manually call: POST $apiUrl/api/dev/bootstrap"
        }
    }
}

# =============================================================================
# Summary
# =============================================================================
Write-Header "Development Environment Ready"

Write-Host ""
Write-Host "  Services:" -ForegroundColor White
if (-not $SkipDocker) {
    Write-Host "    PostgreSQL  : " -NoNewline; Write-Host "localhost:5432" -ForegroundColor Yellow -NoNewline; Write-Host " (morph/morph)" -ForegroundColor Gray
    Write-Host "    Redis       : " -NoNewline; Write-Host "localhost:6379" -ForegroundColor Yellow
}
if (-not $SkipApi) {
    Write-Host "    MorphDB API : " -NoNewline; Write-Host "http://localhost:5400" -ForegroundColor Yellow
    Write-Host "    Swagger     : " -NoNewline; Write-Host "http://localhost:5400/swagger" -ForegroundColor Yellow
}
if (-not $SkipDesk) {
    Write-Host "    Desk App    : " -NoNewline; Write-Host "Electron (Hot Reload)" -ForegroundColor Yellow
}
Write-Host ""
Write-Host "  Press Ctrl+C in each terminal tab to stop services." -ForegroundColor Gray
Write-Host ""
