# Debug script for Azure Functions development
# Provides detailed diagnostics and troubleshooting information

param(
    [Parameter(Mandatory=$false)]
    [switch]$Detailed,
    
    [Parameter(Mandatory=$false)]
    [switch]$ShowLogs,
    
    [Parameter(Mandatory=$false)]
    [switch]$TestConnections
)

$ErrorActionPreference = "SilentlyContinue"

function Write-DebugInfo {
    param(
        [string]$Message,
        [string]$Level = "INFO"
    )
    
    $timestamp = Get-Date -Format "HH:mm:ss"
    $color = switch ($Level) {
        "SUCCESS" { "Green" }
        "WARNING" { "Yellow" }
        "ERROR" { "Red" }
        "DEBUG" { "Gray" }
        default { "White" }
    }
    
    Write-Host "[$timestamp] $Message" -ForegroundColor $color
}

function Test-ServiceHealth {
    Write-DebugInfo "Checking service health..." "INFO"
    
    # Check Azure Functions
    try {
        $response = Invoke-WebRequest -Uri "http://localhost:7071/admin/host/status" -TimeoutSec 5 -ErrorAction Stop
        Write-DebugInfo "Azure Functions: Running (Status: $($response.StatusCode))" "SUCCESS"
        
        if ($Detailed) {
            $content = $response.Content | ConvertFrom-Json
            Write-DebugInfo "  Version: $($content.version)" "DEBUG"
            Write-DebugInfo "  State: $($content.state)" "DEBUG"
        }
    } catch {
        Write-DebugInfo "Azure Functions: Not responding" "ERROR"
        Write-DebugInfo "  Error: $($_.Exception.Message)" "ERROR"
    }
    
    # Check Azurite services
    $azuriteServices = @(
        @{ Name = "Blob"; Port = 10000 }
        @{ Name = "Queue"; Port = 10001 }
        @{ Name = "Table"; Port = 10002 }
    )
    
    foreach ($service in $azuriteServices) {
        try {
            $response = Invoke-WebRequest -Uri "http://127.0.0.1:$($service.Port)" -TimeoutSec 3 -ErrorAction Stop
            Write-DebugInfo "Azurite $($service.Name): Running (Status: $($response.StatusCode))" "SUCCESS"
        } catch {
            Write-DebugInfo "Azurite $($service.Name): Not responding" "ERROR"
        }
    }
}

function Show-ProcessInfo {
    Write-DebugInfo "Checking running processes..." "INFO"
    
    # Azure Functions processes
    $funcProcesses = Get-Process -Name "func" -ErrorAction SilentlyContinue
    if ($funcProcesses) {
        foreach ($proc in $funcProcesses) {
            Write-DebugInfo "Functions Process: PID $($proc.Id), Memory: $([math]::Round($proc.WorkingSet64/1MB, 2)) MB" "SUCCESS"
            if ($Detailed) {
                Write-DebugInfo "  Start Time: $($proc.StartTime)" "DEBUG"
                Write-DebugInfo "  CPU Time: $($proc.TotalProcessorTime)" "DEBUG"
            }
        }
    } else {
        Write-DebugInfo "No Functions processes found" "WARNING"
    }
    
    # Azurite processes
    $azuriteProcesses = Get-Process -Name "azurite" -ErrorAction SilentlyContinue
    if ($azuriteProcesses) {
        foreach ($proc in $azuriteProcesses) {
            Write-DebugInfo "Azurite Process: PID $($proc.Id), Memory: $([math]::Round($proc.WorkingSet64/1MB, 2)) MB" "SUCCESS"
            if ($Detailed) {
                Write-DebugInfo "  Start Time: $($proc.StartTime)" "DEBUG"
                Write-DebugInfo "  CPU Time: $($proc.TotalProcessorTime)" "DEBUG"
            }
        }
    } else {
        Write-DebugInfo "No Azurite processes found" "WARNING"
    }
}

function Show-PortStatus {
    Write-DebugInfo "Checking port status..." "INFO"
    
    $ports = @(
        @{ Port = 7071; Service = "Azure Functions" }
        @{ Port = 10000; Service = "Azurite Blob" }
        @{ Port = 10001; Service = "Azurite Queue" }
        @{ Port = 10002; Service = "Azurite Table" }
    )
    
    foreach ($portInfo in $ports) {
        try {
            $connection = Test-NetConnection -ComputerName "localhost" -Port $portInfo.Port -InformationLevel Quiet -WarningAction SilentlyContinue
            if ($connection) {
                Write-DebugInfo "Port $($portInfo.Port) ($($portInfo.Service)): Open" "SUCCESS"
            } else {
                Write-DebugInfo "Port $($portInfo.Port) ($($portInfo.Service)): Closed" "WARNING"
            }
        } catch {
            Write-DebugInfo "Port $($portInfo.Port) ($($portInfo.Service)): Cannot test" "ERROR"
        }
    }
}

function Show-ConfigurationInfo {
    Write-DebugInfo "Checking configuration files..." "INFO"
    
    # Check host.json
    if (Test-Path "host.json") {
        Write-DebugInfo "host.json: Found" "SUCCESS"
        if ($Detailed) {
            try {
                $hostConfig = Get-Content "host.json" | ConvertFrom-Json
                Write-DebugInfo "  Functions Version: $($hostConfig.version)" "DEBUG"
                Write-DebugInfo "  Extensions Bundle: $($hostConfig.extensionBundle.id)" "DEBUG"
            } catch {
                Write-DebugInfo "  Error reading host.json: $($_.Exception.Message)" "ERROR"
            }
        }
    } else {
        Write-DebugInfo "host.json: Missing" "ERROR"
    }
    
    # Check local.settings.json
    if (Test-Path "local.settings.json") {
        Write-DebugInfo "local.settings.json: Found" "SUCCESS"
        if ($Detailed) {
            try {
                $localSettings = Get-Content "local.settings.json" | ConvertFrom-Json
                Write-DebugInfo "  Runtime: $($localSettings.Values.FUNCTIONS_WORKER_RUNTIME)" "DEBUG"
                Write-DebugInfo "  Storage: $($localSettings.Values.AzureWebJobsStorage)" "DEBUG"
                if ($localSettings.Values.AZURE_OPENAI_API_KEY) {
                    Write-DebugInfo "  OpenAI Key: Configured" "DEBUG"
                } else {
                    Write-DebugInfo "  OpenAI Key: Not configured" "WARNING"
                }
            } catch {
                Write-DebugInfo "  Error reading local.settings.json: $($_.Exception.Message)" "ERROR"
            }
        }
    } else {
        Write-DebugInfo "local.settings.json: Missing" "ERROR"
    }
}

function Show-LogFiles {
    if (-not $ShowLogs) { return }
    
    Write-DebugInfo "Checking log files..." "INFO"
    
    # Azurite logs
    if (Test-Path "azurite\debug.log") {
        Write-DebugInfo "Azurite debug log found" "SUCCESS"
        Write-DebugInfo "Recent Azurite log entries:" "INFO"
        Get-Content "azurite\debug.log" -Tail 10 | ForEach-Object {
            Write-DebugInfo "  $_" "DEBUG"
        }
    } else {
        Write-DebugInfo "No Azurite debug log found" "WARNING"
    }
    
    # Function logs (if any)
    $logFiles = Get-ChildItem -Path "." -Filter "*.log" -Recurse | Where-Object { $_.Name -notlike "*debug*" }
    if ($logFiles) {
        Write-DebugInfo "Found $($logFiles.Count) additional log files" "INFO"
        foreach ($logFile in $logFiles) {
            Write-DebugInfo "  $($logFile.FullName)" "DEBUG"
        }
    }
}

function Test-FunctionEndpoints {
    if (-not $TestConnections) { return }
    
    Write-DebugInfo "Testing function endpoints..." "INFO"
    
    $endpoints = @(
        @{ Name = "HttpExample"; Url = "http://localhost:7071/api/HttpExample" }
        @{ Name = "Search"; Url = "http://localhost:7071/api/Search?q=test" }
        @{ Name = "SearchStatus"; Url = "http://localhost:7071/api/SearchStatus" }
    )
    
    foreach ($endpoint in $endpoints) {
        try {
            $response = Invoke-WebRequest -Uri $endpoint.Url -Method GET -TimeoutSec 10 -ErrorAction Stop
            Write-DebugInfo "$($endpoint.Name): Responding (Status: $($response.StatusCode))" "SUCCESS"
            
            if ($Detailed -and $response.Content) {
                $contentPreview = if ($response.Content.Length -gt 100) { 
                    $response.Content.Substring(0, 100) + "..." 
                } else { 
                    $response.Content 
                }
                Write-DebugInfo "  Response: $contentPreview" "DEBUG"
            }
        } catch {
            Write-DebugInfo "$($endpoint.Name): Not responding" "ERROR"
            Write-DebugInfo "  Error: $($_.Exception.Message)" "ERROR"
        }
    }
}

function Show-SystemInfo {
    if (-not $Detailed) { return }
    
    Write-DebugInfo "System Information:" "INFO"
    Write-DebugInfo "  OS: $([System.Environment]::OSVersion.VersionString)" "DEBUG"
    Write-DebugInfo "  .NET Version: $([System.Environment]::Version)" "DEBUG"
    Write-DebugInfo "  Machine Name: $([System.Environment]::MachineName)" "DEBUG"
    Write-DebugInfo "  Current Directory: $(Get-Location)" "DEBUG"
    Write-DebugInfo "  Available Memory: $([math]::Round((Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory/1MB, 2)) GB" "DEBUG"
}

# Main execution
Write-Host ""
Write-Host "RagSearch Azure Functions Debug Information" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host ""

Show-SystemInfo
Test-ServiceHealth
Show-ProcessInfo
Show-PortStatus
Show-ConfigurationInfo
Show-LogFiles
Test-FunctionEndpoints

Write-Host ""
Write-DebugInfo "Debug information complete" "SUCCESS"
Write-Host ""