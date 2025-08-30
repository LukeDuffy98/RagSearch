# Development Helper Script for Azure Functions
# This script provides interactive menus for common development tasks

param(
    [Parameter(Mandatory=$false)]
    [int]$Choice = 0
)

$ErrorActionPreference = "Continue"

function Show-Banner {
    Write-Host ""
    Write-Host "RagSearch Development Helper" -ForegroundColor Green
    Write-Host "==========================" -ForegroundColor Green
    Write-Host ""
}

function Show-Menu {
    Write-Host "Choose an option:" -ForegroundColor Cyan
    Write-Host "1. Build project" -ForegroundColor White
    Write-Host "2. Clean build artifacts" -ForegroundColor White
    Write-Host "3. Start Azurite storage emulator" -ForegroundColor White
    Write-Host "4. Start Azure Functions locally" -ForegroundColor White
    Write-Host "5. Test HTTP function" -ForegroundColor White
    Write-Host "6. Deploy to Azure" -ForegroundColor White
    Write-Host "7. Run comprehensive tests" -ForegroundColor White
    Write-Host "8. Check service health" -ForegroundColor White
    Write-Host "9. View logs" -ForegroundColor White
    Write-Host "0. Exit" -ForegroundColor Gray
    Write-Host ""
}

function Build-Project {
    Write-Host "Building project..." -ForegroundColor Yellow
    dotnet build --configuration Debug
    if ($LASTEXITCODE -eq 0) {
        Write-Host "Build successful!" -ForegroundColor Green
    } else {
        Write-Host "Build failed!" -ForegroundColor Red
    }
}

function Clean-Project {
    Write-Host "Cleaning build artifacts..." -ForegroundColor Yellow
    dotnet clean
    Remove-Item -Path "bin", "obj" -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "Clean completed!" -ForegroundColor Green
}

function Start-Azurite {
    Write-Host "Starting Azurite storage emulator..." -ForegroundColor Yellow
    
    # Check if Azurite is already running
    $azuriteProcess = Get-Process -Name "azurite" -ErrorAction SilentlyContinue
    if ($azuriteProcess) {
        Write-Host "Azurite is already running (PID: $($azuriteProcess.Id))" -ForegroundColor Green
        return
    }
    
    # Create azurite directory if it doesn't exist
    if (-not (Test-Path "azurite")) {
        New-Item -ItemType Directory -Path "azurite" | Out-Null
    }
    
    # Start Azurite
    Start-Process -FilePath "azurite" -ArgumentList "--silent", "--location", "azurite", "--debug", "azurite\debug.log" -WindowStyle Hidden
    Start-Sleep -Seconds 3
    
    # Verify it's running
    try {
        $response = Invoke-WebRequest -Uri "http://127.0.0.1:10000" -TimeoutSec 5 -ErrorAction Stop
        Write-Host "Azurite started successfully!" -ForegroundColor Green
    } catch {
        Write-Host "Failed to start Azurite or verify connection" -ForegroundColor Red
    }
}

function Start-Functions {
    Write-Host "Starting Azure Functions locally..." -ForegroundColor Yellow
    Write-Host "Press Ctrl+C to stop when ready" -ForegroundColor Gray
    Write-Host ""
    
    # Ensure Azurite is running first
    Start-Azurite
    
    # Start Functions
    func start
}

function Test-HttpFunction {
    Write-Host "Testing HTTP function..." -ForegroundColor Yellow
    
    try {
        $response = Invoke-RestMethod -Uri "http://localhost:7071/api/HttpExample?name=World" -Method GET
        Write-Host "Function response:" -ForegroundColor Green
        Write-Host $response -ForegroundColor White
    } catch {
        Write-Host "Error testing function: $($_.Exception.Message)" -ForegroundColor Red
        Write-Host "Make sure Azure Functions are running (option 4)" -ForegroundColor Yellow
    }
}

function Deploy-ToAzure {
    Write-Host "Deploying to Azure..." -ForegroundColor Yellow
    
    $resourceGroup = Read-Host "Enter resource group name"
    if (-not $resourceGroup) {
        Write-Host "Resource group name is required" -ForegroundColor Red
        return
    }
    
    .\deploy.ps1 -ResourceGroupName $resourceGroup
}

function Run-Tests {
    Write-Host "Running comprehensive tests..." -ForegroundColor Yellow
    .\Scripts\test-suite.ps1 -TestType All -GenerateReport
}

function Check-Health {
    Write-Host "Checking service health..." -ForegroundColor Yellow
    .\Scripts\debug-functions.ps1 -TestConnections
}

function View-Logs {
    Write-Host "Viewing recent logs..." -ForegroundColor Yellow
    .\Scripts\start-dev.ps1 -LogsOnly
}

# Main execution
Show-Banner

if ($Choice -gt 0) {
    # Direct execution based on parameter
    switch ($Choice) {
        1 { Build-Project }
        2 { Clean-Project }
        3 { Start-Azurite }
        4 { Start-Functions }
        5 { Test-HttpFunction }
        6 { Deploy-ToAzure }
        7 { Run-Tests }
        8 { Check-Health }
        9 { View-Logs }
        default { Write-Host "Invalid choice: $Choice" -ForegroundColor Red }
    }
} else {
    # Interactive mode
    do {
        Show-Menu
        $choice = Read-Host "Enter your choice (0-9)"
        
        switch ($choice) {
            "1" { Build-Project }
            "2" { Clean-Project }
            "3" { Start-Azurite }
            "4" { Start-Functions }
            "5" { Test-HttpFunction }
            "6" { Deploy-ToAzure }
            "7" { Run-Tests }
            "8" { Check-Health }
            "9" { View-Logs }
            "0" { 
                Write-Host "Goodbye!" -ForegroundColor Green
                break
            }
            default { 
                Write-Host "Invalid choice. Please enter 0-9." -ForegroundColor Red
            }
        }
        
        if ($choice -ne "0" -and $choice -ne "4") {
            Write-Host ""
            Read-Host "Press Enter to continue"
            Clear-Host
            Show-Banner
        }
        
    } while ($choice -ne "0")
}