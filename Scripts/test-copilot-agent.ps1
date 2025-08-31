#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Test Copilot Agent services and MCP/MS Docs connectivity
.DESCRIPTION
    Comprehensive testing script for the Copilot Agent environment including:
    - MCP server configuration and health checks
    - MS Docs connectivity and caching
    - Rate limiting and circuit breaker functionality
    - Secure logging and metrics collection
.PARAMETER TestType
    Type of test to run: All, Health, MCP, MSdocs, Cache, Metrics, Security
.PARAMETER DetailedOutput
    Show detailed test output and responses
.PARAMETER BaseUrl
    Base URL for the function app (default: http://localhost:7071)
.PARAMETER FunctionKey
    Function key for authenticated endpoints
.EXAMPLE
    .\test-copilot-agent.ps1 -TestType All -DetailedOutput
.EXAMPLE
    .\test-copilot-agent.ps1 -TestType Health -BaseUrl "https://myapp.azurewebsites.net" -FunctionKey "abc123"
#>

param(
    [Parameter(Mandatory=$false)]
    [ValidateSet("All", "Health", "MCP", "MSdocs", "Cache", "Metrics", "Security")]
    [string]$TestType = "All",
    
    [Parameter(Mandatory=$false)]
    [switch]$DetailedOutput,
    
    [Parameter(Mandatory=$false)]
    [string]$BaseUrl = "http://localhost:7071",
    
    [Parameter(Mandatory=$false)]
    [string]$FunctionKey = ""
)

# Colors for output
$Green = "`e[32m"
$Red = "`e[31m"
$Yellow = "`e[33m"
$Blue = "`e[34m"
$Reset = "`e[0m"

# Test results tracking
$TestResults = @{
    Passed = 0
    Failed = 0
    Skipped = 0
    Tests = @()
}

function Write-TestHeader {
    param([string]$Title)
    Write-Host ""
    Write-Host "$Blue=== $Title ===$Reset" -ForegroundColor Blue
}

function Write-TestResult {
    param(
        [string]$TestName,
        [bool]$Passed,
        [string]$Details = "",
        [object]$Response = $null
    )
    
    $status = if ($Passed) { "${Green}PASS${Reset}" } else { "${Red}FAIL${Reset}" }
    Write-Host "  $status $TestName"
    
    if ($Details) {
        Write-Host "    $Details" -ForegroundColor Gray
    }
    
    if ($DetailedOutput -and $Response) {
        Write-Host "    Response:" -ForegroundColor Gray
        $Response | ConvertTo-Json -Depth 3 | Write-Host -ForegroundColor Gray
    }
    
    $TestResults.Tests += @{
        Name = $TestName
        Passed = $Passed
        Details = $Details
    }
    
    if ($Passed) {
        $TestResults.Passed++
    } else {
        $TestResults.Failed++
    }
}

function Invoke-AgentApi {
    param(
        [string]$Endpoint,
        [string]$Method = "GET",
        [object]$Body = $null,
        [bool]$RequireAuth = $false
    )
    
    try {
        $headers = @{
            "Content-Type" = "application/json"
            "x-correlation-id" = [Guid]::NewGuid().ToString()
        }
        
        if ($RequireAuth -and $FunctionKey) {
            $headers["x-functions-key"] = $FunctionKey
        }
        
        $uri = "$BaseUrl$Endpoint"
        $params = @{
            Uri = $uri
            Method = $Method
            Headers = $headers
            TimeoutSec = 30
        }
        
        if ($Body) {
            $params.Body = $Body | ConvertTo-Json -Depth 3
        }
        
        $response = Invoke-RestMethod @params
        return @{ Success = $true; Data = $response; StatusCode = 200 }
    }
    catch {
        $statusCode = if ($_.Exception.Response) { $_.Exception.Response.StatusCode.Value__ } else { 0 }
        return @{ 
            Success = $false
            Error = $_.Exception.Message
            StatusCode = $statusCode
            Data = $null
        }
    }
}

function Test-HealthCheck {
    Write-TestHeader "Health Check Tests"
    
    # Test basic health check endpoint
    $result = Invoke-AgentApi -Endpoint "/api/AgentHealthCheck"
    
    if ($result.Success) {
        $health = $result.Data
        
        # Check overall health status
        $overallHealthy = $health.overallStatus -in @("Healthy", "Degraded")
        Write-TestResult "Overall health check" $overallHealthy "Status: $($health.overallStatus)" $health
        
        # Check MCP server health
        if ($health.mcpServerHealth) {
            $mcpHealthy = $health.mcpServerHealth.isHealthy
            Write-TestResult "MCP server health" $mcpHealthy "MCP Status: $($health.mcpServerHealth.status)" $health.mcpServerHealth
        } else {
            Write-TestResult "MCP server health" $false "MCP health data missing"
        }
        
        # Check MS Docs connectivity
        if ($health.msDocsConnectivity) {
            $msDocsHealthy = $health.msDocsConnectivity.isConnected
            Write-TestResult "MS Docs connectivity" $msDocsHealthy "Connected: $($health.msDocsConnectivity.isConnected)" $health.msDocsConnectivity
        } else {
            Write-TestResult "MS Docs connectivity" $false "MS Docs connectivity data missing"
        }
        
        # Check configuration
        if ($health.configuration) {
            $config = $health.configuration
            Write-TestResult "MCP command configured" $config.mcpCommand
            Write-TestResult "Allowed repos configured" $config.allowedReposConfigured
            Write-TestResult "Allowed hosts configured" $config.allowedHostsConfigured
        }
        
        # Check response time
        $responseTimeOk = $health.responseTimeMs -lt 5000
        Write-TestResult "Response time acceptable" $responseTimeOk "Response time: $($health.responseTimeMs)ms"
        
    } else {
        Write-TestResult "Health check endpoint" $false "Error: $($result.Error)"
    }
}

function Test-Metrics {
    Write-TestHeader "Metrics and Observability Tests"
    
    # Test metrics endpoint
    $result = Invoke-AgentApi -Endpoint "/api/AgentMetrics"
    
    if ($result.Success) {
        $metrics = $result.Data
        
        # Check basic metrics structure
        $hasMetrics = $null -ne $metrics.metrics
        Write-TestResult "Metrics collection" $hasMetrics "Metrics available: $hasMetrics" $metrics.metrics
        
        # Check cache statistics
        if ($metrics.cacheStatistics) {
            $cache = $metrics.cacheStatistics
            $cacheWorking = $cache.totalItems -ge 0
            Write-TestResult "Cache statistics" $cacheWorking "Total items: $($cache.totalItems), Hit rate: $($cache.hitRate)" $cache
        } else {
            Write-TestResult "Cache statistics" $false "Cache statistics missing"
        }
        
        # Check rate limit status
        if ($metrics.rateLimitStatus) {
            $rateLimits = $metrics.rateLimitStatus
            $github = $rateLimits.github
            $msdocs = $rateLimits.msdocs
            
            if ($github) {
                Write-TestResult "GitHub rate limit tracking" $true "Remaining: $($github.requestsRemaining), Allowed: $($github.isAllowed)" $github
            }
            
            if ($msdocs) {
                Write-TestResult "MS Docs rate limit tracking" $true "Remaining: $($msdocs.requestsRemaining), Allowed: $($msdocs.isAllowed)" $msdocs
            }
        }
        
        # Check circuit breaker states
        if ($metrics.circuitBreakerStates) {
            $circuits = $metrics.circuitBreakerStates
            $githubCircuit = $circuits.github -eq "Closed"
            $msDocsCircuit = $circuits.msdocs -eq "Closed"
            
            Write-TestResult "GitHub circuit breaker" $githubCircuit "State: $($circuits.github)"
            Write-TestResult "MS Docs circuit breaker" $msDocsCircuit "State: $($circuits.msdocs)"
        }
        
    } else {
        Write-TestResult "Metrics endpoint" $false "Error: $($result.Error)"
    }
}

function Test-CacheManagement {
    Write-TestHeader "Cache Management Tests"
    
    # Test cache cleanup
    $cleanupResult = Invoke-AgentApi -Endpoint "/api/AgentCacheManagement" -Method "POST"
    
    if ($cleanupResult.Success) {
        Write-TestResult "Cache cleanup" $true "Cache cleanup successful" $cleanupResult.Data
    } else {
        $isExpectedError = $cleanupResult.StatusCode -eq 401 -or $cleanupResult.StatusCode -eq 403
        if ($isExpectedError) {
            Write-TestResult "Cache cleanup (auth required)" $true "Authentication required as expected"
        } else {
            Write-TestResult "Cache cleanup" $false "Error: $($cleanupResult.Error)"
        }
    }
    
    # Test cache invalidation (without URL - should return 400)
    $invalidationResult = Invoke-AgentApi -Endpoint "/api/AgentCacheManagement" -Method "DELETE"
    
    $expectBadRequest = $invalidationResult.StatusCode -eq 400
    Write-TestResult "Cache invalidation validation" $expectBadRequest "Returns 400 for missing URL as expected"
    
    # Test cache invalidation with URL
    $testUrl = "https://learn.microsoft.com/test-page"
    $invalidationWithUrlResult = Invoke-AgentApi -Endpoint "/api/AgentCacheManagement" -Method "DELETE" -Body @{ url = $testUrl }
    
    if ($invalidationWithUrlResult.Success) {
        Write-TestResult "Cache invalidation with URL" $true "URL invalidation successful" $invalidationWithUrlResult.Data
    } else {
        $isExpectedError = $invalidationWithUrlResult.StatusCode -eq 401 -or $invalidationWithUrlResult.StatusCode -eq 403
        if ($isExpectedError) {
            Write-TestResult "Cache invalidation (auth required)" $true "Authentication required as expected"
        } else {
            Write-TestResult "Cache invalidation with URL" $false "Error: $($invalidationWithUrlResult.Error)"
        }
    }
}

function Test-McpConnectivity {
    Write-TestHeader "MCP Server Tests"
    
    # Check if MCP server is installed
    try {
        $mcpVersion = & npx @modelcontextprotocol/server-github --version 2>$null
        Write-TestResult "MCP server installation" $true "Version: $mcpVersion"
    }
    catch {
        Write-TestResult "MCP server installation" $false "MCP server not found or not executable"
        return
    }
    
    # Check Node.js version
    try {
        $nodeVersion = & node --version 2>$null
        $versionNumber = $nodeVersion -replace 'v', '' -as [Version]
        $nodeVersionOk = $versionNumber -ge [Version]"18.0.0"
        Write-TestResult "Node.js version" $nodeVersionOk "Version: $nodeVersion (requires 18+)"
    }
    catch {
        Write-TestResult "Node.js installation" $false "Node.js not found"
    }
    
    # Test MCP server help command
    try {
        $mcpHelp = & npx @modelcontextprotocol/server-github --help 2>$null
        $helpWorks = $mcpHelp -like "*github*" -or $mcpHelp -like "*MCP*"
        Write-TestResult "MCP server functionality" $helpWorks "Help command executed successfully"
    }
    catch {
        Write-TestResult "MCP server functionality" $false "Error executing MCP server help"
    }
}

function Test-MsDocsConnectivity {
    Write-TestHeader "MS Docs Connectivity Tests"
    
    # Test direct connectivity to MS Docs endpoints
    $testUrls = @(
        "https://learn.microsoft.com",
        "https://docs.microsoft.com"
    )
    
    foreach ($url in $testUrls) {
        try {
            $response = Invoke-WebRequest -Uri $url -Method Head -TimeoutSec 10 -UseBasicParsing
            $connected = $response.StatusCode -eq 200
            Write-TestResult "Connectivity to $(([Uri]$url).Host)" $connected "Status: $($response.StatusCode)"
        }
        catch {
            Write-TestResult "Connectivity to $(([Uri]$url).Host)" $false "Error: $($_.Exception.Message)"
        }
    }
    
    # Test with User-Agent (simulating function behavior)
    try {
        $headers = @{ "User-Agent" = "RagSearch/1.0 (Copilot Agent Coder)" }
        $response = Invoke-WebRequest -Uri "https://learn.microsoft.com" -Method Head -Headers $headers -TimeoutSec 10 -UseBasicParsing
        $userAgentOk = $response.StatusCode -eq 200
        Write-TestResult "MS Docs with User-Agent" $userAgentOk "Status: $($response.StatusCode)"
    }
    catch {
        Write-TestResult "MS Docs with User-Agent" $false "Error: $($_.Exception.Message)"
    }
}

function Test-SecurityFeatures {
    Write-TestHeader "Security and Compliance Tests"
    
    # Test that health check doesn't expose secrets
    $healthResult = Invoke-AgentApi -Endpoint "/api/AgentHealthCheck"
    
    if ($healthResult.Success) {
        $healthJson = $healthResult.Data | ConvertTo-Json -Depth 10
        
        # Check for common secret patterns
        $secretPatterns = @(
            "Bearer\s+[A-Za-z0-9\-_]+",
            "ghp_[A-Za-z0-9]+",
            "[Aa]pi[_-]?[Kk]ey",
            "[Pp]assword",
            "[Ss]ecret"
        )
        
        $secretFound = $false
        foreach ($pattern in $secretPatterns) {
            if ($healthJson -match $pattern) {
                $secretFound = $true
                break
            }
        }
        
        Write-TestResult "Secret redaction in health check" (-not $secretFound) "No secrets found in response"
    }
    
    # Test metrics endpoint for secrets
    $metricsResult = Invoke-AgentApi -Endpoint "/api/AgentMetrics"
    
    if ($metricsResult.Success) {
        $metricsJson = $metricsResult.Data | ConvertTo-Json -Depth 10
        
        $secretFound = $false
        foreach ($pattern in $secretPatterns) {
            if ($metricsJson -match $pattern) {
                $secretFound = $true
                break
            }
        }
        
        Write-TestResult "Secret redaction in metrics" (-not $secretFound) "No secrets found in response"
    }
    
    # Test correlation ID in responses
    $correlationIdFound = $healthResult.Data.correlationId -and $healthResult.Data.correlationId.Length -gt 0
    Write-TestResult "Correlation ID tracking" $correlationIdFound "Correlation ID present in response"
}

function Write-TestSummary {
    Write-Host ""
    Write-Host "$Blue=== Test Summary ===$Reset" -ForegroundColor Blue
    Write-Host "  Total Tests: $($TestResults.Passed + $TestResults.Failed + $TestResults.Skipped)"
    Write-Host "  ${Green}Passed: $($TestResults.Passed)$Reset"
    Write-Host "  ${Red}Failed: $($TestResults.Failed)$Reset"
    Write-Host "  ${Yellow}Skipped: $($TestResults.Skipped)$Reset"
    
    if ($TestResults.Failed -gt 0) {
        Write-Host ""
        Write-Host "Failed Tests:" -ForegroundColor Red
        $TestResults.Tests | Where-Object { -not $_.Passed } | ForEach-Object {
            Write-Host "  - $($_.Name): $($_.Details)" -ForegroundColor Red
        }
    }
    
    $overallSuccess = $TestResults.Failed -eq 0
    $status = if ($overallSuccess) { "${Green}SUCCESS${Reset}" } else { "${Red}FAILURE${Reset}" }
    Write-Host ""
    Write-Host "Overall Result: $status"
    
    return $overallSuccess
}

# Main execution
Write-Host "${Blue}Copilot Agent Test Suite${Reset}" -ForegroundColor Blue
Write-Host "Testing against: $BaseUrl"
Write-Host "Test Type: $TestType"
Write-Host ""

# Run tests based on type
switch ($TestType) {
    "All" {
        Test-HealthCheck
        Test-Metrics
        Test-CacheManagement
        Test-McpConnectivity
        Test-MsDocsConnectivity
        Test-SecurityFeatures
    }
    "Health" { Test-HealthCheck }
    "MCP" { Test-McpConnectivity }
    "MSdocs" { Test-MsDocsConnectivity }
    "Cache" { Test-CacheManagement }
    "Metrics" { Test-Metrics }
    "Security" { Test-SecurityFeatures }
}

# Show summary and exit with appropriate code
$success = Write-TestSummary
exit $(if ($success) { 0 } else { 1 })