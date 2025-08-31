using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RagSearch.Services
{
    /// <summary>
    /// Configuration for structured logging with secret redaction
    /// </summary>
    public class SecureLoggingConfiguration
    {
        public bool RedactionEnabled { get; set; } = true;
        public bool VerboseLoggingEnabled { get; set; } = false;
        public string CorrelationIdHeader { get; set; } = "x-correlation-id";
        public LogLevel MinimumLevel { get; set; } = LogLevel.Information;

        public static SecureLoggingConfiguration FromConfiguration(IConfiguration configuration)
        {
            return new SecureLoggingConfiguration
            {
                RedactionEnabled = bool.Parse(configuration["LOG_REDACTION_ENABLED"] ?? "true"),
                VerboseLoggingEnabled = bool.Parse(configuration["ENABLE_VERBOSE_LOGGING"] ?? "false"),
                CorrelationIdHeader = configuration["TELEMETRY_CORRELATION_ID_HEADER"] ?? "x-correlation-id",
                MinimumLevel = Enum.Parse<LogLevel>(configuration["AGENT_LOG_LEVEL"] ?? "Information", true)
            };
        }
    }

    /// <summary>
    /// Service for secure structured logging with automatic secret redaction
    /// </summary>
    public interface ISecureLogger
    {
        void LogWithRedaction(LogLevel level, string message, params object[] args);
        void LogRequestResponse(string operation, object? request, object? response, TimeSpan duration);
        void LogError(Exception exception, string message, params object[] args);
        void LogMetrics(string metricName, double value, Dictionary<string, string>? dimensions = null);
        IDisposable BeginScope(string correlationId);
    }

    public class SecureLogger : ISecureLogger
    {
        private readonly ILogger<SecureLogger> _logger;
        private readonly SecureLoggingConfiguration _config;

        // Patterns for detecting sensitive information
        private static readonly Regex[] SensitivePatterns = new[]
        {
            new Regex(@"Bearer\s+[A-Za-z0-9\-_]+", RegexOptions.IgnoreCase | RegexOptions.Compiled), // Bearer tokens
            new Regex(@"ghp_[A-Za-z0-9]+", RegexOptions.Compiled), // GitHub personal access tokens
            new Regex(@"[Aa]uthorization:\s*[^\s]+", RegexOptions.Compiled), // Authorization headers
            new Regex(@"[Aa]pi[_-]?[Kk]ey:\s*[^\s]+", RegexOptions.Compiled), // API keys
            new Regex(@"[Pp]assword:\s*[^\s]+", RegexOptions.Compiled), // Passwords
            new Regex(@"[Ss]ecret:\s*[^\s]+", RegexOptions.Compiled), // Secrets
            new Regex(@"[Tt]oken:\s*[^\s]+", RegexOptions.Compiled), // Generic tokens
        };

        public SecureLogger(ILogger<SecureLogger> logger, SecureLoggingConfiguration config)
        {
            _logger = logger;
            _config = config;
        }

        public void LogWithRedaction(LogLevel level, string message, params object[] args)
        {
            if (!_logger.IsEnabled(level) || level < _config.MinimumLevel)
                return;

            var redactedMessage = _config.RedactionEnabled ? RedactSensitiveInfo(message) : message;
            var redactedArgs = _config.RedactionEnabled ? RedactArgs(args) : args;

            _logger.Log(level, redactedMessage, redactedArgs);
        }

        public void LogRequestResponse(string operation, object? request, object? response, TimeSpan duration)
        {
            if (!_logger.IsEnabled(LogLevel.Information))
                return;

            var logData = new
            {
                Operation = operation,
                DurationMs = duration.TotalMilliseconds,
                Request = _config.RedactionEnabled ? RedactObject(request) : request,
                Response = _config.RedactionEnabled ? RedactObject(response) : response,
                Timestamp = DateTime.UtcNow
            };

            if (_config.VerboseLoggingEnabled)
            {
                _logger.LogInformation("API Call: {LogData}", JsonSerializer.Serialize(logData));
            }
            else
            {
                _logger.LogInformation("API Call: {Operation} completed in {DurationMs}ms", 
                    operation, duration.TotalMilliseconds);
            }
        }

        public void LogError(Exception exception, string message, params object[] args)
        {
            var redactedMessage = _config.RedactionEnabled ? RedactSensitiveInfo(message) : message;
            var redactedArgs = _config.RedactionEnabled ? RedactArgs(args) : args;

            _logger.LogError(exception, redactedMessage, redactedArgs);
        }

        public void LogMetrics(string metricName, double value, Dictionary<string, string>? dimensions = null)
        {
            if (!_logger.IsEnabled(LogLevel.Information))
                return;

            var metricsData = new
            {
                MetricName = metricName,
                Value = value,
                Dimensions = dimensions ?? new Dictionary<string, string>(),
                Timestamp = DateTime.UtcNow
            };

            _logger.LogInformation("Metric: {MetricsData}", JsonSerializer.Serialize(metricsData));
        }

        public IDisposable BeginScope(string correlationId)
        {
            return _logger.BeginScope(new Dictionary<string, object>
            {
                ["CorrelationId"] = correlationId,
                ["Timestamp"] = DateTime.UtcNow
            });
        }

        private string RedactSensitiveInfo(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            var result = input;
            foreach (var pattern in SensitivePatterns)
            {
                result = pattern.Replace(result, match =>
                {
                    var original = match.Value;
                    var colonIndex = original.IndexOf(':');
                    if (colonIndex > 0)
                    {
                        return original.Substring(0, colonIndex + 1) + " [REDACTED]";
                    }
                    return "[REDACTED]";
                });
            }
            return result;
        }

        private object[] RedactArgs(object[] args)
        {
            return args.Select(RedactObject).ToArray();
        }

        private object? RedactObject(object? obj)
        {
            if (obj == null)
                return null;

            if (obj is string str)
                return RedactSensitiveInfo(str);

            try
            {
                var json = JsonSerializer.Serialize(obj);
                var redactedJson = RedactSensitiveInfo(json);
                return JsonSerializer.Deserialize<object>(redactedJson);
            }
            catch
            {
                // If serialization fails, just return the object as-is
                return obj;
            }
        }
    }

    /// <summary>
    /// Service for collecting and reporting basic metrics
    /// </summary>
    public interface IMetricsCollector
    {
        void IncrementCounter(string name, Dictionary<string, string>? tags = null);
        void RecordHistogram(string name, double value, Dictionary<string, string>? tags = null);
        void SetGauge(string name, double value, Dictionary<string, string>? tags = null);
        Task<Dictionary<string, object>> GetMetricsSnapshotAsync();
    }

    public class SimpleMetricsCollector : IMetricsCollector
    {
        private readonly ConcurrentDictionary<string, long> _counters = new();
        private readonly ConcurrentDictionary<string, List<double>> _histograms = new();
        private readonly ConcurrentDictionary<string, double> _gauges = new();
        private readonly ISecureLogger _logger;

        public SimpleMetricsCollector(ISecureLogger logger)
        {
            _logger = logger;
        }

        public void IncrementCounter(string name, Dictionary<string, string>? tags = null)
        {
            var key = CreateKey(name, tags);
            _counters.AddOrUpdate(key, 1, (_, current) => current + 1);
            _logger.LogMetrics($"counter.{name}", 1, tags);
        }

        public void RecordHistogram(string name, double value, Dictionary<string, string>? tags = null)
        {
            var key = CreateKey(name, tags);
            _histograms.AddOrUpdate(key, new List<double> { value }, (_, list) =>
            {
                lock (list)
                {
                    list.Add(value);
                    // Keep only last 1000 values to prevent memory leaks
                    if (list.Count > 1000)
                    {
                        list.RemoveRange(0, list.Count - 1000);
                    }
                    return list;
                }
            });
            _logger.LogMetrics($"histogram.{name}", value, tags);
        }

        public void SetGauge(string name, double value, Dictionary<string, string>? tags = null)
        {
            var key = CreateKey(name, tags);
            _gauges.AddOrUpdate(key, value, (_, _) => value);
            _logger.LogMetrics($"gauge.{name}", value, tags);
        }

        public Task<Dictionary<string, object>> GetMetricsSnapshotAsync()
        {
            var snapshot = new Dictionary<string, object>
            {
                ["counters"] = _counters.ToDictionary(kvp => kvp.Key, kvp => (object)kvp.Value),
                ["gauges"] = _gauges.ToDictionary(kvp => kvp.Key, kvp => (object)kvp.Value),
                ["histograms"] = _histograms.ToDictionary(kvp => kvp.Key, kvp => (object)new
                {
                    Count = kvp.Value.Count,
                    Average = kvp.Value.Count > 0 ? kvp.Value.Average() : 0,
                    Min = kvp.Value.Count > 0 ? kvp.Value.Min() : 0,
                    Max = kvp.Value.Count > 0 ? kvp.Value.Max() : 0
                }),
                ["timestamp"] = DateTime.UtcNow
            };

            return Task.FromResult(snapshot);
        }

        private static string CreateKey(string name, Dictionary<string, string>? tags)
        {
            if (tags == null || tags.Count == 0)
                return name;

            var tagString = string.Join(",", tags.OrderBy(t => t.Key).Select(t => $"{t.Key}={t.Value}"));
            return $"{name}|{tagString}";
        }
    }
}