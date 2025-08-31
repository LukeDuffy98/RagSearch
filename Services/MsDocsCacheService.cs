using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text.Json;

namespace RagSearch.Services
{
    /// <summary>
    /// Configuration for MS Docs content caching
    /// </summary>
    public class MsDocsCacheConfiguration
    {
        public int TtlSeconds { get; set; } = 86400; // 24 hours default
        public int MaxCacheSize { get; set; } = 10000; // Maximum number of cached items
        public string[] AllowedHosts { get; set; } = Array.Empty<string>();

        public static MsDocsCacheConfiguration FromConfiguration(IConfiguration configuration)
        {
            return new MsDocsCacheConfiguration
            {
                TtlSeconds = int.Parse(configuration["MSDOCS_CACHE_TTL_SECONDS"] ?? "86400"),
                MaxCacheSize = int.Parse(configuration["MSDOCS_CACHE_MAX_SIZE"] ?? "10000"),
                AllowedHosts = configuration["MSDOCS_ALLOWED_HOSTS"]?.Split(',') ?? Array.Empty<string>()
            };
        }
    }

    /// <summary>
    /// Cached content item for MS Docs
    /// </summary>
    public class CachedMsDocsContent
    {
        public string Url { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;
        public DateTime CachedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
        public string? ETag { get; set; }
        public DateTime? LastModified { get; set; }
        public Dictionary<string, string> Metadata { get; set; } = new();
        public bool IsValid => DateTime.UtcNow < ExpiresAt;
    }

    /// <summary>
    /// Service for caching MS Docs content with TTL and invalidation
    /// </summary>
    public interface IMsDocsCacheService
    {
        Task<CachedMsDocsContent?> GetAsync(string url, CancellationToken cancellationToken = default);
        Task SetAsync(string url, string content, string contentType, string? etag = null, DateTime? lastModified = null, CancellationToken cancellationToken = default);
        Task InvalidateAsync(string url, CancellationToken cancellationToken = default);
        Task<CacheStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default);
        Task CleanupExpiredAsync(CancellationToken cancellationToken = default);
        bool IsUrlAllowed(string url);
    }

    public class CacheStatistics
    {
        public int TotalItems { get; set; }
        public int ExpiredItems { get; set; }
        public long TotalSizeBytes { get; set; }
        public double HitRate { get; set; }
        public DateTime LastCleanup { get; set; }
        public Dictionary<string, int> HostBreakdown { get; set; } = new();
    }

    public class InMemoryMsDocsCacheService : IMsDocsCacheService
    {
        private readonly ConcurrentDictionary<string, CachedMsDocsContent> _cache = new();
        private readonly MsDocsCacheConfiguration _config;
        private readonly ILogger<InMemoryMsDocsCacheService> _logger;
        
        // Statistics tracking
        private long _hits = 0;
        private long _misses = 0;
        private DateTime _lastCleanup = DateTime.UtcNow;

        public InMemoryMsDocsCacheService(MsDocsCacheConfiguration config, ILogger<InMemoryMsDocsCacheService> logger)
        {
            _config = config;
            _logger = logger;
        }

        public Task<CachedMsDocsContent?> GetAsync(string url, CancellationToken cancellationToken = default)
        {
            if (!IsUrlAllowed(url))
            {
                _logger.LogWarning("URL not in allowlist: {Url}", url);
                return Task.FromResult<CachedMsDocsContent?>(null);
            }

            if (_cache.TryGetValue(url, out var cached))
            {
                if (cached.IsValid)
                {
                    Interlocked.Increment(ref _hits);
                    _logger.LogDebug("Cache hit for URL: {Url}", url);
                    return Task.FromResult<CachedMsDocsContent?>(cached);
                }
                else
                {
                    // Remove expired item
                    _cache.TryRemove(url, out _);
                    _logger.LogDebug("Removed expired cache entry for URL: {Url}", url);
                }
            }

            Interlocked.Increment(ref _misses);
            _logger.LogDebug("Cache miss for URL: {Url}", url);
            return Task.FromResult<CachedMsDocsContent?>(null);
        }

        public Task SetAsync(string url, string content, string contentType, string? etag = null, DateTime? lastModified = null, CancellationToken cancellationToken = default)
        {
            if (!IsUrlAllowed(url))
            {
                _logger.LogWarning("Attempted to cache URL not in allowlist: {Url}", url);
                return Task.CompletedTask;
            }

            // Check cache size limit
            if (_cache.Count >= _config.MaxCacheSize)
            {
                _logger.LogWarning("Cache size limit reached, triggering cleanup");
                _ = Task.Run(() => CleanupExpiredAsync(cancellationToken), cancellationToken);
            }

            var now = DateTime.UtcNow;
            var cached = new CachedMsDocsContent
            {
                Url = url,
                Content = content,
                ContentType = contentType,
                CachedAt = now,
                ExpiresAt = now.AddSeconds(_config.TtlSeconds),
                ETag = etag,
                LastModified = lastModified,
                Metadata = new Dictionary<string, string>
                {
                    ["ContentLength"] = content.Length.ToString(),
                    ["Host"] = GetHostFromUrl(url)
                }
            };

            _cache.AddOrUpdate(url, cached, (_, _) => cached);
            _logger.LogDebug("Cached content for URL: {Url}, Size: {Size} bytes, Expires: {ExpiresAt}", 
                url, content.Length, cached.ExpiresAt);

            return Task.CompletedTask;
        }

        public Task InvalidateAsync(string url, CancellationToken cancellationToken = default)
        {
            if (_cache.TryRemove(url, out var removed))
            {
                _logger.LogInformation("Invalidated cache entry for URL: {Url}", url);
            }
            return Task.CompletedTask;
        }

        public Task<CacheStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
        {
            var validItems = _cache.Values.Where(c => c.IsValid).ToList();
            var expiredItems = _cache.Values.Where(c => !c.IsValid).ToList();
            
            var totalRequests = _hits + _misses;
            var hitRate = totalRequests > 0 ? (double)_hits / totalRequests : 0.0;

            var hostBreakdown = validItems
                .GroupBy(c => c.Metadata.GetValueOrDefault("Host", "unknown"))
                .ToDictionary(g => g.Key, g => g.Count());

            var totalSize = validItems.Sum(c => c.Content.Length);

            var statistics = new CacheStatistics
            {
                TotalItems = validItems.Count,
                ExpiredItems = expiredItems.Count,
                TotalSizeBytes = totalSize,
                HitRate = hitRate,
                LastCleanup = _lastCleanup,
                HostBreakdown = hostBreakdown
            };

            return Task.FromResult(statistics);
        }

        public Task CleanupExpiredAsync(CancellationToken cancellationToken = default)
        {
            var removedCount = 0;
            var now = DateTime.UtcNow;

            // Remove expired items
            var expiredKeys = _cache
                .Where(kvp => !kvp.Value.IsValid)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in expiredKeys)
            {
                if (_cache.TryRemove(key, out _))
                {
                    removedCount++;
                }
            }

            // If still over limit, remove oldest items
            if (_cache.Count > _config.MaxCacheSize)
            {
                var oldestKeys = _cache
                    .OrderBy(kvp => kvp.Value.CachedAt)
                    .Take(_cache.Count - _config.MaxCacheSize)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var key in oldestKeys)
                {
                    if (_cache.TryRemove(key, out _))
                    {
                        removedCount++;
                    }
                }
            }

            _lastCleanup = now;
            _logger.LogInformation("Cache cleanup completed: removed {RemovedCount} items, {TotalItems} items remaining", 
                removedCount, _cache.Count);

            return Task.CompletedTask;
        }

        public bool IsUrlAllowed(string url)
        {
            if (_config.AllowedHosts.Length == 0)
            {
                _logger.LogWarning("No MS Docs hosts configured in allowlist");
                return false;
            }

            try
            {
                var uri = new Uri(url);
                var host = uri.Host.ToLowerInvariant();
                return _config.AllowedHosts.Any(allowedHost => 
                    host.Equals(allowedHost.ToLowerInvariant(), StringComparison.OrdinalIgnoreCase) ||
                    host.EndsWith($".{allowedHost.ToLowerInvariant()}", StringComparison.OrdinalIgnoreCase));
            }
            catch (UriFormatException)
            {
                _logger.LogWarning("Invalid URL format: {Url}", url);
                return false;
            }
        }

        private static string GetHostFromUrl(string url)
        {
            try
            {
                return new Uri(url).Host;
            }
            catch
            {
                return "unknown";
            }
        }
    }

    /// <summary>
    /// HTTP client service for fetching MS Docs content with caching
    /// </summary>
    public interface IMsDocsHttpService
    {
        Task<string> GetContentAsync(string url, CancellationToken cancellationToken = default);
        Task<bool> TestConnectivityAsync(string[] testUrls, CancellationToken cancellationToken = default);
    }

    public class MsDocsHttpService : IMsDocsHttpService
    {
        private readonly HttpClient _httpClient;
        private readonly IMsDocsCacheService _cacheService;
        private readonly IResilientHttpService _resilientHttpService;
        private readonly ILogger<MsDocsHttpService> _logger;

        public MsDocsHttpService(
            HttpClient httpClient,
            IMsDocsCacheService cacheService,
            IResilientHttpService resilientHttpService,
            ILogger<MsDocsHttpService> logger)
        {
            _httpClient = httpClient;
            _cacheService = cacheService;
            _resilientHttpService = resilientHttpService;
            _logger = logger;
        }

        public async Task<string> GetContentAsync(string url, CancellationToken cancellationToken = default)
        {
            if (!_cacheService.IsUrlAllowed(url))
            {
                throw new UnauthorizedAccessException($"URL not in allowlist: {url}");
            }

            // Try cache first
            var cached = await _cacheService.GetAsync(url, cancellationToken);
            if (cached != null)
            {
                _logger.LogDebug("Returning cached content for URL: {Url}", url);
                return cached.Content;
            }

            // Fetch from MS Docs with resilience patterns
            return await _resilientHttpService.ExecuteWithResilienceAsync(async () =>
            {
                _logger.LogInformation("Fetching content from MS Docs: {Url}", url);
                
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("User-Agent", "RagSearch/1.0 (Copilot Agent Coder)");
                
                var response = await _httpClient.SendAsync(request, cancellationToken);
                response.EnsureSuccessStatusCode();
                
                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                var contentType = response.Content.Headers.ContentType?.MediaType ?? "text/html";
                var etag = response.Headers.ETag?.Tag;
                var lastModified = response.Content.Headers.LastModified?.DateTime;
                
                // Cache the content
                await _cacheService.SetAsync(url, content, contentType, etag, lastModified, cancellationToken);
                
                _logger.LogInformation("Successfully fetched and cached content from MS Docs: {Url}, Size: {Size} bytes", 
                    url, content.Length);
                
                return content;
            }, "msdocs", cancellationToken: cancellationToken);
        }

        public async Task<bool> TestConnectivityAsync(string[] testUrls, CancellationToken cancellationToken = default)
        {
            var results = new List<bool>();
            
            foreach (var url in testUrls)
            {
                try
                {
                    _logger.LogInformation("Testing connectivity to: {Url}", url);
                    var request = new HttpRequestMessage(HttpMethod.Head, url);
                    request.Headers.Add("User-Agent", "RagSearch/1.0 (Connectivity Test)");
                    
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    cts.CancelAfter(TimeSpan.FromSeconds(10)); // Short timeout for connectivity test
                    
                    var response = await _httpClient.SendAsync(request, cts.Token);
                    var success = response.IsSuccessStatusCode;
                    results.Add(success);
                    
                    _logger.LogInformation("Connectivity test for {Url}: {Result}", url, success ? "SUCCESS" : "FAILED");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Connectivity test failed for {Url}", url);
                    results.Add(false);
                }
            }
            
            var successRate = results.Count > 0 ? results.Count(r => r) / (double)results.Count : 0;
            _logger.LogInformation("Overall connectivity test results: {SuccessCount}/{TotalCount} ({SuccessRate:P0})", 
                results.Count(r => r), results.Count, successRate);
            
            return successRate >= 0.5; // Consider success if at least 50% of tests pass
        }
    }
}