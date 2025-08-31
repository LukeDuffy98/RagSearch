using System.Net;
using System.Text.Json;
using Azure.Storage.Blobs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Extensions.Logging;
using RagSearch.Models;
using RagSearch.Services;
using System.Text.RegularExpressions;

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
            var createResponse = await container.CreateIfNotExistsAsync();
            bool containerCreated = createResponse != null; // null means already existed
            if (containerCreated)
            {
                _logger.LogInformation("Created source container '{Container}' for ingestion", options.Container);
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
                var imagesList = new List<string>();
                var imagesDetailed = new List<object>();
                bool isImage = string.Equals(docToIndex.FileType, "png", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(docToIndex.FileType, "jpg", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(docToIndex.FileType, "jpeg", StringComparison.OrdinalIgnoreCase);
                if (isImage)
                {
                    imagesList.Add(blob.Uri.ToString());
                    // Heuristic caption/keywords from file name and content type
                    var caption = InferCaptionFromBlobUrl(blob.Uri.ToString());
                    var keywords = InferKeywordsFromBlob(blob);
                    imagesDetailed.Add(new { url = blob.Uri.ToString(), contentType = docToIndex.ContentType, fileSize = docToIndex.FileSize, caption, keywords });
                }

                var sd = new SearchDocument
                {
                    Id = docToIndex.Id,
                    Title = processedDoc.Title,
                    Content = processedDoc.ExtractedText,
                    Summary = processedDoc.Summary,
                    ContentType = isImage ? "image" : "text",
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
                    HasImages = isImage,
                    ImageCount = isImage ? 1 : (processedDoc.ExtractedImages?.Length ?? 0),
                    ImageCaption = isImage ? InferCaptionFromBlobUrl(blob.Uri.ToString()) : null,
                    ImageKeywords = isImage ? InferKeywordsFromBlob(blob) : null,
                    Metadata = JsonSerializer.Serialize(new
                    {
                        images = imagesList.Count > 0 ? imagesList : null,
                        imagesDetailed = imagesDetailed.Count > 0 ? imagesDetailed : null,
                        original = docToIndex.AdditionalMetadata
                    })
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
                prefix = options.Prefix,
                containerCreated
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

    // Local helpers for blob image caption/keywords
    private static string? InferCaptionFromBlobUrl(string url)
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

    private static string[]? InferKeywordsFromBlob(BlobClient blob)
    {
        try
        {
            var uri = blob.Uri;
            var tokens = new List<string>();
            var name = System.IO.Path.GetFileNameWithoutExtension(uri.AbsolutePath);
            if (!string.IsNullOrWhiteSpace(name)) tokens.AddRange(Regex.Split(name, "[^A-Za-z0-9]+").Where(p => !string.IsNullOrWhiteSpace(p)));
            var segs = uri.Segments?.Select(s => s.Trim('/')).Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
            if (segs != null) foreach (var seg in segs) tokens.AddRange(Regex.Split(seg, "[^A-Za-z0-9]+").Where(p => !string.IsNullOrWhiteSpace(p)));
            var kws = tokens.Select(t => t.Trim().ToLowerInvariant()).Where(t => t.Length >= 3).Distinct().Take(8).ToArray();
            return kws.Length > 0 ? kws : null;
        }
        catch { return null; }
    }
}
