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

# Build project
function Test-ProjectBuild {
    Write-LogMessage "Building project..." "Info"
    
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    
    try {
        $buildOutput = dotnet build --configuration Release --verbosity minimal 2>&1
        $stopwatch.Stop()
        
        if ($LASTEXITCODE -eq 0) {
            Add-TestResult "Project Build" "PASS" "Build completed successfully" $stopwatch.Elapsed
        } else {
            Add-TestResult "Project Build" "FAIL" "Build failed" $stopwatch.Elapsed $buildOutput
        }
    } catch {
        $stopwatch.Stop()
        Add-TestResult "Project Build" "FAIL" "Build exception" $stopwatch.Elapsed $_.Exception.Message
    }
}

# Run unit tests
function Test-UnitTests {
    Write-LogMessage "Running unit tests..." "Info"
    
    # Check if test project exists
    $testProjects = Get-ChildItem -Path . -Recurse -Filter "*.Tests.csproj"
    
    if ($testProjects.Count -eq 0) {
        Add-TestResult "Unit Tests" "SKIP" "No test projects found"
        return
    }
    
    foreach ($testProject in $testProjects) {
        $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
        
        try {
            $testOutput = dotnet test $testProject.FullName --configuration Release --logger "console;verbosity=detailed" --collect:"XPlat Code Coverage" 2>&1
            $stopwatch.Stop()
            
            if ($LASTEXITCODE -eq 0) {
                # Parse test results
                $passedTests = ($testOutput | Select-String "Passed!").Count
                $failedTests = ($testOutput | Select-String "Failed!").Count
                
                Add-TestResult "Unit Tests: $($testProject.BaseName)" "PASS" "Passed: $passedTests, Failed: $failedTests" $stopwatch.Elapsed
            } else {
                Add-TestResult "Unit Tests: $($testProject.BaseName)" "FAIL" "Tests failed" $stopwatch.Elapsed $testOutput
            }
        } catch {
            $stopwatch.Stop()
            Add-TestResult "Unit Tests: $($testProject.BaseName)" "FAIL" "Test execution exception" $stopwatch.Elapsed $_.Exception.Message
        }
    }
}

# Test Azurite connectivity
function Test-AzuriteConnection {
    Write-LogMessage "Testing Azurite connection..." "Info"
    
    $azuriteEndpoints = @(
        @{ Name = "Blob Service"; Url = "http://127.0.0.1:10000" }
        @{ Name = "Queue Service"; Url = "http://127.0.0.1:10001" }
        @{ Name = "Table Service"; Url = "http://127.0.0.1:10002" }
    )
    
    foreach ($endpoint in $azuriteEndpoints) {
        try {
            $response = Invoke-WebRequest -Uri $endpoint.Url -Method GET -TimeoutSec 5 -ErrorAction Stop
            Add-TestResult "Azurite: $($endpoint.Name)" "PASS" "HTTP Status: $($response.StatusCode)"
        } catch {
            Add-TestResult "Azurite: $($endpoint.Name)" "FAIL" "Connection failed" -ErrorMessage $_.Exception.Message
        }
    }
}

# Start Azure Functions locally (if not running)
function Start-FunctionsLocally {
    Write-LogMessage "Checking if Azure Functions are running..." "Info"
    
    try {
        $response = Invoke-WebRequest -Uri "$FunctionUrl/admin/host/status" -TimeoutSec 5 -ErrorAction Stop
        Add-TestResult "Functions Runtime" "PASS" "Already running"
        return $true
    } catch {
        Write-LogMessage "Functions not running, attempting to start..." "Warning"
    }
    
    # Check if Azurite is running, start if needed
    try {
        Invoke-WebRequest -Uri "http://127.0.0.1:10000" -TimeoutSec 2 -ErrorAction Stop | Out-Null
        Write-LogMessage "Azurite is running" "Success"
    } catch {
        Write-LogMessage "Starting Azurite..." "Info"
        Start-Process -FilePath "azurite" -ArgumentList "--silent", "--location", "azurite", "--debug", "azurite\debug.log" -WindowStyle Hidden
        Start-Sleep -Seconds 3
    }
    
    # Start Functions
    Write-LogMessage "Starting Azure Functions..." "Info"
    $funcProcess = Start-Process -FilePath "func" -ArgumentList "start" -PassThru -WindowStyle Hidden
    
    # Wait for startup
    $timeout = 30
    $elapsed = 0
    
    while ($elapsed -lt $timeout) {
        Start-Sleep -Seconds 2
        $elapsed += 2
        
        try {
            $response = Invoke-WebRequest -Uri "$FunctionUrl/admin/host/status" -TimeoutSec 5 -ErrorAction Stop
            Add-TestResult "Functions Startup" "PASS" "Started successfully after $elapsed seconds"
            return $true
        } catch {
            # Continue waiting
        }
    }
    
    Add-TestResult "Functions Startup" "FAIL" "Failed to start within $timeout seconds"
    return $false
}

# Test HTTP functions
function Test-HttpFunctions {
    Write-LogMessage "Testing HTTP functions..." "Info"
    
    if (-not (Start-FunctionsLocally)) {
        Add-TestResult "HTTP Function Tests" "SKIP" "Functions not running"
        return
    }
    
    # Test HttpExample function
    $httpTests = @(
        @{ 
            Name = "HttpExample GET"
            Method = "GET"
            Url = "$FunctionUrl/api/HttpExample"
            ExpectedStatus = 200
        }
        @{ 
            Name = "HttpExample POST"
            Method = "POST"
            Url = "$FunctionUrl/api/HttpExample"
            Body = '{"test": "data"}'
            ContentType = "application/json"
            ExpectedStatus = 200
        }
    )
    
    foreach ($test in $httpTests) {
        $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
        
        try {
            $params = @{
                Uri = $test.Url
                Method = $test.Method
                TimeoutSec = 10
                ErrorAction = "Stop"
            }
            
            if ($test.Body) {
                $params.Body = $test.Body
                $params.ContentType = $test.ContentType
            }
            
            $response = Invoke-WebRequest @params
            $stopwatch.Stop()
            
            if ($response.StatusCode -eq $test.ExpectedStatus) {
                $details = "Status: $($response.StatusCode), Length: $($response.Content.Length)"
                Add-TestResult $test.Name "PASS" $details $stopwatch.Elapsed
            } else {
                Add-TestResult $test.Name "FAIL" "Expected: $($test.ExpectedStatus), Got: $($response.StatusCode)" $stopwatch.Elapsed
            }
        } catch {
            $stopwatch.Stop()
            Add-TestResult $test.Name "FAIL" "Request failed" $stopwatch.Elapsed $_.Exception.Message
        }
    }
}

# Test RAG Search functions
function Test-RagSearchFunctions {
    Write-LogMessage "Testing RAG Search functions..." "Info"
    
    if (-not (Start-FunctionsLocally)) {
        Add-TestResult "RAG Search Function Tests" "SKIP" "Functions not running"
        return
    }
    
    # Test Search Status endpoint first
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        $response = Invoke-WebRequest -Uri "$FunctionUrl/api/SearchStatus" -Method GET -TimeoutSec 15 -ErrorAction Stop
        $stopwatch.Stop()
        
        if ($response.StatusCode -eq 200) {
            $content = $response.Content | ConvertFrom-Json
            $details = "Index: $($content.indexName), Docs: $($content.documentCount), Storage: $($content.persistentStorage)"
            Add-TestResult "Search Status Check" "PASS" $details $stopwatch.Elapsed
        } else {
            Add-TestResult "Search Status Check" "FAIL" "Unexpected status: $($response.StatusCode)" $stopwatch.Elapsed
        }
    } catch {
        $stopwatch.Stop()
        Add-TestResult "Search Status Check" "FAIL" "Request failed" $stopwatch.Elapsed $_.Exception.Message
    }
    
    # Test adding sample documents to persistent index
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        $response = Invoke-WebRequest -Uri "$FunctionUrl/api/AddTestDocuments" -Method POST -TimeoutSec 30 -ErrorAction Stop
        $stopwatch.Stop()
        
        if ($response.StatusCode -eq 200) {
            $content = $response.Content | ConvertFrom-Json
            $details = "Indexed: $($content.indexedDocuments)/$($content.totalDocuments) docs, Persistent: $($content.persistentStorage)"
            Add-TestResult "Add Test Documents" "PASS" $details $stopwatch.Elapsed
        } else {
            Add-TestResult "Add Test Documents" "FAIL" "Unexpected status: $($response.StatusCode)" $stopwatch.Elapsed
        }
    } catch {
        $stopwatch.Stop()
        Add-TestResult "Add Test Documents" "FAIL" "Request failed" $stopwatch.Elapsed $_.Exception.Message
    }
    
    # Test keyword search functionality
    $searchTests = @(
        @{ 
            Name = "Keyword Search - Azure Functions"
            Query = "Azure Functions"
            SearchType = "keyword"
            ExpectedMinResults = 1
        }
        @{ 
            Name = "Keyword Search - Serverless"
            Query = "serverless"
            SearchType = "keyword"
            ExpectedMinResults = 1
        }
        @{ 
            Name = "Keyword Search - RAG"
            Query = "retrieval augmented generation"
            SearchType = "keyword"
            ExpectedMinResults = 1
        }
    )
    
    foreach ($test in $searchTests) {
        $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
        
        try {
            $searchUrl = "$FunctionUrl/api/Search?q=$([System.Uri]::EscapeDataString($test.Query))&type=$($test.SearchType)"
            $response = Invoke-WebRequest -Uri $searchUrl -Method GET -TimeoutSec 15 -ErrorAction Stop
            $stopwatch.Stop()
            
            if ($response.StatusCode -eq 200) {
                $content = $response.Content | ConvertFrom-Json
                if ($content.totalResults -ge $test.ExpectedMinResults) {
                    $details = "Found: $($content.totalResults) results, Time: $($content.executionTimeMs)ms, Type: $($content.searchType)"
                    Add-TestResult $test.Name "PASS" $details $stopwatch.Elapsed
                } else {
                    Add-TestResult $test.Name "FAIL" "Expected >= $($test.ExpectedMinResults), Got: $($content.totalResults)" $stopwatch.Elapsed
                }
            } else {
                Add-TestResult $test.Name "FAIL" "Unexpected status: $($response.StatusCode)" $stopwatch.Elapsed
            }
        } catch {
            $stopwatch.Stop()
            Add-TestResult $test.Name "FAIL" "Request failed" $stopwatch.Elapsed $_.Exception.Message
        }
    }
    
    # Test POST search with JSON body
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        $searchBody = @{
            query = "Azure AI Search"
            searchType = "keyword"
            contentTypes = @("text")
            maxResults = 5
        } | ConvertTo-Json
        
        $response = Invoke-WebRequest -Uri "$FunctionUrl/api/Search" -Method POST -Body $searchBody -ContentType "application/json" -TimeoutSec 15 -ErrorAction Stop
        $stopwatch.Stop()
        
        if ($response.StatusCode -eq 200) {
            $content = $response.Content | ConvertFrom-Json
            $details = "Found: $($content.totalResults) results, Max: $($content.results.Length), Time: $($content.executionTimeMs)ms"
            Add-TestResult "POST Search with JSON" "PASS" $details $stopwatch.Elapsed
        } else {
            Add-TestResult "POST Search with JSON" "FAIL" "Unexpected status: $($response.StatusCode)" $stopwatch.Elapsed
        }
    } catch {
        $stopwatch.Stop()
        Add-TestResult "POST Search with JSON" "FAIL" "Request failed" $stopwatch.Elapsed $_.Exception.Message
    }
    
    # Clean up test documents
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        $response = Invoke-WebRequest -Uri "$FunctionUrl/api/ClearTestDocuments" -Method DELETE -TimeoutSec 15 -ErrorAction Stop
        $stopwatch.Stop()
        
        if ($response.StatusCode -eq 200) {
            $content = $response.Content | ConvertFrom-Json
            $details = "Deleted: $($content.deletedDocuments)/$($content.totalAttempted) docs"
            Add-TestResult "Clear Test Documents" "PASS" $details $stopwatch.Elapsed
        } else {
            Add-TestResult "Clear Test Documents" "PASS" "Cleanup completed (status: $($response.StatusCode))" $stopwatch.Elapsed
        }
    } catch {
        $stopwatch.Stop()
        Add-TestResult "Clear Test Documents" "WARN" "Cleanup failed (not critical)" $stopwatch.Elapsed $_.Exception.Message
    }
}

# Test local development environment
function Test-LocalEnvironment {
    Write-LogMessage "Testing local development environment..." "Info"
    
    # Check required files
    $requiredFiles = @(
        "host.json",
        "local.settings.json",
        "RagSearch.csproj",
        "Program.cs"
    )
    
    foreach ($file in $requiredFiles) {
        if (Test-Path $file) {
            Add-TestResult "Required File: $file" "PASS" "File exists"
        } else {
            Add-TestResult "Required File: $file" "FAIL" "File missing"
        }
    }
    
    # Check configuration files
    try {
        $hostJson = Get-Content "host.json" | ConvertFrom-Json
        Add-TestResult "host.json Validation" "PASS" "Valid JSON configuration"
    } catch {
        Add-TestResult "host.json Validation" "FAIL" "Invalid JSON" -ErrorMessage $_.Exception.Message
    }
    
    try {
        $localSettings = Get-Content "local.settings.json" | ConvertFrom-Json
        $storageConnection = $localSettings.Values.AzureWebJobsStorage
        
        if ($storageConnection -eq "UseDevelopmentStorage=true") {
            Add-TestResult "Storage Configuration" "PASS" "Using development storage"
        } else {
            Add-TestResult "Storage Configuration" "WARNING" "Using custom storage connection"
        }
    } catch {
        Add-TestResult "local.settings.json Validation" "FAIL" "Invalid JSON" -ErrorMessage $_.Exception.Message
    }
}

# Debug function execution
function Test-DebugExecution {
    Write-LogMessage "Running debug diagnostics..." "Info"
    
    # Check processes
    $azuriteProcess = Get-Process -Name "azurite" -ErrorAction SilentlyContinue
    if ($azuriteProcess) {
        Add-TestResult "Azurite Process" "PASS" "Running (PID: $($azuriteProcess.Id))"
    } else {
        Add-TestResult "Azurite Process" "FAIL" "Not running"
    }
    
    $funcProcess = Get-Process -Name "func" -ErrorAction SilentlyContinue
    if ($funcProcess) {
        Add-TestResult "Functions Process" "PASS" "Running (PID: $($funcProcess.Id))"
    } else {
        Add-TestResult "Functions Process" "FAIL" "Not running"
    }
    
    # Check ports
    $ports = @(7071, 10000, 10001, 10002)
    foreach ($port in $ports) {
        try {
            $connection = Test-NetConnection -ComputerName "localhost" -Port $port -InformationLevel Quiet -WarningAction SilentlyContinue
            if ($connection) {
                Add-TestResult "Port $port" "PASS" "Port is open"
            } else {
                Add-TestResult "Port $port" "FAIL" "Port is closed"
            }
        } catch {
            Add-TestResult "Port $port" "FAIL" "Cannot test port"
        }
    }
    
    # Check log files
    if (Test-Path "azurite\debug.log") {
        $logContent = Get-Content "azurite\debug.log" -Tail 10
        Add-TestResult "Azurite Logs" "PASS" "Log file exists, last entries available"
        
        if ($DetailedOutput) {
            Write-LogMessage "Recent Azurite log entries:" "Debug"
            $logContent | ForEach-Object { Write-LogMessage "  $_" "Debug" }
        }
    } else {
        Add-TestResult "Azurite Logs" "WARNING" "No log file found"
    }
}

# Generate HTML report
function Generate-HtmlReport {
    if (-not $GenerateReport) { return }
    
    Write-LogMessage "Generating HTML report..." "Info"
    
    $totalTests = $Script:TestResults.Count
    $passedTests = ($Script:TestResults | Where-Object { $_.Status -eq "PASS" }).Count
    $failedTests = ($Script:TestResults | Where-Object { $_.Status -eq "FAIL" }).Count
    $skippedTests = ($Script:TestResults | Where-Object { $_.Status -eq "SKIP" }).Count
    
    $html = @"
<!DOCTYPE html>
<html>
<head>
    <title>RagSearch Azure Functions Test Report</title>
    <style>
        body { font-family: Arial, sans-serif; margin: 20px; }
        .header { background-color: #f0f0f0; padding: 20px; border-radius: 5px; }
        .summary { margin: 20px 0; }
        .test-results { margin: 20px 0; }
        table { border-collapse: collapse; width: 100%; }
        th, td { border: 1px solid #ddd; padding: 8px; text-align: left; }
        th { background-color: #f2f2f2; }
        .pass { background-color: #d4edda; }
        .fail { background-color: #f8d7da; }
        .skip { background-color: #fff3cd; }
        .warning { background-color: #fff3cd; }
    </style>
</head>
<body>
    <div class="header">
        <h1>RagSearch Azure Functions Test Report</h1>
        <p>Generated: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")</p>
        <p>Test Type: $TestType</p>
        <p>Duration: $([math]::Round(((Get-Date) - $Script:StartTime).TotalSeconds, 2)) seconds</p>
    </div>
    
    <div class="summary">
        <h2>Test Summary</h2>
        <p>Total Tests: $totalTests</p>
        <p>Passed: $passedTests</p>
        <p>Failed: $failedTests</p>
        <p>Skipped: $skippedTests</p>
        <p>Success Rate: $([math]::Round(($passedTests / $totalTests) * 100, 1))%</p>
    </div>
    
    <div class="test-results">
        <h2>Test Results</h2>
        <table>
            <tr>
                <th>Test Name</th>
                <th>Status</th>
                <th>Duration</th>
                <th>Details</th>
                <th>Error Message</th>
            </tr>
"@

    foreach ($result in $Script:TestResults) {
        $cssClass = $result.Status.ToLower()
        $duration = if ($result.Duration.TotalSeconds -gt 0) { "$([math]::Round($result.Duration.TotalSeconds, 2))s" } else { "-" }
        
        $html += @"
            <tr class="$cssClass">
                <td>$($result.TestName)</td>
                <td>$($result.Status)</td>
                <td>$duration</td>
                <td>$($result.Details)</td>
                <td>$($result.ErrorMessage)</td>
            </tr>
"@
    }

    $html += @"
        </table>
    </div>
</body>
</html>
"@

    $html | Out-File -FilePath $Script:ReportFile -Encoding UTF8
    Write-LogMessage "HTML report generated: $Script:ReportFile" "Success"
}

# Main execution
function Main {
    Write-Host "RagSearch Azure Functions Test Suite" -ForegroundColor Green
    Write-Host "====================================" -ForegroundColor Green
    
    Initialize-Logging
    
    # Run tests based on type
    switch ($TestType) {
        "All" {
            Test-Prerequisites
            Test-ProjectBuild
            Test-LocalEnvironment
            Test-AzuriteConnection
            Test-UnitTests
            Test-HttpFunctions
            Test-RagSearchFunctions
            Test-DebugExecution
        }
        "Unit" {
            Test-Prerequisites
            Test-ProjectBuild
            Test-UnitTests
        }
        "Integration" {
            Test-Prerequisites
            Test-ProjectBuild
            Test-LocalEnvironment
            Test-AzuriteConnection
            Test-HttpFunctions
            Test-RagSearchFunctions
        }
        "Local" {
            Test-Prerequisites
            Test-LocalEnvironment
            Test-AzuriteConnection
        }
        "Http" {
            Test-Prerequisites
            Test-HttpFunctions
            Test-RagSearchFunctions
        }
        "Debug" {
            Test-Prerequisites
            Test-LocalEnvironment
            Test-AzuriteConnection
            Test-DebugExecution
        }
    }
    
    # Generate report
    Generate-HtmlReport
    
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
    
    if ($GenerateReport) {
        Write-Host "HTML Report: $Script:ReportFile" -ForegroundColor Gray
    }
    
    # Exit with appropriate code
    if ($failedTests -gt 0) {
        exit 1
    } else {
        exit 0
    }
}

# Test search service comparison between simplified and Azure Search
function Test-SearchServiceComparison {
    Write-LogMessage "Testing search service comparison" "Info"
    
    try {
        # Test with simplified search service
        Write-Host "Testing Simplified Search Service..." -ForegroundColor Yellow
        
        $simplifiedResults = @()
        $testQueries = @(
            @{ Query = "azure functions"; Type = "keyword" },
            @{ Query = "deployment best practices"; Type = "vector" },
            @{ Query = "serverless architecture"; Type = "hybrid" }
        )
        
        foreach ($testQuery in $testQueries) {
            $searchBody = @{
                query = $testQuery.Query
                searchType = $testQuery.Type
                maxResults = 5
            } | ConvertTo-Json
            
            try {
                $startTime = Get-Date
                $response = Invoke-RestMethod -Uri "$Script:FunctionUrl/api/search" -Method POST -Body $searchBody -ContentType "application/json" -TimeoutSec 30
                $endTime = Get-Date
                $executionTime = ($endTime - $startTime).TotalMilliseconds
                
                $simplifiedResults += @{
                    Query = $testQuery.Query
                    Type = $testQuery.Type
                    ExecutionTime = $executionTime
                    ResultCount = $response.totalResults
                    Success = $true
                }
                
                Write-Host "  ✓ $($testQuery.Type) search: $($response.totalResults) results in $($executionTime)ms" -ForegroundColor Green
            }
            catch {
                $simplifiedResults += @{
                    Query = $testQuery.Query
                    Type = $testQuery.Type
                    ExecutionTime = 0
                    ResultCount = 0
                    Success = $false
                    Error = $_.Exception.Message
                }
                Write-Host "  ✗ $($testQuery.Type) search failed: $($_.Exception.Message)" -ForegroundColor Red
            }
        }
        
        # Test vector search capabilities specifically
        Write-Host "Testing Vector Search with Embeddings..." -ForegroundColor Yellow
        
        $embeddingTest = @{
            query = "How to deploy Azure Functions with containers"
            searchType = "vector"
            maxResults = 3
        } | ConvertTo-Json
        
        try {
            $vectorResponse = Invoke-RestMethod -Uri "$Script:FunctionUrl/api/search" -Method POST -Body $embeddingTest -ContentType "application/json" -TimeoutSec 45
            Write-Host "  ✓ Vector search with embeddings: $($vectorResponse.totalResults) results" -ForegroundColor Green
            Write-Host "  ✓ Execution time: $($vectorResponse.executionTimeMs)ms" -ForegroundColor Green
            
            if ($vectorResponse.results -and $vectorResponse.results.Count -gt 0) {
                Write-Host "  ✓ First result score: $($vectorResponse.results[0].score)" -ForegroundColor Green
            }
        }
        catch {
            Write-Host "  ✗ Vector search failed: $($_.Exception.Message)" -ForegroundColor Red
        }
        
        # Test persistence by checking index statistics
        Write-Host "Testing Data Persistence..." -ForegroundColor Yellow
        
        try {
            $statsResponse = Invoke-RestMethod -Uri "$Script:FunctionUrl/api/search/status" -Method GET -TimeoutSec 15
            Write-Host "  ✓ Index contains $($statsResponse.documentCount) documents" -ForegroundColor Green
            Write-Host "  ✓ Storage size: $($statsResponse.storageSize) bytes" -ForegroundColor Green
        }
        catch {
            Write-Host "  ✗ Failed to get index statistics: $($_.Exception.Message)" -ForegroundColor Red
        }
        
        # Summary
        $successfulTests = ($simplifiedResults | Where-Object { $_.Success -eq $true }).Count
        $totalTests = $simplifiedResults.Count
        
        Write-Host ""
        Write-Host "Search Service Test Summary:" -ForegroundColor Cyan
        Write-Host "  Successful tests: $successfulTests/$totalTests" -ForegroundColor $(if ($successfulTests -eq $totalTests) { "Green" } else { "Yellow" })
        
        if ($successfulTests -gt 0) {
            $avgExecutionTime = ($simplifiedResults | Where-Object { $_.Success -eq $true } | Measure-Object -Property ExecutionTime -Average).Average
            Write-Host "  Average execution time: $([math]::Round($avgExecutionTime, 2))ms" -ForegroundColor Green
        }
        
        return $successfulTests -gt 0
    }
    catch {
        Write-LogMessage "Search service comparison test failed: $($_.Exception.Message)" "Error"
        return $false
    }
}

# Execute main function
Main
