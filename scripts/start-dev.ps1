<#
.SYNOPSIS
    MorphDB Development Environment Startup Script

.DESCRIPTION
    Starts all development services with hot reload:
    - Docker containers (PostgreSQL, Redis)
    - MorphDB.Service (.NET with hot reload)
    - MorphDB Desk (Electron with hot reload)

    With -Headless it starts only what a script needs to drive the service -- PostgreSQL in a
    compose project of its own and the service as a background process on a port of its own --
    and records the process id, project and ports in .dev-state.json so that stop-dev.ps1 can
    stop exactly that and nothing else. No terminal tabs, no Desk, no file watcher; the service
    runs the Release build. Two headless runs can coexist by taking different -Project/-PgPort/
    -Port values, and neither collides with the interactive setup on 5432/5400.

.PARAMETER Headless
    Start PostgreSQL and the service in the background, recording them in .dev-state.json.

.PARAMETER Project
    Compose project name for the headless run (default: morphdb-dev). Isolates its containers
    and volumes from any other copy of this repository.

.PARAMETER PgPort
    Host port for the headless run's PostgreSQL (default: 55432).

.PARAMETER Port
    Host port for the headless run's service (default: 5400).

.NOTES
    Requires: Docker Desktop, .NET 10 SDK; Node.js 20+ and Windows Terminal for the interactive mode
#>

param(
    [switch]$SkipDocker,
    [switch]$SkipApi,
    [switch]$SkipDesk,
    [switch]$Headless,
    [string]$Project = "morphdb-dev",
    [int]$PgPort = 55432,
    [int]$Port = 5400,
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
  -Headless      PostgreSQL + service in the background, recorded in .dev-state.json
  -Project NAME  Compose project for the headless run (default: morphdb-dev)
  -PgPort N      PostgreSQL host port for the headless run (default: 55432)
  -Port N        Service host port for the headless run (default: 5400)
  -Help          Show this help message

Examples:
  .\start-dev.ps1                    # Start everything
  .\start-dev.ps1 -SkipDesk          # Start only backend services
  .\start-dev.ps1 -SkipDocker        # Skip Docker (use existing containers)
  .\start-dev.ps1 -Headless          # Background run for scripts; stop with .\stop-dev.ps1

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

# =============================================================================
# Headless run: PostgreSQL + the service in the background, recorded for stop-dev.ps1
# =============================================================================
if ($Headless) {
    $StateFile = Join-Path $ProjectRoot ".dev-state.json"
    if (Test-Path $StateFile) {
        Write-Err "A headless run is already recorded in .dev-state.json -- run stop-dev.ps1 first"
        exit 1
    }

    Write-Header "Headless Run (project '$Project', PostgreSQL $PgPort, API $Port)"

    if (-not $SkipDocker) {
        Write-Step "Starting PostgreSQL..."
        $env:MORPHDB_PG_PORT = "$PgPort"
        Push-Location $ProjectRoot
        docker compose -p $Project up -d postgres
        $composeExit = $LASTEXITCODE
        Pop-Location
        if ($composeExit -ne 0) {
            Write-Err "Failed to start PostgreSQL"
            exit 1
        }

        Write-Step "Waiting for PostgreSQL..."
        $ready = $false
        for ($i = 0; $i -lt 30; $i++) {
            docker compose -p $Project exec -T postgres pg_isready -U morph -d morphdb 2>&1 | Out-Null
            if ($LASTEXITCODE -eq 0) { $ready = $true; break }
            Start-Sleep -Seconds 1
        }
        if (-not $ready) {
            Write-Err "PostgreSQL did not become ready in 30 seconds"
            exit 1
        }
        Write-Step "PostgreSQL is ready on 127.0.0.1:$PgPort"
    }

    Write-Step "Building MorphDB.Service (Release)..."
    $buildResult = dotnet build (Join-Path $ServicePath "MorphDB.Service.csproj") --configuration Release --verbosity quiet 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Err "Build failed!"
        Write-Host $buildResult -ForegroundColor Red
        exit 1
    }
    $dll = Join-Path $ServicePath "bin\Release\net10.0\MorphDB.Service.dll"
    if (-not (Test-Path $dll)) {
        Write-Err "Built output not found: $dll"
        exit 1
    }

    # The child inherits these; nothing else in this shell needs them.
    $env:ASPNETCORE_ENVIRONMENT = "Development"
    $env:ASPNETCORE_URLS = "http://127.0.0.1:$Port"
    $env:ConnectionStrings__MorphDB = "Host=127.0.0.1;Port=$PgPort;Database=morphdb;Username=morph;Password=morph"

    $outLog = Join-Path $ProjectRoot ".dev-service.log"
    $errLog = Join-Path $ProjectRoot ".dev-service.err.log"

    # Launched through cmd.exe rather than Start-Process -RedirectStandardOutput. That form starts
    # the child with handle inheritance, so it also inherits whatever this script's own output is
    # connected to -- and a caller that pipes or captures this script then waits for that handle
    # to close, which is when the service exits, not when this script does. ShellExecute (the path
    # Start-Process takes without redirection) inherits nothing; cmd does the redirection to files
    # on the far side of it. The service is cmd's child, so its id is looked up rather than returned.
    $dotnetExe = (Get-Command dotnet).Source
    $launcher = Start-Process -FilePath "cmd.exe" -WorkingDirectory $ServicePath -WindowStyle Hidden -PassThru `
        -ArgumentList "/d /c `"`"$dotnetExe`" `"$dll`" > `"$outLog`" 2> `"$errLog`"`""
    $service = $null
    for ($i = 0; $i -lt 50; $i++) {
        $service = Get-CimInstance Win32_Process -Filter "ParentProcessId=$($launcher.Id) AND Name='dotnet.exe'" |
            Select-Object -First 1
        if ($service) { break }
        if ($launcher.HasExited) { break }
        Start-Sleep -Milliseconds 200
    }
    if (-not $service) {
        Write-Err "MorphDB.Service did not start (see $errLog)"
        Get-Content $errLog -Tail 20 -ErrorAction SilentlyContinue | ForEach-Object { Write-Info $_ }
        exit 1
    }
    $servicePid = $service.ProcessId

    # Recorded before the health wait so that stop-dev.ps1 can clean up a run that never came up.
    @{ pid = $servicePid; project = $Project; port = $Port; pgPort = $PgPort; startedAt = (Get-Date).ToString("o") } |
        ConvertTo-Json | Out-File -FilePath $StateFile -Encoding UTF8
    Write-Step "MorphDB.Service started (PID $servicePid); recorded in .dev-state.json"

    $apiUrl = "http://127.0.0.1:$Port"
    Write-Host "  Waiting for $apiUrl/health/live" -NoNewline
    $waited = 0
    $healthy = $false
    while ($waited -lt 120) {
        if (-not (Get-Process -Id $servicePid -ErrorAction SilentlyContinue)) { break }
        try {
            $response = Invoke-WebRequest -Uri "$apiUrl/health/live" -TimeoutSec 3 -ErrorAction Stop
            if ($response.StatusCode -eq 200) { $healthy = $true; break }
        } catch { }
        Start-Sleep -Seconds 2
        $waited += 2
        Write-Host "." -NoNewline
    }
    Write-Host ""
    if (-not $healthy) {
        Write-Err "The service did not answer /health/live (see $errLog); .dev-state.json is kept so stop-dev.ps1 can clean up"
        Get-Content $errLog -Tail 20 -ErrorAction SilentlyContinue | ForEach-Object { Write-Info $_ }
        exit 1
    }
    Write-Step "Service is live"

    Write-Header "Headless Run Ready"
    Write-Host ""
    Write-Host "    PostgreSQL  : " -NoNewline; Write-Host "127.0.0.1:$PgPort" -ForegroundColor Yellow -NoNewline; Write-Host " (morph/morph, compose project '$Project')" -ForegroundColor Gray
    Write-Host "    MorphDB API : " -NoNewline; Write-Host $apiUrl -ForegroundColor Yellow -NoNewline; Write-Host " (PID $servicePid, log: .dev-service.log)" -ForegroundColor Gray
    Write-Host ""
    Write-Host "  Stop with .\scripts\stop-dev.ps1 -- it stops this process and this compose project, nothing else." -ForegroundColor Gray
    Write-Host ""
    exit 0
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

    # Wait for PostgreSQL. Asked through compose rather than by container name: containers carry
    # no fixed name (see docker-compose.yml), so `docker inspect morphdb-postgres` found nothing
    # and this loop used to run its full 30 seconds every time.
    Write-Step "Waiting for PostgreSQL..."
    $maxAttempts = 30
    for ($i = 0; $i -lt $maxAttempts; $i++) {
        docker compose exec -T postgres pg_isready -U morph -d morphdb 2>&1 | Out-Null
        if ($LASTEXITCODE -eq 0) {
            Write-Step "PostgreSQL is ready"
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
# Wait for API
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
