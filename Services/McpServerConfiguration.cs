using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace RagSearch.Services
{
    /// <summary>
    /// Configuration for Model Context Protocol (MCP) server settings
    /// </summary>
    public class McpServerConfiguration
    {
        public string Transport { get; set; } = "stdio";
        public string Command { get; set; } = string.Empty;
        public int HealthCheckIntervalMs { get; set; } = 60000;
        public int MaxRetries { get; set; } = 3;
        public int RetryDelayMs { get; set; } = 1000;
        public string[] AllowedRepos { get; set; } = Array.Empty<string>();
        public string[] AllowedMsDocsHosts { get; set; } = Array.Empty<string>();
        
        public static McpServerConfiguration FromConfiguration(IConfiguration configuration)
        {
            return new McpServerConfiguration
            {
                Transport = configuration["MCP_SERVER_TRANSPORT"] ?? "stdio",
                Command = configuration["MCP_SERVER_COMMAND"] ?? string.Empty,
                HealthCheckIntervalMs = int.Parse(configuration["MCP_SERVER_HEALTH_CHECK_INTERVAL_MS"] ?? "60000"),
                MaxRetries = int.Parse(configuration["MCP_SERVER_MAX_RETRIES"] ?? "3"),
                RetryDelayMs = int.Parse(configuration["MCP_SERVER_RETRY_DELAY_MS"] ?? "1000"),
                AllowedRepos = configuration["GITHUB_REPO_ALLOWLIST"]?.Split(',') ?? Array.Empty<string>(),
                AllowedMsDocsHosts = configuration["MSDOCS_ALLOWED_HOSTS"]?.Split(',') ?? Array.Empty<string>()
            };
        }
    }

    /// <summary>
    /// MCP server health check service
    /// </summary>
    public interface IMcpHealthCheckService
    {
        Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default);
        Task<McpHealthStatus> GetDetailedHealthAsync(CancellationToken cancellationToken = default);
    }

    public class McpHealthStatus
    {
        public bool IsHealthy { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime LastChecked { get; set; }
        public TimeSpan ResponseTime { get; set; }
        public string? ErrorMessage { get; set; }
        public Dictionary<string, object> AdditionalInfo { get; set; } = new();
    }

    public class McpHealthCheckService : IMcpHealthCheckService
    {
        private readonly McpServerConfiguration _config;
        private readonly ILogger<McpHealthCheckService> _logger;
        private McpHealthStatus? _lastHealthStatus;

        public McpHealthCheckService(McpServerConfiguration config, ILogger<McpHealthCheckService> logger)
        {
            _config = config;
            _logger = logger;
        }

        public async Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default)
        {
            var healthStatus = await GetDetailedHealthAsync(cancellationToken);
            return healthStatus.IsHealthy;
        }

        public async Task<McpHealthStatus> GetDetailedHealthAsync(CancellationToken cancellationToken = default)
        {
            var startTime = DateTime.UtcNow;
            var healthStatus = new McpHealthStatus
            {
                LastChecked = startTime
            };

            try
            {
                _logger.LogDebug("Starting MCP server health check");

                // Basic configuration validation
                if (string.IsNullOrEmpty(_config.Command))
                {
                    healthStatus.IsHealthy = false;
                    healthStatus.Status = "Configuration Error";
                    healthStatus.ErrorMessage = "MCP server command not configured";
                    return healthStatus;
                }

                // Validate allowlists
                if (_config.AllowedRepos.Length == 0)
                {
                    _logger.LogWarning("No GitHub repositories configured in allowlist");
                }

                if (_config.AllowedMsDocsHosts.Length == 0)
                {
                    _logger.LogWarning("No MS Docs hosts configured in allowlist");
                }

                // For now, mark as healthy if configuration is valid
                // In a real implementation, you would test actual MCP server connectivity
                healthStatus.IsHealthy = true;
                healthStatus.Status = "Healthy";
                healthStatus.ResponseTime = DateTime.UtcNow - startTime;
                healthStatus.AdditionalInfo["ConfiguredRepos"] = _config.AllowedRepos.Length;
                healthStatus.AdditionalInfo["ConfiguredHosts"] = _config.AllowedMsDocsHosts.Length;

                _logger.LogInformation("MCP server health check passed in {ResponseTime}ms", 
                    healthStatus.ResponseTime.TotalMilliseconds);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MCP server health check failed");
                healthStatus.IsHealthy = false;
                healthStatus.Status = "Error";
                healthStatus.ErrorMessage = ex.Message;
                healthStatus.ResponseTime = DateTime.UtcNow - startTime;
            }

            _lastHealthStatus = healthStatus;
            return healthStatus;
        }
    }
}