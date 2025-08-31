using System.Net;
using System.Text.Json;
using Azure.Storage.Blobs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Extensions.Logging;
using RagSearch.Models;
using RagSearch.Services;

namespace RagSearch.Functions;

public class BlobBatchIngestionFunction
{
    private readonly ILogger<BlobBatchIngestionFunction> _logger;
    private readonly BlobServiceClient _blobServiceClient;
    private readonly IDocumentProcessingService _docProcessor;
    private readonly ISearchService _searchService;

    public BlobBatchIngestionFunction(
        ILoggerFactory loggerFactory,
        BlobServiceClient blobServiceClient,
        IDocumentProcessingService docProcessor,
        ISearchService searchService)
    {
        _logger = loggerFactory.CreateLogger<BlobBatchIngestionFunction>();
        _blobServiceClient = blobServiceClient;
        _docProcessor = docProcessor;
        _searchService = searchService;
    }

    public record IngestRequest(string Container, string? Prefix = null, string[]? AllowedExtensions = null, int MaxFiles = 50);

    [Function("IngestBlobsBatch")]
    [OpenApiOperation(operationId: "Ingest_Blobs_Batch", tags: new[] { "Ingestion" }, Summary = "Batch ingest from Blob Storage", Description = "Processes documents from a blob container and adds them to the search index.")]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(IngestRequest), Required = true, Description = "Ingestion options: container name, optional prefix, allowed extensions, max files.")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(object), Summary = "Ingest results", Description = "Summary of batch ingestion.")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "Ingest/BlobsBatch")] HttpRequestData req,
        FunctionContext context)
    {
        var resp = req.CreateResponse();
        try
        {
            var body = await new StreamReader(req.Body).ReadToEndAsync();
            var options = JsonSerializer.Deserialize<IngestRequest>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                          ?? new IngestRequest(Container: "docs");

            if (string.IsNullOrWhiteSpace(options.Container))
            {
                resp.StatusCode = HttpStatusCode.BadRequest;
                await resp.WriteStringAsync("Container is required");
                return resp;
            }

            var allow = new HashSet<string>(options.AllowedExtensions ?? new[] { ".pdf", ".docx", ".pptx", ".txt", ".png", ".jpg", ".jpeg" }, StringComparer.OrdinalIgnoreCase);

            var container = _blobServiceClient.GetBlobContainerClient(options.Container);
            if (!await container.ExistsAsync())
            {
                resp.StatusCode = HttpStatusCode.BadRequest;
                await resp.WriteStringAsync($"Container '{options.Container}' does not exist.");
                return resp;
            }

            await _searchService.EnsureIndexExistsAsync();

            int processed = 0, indexed = 0;
            var docsToIndex = new List<SearchDocument>();

            await foreach (var item in container.GetBlobsAsync(prefix: options.Prefix))
            {
                if (processed >= options.MaxFiles) break;

                var name = item.Name;
                var ext = System.IO.Path.GetExtension(name);
                if (!allow.Contains(ext)) continue;

                var blob = container.GetBlobClient(name);
                var download = await blob.DownloadContentAsync();
                var bytes = download.Value.Content.ToArray();

                var docToIndex = new DocumentToIndex
                {
                    Id = $"{options.Container}:{name}",
                    SourceUrl = blob.Uri.ToString(),
                    SourceType = "blob",
                    SourceContainer = options.Container,
                    ContentType = item.Properties.ContentType ?? MimeForExt(ext),
                    FileType = ext.Trim('.').ToLowerInvariant(),
                    Content = bytes,
                    FileSize = item.Properties.ContentLength ?? bytes.LongLength,
                    Created = item.Properties.CreatedOn?.UtcDateTime ?? DateTime.UtcNow,
                    Modified = item.Properties.LastModified?.UtcDateTime ?? DateTime.UtcNow
                };

                var processedDoc = await _docProcessor.ProcessAsync(docToIndex);

                // Convert to SearchDocument for indexing
                var sd = new SearchDocument
                {
                    Id = docToIndex.Id,
                    Title = processedDoc.Title,
                    Content = processedDoc.ExtractedText,
                    Summary = processedDoc.Summary,
                    ContentType = "text",
                    FileType = docToIndex.FileType,
                    Created = docToIndex.Created,
                    Modified = docToIndex.Modified,
                    Indexed = DateTime.UtcNow,
                    FileSize = docToIndex.FileSize,
                    Url = docToIndex.SourceUrl,
                    SourceContainer = docToIndex.SourceContainer,
                    SourceType = docToIndex.SourceType,
                    Author = processedDoc.Author,
                    Language = processedDoc.Language,
                    KeyPhrases = processedDoc.KeyPhrases,
                    HasImages = string.Equals(docToIndex.FileType, "png", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(docToIndex.FileType, "jpg", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(docToIndex.FileType, "jpeg", StringComparison.OrdinalIgnoreCase),
                    ImageCount = processedDoc.ExtractedImages?.Length ?? 0,
                    Metadata = JsonSerializer.Serialize(docToIndex.AdditionalMetadata)
                };

                docsToIndex.Add(sd);
                processed++;

                // Periodic flush to keep memory bounded
                if (docsToIndex.Count >= 10)
                {
                    var ids = await _searchService.IndexDocumentsAsync(docsToIndex.ToArray());
                    indexed += ids.Length;
                    docsToIndex.Clear();
                }
            }

            if (docsToIndex.Count > 0)
            {
                var ids = await _searchService.IndexDocumentsAsync(docsToIndex.ToArray());
                indexed += ids.Length;
            }

            resp.StatusCode = HttpStatusCode.OK;
            resp.Headers.Add("Content-Type", "application/json");
            await resp.WriteStringAsync(JsonSerializer.Serialize(new
            {
                processed,
                indexed,
                container = options.Container,
                prefix = options.Prefix
            }, new JsonSerializerOptions { WriteIndented = true }));
            return resp;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during blob batch ingestion");
            resp.StatusCode = HttpStatusCode.InternalServerError;
            await resp.WriteStringAsync("Error during ingestion");
            return resp;
        }
    }

    private static string MimeForExt(string ext)
    {
        return ext.ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            ".txt" => "text/plain",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            _ => "application/octet-stream"
        };
    }
}
