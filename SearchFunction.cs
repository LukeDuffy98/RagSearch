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
/// Azure Function for handling search requests against the persistent Azure AI Search index
/// </summary>
public class SearchFunction
{
    private readonly ILogger<SearchFunction> _logger;
    private readonly ISearchService _searchService;

    public SearchFunction(ILoggerFactory loggerFactory, ISearchService searchService)
    {
        _logger = loggerFactory.CreateLogger<SearchFunction>();
        _searchService = searchService ?? throw new ArgumentNullException(nameof(searchService));
    }

    /// <summary>
    /// HTTP GET endpoint for performing searches using query parameters
    /// </summary>
    [Function("SearchGet")]
    [OpenApiOperation(operationId: "Search_Run_Get", tags: new[] { "Search" }, Summary = "Search documents (GET)", Description = "Performs keyword/hybrid searches over the persistent index using query parameters.")]
    [OpenApiParameter(name: "q", In = ParameterLocation.Query, Required = false, Type = typeof(string), Summary = "Query text", Description = "Search query text.")]
    [OpenApiParameter(name: "type", In = ParameterLocation.Query, Required = false, Type = typeof(string), Summary = "Type/mode", Description = "Search mode (keyword|vector|hybrid) OR content filter (text|image)")]
    [OpenApiParameter(name: "content", In = ParameterLocation.Query, Required = false, Type = typeof(string), Summary = "Content filter", Description = "Comma-separated content types (e.g., text,image). If omitted, you can also pass type=image or type=text as a shorthand.")]
    [OpenApiParameter(name: "maxResults", In = ParameterLocation.Query, Required = false, Type = typeof(int), Summary = "Max results", Description = "Maximum results to return")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(SearchResponse), Summary = "Search results", Description = "Search results payload.")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.BadRequest, contentType: "text/plain", bodyType: typeof(string))]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.InternalServerError, contentType: "text/plain", bodyType: typeof(string))]
    public async Task<HttpResponseData> Get(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "Search")] HttpRequestData req)
    {
        _logger.LogInformation("Search GET processing request");

        try
        {
            var indexExists = await _searchService.EnsureIndexExistsAsync();
            if (!indexExists)
            {
                _logger.LogError("Failed to ensure search index exists");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync("Search service is not available");
                return errorResponse;
            }

            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);

            var qText = query["q"] ?? string.Empty;
            var typeParam = (query["type"] ?? string.Empty).Trim();
            var contentParam = (query["content"] ?? string.Empty).Trim();

            // Determine content filter. Priority: explicit 'content' param; else allow shorthand via type=image|text; else default both.
            string[] contentTypes;
            if (!string.IsNullOrEmpty(contentParam))
            {
                contentTypes = contentParam.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            }
            else if (typeParam.Equals("image", StringComparison.OrdinalIgnoreCase) || typeParam.Equals("text", StringComparison.OrdinalIgnoreCase))
            {
                contentTypes = new[] { typeParam.ToLowerInvariant() };
            }
            else
            {
                contentTypes = new[] { "text", "image" };
            }

            // Determine search mode. Accept keyword|vector|hybrid via 'type', otherwise default to Keyword.
            SearchType mode = SearchType.Keyword;
            if (Enum.TryParse<SearchType>(typeParam, true, out var parsedMode))
            {
                mode = parsedMode;
            }

            var searchRequest = new SearchRequest
            {
                Query = qText,
                SearchType = mode,
                ContentTypes = contentTypes,
                MaxResults = int.TryParse(query["maxResults"], out var maxResults) ? maxResults : 10
            };

            if (string.IsNullOrEmpty(searchRequest.Query))
            {
                var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequestResponse.WriteStringAsync("Query parameter is required");
                return badRequestResponse;
            }

            var searchResponse = await _searchService.SearchAsync(searchRequest);

            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "application/json");
            var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
            await response.WriteStringAsync(JsonSerializer.Serialize(searchResponse, jsonOptions));
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing GET search request");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteStringAsync("An error occurred while processing the search request");
            return errorResponse;
        }
    }

    /// <summary>
    /// HTTP POST endpoint for performing searches using JSON body
    /// </summary>
    [Function("SearchPost")]
    [OpenApiOperation(operationId: "Search_Run_Post", tags: new[] { "Search" }, Summary = "Search documents (POST)", Description = "Performs searches with rich options in the request body.")]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(SearchRequest), Required = true, Description = "Search request body.")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(SearchResponse), Summary = "Search results", Description = "Search results payload.")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.BadRequest, contentType: "text/plain", bodyType: typeof(string))]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.InternalServerError, contentType: "text/plain", bodyType: typeof(string))]
    public async Task<HttpResponseData> Post(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "Search")] HttpRequestData req)
    {
        _logger.LogInformation("Search POST processing request");

        try
        {
            var indexExists = await _searchService.EnsureIndexExistsAsync();
            if (!indexExists)
            {
                _logger.LogError("Failed to ensure search index exists");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync("Search service is not available");
                return errorResponse;
            }

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            if (string.IsNullOrEmpty(requestBody))
            {
                var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequestResponse.WriteStringAsync("Request body is required for POST requests");
                return badRequestResponse;
            }

            var searchRequest = JsonSerializer.Deserialize<SearchRequest>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new SearchRequest();

            if (string.IsNullOrEmpty(searchRequest.Query))
            {
                var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequestResponse.WriteStringAsync("Query is required");
                return badRequestResponse;
            }

            var searchResponse = await _searchService.SearchAsync(searchRequest);

            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "application/json");
            var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
            await response.WriteStringAsync(JsonSerializer.Serialize(searchResponse, jsonOptions));
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing POST search request");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteStringAsync("An error occurred while processing the search request");
            return errorResponse;
        }
    }

    /// <summary>
    /// HTTP endpoint for getting search index status and statistics
    /// </summary>
    [Function("SearchStatus")]
    [OpenApiOperation(operationId: "Search_Status", tags: new[] { "Search" }, Summary = "Index status", Description = "Gets status and statistics for the search index.")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(SearchStatusResponse), Summary = "Status info", Description = "Aggregated status and stats.")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.InternalServerError, contentType: "text/plain", bodyType: typeof(string))]
    public async Task<HttpResponseData> GetStatus(
        [HttpTrigger(AuthorizationLevel.Function, "get")] HttpRequestData req)
    {
        _logger.LogInformation("Getting search index status");

        try
        {
            // Get index statistics
            var stats = await _searchService.GetIndexStatisticsAsync();
            
            var statusInfo = new SearchStatusResponse
            {
                IndexName = "ragsearch-documents",
                DocumentCount = stats.DocumentCount,
                StorageSize = stats.StorageSize,
                Status = "Available",
                LastUpdated = DateTime.UtcNow,
                PersistentStorage = true,
                Features = new SearchStatusFeatures
                {
                    KeywordSearch = true,
                    VectorSearch = false, // Will be enabled when OpenAI service is added
                    HybridSearch = false, // Will be enabled when OpenAI service is added
                    SemanticSearch = true
                }
            };

            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "application/json");
            
            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            };
            
            await response.WriteStringAsync(JsonSerializer.Serialize(statusInfo, jsonOptions));

            _logger.LogInformation("Search index status retrieved successfully: {DocumentCount} documents, {StorageSize} bytes", 
                stats.DocumentCount, stats.StorageSize);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting search index status");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteStringAsync("An error occurred while getting search status");
            return errorResponse;
        }
    }

    /// <summary>
    /// HTTP endpoint for rebuilding the search index (development/admin use)
    /// WARNING: This will delete all existing data
    /// </summary>
    [Function("RebuildIndex")]
    [OpenApiOperation(operationId: "Search_RebuildIndex", tags: new[] { "Search-Admin" }, Summary = "Rebuild index", Description = "Deletes and rebuilds the search index. Admin only.")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "text/plain", bodyType: typeof(string), Summary = "Rebuilt", Description = "Index rebuilt successfully.")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.InternalServerError, contentType: "text/plain", bodyType: typeof(string))]
    public async Task<HttpResponseData> RebuildIndex(
        [HttpTrigger(AuthorizationLevel.Admin, "post")] HttpRequestData req)
    {
        _logger.LogWarning("Index rebuild requested - this will delete all existing data");

        try
        {
            var success = await _searchService.RebuildIndexAsync();
            
            if (success)
            {
                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteStringAsync("Search index rebuilt successfully");
                _logger.LogInformation("Search index rebuilt successfully");
                return response;
            }
            else
            {
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync("Failed to rebuild search index");
                return errorResponse;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error rebuilding search index");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteStringAsync("An error occurred while rebuilding the search index");
            return errorResponse;
        }
    }
}