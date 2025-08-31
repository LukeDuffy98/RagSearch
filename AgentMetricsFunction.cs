using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using RagSearch.Services;
using System.Net;
using System.Text.Json;

namespace RagSearch
{
    /// <summary>
    /// Function to provide metrics and observability data for Copilot Agent services
    /// </summary>
    public class AgentMetricsFunction
    {
        private readonly ILogger<AgentMetricsFunction> _logger;
        private readonly IMetricsCollector _metricsCollector;
        private readonly IMsDocsCacheService _cacheService;
        private readonly IRateLimiter _rateLimiter;
        private readonly ICircuitBreaker _circuitBreaker;
        private readonly ISecureLogger _secureLogger;

        public AgentMetricsFunction(
            ILogger<AgentMetricsFunction> logger,
            IMetricsCollector metricsCollector,
            IMsDocsCacheService cacheService,
            IRateLimiter rateLimiter,
            ICircuitBreaker circuitBreaker,
            ISecureLogger secureLogger)
        {
            _logger = logger;
            _metricsCollector = metricsCollector;
            _cacheService = cacheService;
            _rateLimiter = rateLimiter;
            _circuitBreaker = circuitBreaker;
            _secureLogger = secureLogger;
        }

        [Function("AgentMetrics")]
        public async Task<HttpResponseData> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get")] HttpRequestData req)
        {
            var correlationId = Guid.NewGuid().ToString();
            using var scope = _secureLogger.BeginScope(correlationId);

            _secureLogger.LogWithRedaction(LogLevel.Information,
                "Retrieving agent metrics with correlation ID: {CorrelationId}", correlationId);

            try
            {
                var metricsReport = new AgentMetricsReport
                {
                    CorrelationId = correlationId,
                    Timestamp = DateTime.UtcNow
                };

                // Get general metrics
                metricsReport.Metrics = await _metricsCollector.GetMetricsSnapshotAsync();

                // Get cache statistics
                metricsReport.CacheStatistics = await _cacheService.GetStatisticsAsync();

                // Get rate limiter status for key services
                metricsReport.RateLimitStatus = new Dictionary<string, RateLimitStatus>
                {
                    ["github"] = await _rateLimiter.GetStatusAsync("github"),
                    ["msdocs"] = await _rateLimiter.GetStatusAsync("msdocs")
                };

                // Get circuit breaker states
                metricsReport.CircuitBreakerStates = new Dictionary<string, string>
                {
                    ["github"] = _circuitBreaker.GetState("github").ToString(),
                    ["msdocs"] = _circuitBreaker.GetState("msdocs").ToString()
                };

                // Calculate some derived metrics
                var cacheStats = metricsReport.CacheStatistics;
                metricsReport.DerivedMetrics = new Dictionary<string, object>
                {
                    ["cache_efficiency"] = cacheStats.HitRate,
                    ["cache_utilization"] = cacheStats.TotalItems > 0 ? 
                        (double)cacheStats.TotalItems / 10000 : 0, // Assuming max 10k items
                    ["data_size_mb"] = cacheStats.TotalSizeBytes / (1024.0 * 1024.0),
                    ["expired_item_ratio"] = cacheStats.TotalItems > 0 ? 
                        (double)cacheStats.ExpiredItems / (cacheStats.TotalItems + cacheStats.ExpiredItems) : 0
                };

                // Record this metrics call
                _metricsCollector.IncrementCounter("metrics_requests_total");

                var response = req.CreateResponse(HttpStatusCode.OK);
                response.Headers.Add("Content-Type", "application/json");
                await response.WriteStringAsync(JsonSerializer.Serialize(metricsReport, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true
                }));

                return response;
            }
            catch (Exception ex)
            {
                _secureLogger.LogError(ex, "Failed to retrieve agent metrics");

                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                errorResponse.Headers.Add("Content-Type", "application/json");
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    error = "Failed to retrieve metrics",
                    correlationId,
                    timestamp = DateTime.UtcNow
                }, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                }));

                return errorResponse;
            }
        }

        [Function("AgentCacheManagement")]
        public async Task<HttpResponseData> CacheManagement(
            [HttpTrigger(AuthorizationLevel.Function, "post", "delete")] HttpRequestData req)
        {
            var correlationId = Guid.NewGuid().ToString();
            using var scope = _secureLogger.BeginScope(correlationId);

            var method = req.Method.ToUpperInvariant();
            _secureLogger.LogWithRedaction(LogLevel.Information,
                "Cache management operation: {Method} with correlation ID: {CorrelationId}", method, correlationId);

            try
            {
                if (method == "POST")
                {
                    // Trigger cache cleanup
                    await _cacheService.CleanupExpiredAsync();
                    _metricsCollector.IncrementCounter("cache_cleanup_manual");

                    var response = req.CreateResponse(HttpStatusCode.OK);
                    response.Headers.Add("Content-Type", "application/json");
                    await response.WriteStringAsync(JsonSerializer.Serialize(new
                    {
                        message = "Cache cleanup completed",
                        correlationId,
                        timestamp = DateTime.UtcNow
                    }, new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    }));

                    return response;
                }
                else if (method == "DELETE")
                {
                    // Read URL from request body if provided
                    string? urlToInvalidate = null;
                    if (req.Body.CanRead)
                    {
                        var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                        if (!string.IsNullOrEmpty(requestBody))
                        {
                            var requestData = JsonSerializer.Deserialize<CacheInvalidationRequest>(requestBody);
                            urlToInvalidate = requestData?.Url;
                        }
                    }

                    if (!string.IsNullOrEmpty(urlToInvalidate))
                    {
                        await _cacheService.InvalidateAsync(urlToInvalidate);
                        _metricsCollector.IncrementCounter("cache_invalidation_single");

                        var response = req.CreateResponse(HttpStatusCode.OK);
                        response.Headers.Add("Content-Type", "application/json");
                        await response.WriteStringAsync(JsonSerializer.Serialize(new
                        {
                            message = $"Cache entry invalidated for URL: {urlToInvalidate}",
                            correlationId,
                            timestamp = DateTime.UtcNow
                        }, new JsonSerializerOptions
                        {
                            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                        }));

                        return response;
                    }
                    else
                    {
                        var response = req.CreateResponse(HttpStatusCode.BadRequest);
                        response.Headers.Add("Content-Type", "application/json");
                        await response.WriteStringAsync(JsonSerializer.Serialize(new
                        {
                            error = "URL is required for cache invalidation",
                            correlationId,
                            timestamp = DateTime.UtcNow
                        }, new JsonSerializerOptions
                        {
                            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                        }));

                        return response;
                    }
                }
                else
                {
                    var response = req.CreateResponse(HttpStatusCode.MethodNotAllowed);
                    response.Headers.Add("Content-Type", "application/json");
                    await response.WriteStringAsync(JsonSerializer.Serialize(new
                    {
                        error = $"Method {method} not allowed",
                        correlationId,
                        timestamp = DateTime.UtcNow
                    }, new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    }));

                    return response;
                }
            }
            catch (Exception ex)
            {
                _secureLogger.LogError(ex, "Cache management operation failed");

                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                errorResponse.Headers.Add("Content-Type", "application/json");
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    error = "Cache management operation failed",
                    correlationId,
                    timestamp = DateTime.UtcNow
                }, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                }));

                return errorResponse;
            }
        }
    }

    public class AgentMetricsReport
    {
        public string CorrelationId { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public Dictionary<string, object>? Metrics { get; set; }
        public CacheStatistics? CacheStatistics { get; set; }
        public Dictionary<string, RateLimitStatus>? RateLimitStatus { get; set; }
        public Dictionary<string, string>? CircuitBreakerStates { get; set; }
        public Dictionary<string, object>? DerivedMetrics { get; set; }
    }

    public class CacheInvalidationRequest
    {
        public string? Url { get; set; }
    }
}