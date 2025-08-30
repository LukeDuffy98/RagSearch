# PowerShell Test Suite for RagSearch Azure Functions
# This script provides comprehensive testing capabilities with detailed debug logging

param(
    [Parameter(Mandatory=$false)]
    [ValidateSet("All", "Unit", "Integration", "Local", "Http", "Debug")]
    [string]$TestType = "All",
    
    [Parameter(Mandatory=$false)]
    [switch]$DetailedOutput,
    
    [Parameter(Mandatory=$false)]
    [switch]$GenerateReport,
    
    [Parameter(Mandatory=$false)]
    [string]$OutputPath = "TestResults"
)

# Global variables
$Script:TestResults = @()
$Script:StartTime = Get-Date
$Script:FunctionUrl = "http://localhost:7071"

# Color coding for output
$Colors = @{
    Success = "Green"
    Warning = "Yellow"
    Error = "Red"
    Info = "Cyan"
    Debug = "Gray"
}

# Initialize logging
function Initialize-Logging {
    if (!(Test-Path $OutputPath)) {
        New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null
    }
    
    $Script:LogFile = Join-Path $OutputPath "test-execution-$(Get-Date -Format 'yyyyMMdd-HHmmss').log"
    $Script:ReportFile = Join-Path $OutputPath "test-report-$(Get-Date -Format 'yyyyMMdd-HHmmss').html"
    
    Write-LogMessage "Test execution started" "Info"
    Write-LogMessage "Test Type: $TestType" "Info"
    Write-LogMessage "Output Path: $OutputPath" "Info"
}

# Logging function
function Write-LogMessage {
    param(
        [string]$Message,
        [string]$Level = "Info",
        [switch]$NoConsole
    )
    
    $Timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $LogEntry = "[$Timestamp] [$Level] $Message"
    
    # Write to log file
    if ($Script:LogFile) {
        Add-Content -Path $Script:LogFile -Value $LogEntry
    }
    
    # Write to console
    if (-not $NoConsole) {
        $Color = $Colors[$Level]
        if ($Color) {
            Write-Host $LogEntry -ForegroundColor $Color
        } else {
            Write-Host $LogEntry
        }
    }
}

# Test result tracking
function Add-TestResult {
    param(
        [string]$TestName,
        [string]$Status,
        [string]$Details = "",
        [timespan]$Duration = [timespan]::Zero,
        [string]$ErrorMessage = ""
    )
    
    $Result = [PSCustomObject]@{
        TestName = $TestName
        Status = $Status
        Details = $Details
        Duration = $Duration
        ErrorMessage = $ErrorMessage
        Timestamp = Get-Date
    }
    
    $Script:TestResults += $Result
    
    $StatusColor = switch ($Status) {
        "PASS" { "Success" }
        "FAIL" { "Error" }
        "SKIP" { "Warning" }
        default { "Info" }
    }
    
    Write-LogMessage "Test: $TestName - $Status" $StatusColor
    if ($Details) {
        Write-LogMessage "  Details: $Details" "Debug"
    }
    if ($ErrorMessage) {
        Write-LogMessage "  Error: $ErrorMessage" "Error"
    }
}

# Check prerequisites
function Test-Prerequisites {
    Write-LogMessage "Checking prerequisites..." "Info"
    
    $tests = @(
        @{ Name = "dotnet CLI"; Command = "dotnet --version"; Required = $true }
        @{ Name = "Azure Functions Core Tools"; Command = "func --version"; Required = $true }
        @{ Name = "Azurite"; Command = "azurite --version"; Required = $false }
        @{ Name = "Git"; Command = "git --version"; Required = $false }
    )
    
    foreach ($test in $tests) {
        try {
            $version = Invoke-Expression $test.Command 2>$null
            if ($LASTEXITCODE -eq 0) {
                Add-TestResult "Prerequisite: $($test.Name)" "PASS" "Version: $version"
            } else {
                throw "Command failed"
            }
        } catch {
            $status = if ($test.Required) { "FAIL" } else { "SKIP" }
            Add-TestResult "Prerequisite: $($test.Name)" $status "Not found or not working"
        }
    }
}

# Main execution function
function Main {
    Write-Host "RagSearch Azure Functions Test Suite" -ForegroundColor Green
    Write-Host "====================================" -ForegroundColor Green
    
    Initialize-Logging
    
    # Run tests based on type
    switch ($TestType) {
        "All" {
            Test-Prerequisites
        }
        "Unit" {
            Test-Prerequisites
        }
        "Integration" {
            Test-Prerequisites
        }
        "Local" {
            Test-Prerequisites
        }
        "Http" {
            Test-Prerequisites
        }
        "Debug" {
            Test-Prerequisites
        }
    }
    
    # Summary
    $totalTests = $Script:TestResults.Count
    $passedTests = ($Script:TestResults | Where-Object { $_.Status -eq "PASS" }).Count
    $failedTests = ($Script:TestResults | Where-Object { $_.Status -eq "FAIL" }).Count
    
    Write-Host ""
    Write-Host "Test Execution Complete" -ForegroundColor Green
    Write-Host "======================" -ForegroundColor Green
    Write-Host "Total Tests: $totalTests" -ForegroundColor Cyan
    Write-Host "Passed: $passedTests" -ForegroundColor Green
    Write-Host "Failed: $failedTests" -ForegroundColor $(if ($failedTests -gt 0) { "Red" } else { "Green" })
    Write-Host "Log File: $Script:LogFile" -ForegroundColor Gray
    
    # Exit with appropriate code
    if ($failedTests -gt 0) {
        exit 1
    } else {
        exit 0
    }
}

Main