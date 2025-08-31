using System.Net;
using System.Text.Json;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Extensions.Logging;

namespace RagSearch.Functions;

public class UploadBlobFunction
{
    private readonly ILogger<UploadBlobFunction> _logger;
    private readonly BlobServiceClient _blobServiceClient;

    public UploadBlobFunction(ILoggerFactory loggerFactory, BlobServiceClient blobServiceClient)
    {
        _logger = loggerFactory.CreateLogger<UploadBlobFunction>();
        _blobServiceClient = blobServiceClient;
    }

    public class UploadBlobRequest
    {
        public string Container { get; set; } = "docs";
        public string Name { get; set; } = string.Empty;
        public string ContentBase64 { get; set; } = string.Empty;
        public string? ContentType { get; set; }
    }

    [Function("UploadBlob")]
    [OpenApiOperation(operationId: "Upload_Blob", tags: new[] { "Ingestion" }, Summary = "Upload a blob (dev)", Description = "Uploads a file to Blob Storage using base64 content. For local/dev use.")]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(UploadBlobRequest), Required = true, Description = "Container, name, base64 content, contentType")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(object), Summary = "Uploaded", Description = "Upload result")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "UploadBlob")] HttpRequestData req)
    {
        var resp = req.CreateResponse();
        try
        {
            var body = await new StreamReader(req.Body).ReadToEndAsync();
            var payload = JsonSerializer.Deserialize<UploadBlobRequest>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                          ?? new UploadBlobRequest();

            if (string.IsNullOrWhiteSpace(payload.Name) || string.IsNullOrWhiteSpace(payload.ContentBase64))
            {
                resp.StatusCode = HttpStatusCode.BadRequest;
                await resp.WriteStringAsync("'name' and 'contentBase64' are required.");
                return resp;
            }

            var container = _blobServiceClient.GetBlobContainerClient(payload.Container);
            await container.CreateIfNotExistsAsync();
            var blob = container.GetBlobClient(payload.Name);

            var bytes = Convert.FromBase64String(payload.ContentBase64);
            using var ms = new MemoryStream(bytes);

            var options = new BlobUploadOptions();
            if (!string.IsNullOrWhiteSpace(payload.ContentType))
            {
                options.HttpHeaders = new BlobHttpHeaders { ContentType = payload.ContentType };
            }

            await blob.UploadAsync(ms, options: options, cancellationToken: default);

            resp.StatusCode = HttpStatusCode.OK;
            resp.Headers.Add("Content-Type", "application/json");
            await resp.WriteStringAsync(JsonSerializer.Serialize(new
            {
                uploaded = true,
                container = payload.Container,
                name = payload.Name,
                url = blob.Uri.ToString()
            }));
            return resp;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "UploadBlob failed");
            resp.StatusCode = HttpStatusCode.InternalServerError;
            await resp.WriteStringAsync("Upload failed");
            return resp;
        }
    }
}
