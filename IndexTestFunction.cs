using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using RagSearch.Models;
using RagSearch.Services;
using System.Net;
using System.Text.Json;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.OpenApi.Models;
using System.Text.RegularExpressions;

namespace RagSearch.Functions;

/// <summary>
/// Test function for indexing sample documents to verify persistent search functionality
/// This function is for development and testing purposes only
/// </summary>
public class IndexTestFunction
{
    private readonly ILogger<IndexTestFunction> _logger;
    private readonly ISearchService _searchService;

    public IndexTestFunction(ILoggerFactory loggerFactory, ISearchService searchService)
    {
        _logger = loggerFactory.CreateLogger<IndexTestFunction>();
        _searchService = searchService ?? throw new ArgumentNullException(nameof(searchService));
    }

    /// <summary>
    /// HTTP endpoint for adding sample test documents to the persistent index
    /// </summary>
    [Function("AddTestDocuments")]
    [OpenApiOperation(operationId: "Test_AddDocuments", tags: new[] { "TestData" }, Summary = "Add test docs", Description = "Adds sample documents to the persistent index for testing.")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(object), Summary = "Indexed", Description = "Indexing result with counts.")]
    public async Task<HttpResponseData> AddTestDocuments(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req)
    {
        _logger.LogInformation("Adding test documents to persistent search index");

        try
        {
            // Ensure the persistent index exists
            var indexExists = await _searchService.EnsureIndexExistsAsync();
            if (!indexExists)
            {
                _logger.LogError("Failed to ensure search index exists");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync("Search service is not available");
                return errorResponse;
            }

            // Create sample test documents
            var testDocuments = new[]
            {
                new SearchDocument
                {
                    Id = "test-doc-1",
                    Title = "Azure Functions Overview",
                    Content = "Azure Functions is a serverless compute service that lets you run event-triggered code without having to explicitly provision or manage infrastructure. With Azure Functions, you can run code in response to a variety of events.",
                    Summary = "Introduction to Azure Functions serverless computing platform",
                    ContentType = "text",
                    FileType = "markdown",
                    Created = DateTime.UtcNow.AddDays(-7),
                    Modified = DateTime.UtcNow.AddDays(-7),
                    FileSize = 1024,
                    Url = "https://example.com/azure-functions-overview.md",
                    SourceType = "url",
                    Author = "Microsoft Documentation",
                    Language = "en",
                    KeyPhrases = new[] { "Azure Functions", "serverless", "compute", "event-triggered" },
                    HasImages = false,
                    ImageCount = 0
                },
                new SearchDocument
                {
                    Id = "test-doc-2",
                    Title = "Getting Started with Azure AI Search",
                    Content = "Azure AI Search is a cloud search service that gives developers infrastructure, APIs, and tools for building a rich search experience over private, heterogeneous content in web, mobile, and enterprise applications. This service provides persistent, durable indexing capabilities.",
                    Summary = "Guide to implementing search functionality with Azure AI Search",
                    ContentType = "text",
                    FileType = "pdf",
                    Created = DateTime.UtcNow.AddDays(-3),
                    Modified = DateTime.UtcNow.AddDays(-3),
                    FileSize = 2048,
                    Url = "https://example.com/azure-search-guide.pdf",
                    SourceType = "url",
                    Author = "Azure Documentation Team",
                    Language = "en",
                    KeyPhrases = new[] { "Azure AI Search", "search service", "indexing", "persistent storage" },
                    HasImages = true,
                    ImageCount = 3
                },
                new SearchDocument
                {
                    Id = "test-doc-3",
                    Title = "RAG Architecture Best Practices",
                    Content = "Retrieval-Augmented Generation (RAG) combines the power of large language models with information retrieval systems. Key components include document indexing, vector embeddings, semantic search, and persistent storage of indexed content for reliable performance.",
                    Summary = "Best practices for implementing RAG search systems",
                    ContentType = "text",
                    FileType = "docx",
                    Created = DateTime.UtcNow.AddDays(-1),
                    Modified = DateTime.UtcNow.AddDays(-1),
                    FileSize = 3072,
                    Url = "https://example.com/rag-best-practices.docx",
                    SourceType = "url",
                    Author = "AI Research Team",
                    Language = "en",
                    KeyPhrases = new[] { "RAG", "retrieval-augmented generation", "vector embeddings", "semantic search" },
                    HasImages = false,
                    ImageCount = 0
                }
            };

            // Index the test documents in the persistent storage
            _logger.LogInformation("Indexing {DocumentCount} test documents persistently", testDocuments.Length);
            var indexedIds = await _searchService.IndexDocumentsAsync(testDocuments);

            var result = new
            {
                Message = "Test documents added to persistent search index",
                IndexedDocuments = indexedIds.Length,
                TotalDocuments = testDocuments.Length,
                DocumentIds = indexedIds,
                PersistentStorage = true,
                IndexName = "ragsearch-documents"
            };

            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "application/json");
            
            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            };
            
            await response.WriteStringAsync(JsonSerializer.Serialize(result, jsonOptions));

            _logger.LogInformation("Successfully indexed {IndexedCount}/{TotalCount} test documents in persistent storage", 
                indexedIds.Length, testDocuments.Length);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding test documents to search index");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteStringAsync("An error occurred while adding test documents");
            return errorResponse;
        }
    }

    /// <summary>
    /// HTTP endpoint for clearing test documents from the persistent index
    /// </summary>
    [Function("ClearTestDocuments")]
    [OpenApiOperation(operationId: "Test_ClearDocuments", tags: new[] { "TestData" }, Summary = "Clear test docs", Description = "Deletes the sample documents from the index.")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(object), Summary = "Cleared", Description = "Deletion result with counts.")]
    public async Task<HttpResponseData> ClearTestDocuments(
        [HttpTrigger(AuthorizationLevel.Function, "delete")] HttpRequestData req)
    {
        _logger.LogInformation("Clearing test documents from persistent search index");

        try
        {
            var testDocumentIds = new[] { "test-doc-1", "test-doc-2", "test-doc-3" };
            var deletedCount = 0;

            foreach (var documentId in testDocumentIds)
            {
                var success = await _searchService.DeleteDocumentAsync(documentId);
                if (success)
                {
                    deletedCount++;
                }
            }

            var result = new
            {
                Message = "Test documents cleared from persistent search index",
                DeletedDocuments = deletedCount,
                TotalAttempted = testDocumentIds.Length,
                PersistentStorage = true
            };

            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "application/json");
            
            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            };
            
            await response.WriteStringAsync(JsonSerializer.Serialize(result, jsonOptions));

            _logger.LogInformation("Successfully deleted {DeletedCount}/{TotalCount} test documents from persistent storage", 
                deletedCount, testDocumentIds.Length);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing test documents from search index");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteStringAsync("An error occurred while clearing test documents");
            return errorResponse;
        }
    }

    /// <summary>
    /// HTTP endpoint for adding a custom document from URL content
    /// </summary>
    [Function("AddUrlDocument")]
    [OpenApiOperation(operationId: "Test_AddUrlDocument", tags: new[] { "TestData" }, Summary = "Add URL doc", Description = "Adds a custom document provided in the request body.")]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(CustomDocumentRequest), Required = true, Description = "Document payload.")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(object), Summary = "Added", Description = "Indexing result for the custom document.")]
    public async Task<HttpResponseData> AddUrlDocument(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req)
    {
        _logger.LogInformation("Adding custom URL document to persistent search index");

        try
        {
            // Read the JSON request body
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            
            if (string.IsNullOrEmpty(requestBody))
            {
                var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequestResponse.WriteStringAsync("Request body with document data is required");
                return badRequestResponse;
            }

            var customDoc = JsonSerializer.Deserialize<CustomDocumentRequest>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (customDoc == null || string.IsNullOrEmpty(customDoc.Content))
            {
                var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequestResponse.WriteStringAsync("Valid document content is required");
                return badRequestResponse;
            }

            // Ensure the persistent index exists
            var indexExists = await _searchService.EnsureIndexExistsAsync();
            if (!indexExists)
            {
                _logger.LogError("Failed to ensure search index exists");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync("Search service is not available");
                return errorResponse;
            }

        // Extract image URLs from HTML content if present
            var imageUrls = new List<string>();
    var imagesDetailed = new List<ImageInfo>();
            var imageAltText = new Dictionary<string, string>();
            var childDocs = new List<SearchDocument>();
            if (!string.IsNullOrWhiteSpace(customDoc.Content))
            {
                try
                {
                    var rx = new Regex("<img[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
                    var matches = rx.Matches(customDoc.Content);
                    foreach (Match m in matches)
                    {
                        var tag = m.Value;
                        // Extract src
                        var srcMatch = Regex.Match(tag, "src=\"(?<v>[^\"]+)\"", RegexOptions.IgnoreCase);
                        if (!srcMatch.Success) continue;
                        var src = srcMatch.Groups["v"].Value;
                        if (string.IsNullOrWhiteSpace(src)) continue;
                        // Extract alt if present
                        var altMatch = Regex.Match(tag, "alt=\"(?<v>[^\"]*)\"", RegexOptions.IgnoreCase);
                        if (altMatch.Success)
                        {
                            imageAltText[src] = altMatch.Groups["v"].Value;
                        }
                        // Make absolute URL if needed
                        if (!Uri.TryCreate(src, UriKind.Absolute, out var abs) && !string.IsNullOrWhiteSpace(customDoc.Url))
                        {
                            if (Uri.TryCreate(new Uri(customDoc.Url), src, out var combined))
                src = combined.ToString();
                        }
            imageUrls.Add(src);
                    }
                }
                catch { }
            }

            // Create the search document
            var document = new SearchDocument
            {
                Id = customDoc.Id ?? Guid.NewGuid().ToString(),
                Title = customDoc.Title ?? "Untitled Document",
                Content = customDoc.Content,
                Summary = customDoc.Summary ?? GenerateSummary(customDoc.Content),
                ContentType = "text",
                FileType = "url",
                Created = DateTime.UtcNow,
                Modified = DateTime.UtcNow,
                FileSize = customDoc.Content.Length,
                Url = customDoc.Url ?? "unknown",
                SourceType = "url",
                Author = customDoc.Author ?? "Unknown",
                Language = "en",
                KeyPhrases = ExtractKeyPhrases(customDoc.Content),
                HasImages = imageUrls.Count > 0,
                ImageCount = imageUrls.Count,
                Metadata = imageUrls.Count > 0 ? JsonSerializer.Serialize(new { images = imageUrls }) : null
            };

            // If we found image URLs, try to fetch lightweight metadata and add imagesDetailed
            if (imageUrls.Count > 0)
            {
                int i = 0;
                foreach (var url in imageUrls)
                {
                    try
                    {
                        using var http = new HttpClient();
                        using var reqMsg = new HttpRequestMessage(HttpMethod.Head, url);
                        using var head = await http.SendAsync(reqMsg);
                        string? contentType = head.Content.Headers.ContentType?.MediaType;
                        long? size = head.Content.Headers.ContentLength;

                        string? ocrPreview = null; // We don't download bodies here to keep it light

                        // Heuristic caption and keywords
                        string? alt = imageAltText.TryGetValue(url, out var a) ? a : null;
                        string? caption = !string.IsNullOrWhiteSpace(alt) ? alt : InferCaptionFromUrl(url);
                        var keywords = InferKeywords(alt, contentType, url);

                        imagesDetailed.Add(new ImageInfo
                        {
                            Id = Guid.NewGuid().ToString("n"),
                            Url = url,
                            ContentType = contentType,
                            FileSize = size,
                            Caption = caption,
                            Keywords = keywords,
                            OcrPreview = ocrPreview,
                            ParentId = document.Id,
                            ParentUrl = document.Url
                        });

                        // Create a child image document for indexing
                        var imgDoc = new SearchDocument
                        {
                            Id = $"{document.Id}#img-{++i}",
                            Title = !string.IsNullOrWhiteSpace(alt) ? alt : $"Image {i} - {document.Title}",
                            Content = alt ?? string.Empty,
                            Summary = !string.IsNullOrWhiteSpace(alt) ? alt : (caption ?? "Image referenced by parent document"),
                            ContentType = "image",
                            FileType = InferFileTypeFromUrlOrContentType(url, contentType),
                            Created = DateTime.UtcNow,
                            Modified = DateTime.UtcNow,
                            FileSize = size ?? 0,
                            Url = url,
                            SourceType = "url",
                            Author = document.Author,
                            Language = document.Language,
                            KeyPhrases = null,
                            HasImages = false,
                            ImageCount = 0,
                            ImageCaption = caption,
                            ImageKeywords = keywords,
                            Metadata = JsonSerializer.Serialize(new { parentId = document.Id, parentUrl = document.Url, caption, keywords })
                        };
                        childDocs.Add(imgDoc);
                    }
                    catch
                    {
                        // Ignore failures; we still return the URL
                        // On failure, still add a minimal record with inferred caption
                        var alt2 = imageAltText.TryGetValue(url, out var a2) ? a2 : null;
                        imagesDetailed.Add(new ImageInfo { Url = url, ParentId = document.Id, ParentUrl = document.Url, Caption = !string.IsNullOrWhiteSpace(alt2) ? alt2 : InferCaptionFromUrl(url), Keywords = InferKeywords(alt2, null, url) });
                    }
                }

                // Merge into metadata
                try
                {
                    var metaAnon = new { images = imageUrls, imagesDetailed = imagesDetailed };
                    document.Metadata = JsonSerializer.Serialize(metaAnon);
                }
                catch { }
            }

            // Index the document
            var indexedId = await _searchService.IndexDocumentAsync(document);

            // Index child image documents if any
            if (childDocs.Count > 0)
            {
                try { await _searchService.IndexDocumentsAsync(childDocs.ToArray()); } catch { }
            }

            var result = new
            {
                Message = "Custom URL document added to persistent search index",
                DocumentId = indexedId,
                Title = document.Title,
                ContentLength = document.Content.Length,
                Url = document.Url,
                PersistentStorage = true,
                IndexName = "ragsearch-documents"
            };

            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "application/json");
            
            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            };
            
            await response.WriteStringAsync(JsonSerializer.Serialize(result, jsonOptions));

            _logger.LogInformation("Successfully indexed custom document '{DocumentId}' from URL '{Url}'", 
                indexedId, document.Url);

            return response;
        }
    catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding custom URL document to search index");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteStringAsync("An error occurred while adding the custom document");
            return errorResponse;
        }
    }

    private static string? InferCaptionFromUrl(string url)
    {
        try
        {
            var uri = new Uri(url);
            var name = System.IO.Path.GetFileNameWithoutExtension(uri.AbsolutePath);
            if (string.IsNullOrWhiteSpace(name)) return null;
            name = name.Replace('-', ' ').Replace('_', ' ');
            return System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name);
        }
        catch { return null; }
    }

    private static string[]? InferKeywords(string? alt, string? contentType, string url)
    {
        var tokens = new List<string>();
        if (!string.IsNullOrWhiteSpace(alt)) tokens.AddRange(Tokenize(alt));
        if (!string.IsNullOrWhiteSpace(contentType)) tokens.AddRange(Tokenize(contentType));
        try
        {
            var uri = new Uri(url);
            tokens.AddRange(Tokenize(System.IO.Path.GetFileNameWithoutExtension(uri.AbsolutePath)));
            var segs = uri.Segments?.Select(s => s.Trim('/')).Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
            if (segs != null) tokens.AddRange(segs.SelectMany(Tokenize));
        }
        catch { }
        var kws = tokens.Select(t => t.Trim().ToLowerInvariant())
                        .Where(t => t.Length >= 3)
                        .Distinct()
                        .Take(8)
                        .ToArray();
        return kws.Length > 0 ? kws : null;
    }

    private static IEnumerable<string> Tokenize(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) yield break;
        foreach (var part in Regex.Split(s, "[^A-Za-z0-9]+").Where(p => !string.IsNullOrWhiteSpace(p)))
            yield return part;
    }

    private string GenerateSummary(string content)
    {
        // Simple summary generation - take first 150 characters
        if (string.IsNullOrEmpty(content)) return "";
        return content.Length > 150 ? content[..150] + "..." : content;
    }

    private static string InferFileTypeFromUrlOrContentType(string url, string? contentType)
    {
        try
        {
            var ext = System.IO.Path.GetExtension(new Uri(url).AbsolutePath).Trim('.').ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(ext)) return ext;
        }
        catch { }
        return contentType switch
        {
            "image/png" => "png",
            "image/jpeg" => "jpg",
            _ => "image"
        };
    }

    private string[] ExtractKeyPhrases(string content)
    {
        // Simple key phrase extraction - find common security and AI terms
        var commonTerms = new[] { "Security Copilot", "prompt", "Azure", "Microsoft", "AI", "analysis", "security", "data", "response", "context", "goal", "effective" };
        return commonTerms.Where(term => content.Contains(term, StringComparison.OrdinalIgnoreCase)).ToArray();
    }
}

/// <summary>
/// Request model for adding custom documents
/// </summary>
public class CustomDocumentRequest
{
    public string? Id { get; set; }
    public string? Title { get; set; }
    public string Content { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public string? Url { get; set; }
    public string? Author { get; set; }
}