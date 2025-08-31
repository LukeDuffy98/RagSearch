using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using RagSearch.Services;
using System.Net;
using System.Text.Json;

namespace RagSearch
{
    /// <summary>
    /// Health check function for Copilot Agent services (MCP, MS Docs, etc.)
    /// </summary>
    public class AgentHealthCheckFunction
    {
        private readonly ILogger<AgentHealthCheckFunction> _logger;
        private readonly IMcpHealthCheckService _mcpHealthCheck;
        private readonly IMsDocsHttpService _msDocsService;
        private readonly IMsDocsCacheService _cacheService;
        private readonly IMetricsCollector _metricsCollector;
        private readonly ISecureLogger _secureLogger;
        private readonly McpServerConfiguration _mcpConfig;
        private readonly MsDocsCacheConfiguration _cacheConfig;

        public AgentHealthCheckFunction(
            ILogger<AgentHealthCheckFunction> logger,
            IMcpHealthCheckService mcpHealthCheck,
            IMsDocsHttpService msDocsService,
            IMsDocsCacheService cacheService,
            IMetricsCollector metricsCollector,
            ISecureLogger secureLogger,
            McpServerConfiguration mcpConfig,
            MsDocsCacheConfiguration cacheConfig)
        {
            _logger = logger;
            _mcpHealthCheck = mcpHealthCheck;
            _msDocsService = msDocsService;
            _cacheService = cacheService;
            _metricsCollector = metricsCollector;
            _secureLogger = secureLogger;
            _mcpConfig = mcpConfig;
            _cacheConfig = cacheConfig;
        }

        [Function("AgentHealthCheck")]
        public async Task<HttpResponseData> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get")] HttpRequestData req)
        {
            var correlationId = Guid.NewGuid().ToString();
            using var scope = _secureLogger.BeginScope(correlationId);

            _secureLogger.LogWithRedaction(LogLevel.Information, 
                "Starting Copilot Agent health check with correlation ID: {CorrelationId}", correlationId);

            var startTime = DateTime.UtcNow;
            var healthReport = new AgentHealthReport
            {
                CorrelationId = correlationId,
                Timestamp = startTime,
                OverallStatus = "Healthy"
            };

            try
            {
                // Check MCP server health
                _logger.LogInformation("Checking MCP server health...");
                var mcpHealth = await _mcpHealthCheck.GetDetailedHealthAsync();
                healthReport.McpServerHealth = mcpHealth;
                
                if (!mcpHealth.IsHealthy)
                {
                    healthReport.OverallStatus = "Degraded";
                }

                // Check MS Docs connectivity
                _logger.LogInformation("Testing MS Docs connectivity...");
                var testUrls = new[]
                {
                    "https://learn.microsoft.com",
                    "https://docs.microsoft.com"
                };

                var msDocsConnectivity = await _msDocsService.TestConnectivityAsync(testUrls);
                healthReport.MsDocsConnectivity = new MsDocsHealthStatus
                {
                    IsConnected = msDocsConnectivity,
                    TestedUrls = testUrls,
                    AllowedHosts = _cacheConfig.AllowedHosts
                };

                if (!msDocsConnectivity)
                {
                    healthReport.OverallStatus = "Degraded";
                }

                // Get cache statistics
                _logger.LogInformation("Retrieving cache statistics...");
                healthReport.CacheStatistics = await _cacheService.GetStatisticsAsync();

                // Get metrics snapshot
                _logger.LogInformation("Collecting metrics...");
                healthReport.Metrics = await _metricsCollector.GetMetricsSnapshotAsync();

                // Get configuration status
                healthReport.Configuration = new ConfigurationStatus
                {
                    McpCommand = !string.IsNullOrEmpty(_mcpConfig.Command),
                    AllowedReposConfigured = _mcpConfig.AllowedRepos.Length > 0,
                    AllowedHostsConfigured = _cacheConfig.AllowedHosts.Length > 0,
                    CacheTtlSeconds = _cacheConfig.TtlSeconds
                };

                var duration = DateTime.UtcNow - startTime;
                healthReport.ResponseTimeMs = duration.TotalMilliseconds;

                // Record metrics
                _metricsCollector.IncrementCounter("health_check_total");
                _metricsCollector.RecordHistogram("health_check_duration_ms", duration.TotalMilliseconds);

                _secureLogger.LogWithRedaction(LogLevel.Information,
                    "Health check completed in {DurationMs}ms with status: {Status}",
                    duration.TotalMilliseconds, healthReport.OverallStatus);

                var response = req.CreateResponse(HttpStatusCode.OK);
                response.Headers.Add("Content-Type", "application/json");
                await response.WriteStringAsync(JsonSerializer.Serialize(healthReport, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true
                }));

                return response;
            }
            catch (Exception ex)
            {
                _secureLogger.LogError(ex, "Health check failed");
                
                healthReport.OverallStatus = "Unhealthy";
                healthReport.Error = ex.Message;
                healthReport.ResponseTimeMs = (DateTime.UtcNow - startTime).TotalMilliseconds;

                _metricsCollector.IncrementCounter("health_check_errors");

                var response = req.CreateResponse(HttpStatusCode.ServiceUnavailable);
                response.Headers.Add("Content-Type", "application/json");
                await response.WriteStringAsync(JsonSerializer.Serialize(healthReport, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true
                }));

                return response;
            }
        }
    }

    public class AgentHealthReport
    {
        public string CorrelationId { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public string OverallStatus { get; set; } = "Unknown";
        public double ResponseTimeMs { get; set; }
        public string? Error { get; set; }
        
        public McpHealthStatus? McpServerHealth { get; set; }
        public MsDocsHealthStatus? MsDocsConnectivity { get; set; }
        public CacheStatistics? CacheStatistics { get; set; }
        public Dictionary<string, object>? Metrics { get; set; }
        public ConfigurationStatus? Configuration { get; set; }
    }

    public class MsDocsHealthStatus
    {
        public bool IsConnected { get; set; }
        public string[] TestedUrls { get; set; } = Array.Empty<string>();
        public string[] AllowedHosts { get; set; } = Array.Empty<string>();
    }

    public class ConfigurationStatus
    {
        public bool McpCommand { get; set; }
        public bool AllowedReposConfigured { get; set; }
        public bool AllowedHostsConfigured { get; set; }
        public int CacheTtlSeconds { get; set; }
    }
}