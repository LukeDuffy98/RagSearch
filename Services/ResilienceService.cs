using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace RagSearch.Services
{
    /// <summary>
    /// Configuration for resilience patterns (rate limiting, circuit breaker, retries)
    /// </summary>
    public class ResilienceConfiguration
    {
        public int GitHubRateLimitPerHour { get; set; } = 5000;
        public int MsDocsRateLimitPerHour { get; set; } = 1000;
        public int CircuitBreakerFailureThreshold { get; set; } = 5;
        public int CircuitBreakerTimeoutMs { get; set; } = 60000;
        public int RequestTimeoutMs { get; set; } = 30000;

        public static ResilienceConfiguration FromConfiguration(IConfiguration configuration)
        {
            return new ResilienceConfiguration
            {
                GitHubRateLimitPerHour = int.Parse(configuration["GITHUB_RATE_LIMIT_PER_HOUR"] ?? "5000"),
                MsDocsRateLimitPerHour = int.Parse(configuration["MSDOCS_RATE_LIMIT_PER_HOUR"] ?? "1000"),
                CircuitBreakerFailureThreshold = int.Parse(configuration["CIRCUIT_BREAKER_FAILURE_THRESHOLD"] ?? "5"),
                CircuitBreakerTimeoutMs = int.Parse(configuration["CIRCUIT_BREAKER_TIMEOUT_MS"] ?? "60000"),
                RequestTimeoutMs = int.Parse(configuration["AGENT_REQUEST_TIMEOUT_MS"] ?? "30000")
            };
        }
    }

    /// <summary>
    /// Simple rate limiter implementation
    /// </summary>
    public interface IRateLimiter
    {
        Task<bool> TryAcquireAsync(string key, int maxRequests, TimeSpan window);
        Task<RateLimitStatus> GetStatusAsync(string key);
    }

    public class RateLimitStatus
    {
        public int RequestsRemaining { get; set; }
        public TimeSpan WindowResetTime { get; set; }
        public bool IsAllowed { get; set; }
    }

    public class InMemoryRateLimiter : IRateLimiter
    {
        private readonly ConcurrentDictionary<string, RateLimitBucket> _buckets = new();
        private readonly ILogger<InMemoryRateLimiter> _logger;

        public InMemoryRateLimiter(ILogger<InMemoryRateLimiter> logger)
        {
            _logger = logger;
        }

        public Task<bool> TryAcquireAsync(string key, int maxRequests, TimeSpan window)
        {
            var bucket = _buckets.GetOrAdd(key, _ => new RateLimitBucket(maxRequests, window));
            var allowed = bucket.TryAcquire();
            
            if (!allowed)
            {
                _logger.LogWarning("Rate limit exceeded for key: {Key}", key);
            }
            
            return Task.FromResult(allowed);
        }

        public Task<RateLimitStatus> GetStatusAsync(string key)
        {
            if (_buckets.TryGetValue(key, out var bucket))
            {
                return Task.FromResult(bucket.GetStatus());
            }

            return Task.FromResult(new RateLimitStatus
            {
                IsAllowed = true,
                RequestsRemaining = int.MaxValue,
                WindowResetTime = TimeSpan.Zero
            });
        }

        private class RateLimitBucket
        {
            private readonly int _maxRequests;
            private readonly TimeSpan _window;
            private readonly object _lock = new();
            private DateTime _windowStart = DateTime.UtcNow;
            private int _requestCount = 0;

            public RateLimitBucket(int maxRequests, TimeSpan window)
            {
                _maxRequests = maxRequests;
                _window = window;
            }

            public bool TryAcquire()
            {
                lock (_lock)
                {
                    var now = DateTime.UtcNow;
                    
                    // Reset window if expired
                    if (now - _windowStart >= _window)
                    {
                        _windowStart = now;
                        _requestCount = 0;
                    }

                    if (_requestCount >= _maxRequests)
                    {
                        return false;
                    }

                    _requestCount++;
                    return true;
                }
            }

            public RateLimitStatus GetStatus()
            {
                lock (_lock)
                {
                    var now = DateTime.UtcNow;
                    var elapsed = now - _windowStart;
                    
                    return new RateLimitStatus
                    {
                        IsAllowed = _requestCount < _maxRequests,
                        RequestsRemaining = Math.Max(0, _maxRequests - _requestCount),
                        WindowResetTime = elapsed >= _window ? TimeSpan.Zero : _window - elapsed
                    };
                }
            }
        }
    }

    /// <summary>
    /// Circuit breaker implementation for external service calls
    /// </summary>
    public interface ICircuitBreaker
    {
        Task<T> ExecuteAsync<T>(Func<Task<T>> operation, string circuitName);
        CircuitBreakerState GetState(string circuitName);
    }

    public enum CircuitBreakerState
    {
        Closed,
        Open,
        HalfOpen
    }

    public class CircuitBreakerStatus
    {
        public CircuitBreakerState State { get; set; }
        public int FailureCount { get; set; }
        public DateTime? LastFailureTime { get; set; }
        public DateTime? NextAttemptTime { get; set; }
    }

    public class SimpleCircuitBreaker : ICircuitBreaker
    {
        private readonly ResilienceConfiguration _config;
        private readonly ILogger<SimpleCircuitBreaker> _logger;
        private readonly ConcurrentDictionary<string, CircuitBreakerStatus> _circuits = new();

        public SimpleCircuitBreaker(ResilienceConfiguration config, ILogger<SimpleCircuitBreaker> logger)
        {
            _config = config;
            _logger = logger;
        }

        public async Task<T> ExecuteAsync<T>(Func<Task<T>> operation, string circuitName)
        {
            var circuit = _circuits.GetOrAdd(circuitName, _ => new CircuitBreakerStatus
            {
                State = CircuitBreakerState.Closed
            });

            // Check if circuit is open
            if (circuit.State == CircuitBreakerState.Open)
            {
                if (circuit.NextAttemptTime.HasValue && DateTime.UtcNow < circuit.NextAttemptTime)
                {
                    _logger.LogWarning("Circuit breaker {CircuitName} is open, rejecting call", circuitName);
                    throw new InvalidOperationException($"Circuit breaker {circuitName} is open");
                }
                
                // Try to transition to half-open
                circuit.State = CircuitBreakerState.HalfOpen;
                _logger.LogInformation("Circuit breaker {CircuitName} transitioning to half-open", circuitName);
            }

            try
            {
                var result = await operation();
                
                // Success - reset circuit if it was half-open
                if (circuit.State == CircuitBreakerState.HalfOpen)
                {
                    circuit.State = CircuitBreakerState.Closed;
                    circuit.FailureCount = 0;
                    circuit.LastFailureTime = null;
                    circuit.NextAttemptTime = null;
                    _logger.LogInformation("Circuit breaker {CircuitName} reset to closed", circuitName);
                }
                
                return result;
            }
            catch (Exception ex)
            {
                circuit.FailureCount++;
                circuit.LastFailureTime = DateTime.UtcNow;
                
                if (circuit.FailureCount >= _config.CircuitBreakerFailureThreshold)
                {
                    circuit.State = CircuitBreakerState.Open;
                    circuit.NextAttemptTime = DateTime.UtcNow.AddMilliseconds(_config.CircuitBreakerTimeoutMs);
                    _logger.LogError(ex, "Circuit breaker {CircuitName} opened after {FailureCount} failures", 
                        circuitName, circuit.FailureCount);
                }
                else
                {
                    _logger.LogWarning(ex, "Circuit breaker {CircuitName} recorded failure {FailureCount}/{Threshold}", 
                        circuitName, circuit.FailureCount, _config.CircuitBreakerFailureThreshold);
                }
                
                throw;
            }
        }

        public CircuitBreakerState GetState(string circuitName)
        {
            return _circuits.TryGetValue(circuitName, out var circuit) 
                ? circuit.State 
                : CircuitBreakerState.Closed;
        }
    }

    /// <summary>
    /// Service for resilient external API calls with rate limiting, retries, and circuit breaker
    /// </summary>
    public interface IResilientHttpService
    {
        Task<T> ExecuteWithResilienceAsync<T>(
            Func<Task<T>> operation, 
            string serviceName, 
            int maxRetries = 3,
            CancellationToken cancellationToken = default);
    }

    public class ResilientHttpService : IResilientHttpService
    {
        private readonly IRateLimiter _rateLimiter;
        private readonly ICircuitBreaker _circuitBreaker;
        private readonly ResilienceConfiguration _config;
        private readonly ILogger<ResilientHttpService> _logger;

        public ResilientHttpService(
            IRateLimiter rateLimiter,
            ICircuitBreaker circuitBreaker,
            ResilienceConfiguration config,
            ILogger<ResilientHttpService> logger)
        {
            _rateLimiter = rateLimiter;
            _circuitBreaker = circuitBreaker;
            _config = config;
            _logger = logger;
        }

        public async Task<T> ExecuteWithResilienceAsync<T>(
            Func<Task<T>> operation, 
            string serviceName, 
            int maxRetries = 3,
            CancellationToken cancellationToken = default)
        {
            // Apply rate limiting based on service
            var rateLimit = serviceName.ToLowerInvariant() switch
            {
                "github" => _config.GitHubRateLimitPerHour,
                "msdocs" => _config.MsDocsRateLimitPerHour,
                _ => 1000 // default
            };

            if (!await _rateLimiter.TryAcquireAsync(serviceName, rateLimit, TimeSpan.FromHours(1)))
            {
                throw new InvalidOperationException($"Rate limit exceeded for {serviceName}");
            }

            // Execute with circuit breaker and retries
            return await _circuitBreaker.ExecuteAsync(async () =>
            {
                Exception? lastException = null;
                
                for (int attempt = 1; attempt <= maxRetries; attempt++)
                {
                    try
                    {
                        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                        cts.CancelAfter(_config.RequestTimeoutMs);
                        
                        return await operation();
                    }
                    catch (Exception ex) when (attempt < maxRetries)
                    {
                        lastException = ex;
                        var delay = TimeSpan.FromMilliseconds(Math.Pow(2, attempt) * 1000); // Exponential backoff
                        
                        _logger.LogWarning(ex, "Attempt {Attempt}/{MaxRetries} failed for {ServiceName}, retrying in {Delay}ms", 
                            attempt, maxRetries, serviceName, delay.TotalMilliseconds);
                        
                        await Task.Delay(delay, cancellationToken);
                    }
                }
                
                throw lastException!;
            }, serviceName);
        }
    }
}