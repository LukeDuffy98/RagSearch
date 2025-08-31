using Azure;
using Azure.AI.OpenAI;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Azure.Search.Documents.Models;
using Microsoft.Extensions.Logging;
using RagSearch.Models;
using System.Text.Json;
using AzureSearchDocument = Azure.Search.Documents.Models.SearchDocument;

namespace RagSearch.Services;

/// <summary>
/// Azure AI Search implementation with full enterprise features
/// Provides advanced search capabilities with persistent vector storage
/// </summary>
public class AzureSearchService : ISearchService
{
    private readonly SearchIndexClient _indexClient;
    private readonly SearchClient _searchClient;
    private readonly OpenAIClient _openAIClient;
    private readonly ILogger<AzureSearchService> _logger;
    private readonly string _indexName;
    private const string EmbeddingModel = "text-embedding-3-small";
    private const int EmbeddingDimensions = 1536;

    public AzureSearchService(
        SearchIndexClient indexClient,
        OpenAIClient openAIClient,
        ILogger<AzureSearchService> logger,
        string indexName = "ragsearch-index")
    {
        _indexClient = indexClient ?? throw new ArgumentNullException(nameof(indexClient));
        _openAIClient = openAIClient ?? throw new ArgumentNullException(nameof(openAIClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _indexName = indexName;
        
        // Create search client for the specific index
        _searchClient = _indexClient.GetSearchClient(_indexName);
    }

    /// <summary>
    /// Ensures the Azure AI Search index exists with proper schema
    /// </summary>
    public async Task<bool> EnsureIndexExistsAsync()
    {
        try
        {
            _logger.LogInformation("Ensuring Azure AI Search index '{IndexName}' exists", _indexName);

            // Check if index already exists
            try
            {
                await _indexClient.GetIndexAsync(_indexName);
                _logger.LogInformation("Index '{IndexName}' already exists", _indexName);
                return true;
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                // Index doesn't exist, create it
                _logger.LogInformation("Creating new Azure AI Search index '{IndexName}'", _indexName);
            }

            // Define the search index schema
            var searchIndex = new SearchIndex(_indexName)
            {
                Fields =
                {
                    new SimpleField("id", SearchFieldDataType.String) { IsKey = true, IsFilterable = true },
                    new SearchableField("title") { IsSortable = false },
                    new SearchableField("content") { AnalyzerName = LexicalAnalyzerName.EnMicrosoft },
                    new SearchableField("summary") { IsSortable = false },
                    new SimpleField("contentType", SearchFieldDataType.String) { IsFilterable = true, IsFacetable = true },
                    new SimpleField("fileType", SearchFieldDataType.String) { IsFilterable = true, IsFacetable = true },
                    new SimpleField("created", SearchFieldDataType.DateTimeOffset) { IsFilterable = true, IsSortable = true },
                    new SimpleField("modified", SearchFieldDataType.DateTimeOffset) { IsFilterable = true, IsSortable = true },
                    new SimpleField("indexed", SearchFieldDataType.DateTimeOffset) { IsFilterable = true, IsSortable = true },
                    new SimpleField("fileSize", SearchFieldDataType.Int64) { IsFilterable = true },
                    new SimpleField("url", SearchFieldDataType.String) { IsFilterable = true },
                    new SimpleField("sourceContainer", SearchFieldDataType.String) { IsFilterable = true, IsFacetable = true },
                    new SimpleField("sourceType", SearchFieldDataType.String) { IsFilterable = true, IsFacetable = true },
                    new SearchableField("author") { IsSortable = false },
                    new SimpleField("language", SearchFieldDataType.String) { IsFilterable = true, IsFacetable = true },
                    new SearchField("keyPhrases", SearchFieldDataType.Collection(SearchFieldDataType.String)) { IsSearchable = true },
                    new SimpleField("hasImages", SearchFieldDataType.Boolean) { IsFilterable = true },
                    new SimpleField("imageCount", SearchFieldDataType.Int32) { IsFilterable = true },
                    new SearchableField("imageCaption") { IsFilterable = true },
                    new SearchField("imageKeywords", SearchFieldDataType.Collection(SearchFieldDataType.String)) { IsSearchable = true, IsFilterable = true, IsFacetable = true },
                    new VectorSearchField("contentVector", EmbeddingDimensions, "my-vector-profile"),
                    new SimpleField("metadata", SearchFieldDataType.String) { IsFilterable = false }
                },
                VectorSearch = new VectorSearch
                {
                    Profiles =
                    {
                        new VectorSearchProfile("my-vector-profile", "my-hnsw-config")
                    },
                    Algorithms =
                    {
                        new HnswAlgorithmConfiguration("my-hnsw-config")
                    }
                },
                SemanticSearch = new SemanticSearch
                {
                    Configurations =
                    {
                        new SemanticConfiguration("my-semantic-config", new SemanticPrioritizedFields
                        {
                            TitleField = new SemanticField("title"),
                            ContentFields =
                            {
                                new SemanticField("content")
                            },
                            KeywordsFields =
                            {
                                new SemanticField("keyPhrases")
                            }
                        })
                    }
                }
            };

            await _indexClient.CreateIndexAsync(searchIndex);
            _logger.LogInformation("Successfully created Azure AI Search index '{IndexName}'", _indexName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ensure Azure AI Search index '{IndexName}' exists", _indexName);
            return false;
        }
    }

    /// <summary>
    /// Indexes a single document with vector embeddings
    /// </summary>
    public async Task<string> IndexDocumentAsync(RagSearch.Models.SearchDocument document)
    {
        try
        {
            document.Indexed = DateTime.UtcNow;
            _logger.LogInformation("Indexing document '{DocumentId}' to Azure AI Search", document.Id);

            // Generate embeddings for the document content
            if (!string.IsNullOrWhiteSpace(document.Content))
            {
                document.ContentVector = await GenerateEmbeddingsAsync(document.Content);
            }

            // Upload to Azure AI Search
            var indexDocuments = IndexDocumentsBatch.Upload(new[] { document });
            var result = await _searchClient.IndexDocumentsAsync(indexDocuments);

            var indexResult = result.Value.Results.FirstOrDefault();
            if (indexResult?.Succeeded == true)
            {
                _logger.LogInformation("Successfully indexed document '{DocumentId}'", document.Id);
                return document.Id;
            }
            else
            {
                throw new InvalidOperationException($"Failed to index document: {indexResult?.ErrorMessage}");
            }
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
    public async Task<string[]> IndexDocumentsAsync(RagSearch.Models.SearchDocument[] documents)
    {
        try
        {
            if (documents.Length == 0)
                return Array.Empty<string>();

            _logger.LogInformation("Batch indexing {DocumentCount} documents to Azure AI Search", documents.Length);

            var timestamp = DateTime.UtcNow;
            var successfulIds = new List<string>();

            // Generate embeddings for all documents (in parallel with concurrency limit)
            var semaphore = new SemaphoreSlim(5, 5); // Limit concurrent OpenAI requests
            var embeddingTasks = documents.Select(async doc =>
            {
                await semaphore.WaitAsync();
                try
                {
                    doc.Indexed = timestamp;
                    if (!string.IsNullOrWhiteSpace(doc.Content))
                    {
                        doc.ContentVector = await GenerateEmbeddingsAsync(doc.Content);
                    }
                    return doc;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to generate embeddings for document '{DocumentId}'", doc.Id);
                    return null;
                }
                finally
                {
                    semaphore.Release();
                }
            });

            var documentsWithEmbeddings = (await Task.WhenAll(embeddingTasks))
                .Where(doc => doc != null)
                .ToArray();

            // Upload documents to Azure AI Search in batches
            const int batchSize = 100; // Azure AI Search batch limit
            
            for (int i = 0; i < documentsWithEmbeddings.Length; i += batchSize)
            {
                var batch = documentsWithEmbeddings.Skip(i).Take(batchSize).ToArray();
                var indexDocuments = IndexDocumentsBatch.Upload(batch!);
                
                var result = await _searchClient.IndexDocumentsAsync(indexDocuments);
                
                foreach (var indexResult in result.Value.Results)
                {
                    if (indexResult.Succeeded)
                    {
                        successfulIds.Add(indexResult.Key);
                    }
                    else
                    {
                        _logger.LogError("Failed to index document '{DocumentId}': {Error}", 
                            indexResult.Key, indexResult.ErrorMessage);
                    }
                }
            }

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

            SearchResults<AzureSearchDocument> searchResults;

            switch (request.SearchType)
            {
                case SearchType.Keyword:
                    searchResults = await PerformKeywordSearchAsync(request);
                    break;
                
                case SearchType.Vector:
                    searchResults = await PerformVectorSearchAsync(request);
                    break;
                
                case SearchType.Hybrid:
                    searchResults = await PerformHybridSearchAsync(request);
                    break;
                
                default:
                    throw new ArgumentException($"Unknown search type: {request.SearchType}");
            }

            var results = new List<SearchResult>();
            await foreach (var result in searchResults.GetResultsAsync())
            {
                results.Add(ConvertToSearchResult(result));
                if (results.Count >= request.MaxResults)
                    break;
            }

            var executionTime = (DateTime.UtcNow - startTime).TotalMilliseconds;

            var response = new SearchResponse
            {
                Query = request.Query,
                TotalResults = (int)(searchResults.TotalCount ?? results.Count),
                Results = results.ToArray(),
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
            _logger.LogInformation("Deleting document '{DocumentId}' from Azure AI Search", documentId);

            var deleteDocuments = IndexDocumentsBatch.Delete("id", new[] { documentId });
            var result = await _searchClient.IndexDocumentsAsync(deleteDocuments);

            var deleteResult = result.Value.Results.FirstOrDefault();
            if (deleteResult?.Succeeded == true)
            {
                _logger.LogInformation("Successfully deleted document '{DocumentId}'", documentId);
                return true;
            }
            else
            {
                _logger.LogWarning("Failed to delete document '{DocumentId}': {Error}", 
                    documentId, deleteResult?.ErrorMessage);
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
    /// Gets statistics about the Azure AI Search index
    /// </summary>
    public async Task<IndexStatistics> GetIndexStatisticsAsync()
    {
        try
        {
            var indexStats = await _indexClient.GetIndexStatisticsAsync(_indexName);
            
            var stats = new IndexStatistics
            {
                DocumentCount = indexStats.Value.DocumentCount,
                StorageSize = indexStats.Value.StorageSize,
                LastUpdated = DateTime.UtcNow, // Azure doesn't provide this directly
                IndexType = "Azure AI Search with Vector Store"
            };

            _logger.LogInformation("Azure AI Search index contains {DocumentCount} documents, {StorageSize} bytes", 
                stats.DocumentCount, stats.StorageSize);

            return stats;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting Azure AI Search index statistics");
            throw;
        }
    }

    /// <summary>
    /// Rebuilds the entire Azure AI Search index
    /// </summary>
    public async Task<bool> RebuildIndexAsync()
    {
        try
        {
            _logger.LogWarning("Rebuilding Azure AI Search index - this will delete all existing data");

            // Delete the index
            await _indexClient.DeleteIndexAsync(_indexName);
            
            // Recreate the index
            await EnsureIndexExistsAsync();

            _logger.LogInformation("Successfully rebuilt Azure AI Search index");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error rebuilding Azure AI Search index");
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
                return new float[EmbeddingDimensions];

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
            _logger.LogWarning(ex, "Failed to generate embeddings for text of length {TextLength}", text.Length);
            return null;
        }
    }

    /// <summary>
    /// Performs keyword-based search using Azure AI Search
    /// </summary>
    private async Task<SearchResults<AzureSearchDocument>> PerformKeywordSearchAsync(SearchRequest request)
    {
        var searchOptions = new SearchOptions
        {
            Size = request.MaxResults,
            IncludeTotalCount = true,
            QueryType = SearchQueryType.Simple
        };

    ApplyFiltersToSearchOptions(searchOptions, request);

        return await _searchClient.SearchAsync<AzureSearchDocument>(request.Query, searchOptions);
    }

    /// <summary>
    /// Performs vector-based semantic search using Azure AI Search
    /// </summary>
    private async Task<SearchResults<AzureSearchDocument>> PerformVectorSearchAsync(SearchRequest request)
    {
        var queryEmbedding = await GenerateEmbeddingsAsync(request.Query);
        
        if (queryEmbedding == null)
        {
            throw new InvalidOperationException("Failed to generate embeddings for vector search");
        }

        var searchOptions = new SearchOptions
        {
            Size = request.MaxResults,
            IncludeTotalCount = true,
            VectorSearch = new VectorSearchOptions
            {
                Queries =
                {
                    new VectorizedQuery(queryEmbedding)
                    {
                        KNearestNeighborsCount = request.MaxResults,
                        Fields = { "contentVector" }
                    }
                }
            }
        };

    ApplyFiltersToSearchOptions(searchOptions, request);

        return await _searchClient.SearchAsync<AzureSearchDocument>("*", searchOptions);
    }

    /// <summary>
    /// Performs hybrid search combining keyword and vector search
    /// </summary>
    private async Task<SearchResults<AzureSearchDocument>> PerformHybridSearchAsync(SearchRequest request)
    {
        var queryEmbedding = await GenerateEmbeddingsAsync(request.Query);
        
        if (queryEmbedding == null)
        {
            // Fall back to keyword search if embeddings fail
            return await PerformKeywordSearchAsync(request);
        }

        var searchOptions = new SearchOptions
        {
            Size = request.MaxResults,
            IncludeTotalCount = true,
            QueryType = SearchQueryType.Simple,
            VectorSearch = new VectorSearchOptions
            {
                Queries =
                {
                    new VectorizedQuery(queryEmbedding)
                    {
                        KNearestNeighborsCount = request.MaxResults,
                        Fields = { "contentVector" }
                    }
                }
            }
        };

    ApplyFiltersToSearchOptions(searchOptions, request);

        return await _searchClient.SearchAsync<AzureSearchDocument>(request.Query, searchOptions);
    }

    /// <summary>
    /// Applies filters to Azure AI Search options
    /// </summary>
    private static void ApplyFiltersToSearchOptions(SearchOptions searchOptions, SearchRequest request)
    {
        var filters = request.Filters;
        var filterExpressions = new List<string>();

        // Content types: restrict to text/image types if provided
        if (request.ContentTypes?.Length > 0)
        {
            // Treat "image" as contentType starting with image/
            var typeExprs = new List<string>();
            foreach (var t in request.ContentTypes)
            {
                if (string.Equals(t, "image", StringComparison.OrdinalIgnoreCase))
                {
                    typeExprs.Add("startswith(contentType, 'image/')");
                }
                else if (string.Equals(t, "text", StringComparison.OrdinalIgnoreCase))
                {
                    // allow common textual docs by excluding pure image binaries
                    typeExprs.Add("not startswith(contentType, 'image/')");
                }
                else
                {
                    typeExprs.Add($"contentType eq '{t}'");
                }
            }
            if (typeExprs.Count > 0)
            {
                filterExpressions.Add($"(" + string.Join(" or ", typeExprs) + ")");
            }
        }

        if (filters != null && !string.IsNullOrEmpty(filters.FileType))
        {
            filterExpressions.Add($"contentType eq '{filters.FileType}'");
        }

        if (filters != null && !string.IsNullOrEmpty(filters.SourceContainer))
        {
            filterExpressions.Add($"sourceContainer eq '{filters.SourceContainer}'");
        }

        if (filters != null && !string.IsNullOrEmpty(filters.SourceType))
        {
            filterExpressions.Add($"sourceType eq '{filters.SourceType}'");
        }

        if (filters != null && filters.DateRange != null)
        {
            if (filters.DateRange.From.HasValue)
            {
                filterExpressions.Add($"created ge {filters.DateRange.From.Value:yyyy-MM-ddTHH:mm:ssZ}");
            }
            if (filters.DateRange.To.HasValue)
            {
                filterExpressions.Add($"created le {filters.DateRange.To.Value:yyyy-MM-ddTHH:mm:ssZ}");
            }
        }

        if (filters != null && filters.FileSize != null)
        {
            if (filters.FileSize.Min.HasValue)
            {
                filterExpressions.Add($"fileSize ge {filters.FileSize.Min.Value}");
            }
            if (filters.FileSize.Max.HasValue)
            {
                filterExpressions.Add($"fileSize le {filters.FileSize.Max.Value}");
            }
        }

        // Image-specific filters
        if (filters?.HasImages == true)
        {
            filterExpressions.Add("hasImages eq true");
        }

        if (!string.IsNullOrWhiteSpace(filters?.ImageCaptionContains))
        {
            // Use contains on imageCaption
            var val = filters!.ImageCaptionContains!.Replace("'", "''");
            filterExpressions.Add($"contains(imageCaption, '{val}')");
        }

        if (filters?.ImageKeywordsAny != null && filters.ImageKeywordsAny.Length > 0)
        {
            // search.in for keywords matching any
            var keywords = string.Join(",", filters.ImageKeywordsAny.Select(k => k.Replace("'", "''")));
            filterExpressions.Add($"search.in(imageKeywords, '{keywords}', ',')");
        }

        if (filterExpressions.Count > 0)
        {
            searchOptions.Filter = string.Join(" and ", filterExpressions);
        }
    }

    /// <summary>
    /// Converts Azure AI Search result to our SearchResult model
    /// </summary>
    private static SearchResult ConvertToSearchResult(SearchResult<AzureSearchDocument> searchResult)
    {
        var document = searchResult.Document;
        string? metadataJson = document.GetString("metadata");
        string[]? images = null;
        ImageInfo[]? imagesDetailed = null;
        if (!string.IsNullOrWhiteSpace(metadataJson))
        {
            try
            {
                using var jsonDoc = JsonDocument.Parse(metadataJson);
                if (jsonDoc.RootElement.TryGetProperty("images", out var imagesProp) && imagesProp.ValueKind == JsonValueKind.Array)
                {
                    var list = new List<string>();
                    foreach (var el in imagesProp.EnumerateArray())
                    {
                        var s = el.GetString();
                        if (!string.IsNullOrWhiteSpace(s)) list.Add(s);
                    }
                    images = list.Count > 0 ? list.ToArray() : null;
                }

                if (jsonDoc.RootElement.TryGetProperty("imagesDetailed", out var detProp) && detProp.ValueKind == JsonValueKind.Array)
                {
                    var list = new List<ImageInfo>();
                    foreach (var el in detProp.EnumerateArray())
                    {
                        var info = new ImageInfo
                        {
                            Id = el.TryGetProperty("id", out var id) ? id.GetString() : null,
                            Url = el.TryGetProperty("url", out var url) ? url.GetString() : null,
                            ContentType = el.TryGetProperty("contentType", out var ct) ? ct.GetString() : null,
                            FileSize = el.TryGetProperty("fileSize", out var fs) && fs.TryGetInt64(out var l) ? l : null,
                            Caption = el.TryGetProperty("caption", out var cap) ? cap.GetString() : null,
                            Keywords = el.TryGetProperty("keywords", out var kw) && kw.ValueKind == JsonValueKind.Array ? kw.EnumerateArray().Select(x => x.GetString()).Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>().ToArray() : null,
                            OcrPreview = el.TryGetProperty("ocrPreview", out var op) ? op.GetString() : null,
                            ParentId = el.TryGetProperty("parentId", out var pid) ? pid.GetString() : null,
                            ParentUrl = el.TryGetProperty("parentUrl", out var purl) ? purl.GetString() : null
                        };
                        if (!string.IsNullOrWhiteSpace(info.Url)) list.Add(info);
                    }
                    imagesDetailed = list.Count > 0 ? list.ToArray() : null;
                }
            }
            catch { }
        }
        
        return new SearchResult
        {
            Score = searchResult.Score ?? 0,
            Type = document.GetString("contentType") ?? string.Empty,
            Summary = document.GetString("summary") ?? string.Empty,
            Url = document.GetString("url") ?? string.Empty,
            Content = new SearchResultContent
            {
                Text = document.GetString("content"),
                Metadata = new SearchResultMetadata
                {
                    Title = document.GetString("title"),
                    Author = document.GetString("author"),
                    Created = document.GetDateTimeOffset("created")?.DateTime,
                    Modified = document.GetDateTimeOffset("modified")?.DateTime,
                    FileSize = document.GetInt64("fileSize"),
                    ContentType = document.GetString("contentType"),
                    SourceContainer = document.GetString("sourceContainer"),
                    SourceType = document.GetString("sourceType"),
                    Language = document.GetString("language"),
                    KeyPhrases = document.TryGetValue("keyPhrases", out var keyPhrasesObj) && keyPhrasesObj is IEnumerable<object> keyPhrases
                        ? keyPhrases.Select(x => x?.ToString()).Where(x => !string.IsNullOrEmpty(x)).Cast<string>().ToArray()
                        : null,
                    HasImages = document.TryGetValue("hasImages", out var hasImagesObj) ? hasImagesObj as bool? : null,
                    ImageCount = document.TryGetValue("imageCount", out var imageCountObj) ? imageCountObj as int? : null,
                    Images = images,
                    ImagesDetailed = imagesDetailed
                }
            }
        };
    }

    #endregion
}