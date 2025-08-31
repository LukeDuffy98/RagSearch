using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure;
using Azure.AI.OpenAI;
using Azure.Storage.Blobs;
using RagSearch.Services;
using Azure.AI.FormRecognizer.DocumentAnalysis;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices(services =>
    {
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();
        
        // Get the search service type from configuration
        var searchServiceType = Environment.GetEnvironmentVariable("SEARCH_SERVICE_TYPE") ?? "simplified";
        
        if (searchServiceType.Equals("azure-search", StringComparison.OrdinalIgnoreCase))
        {
            // Configure Azure AI Search for persistent indexing
            services.AddSingleton<SearchIndexClient>(provider =>
            {
                var searchEndpoint = Environment.GetEnvironmentVariable("AZURE_SEARCH_SERVICE_ENDPOINT") 
                    ?? throw new InvalidOperationException("AZURE_SEARCH_SERVICE_ENDPOINT is not configured");
                var searchApiKey = Environment.GetEnvironmentVariable("AZURE_SEARCH_API_KEY")
                    ?? throw new InvalidOperationException("AZURE_SEARCH_API_KEY is not configured");
                
                var credential = new AzureKeyCredential(searchApiKey);
                return new SearchIndexClient(new Uri(searchEndpoint), credential);
            });
            
            services.AddSingleton<SearchClient>(provider =>
            {
                var searchEndpoint = Environment.GetEnvironmentVariable("AZURE_SEARCH_SERVICE_ENDPOINT") 
                    ?? throw new InvalidOperationException("AZURE_SEARCH_SERVICE_ENDPOINT is not configured");
                var searchApiKey = Environment.GetEnvironmentVariable("AZURE_SEARCH_API_KEY")
                    ?? throw new InvalidOperationException("AZURE_SEARCH_API_KEY is not configured");
                var indexName = Environment.GetEnvironmentVariable("AZURE_SEARCH_INDEX_NAME") 
                    ?? "ragsearch-documents";
                
                var credential = new AzureKeyCredential(searchApiKey);
                return new SearchClient(new Uri(searchEndpoint), indexName, credential);
            });
            
            // Register Azure AI Search service
            services.AddScoped<ISearchService, AzureSearchService>();
        }
        else
        {
            // Configure simplified search service with blob storage and OpenAI
            services.AddSingleton<BlobServiceClient>(provider =>
            {
                var connectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage")
                    ?? throw new InvalidOperationException("AzureWebJobsStorage is not configured");
                return new BlobServiceClient(connectionString);
            });
            
            services.AddSingleton<OpenAIClient>(provider =>
            {
                var openAIEndpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT")
                    ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT is not configured");
                var openAIApiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY")
                    ?? throw new InvalidOperationException("AZURE_OPENAI_API_KEY is not configured");
                
                var credential = new AzureKeyCredential(openAIApiKey);
                return new OpenAIClient(new Uri(openAIEndpoint), credential);
            });

            // Document Intelligence client for OCR/text extraction
            services.AddSingleton<DocumentAnalysisClient>(provider =>
            {
                var diEndpoint = Environment.GetEnvironmentVariable("AZURE_DOCUMENTINTELLIGENCE_ENDPOINT")
                    ?? throw new InvalidOperationException("AZURE_DOCUMENTINTELLIGENCE_ENDPOINT is not configured");
                var diKey = Environment.GetEnvironmentVariable("AZURE_DOCUMENTINTELLIGENCE_API_KEY")
                    ?? throw new InvalidOperationException("AZURE_DOCUMENTINTELLIGENCE_API_KEY is not configured");
                var credential = new AzureKeyCredential(diKey);
                return new DocumentAnalysisClient(new Uri(diEndpoint), credential);
            });

            services.AddScoped<IDocumentProcessingService, DocumentProcessingService>();
            
            // Register simplified search service
            services.AddScoped<ISearchService, SimplifiedSearchService>();
        }
    })
    .Build();

host.Run();