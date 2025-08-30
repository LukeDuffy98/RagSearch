using Azure.AI.OpenAI;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;
using RagSearch.Models;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace RagSearch.Services;

/// <summary>
/// Simplified search service that stores documents and embeddings in blob storage
/// Provides keyword and vector search capabilities without requiring Azure AI Search
/// </summary>
public class SimplifiedSearchService : ISearchService
{
    private readonly BlobServiceClient _blobServiceClient;
    private readonly OpenAIClient _openAIClient;
    private readonly ILogger<SimplifiedSearchService> _logger;
    
    // In-memory cache for performance
    private static readonly ConcurrentDictionary<string, SearchDocument> _documentsCache = new();
    private static readonly ConcurrentDictionary<string, float[]> _embeddingsCache = new();
    private static readonly SemaphoreSlim _cacheLock = new(1, 1);
    private static DateTime _lastCacheUpdate = DateTime.MinValue;
    
    // Configuration
    private const string IndexContainerName = "search-index";
    private const string DocumentsBlobName = "documents.json";
    private const string EmbeddingsBlobName = "embeddings.json";
    private const string EmbeddingModel = "text-embedding-3-small";
    private static readonly TimeSpan CacheRefreshInterval = TimeSpan.FromMinutes(5);

    public SimplifiedSearchService(
        BlobServiceClient blobServiceClient,
        OpenAIClient openAIClient,
        ILogger<SimplifiedSearchService> logger)
    {
        _blobServiceClient = blobServiceClient ?? throw new ArgumentNullException(nameof(blobServiceClient));
        _openAIClient = openAIClient ?? throw new ArgumentNullException(nameof(openAIClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Ensures the blob storage containers exist for storing the search index
    /// </summary>
    public async Task<bool> EnsureIndexExistsAsync()
    {
        try
        {
            _logger.LogInformation("Ensuring blob storage index container '{ContainerName}' exists", IndexContainerName);

            var containerClient = _blobServiceClient.GetBlobContainerClient(IndexContainerName);
            await containerClient.CreateIfNotExistsAsync();

            _logger.LogInformation("Successfully ensured index container exists");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ensure index container exists");
            return false;
        }
    }

    /// <summary>
    /// Indexes a single document with vector embeddings
    /// </summary>
    public async Task<string> IndexDocumentAsync(SearchDocument document)
    {
        try
        {
            document.Indexed = DateTime.UtcNow;
            _logger.LogInformation("Indexing document '{DocumentId}' with embeddings", document.Id);

            // Generate embeddings for the document content
            var embeddings = await GenerateEmbeddingsAsync(document.Content);
            
            // Load current data from cache or storage
            await EnsureCacheLoadedAsync();

            // Update cache
            _documentsCache[document.Id] = document;
            if (embeddings != null)
            {
                _embeddingsCache[document.Id] = embeddings;
            }

            // Persist to blob storage
            await SaveIndexToStorageAsync();

            _logger.LogInformation("Successfully indexed document '{DocumentId}' with {EmbeddingDimensions} embedding dimensions", 
                document.Id, embeddings?.Length ?? 0);
            
            return document.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error indexing document '{DocumentId}'", document.Id);
            throw;
        }
    }

    /// <summary>
    /// Indexes multiple documents in batch with embeddings
    /// </summary>
    public async Task<string[]> IndexDocumentsAsync(SearchDocument[] documents)
    {
        try
        {
            if (documents.Length == 0)
                return Array.Empty<string>();

            _logger.LogInformation("Batch indexing {DocumentCount} documents with embeddings", documents.Length);

            var successfulIds = new List<string>();
            var timestamp = DateTime.UtcNow;

            // Load current data
            await EnsureCacheLoadedAsync();

            // Process documents in parallel (but limit concurrency for OpenAI)
            var semaphore = new SemaphoreSlim(5, 5); // Limit to 5 concurrent embedding requests
            var tasks = documents.Select(async doc =>
            {
                await semaphore.WaitAsync();
                try
                {
                    doc.Indexed = timestamp;
                    var embeddings = await GenerateEmbeddingsAsync(doc.Content);
                    
                    _documentsCache[doc.Id] = doc;
                    if (embeddings != null)
                    {
                        _embeddingsCache[doc.Id] = embeddings;
                    }
                    
                    return doc.Id;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to process document '{DocumentId}'", doc.Id);
                    return null;
                }
                finally
                {
                    semaphore.Release();
                }
            });

            var results = await Task.WhenAll(tasks);
            successfulIds.AddRange(results.Where(id => id != null)!);

            // Save all changes to storage
            await SaveIndexToStorageAsync();

            _logger.LogInformation("Successfully batch indexed {SuccessCount}/{TotalCount} documents", 
                successfulIds.Count, documents.Length);

            return successfulIds.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error batch indexing {DocumentCount} documents", documents.Length);
            throw;
        }
    }

    /// <summary>
    /// Performs search with support for keyword, vector, and hybrid search
    /// </summary>
    public async Task<SearchResponse> SearchAsync(SearchRequest request)
    {
        try
        {
            var startTime = DateTime.UtcNow;
            _logger.LogInformation("Executing {SearchType} search for query: '{Query}'", 
                request.SearchType, request.Query);

            // Ensure cache is loaded
            await EnsureCacheLoadedAsync();

            var results = new List<SearchResult>();

            switch (request.SearchType)
            {
                case SearchType.Keyword:
                    results = await PerformKeywordSearchAsync(request);
                    break;
                
                case SearchType.Vector:
                    results = await PerformVectorSearchAsync(request);
                    break;
                
                case SearchType.Hybrid:
                    results = await PerformHybridSearchAsync(request);
                    break;
                
                default:
                    throw new ArgumentException($"Unknown search type: {request.SearchType}");
            }

            var executionTime = (DateTime.UtcNow - startTime).TotalMilliseconds;

            var response = new SearchResponse
            {
                Query = request.Query,
                TotalResults = results.Count,
                Results = results.Take(request.MaxResults).ToArray(),
                ExecutionTimeMs = executionTime,
                SearchType = request.SearchType
            };

            _logger.LogInformation("Search completed in {ExecutionTime}ms, found {ResultCount} results", 
                executionTime, results.Count);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing search for query: '{Query}'", request.Query);
            throw;
        }
    }

    /// <summary>
    /// Deletes a document from the index
    /// </summary>
    public async Task<bool> DeleteDocumentAsync(string documentId)
    {
        try
        {
            _logger.LogInformation("Deleting document '{DocumentId}' from index", documentId);

            await EnsureCacheLoadedAsync();

            bool documentRemoved = _documentsCache.TryRemove(documentId, out _);
            bool embeddingRemoved = _embeddingsCache.TryRemove(documentId, out _);

            if (documentRemoved || embeddingRemoved)
            {
                await SaveIndexToStorageAsync();
                _logger.LogInformation("Successfully deleted document '{DocumentId}' from index", documentId);
                return true;
            }
            else
            {
                _logger.LogWarning("Document '{DocumentId}' not found in index", documentId);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting document '{DocumentId}'", documentId);
            return false;
        }
    }

    /// <summary>
    /// Gets statistics about the search index
    /// </summary>
    public async Task<IndexStatistics> GetIndexStatisticsAsync()
    {
        try
        {
            await EnsureCacheLoadedAsync();

            var stats = new IndexStatistics
            {
                DocumentCount = _documentsCache.Count,
                StorageSize = CalculateStorageSize(),
                LastUpdated = _lastCacheUpdate,
                IndexType = "Simplified Search with Blob Storage"
            };

            _logger.LogInformation("Index contains {DocumentCount} documents, approximately {StorageSize} bytes", 
                stats.DocumentCount, stats.StorageSize);

            return stats;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting index statistics");
            throw;
        }
    }

    /// <summary>
    /// Rebuilds the entire index (clears all data)
    /// </summary>
    public async Task<bool> RebuildIndexAsync()
    {
        try
        {
            _logger.LogWarning("Rebuilding index - this will delete all existing data");

            _documentsCache.Clear();
            _embeddingsCache.Clear();

            await SaveIndexToStorageAsync();

            _logger.LogInformation("Successfully rebuilt index");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error rebuilding index");
            return false;
        }
    }

    #region Private Methods

    /// <summary>
    /// Generates vector embeddings for text using Azure OpenAI
    /// </summary>
    private async Task<float[]?> GenerateEmbeddingsAsync(string text)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(text))
                return new float[1536]; // Return empty embedding with expected dimensions

            // Truncate text if too long (OpenAI has token limits)
            var truncatedText = text.Length > 8000 ? text[..8000] : text;

            var options = new EmbeddingsOptions(EmbeddingModel, new[] { truncatedText });
            var response = await _openAIClient.GetEmbeddingsAsync(options);

            var embedding = response.Value.Data[0].Embedding.ToArray();
            
            _logger.LogDebug("Generated embedding with {Dimensions} dimensions for text of length {TextLength}", 
                embedding.Length, text.Length);

            return embedding;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Azure OpenAI not available, skipping embeddings for text of length {TextLength}", text.Length);
            // Return null to indicate no embeddings available - documents can still be indexed for keyword search
            return null;
        }
    }

    /// <summary>
    /// Performs keyword-based search
    /// </summary>
    private Task<List<SearchResult>> PerformKeywordSearchAsync(SearchRequest request)
    {
        var query = request.Query.ToLowerInvariant();
        var queryWords = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var results = _documentsCache.Values
            .Select(doc => new { Document = doc, Score = CalculateKeywordScore(doc, queryWords) })
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .Select(item => ConvertToSearchResult(item.Document, item.Score))
            .ToList();

        return Task.FromResult(ApplyFilters(results, request.Filters));
    }

    /// <summary>
    /// Performs vector-based semantic search
    /// </summary>
    private async Task<List<SearchResult>> PerformVectorSearchAsync(SearchRequest request)
    {
        // Generate embedding for the search query
        var queryEmbedding = await GenerateEmbeddingsAsync(request.Query);
        
        // If embeddings are not available, return empty results
        if (queryEmbedding == null)
        {
            _logger.LogWarning("Vector search requested but embeddings are not available");
            return new List<SearchResult>();
        }

        var results = _documentsCache.Values
            .Where(doc => _embeddingsCache.ContainsKey(doc.Id) && _embeddingsCache[doc.Id] != null)
            .Select(doc => new 
            { 
                Document = doc, 
                Score = CalculateCosineSimilarity(queryEmbedding, _embeddingsCache[doc.Id]) 
            })
            .OrderByDescending(item => item.Score)
            .Select(item => ConvertToSearchResult(item.Document, item.Score))
            .ToList();

        return ApplyFilters(results, request.Filters);
    }

    /// <summary>
    /// Performs hybrid search combining keyword and vector search
    /// </summary>
    private async Task<List<SearchResult>> PerformHybridSearchAsync(SearchRequest request)
    {
        // Get results from both search types
        var keywordResults = await PerformKeywordSearchAsync(request);
        var vectorResults = await PerformVectorSearchAsync(request);

        // Combine and rerank results
        var combinedResults = new Dictionary<string, SearchResult>();

        // Add keyword results with weight
        foreach (var result in keywordResults)
        {
            var docId = GetDocumentId(result);
            if (!combinedResults.ContainsKey(docId))
            {
                combinedResults[docId] = result;
                combinedResults[docId].Score = result.Score * 0.6; // Weight keyword score
            }
        }

        // Add vector results with weight and boost if already present
        foreach (var result in vectorResults)
        {
            var docId = GetDocumentId(result);
            if (combinedResults.ContainsKey(docId))
            {
                // Boost score for documents that appear in both results
                combinedResults[docId].Score += result.Score * 0.4 + 0.1; // Weight vector score + boost
            }
            else
            {
                combinedResults[docId] = result;
                combinedResults[docId].Score = result.Score * 0.4; // Weight vector score
            }
        }

        return combinedResults.Values
            .OrderByDescending(r => r.Score)
            .ToList();
    }

    /// <summary>
    /// Calculates keyword relevance score
    /// </summary>
    private static double CalculateKeywordScore(SearchDocument document, string[] queryWords)
    {
        var content = document.Content?.ToLowerInvariant() ?? "";
        var title = document.Title?.ToLowerInvariant() ?? "";

        double score = 0;

        foreach (var word in queryWords)
        {
            // Title matches get higher score
            if (title.Contains(word))
                score += 2.0;

            // Content matches
            var contentMatches = CountOccurrences(content, word);
            score += contentMatches * 0.5;

            // Exact phrase bonus
            if (content.Contains(string.Join(" ", queryWords)))
                score += 1.0;
        }

        return score;
    }

    /// <summary>
    /// Calculates cosine similarity between two vectors
    /// </summary>
    private static double CalculateCosineSimilarity(float[] vector1, float[] vector2)
    {
        if (vector1.Length != vector2.Length)
            return 0.0;

        double dotProduct = 0.0;
        double norm1 = 0.0;
        double norm2 = 0.0;

        for (int i = 0; i < vector1.Length; i++)
        {
            dotProduct += vector1[i] * vector2[i];
            norm1 += vector1[i] * vector1[i];
            norm2 += vector2[i] * vector2[i];
        }

        return dotProduct / (Math.Sqrt(norm1) * Math.Sqrt(norm2));
    }

    /// <summary>
    /// Ensures the cache is loaded from storage if needed
    /// </summary>
    private async Task EnsureCacheLoadedAsync()
    {
        if (_documentsCache.IsEmpty || DateTime.UtcNow - _lastCacheUpdate > CacheRefreshInterval)
        {
            await _cacheLock.WaitAsync();
            try
            {
                if (_documentsCache.IsEmpty || DateTime.UtcNow - _lastCacheUpdate > CacheRefreshInterval)
                {
                    await LoadIndexFromStorageAsync();
                    _lastCacheUpdate = DateTime.UtcNow;
                }
            }
            finally
            {
                _cacheLock.Release();
            }
        }
    }

    /// <summary>
    /// Loads the search index from blob storage
    /// </summary>
    private async Task LoadIndexFromStorageAsync()
    {
        try
        {
            _logger.LogInformation("Loading search index from blob storage");
            var containerClient = _blobServiceClient.GetBlobContainerClient(IndexContainerName);

            // Load documents
            var documentsBlobClient = containerClient.GetBlobClient(DocumentsBlobName);
            if (await documentsBlobClient.ExistsAsync())
            {
                var downloadResult = await documentsBlobClient.DownloadContentAsync();
                var documentsJson = downloadResult.Value.Content.ToString();
                var documents = JsonSerializer.Deserialize<Dictionary<string, SearchDocument>>(documentsJson);
                
                _documentsCache.Clear();
                foreach (var kvp in documents ?? new Dictionary<string, SearchDocument>())
                {
                    _documentsCache[kvp.Key] = kvp.Value;
                }
            }

            // Load embeddings
            var embeddingsBlobClient = containerClient.GetBlobClient(EmbeddingsBlobName);
            if (await embeddingsBlobClient.ExistsAsync())
            {
                var downloadResult = await embeddingsBlobClient.DownloadContentAsync();
                var embeddingsJson = downloadResult.Value.Content.ToString();
                var embeddings = JsonSerializer.Deserialize<Dictionary<string, float[]>>(embeddingsJson);
                
                _embeddingsCache.Clear();
                foreach (var kvp in embeddings ?? new Dictionary<string, float[]>())
                {
                    _embeddingsCache[kvp.Key] = kvp.Value;
                }
            }

            _logger.LogInformation("Loaded {DocumentCount} documents and {EmbeddingCount} embeddings from storage", 
                _documentsCache.Count, _embeddingsCache.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading index from storage");
            // Don't throw - allow the service to continue with empty cache
        }
    }

    /// <summary>
    /// Saves the search index to blob storage
    /// </summary>
    private async Task SaveIndexToStorageAsync()
    {
        try
        {
            _logger.LogDebug("Saving search index to blob storage");
            var containerClient = _blobServiceClient.GetBlobContainerClient(IndexContainerName);
            await containerClient.CreateIfNotExistsAsync();

            // Save documents
            var documentsJson = JsonSerializer.Serialize(_documentsCache.ToDictionary(kvp => kvp.Key, kvp => kvp.Value));
            var documentsBlobClient = containerClient.GetBlobClient(DocumentsBlobName);
            await documentsBlobClient.UploadAsync(new BinaryData(documentsJson), overwrite: true);

            // Save embeddings
            var embeddingsJson = JsonSerializer.Serialize(_embeddingsCache.ToDictionary(kvp => kvp.Key, kvp => kvp.Value));
            var embeddingsBlobClient = containerClient.GetBlobClient(EmbeddingsBlobName);
            await embeddingsBlobClient.UploadAsync(new BinaryData(embeddingsJson), overwrite: true);

            _logger.LogDebug("Successfully saved {DocumentCount} documents and {EmbeddingCount} embeddings to storage", 
                _documentsCache.Count, _embeddingsCache.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving index to storage");
            throw;
        }
    }

    /// <summary>
    /// Applies filters to search results
    /// </summary>
    private static List<SearchResult> ApplyFilters(List<SearchResult> results, SearchFilters? filters)
    {
        if (filters == null)
            return results;

        return results.Where(result =>
        {
            var metadata = result.Content?.Metadata;
            if (metadata == null) return true;

            // File type filter
            if (!string.IsNullOrEmpty(filters.FileType) && 
                !string.Equals(metadata.ContentType, filters.FileType, StringComparison.OrdinalIgnoreCase))
                return false;

            // Date range filter
            if (filters.DateRange != null)
            {
                if (filters.DateRange.From.HasValue && metadata.Created < filters.DateRange.From.Value)
                    return false;
                if (filters.DateRange.To.HasValue && metadata.Created > filters.DateRange.To.Value)
                    return false;
            }

            // File size filter
            if (filters.FileSize != null)
            {
                if (filters.FileSize.Min.HasValue && metadata.FileSize < filters.FileSize.Min.Value)
                    return false;
                if (filters.FileSize.Max.HasValue && metadata.FileSize > filters.FileSize.Max.Value)
                    return false;
            }

            return true;
        }).ToList();
    }

    /// <summary>
    /// Converts SearchDocument to SearchResult
    /// </summary>
    private static SearchResult ConvertToSearchResult(SearchDocument document, double score)
    {
        return new SearchResult
        {
            Score = score,
            Type = document.ContentType,
            Summary = document.Summary,
            Url = document.Url,
            Content = new SearchResultContent
            {
                Text = document.Content,
                Metadata = new SearchResultMetadata
                {
                    Title = document.Title,
                    Author = document.Author,
                    Created = document.Created,
                    Modified = document.Modified,
                    FileSize = document.FileSize,
                    ContentType = document.ContentType,
                    SourceContainer = document.SourceContainer,
                    SourceType = document.SourceType,
                    Language = document.Language,
                    KeyPhrases = document.KeyPhrases
                }
            }
        };
    }

    /// <summary>
    /// Helper methods
    /// </summary>
    private static int CountOccurrences(string text, string word)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(word, index, StringComparison.OrdinalIgnoreCase)) != -1)
        {
            count++;
            index += word.Length;
        }
        return count;
    }

    private static string GetDocumentId(SearchResult result)
    {
        // Extract document ID from URL or use a hash of the content
        return result.Url ?? result.Content?.Text?.GetHashCode().ToString() ?? Guid.NewGuid().ToString();
    }

    private long CalculateStorageSize()
    {
        try
        {
            var documentsSize = JsonSerializer.Serialize(_documentsCache).Length;
            var embeddingsSize = JsonSerializer.Serialize(_embeddingsCache).Length;
            return documentsSize + embeddingsSize;
        }
        catch
        {
            return 0;
        }
    }

    #endregion
}