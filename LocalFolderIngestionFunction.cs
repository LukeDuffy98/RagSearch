using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Extensions.Logging;
using RagSearch.Models;
using RagSearch.Services;

namespace RagSearch.Functions;

public class LocalFolderIngestionFunction
{
    private readonly ILogger<LocalFolderIngestionFunction> _logger;
    private readonly IDocumentProcessingService _docProcessor;
    private readonly ISearchService _searchService;

    public LocalFolderIngestionFunction(
        ILoggerFactory loggerFactory,
        IDocumentProcessingService docProcessor,
        ISearchService searchService)
    {
        _logger = loggerFactory.CreateLogger<LocalFolderIngestionFunction>();
        _docProcessor = docProcessor;
        _searchService = searchService;
    }

    public record IngestLocalRequest(string Path, string[]? AllowedExtensions = null, int MaxFiles = 5000);

    [Function("IngestLocalFolder")]
    [OpenApiOperation(operationId: "Ingest_Local_Folder", tags: new[] { "Ingestion" }, Summary = "Ingest from local folder (dev)", Description = "Recursively reads files from a local folder and indexes them. For local/dev only.")]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(IngestLocalRequest), Required = true, Description = "Path, allowedExtensions, maxFiles")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(object), Summary = "Ingestion results", Description = "Summary of local folder ingestion.")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "Ingest/LocalFolder")] HttpRequestData req)
    {
        var resp = req.CreateResponse();
        try
        {
            var body = await new StreamReader(req.Body).ReadToEndAsync();
            var options = JsonSerializer.Deserialize<IngestLocalRequest>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                          ?? throw new ArgumentException("Invalid request body");

            if (string.IsNullOrWhiteSpace(options.Path))
            {
                resp.StatusCode = HttpStatusCode.BadRequest;
                await resp.WriteStringAsync("Path is required");
                return resp;
            }

            var root = System.IO.Path.GetFullPath(options.Path);
            if (!Directory.Exists(root))
            {
                resp.StatusCode = HttpStatusCode.BadRequest;
                await resp.WriteStringAsync($"Folder not found: {root}");
                return resp;
            }

            var allow = new HashSet<string>((options.AllowedExtensions ?? new[] { ".pdf", ".docx", ".pptx", ".txt", ".png", ".jpg", ".jpeg" }), StringComparer.OrdinalIgnoreCase);

            await _searchService.EnsureIndexExistsAsync();

            int processed = 0, indexed = 0;
            var buffer = new List<SearchDocument>(capacity: 16);

            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                if (processed >= options.MaxFiles) break;
                var ext = System.IO.Path.GetExtension(file);
                if (!allow.Contains(ext)) continue;

                try
                {
                    var bytes = await File.ReadAllBytesAsync(file);
                    var fileInfo = new FileInfo(file);

                    var rel = Path.GetRelativePath(root, file)
                        .Replace('\\', '/');

                    var doc = new DocumentToIndex
                    {
                        Id = $"local:{rel}",
                        SourceUrl = file,
                        SourceType = "file",
                        SourceContainer = "local",
                        ContentType = MimeForExt(ext),
                        FileType = ext.Trim('.').ToLowerInvariant(),
                        Content = bytes,
                        FileSize = fileInfo.Length,
                        Created = fileInfo.CreationTimeUtc,
                        Modified = fileInfo.LastWriteTimeUtc
                    };

                    var processedDoc = await _docProcessor.ProcessAsync(doc);

                    var sd = new SearchDocument
                    {
                        Id = doc.Id,
                        Title = processedDoc.Title,
                        Content = processedDoc.ExtractedText,
                        Summary = processedDoc.Summary,
                        ContentType = "text",
                        FileType = doc.FileType,
                        Created = doc.Created,
                        Modified = doc.Modified,
                        Indexed = DateTime.UtcNow,
                        FileSize = doc.FileSize,
                        Url = doc.SourceUrl,
                        SourceContainer = doc.SourceContainer,
                        SourceType = doc.SourceType,
                        Author = processedDoc.Author,
                        Language = processedDoc.Language,
                        KeyPhrases = processedDoc.KeyPhrases,
                        HasImages = false,
                        ImageCount = 0,
                        Metadata = JsonSerializer.Serialize(new { original = doc.AdditionalMetadata })
                    };

                    buffer.Add(sd);
                    processed++;

                    if (buffer.Count >= 10)
                    {
                        var ids = await _searchService.IndexDocumentsAsync(buffer.ToArray());
                        indexed += ids.Length;
                        buffer.Clear();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to process file: {File}", file);
                }
            }

            if (buffer.Count > 0)
            {
                var ids = await _searchService.IndexDocumentsAsync(buffer.ToArray());
                indexed += ids.Length;
            }

            resp.StatusCode = HttpStatusCode.OK;
            resp.Headers.Add("Content-Type", "application/json");
            await resp.WriteStringAsync(JsonSerializer.Serialize(new { processed, indexed, path = root }, new JsonSerializerOptions { WriteIndented = true }));
            return resp;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during local folder ingestion");
            resp.StatusCode = HttpStatusCode.InternalServerError;
            await resp.WriteStringAsync("Error during local ingestion");
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
