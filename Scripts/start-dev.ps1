# Simple Development Startup Script for RagSearch Azure Functions
# This script starts Azurite and Azure Functions in a controlled manner

param(
    [Parameter(Mandatory=$false)]
    [switch]$StopFirst,
    
    [Parameter(Mandatory=$false)]
    [switch]$LogsOnly
)

$ErrorActionPreference = "SilentlyContinue"

function Write-Status {
    param(
        [string]$Message,
        [string]$Level = "INFO"
    )
    
    $timestamp = Get-Date -Format "HH:mm:ss"
    $color = switch ($Level) {
        "SUCCESS" { "Green" }
        "WARNING" { "Yellow" }
        "ERROR" { "Red" }
        default { "White" }
    }
    
    Write-Host "[$timestamp] $Message" -ForegroundColor $color
}

function Stop-AllServices {
    Write-Status "Stopping all services..." "WARNING"
    
    # Stop Functions processes
    Get-Process -Name "func" -ErrorAction SilentlyContinue | ForEach-Object {
        Write-Status "Stopping Functions process (PID: $($_.Id))"
        $_.Kill()
    }
    
    # Stop Azurite processes  
    Get-Process -Name "azurite" -ErrorAction SilentlyContinue | ForEach-Object {
        Write-Status "Stopping Azurite process (PID: $($_.Id))"
        $_.Kill()
    }
    
    # Wait for cleanup
    Start-Sleep -Seconds 2
    Write-Status "Services stopped" "SUCCESS"
}

function Start-AzuriteService {
    Write-Status "Starting Azurite storage emulator..."
    
    # Create directory if needed
    if (-not (Test-Path "azurite")) {
        New-Item -ItemType Directory -Path "azurite" | Out-Null
    }
    
    # Start Azurite in background
    $azuriteJob = Start-Job -ScriptBlock {
        Set-Location $using:PWD
        azurite --silent --location azurite --debug azurite\debug.log
    }
    
    # Wait for startup
    $timeout = 10
    $elapsed = 0
    
    while ($elapsed -lt $timeout) {
        Start-Sleep -Seconds 1
        $elapsed++
        
        try {
            $response = Invoke-WebRequest -Uri "http://127.0.0.1:10000" -TimeoutSec 2 -ErrorAction Stop
            Write-Status "Azurite started successfully" "SUCCESS"
            return $true
        } catch {
            # Continue waiting
        }
    }
    
    Write-Status "Azurite may not have started properly" "WARNING"
    return $false
}

function Start-FunctionsService {
    Write-Status "Starting Azure Functions..."
    
    # Start Functions in background
    $funcJob = Start-Job -ScriptBlock {
        Set-Location $using:PWD
        func start
    }
    
    # Wait for startup
    $timeout = 30
    $elapsed = 0
    
    while ($elapsed -lt $timeout) {
        Start-Sleep -Seconds 2
        $elapsed += 2
        
        try {
            $response = Invoke-WebRequest -Uri "http://localhost:7071/admin/host/status" -TimeoutSec 3 -ErrorAction Stop
            Write-Status "Azure Functions started successfully" "SUCCESS"
            return $true
        } catch {
            # Continue waiting
        }
    }
    
    Write-Status "Azure Functions may not have started properly" "WARNING"
    return $false
}

function Test-Services {
    Write-Status "Testing services..."
    
    # Test Azurite
    try {
        $response = Invoke-WebRequest -Uri "http://127.0.0.1:10000" -TimeoutSec 3 -ErrorAction Stop
        Write-Status "Azurite Blob Service: Running" "SUCCESS"
    } catch {
        Write-Status "Azurite Blob Service: Not responding" "ERROR"
    }
    
    # Test Functions
    try {
        $response = Invoke-WebRequest -Uri "http://localhost:7071/admin/host/status" -TimeoutSec 3 -ErrorAction Stop
        Write-Status "Azure Functions: Running" "SUCCESS"
        
        # Test HTTP function
        try {
            $response = Invoke-WebRequest -Uri "http://localhost:7071/api/HttpExample" -Method GET -TimeoutSec 3 -ErrorAction Stop
            Write-Status "HTTP Function: Responding (Status: $($response.StatusCode))" "SUCCESS"
        } catch {
            Write-Status "HTTP Function: Not responding" "ERROR"
        }
    } catch {
        Write-Status "Azure Functions: Not responding" "ERROR"
    }
}

function Show-ServiceInfo {
    Write-Status "Service Information:"
    
    Write-Host ""
    Write-Host "Azure Functions:" -ForegroundColor Cyan
    Write-Host "  - Admin API: http://localhost:7071/admin/host/status" -ForegroundColor Gray
    Write-Host "  - HTTP Function: http://localhost:7071/api/HttpExample" -ForegroundColor Gray
    Write-Host ""
    Write-Host "Azurite Storage:" -ForegroundColor Cyan
    Write-Host "  - Blob Service: http://127.0.0.1:10000" -ForegroundColor Gray
    Write-Host "  - Queue Service: http://127.0.0.1:10001" -ForegroundColor Gray
    Write-Host "  - Table Service: http://127.0.0.1:10002" -ForegroundColor Gray
    Write-Host ""
    Write-Host "Development Commands:" -ForegroundColor Cyan
    Write-Host "  - Test HTTP: Invoke-RestMethod -Uri http://localhost:7071/api/HttpExample" -ForegroundColor Gray
    Write-Host "  - Check Status: .\Scripts\debug-functions.ps1" -ForegroundColor Gray
    Write-Host "  - Run Tests: .\Scripts\test-suite.ps1 -TestType Http" -ForegroundColor Gray
    Write-Host "  - Stop Services: .\Scripts\start-dev.ps1 -StopFirst" -ForegroundColor Gray
    Write-Host ""
}

function Show-Logs {
    Write-Status "Recent Azurite Logs:"
    if (Test-Path "azurite\debug.log") {
        Get-Content "azurite\debug.log" -Tail 5 | ForEach-Object {
            Write-Host "  $_" -ForegroundColor Gray
        }
    } else {
        Write-Status "No Azurite logs found" "WARNING"
    }
}

# Main execution
Write-Host ""
Write-Host "RagSearch Development Environment" -ForegroundColor Cyan
Write-Host "================================" -ForegroundColor Cyan
Write-Host ""

if ($LogsOnly) {
    Show-Logs
    exit 0
}

if ($StopFirst) {
    Stop-AllServices
    exit 0
}

# Check if services are already running
$azuriteRunning = $false
$functionsRunning = $false

try {
    $response = Invoke-WebRequest -Uri "http://127.0.0.1:10000" -TimeoutSec 2 -ErrorAction Stop
    $azuriteRunning = $true
} catch { }

try {
    $response = Invoke-WebRequest -Uri "http://localhost:7071/admin/host/status" -TimeoutSec 2 -ErrorAction Stop
    $functionsRunning = $true
} catch { }

if ($azuriteRunning -and $functionsRunning) {
    Write-Status "Services are already running!" "SUCCESS"
    Test-Services
    Show-ServiceInfo
    exit 0
}

# Start services
if (-not $azuriteRunning) {
    Start-AzuriteService | Out-Null
}

if (-not $functionsRunning) {
    Start-FunctionsService | Out-Null
}

# Wait a moment for services to stabilize
Start-Sleep -Seconds 3

# Test services
Test-Services

# Show information
Show-ServiceInfo

Write-Status "Development environment ready!" "SUCCESS"
Write-Host ""
Write-Host "Press Ctrl+C to stop all services when done." -ForegroundColor Yellow
Write-Host ""