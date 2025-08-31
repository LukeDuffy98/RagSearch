using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using RagSearch.Models;
using RagSearch.Services;
using System.Net;
using System.Text.Json;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.OpenApi.Models;

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
                HasImages = false,
                ImageCount = 0
            };

            // Index the document
            var indexedId = await _searchService.IndexDocumentAsync(document);

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

    private string GenerateSummary(string content)
    {
        // Simple summary generation - take first 150 characters
        if (string.IsNullOrEmpty(content)) return "";
        return content.Length > 150 ? content[..150] + "..." : content;
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