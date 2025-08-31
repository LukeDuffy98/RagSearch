using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace RagSearch;

public class SwaggerUiRedirectFunction
{
    private readonly ILogger<SwaggerUiRedirectFunction> _logger;

    public SwaggerUiRedirectFunction(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<SwaggerUiRedirectFunction>();
    }

    [Function("Swagger")] // GET /api/swagger by default; we'll route as /swagger using Route below
    public HttpResponseData Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "swagger")] HttpRequestData req)
    {
        var enableSwagger = (Environment.GetEnvironmentVariable("ENABLE_SWAGGER") ?? "true")
            .Equals("true", StringComparison.OrdinalIgnoreCase);

        if (!enableSwagger)
        {
            _logger.LogInformation("Swagger UI disabled by configuration");
            var disabled = req.CreateResponse(HttpStatusCode.NotFound);
            disabled.Headers.Add("Content-Type", "text/plain; charset=utf-8");
            disabled.WriteString("Swagger UI is disabled.");
            return disabled;
        }

        // Redirect to the OpenAPI UI provided by the extension
        var response = req.CreateResponse(HttpStatusCode.Redirect);
        response.Headers.Add("Location", "/api/swagger/ui");
        _logger.LogInformation("Redirecting to Swagger UI at /api/swagger/ui");
        return response;
    }
}
