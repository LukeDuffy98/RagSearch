using System.Text.Json.Serialization;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;

namespace RagSearch.Models;

/// <summary>
/// Document model for Azure AI Search index
/// This represents the structure of documents stored in the persistent search index
/// </summary>
public class SearchDocument
{
    [SimpleField(IsKey = true, IsFilterable = true)]
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [SearchableField(IsSortable = false)]
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [SearchableField(AnalyzerName = "en.microsoft")]
    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [SearchableField(IsSortable = false)]
    [JsonPropertyName("summary")]
    public string Summary { get; set; } = string.Empty;

    [SimpleField(IsFilterable = true, IsFacetable = true)]
    [JsonPropertyName("contentType")]
    public string ContentType { get; set; } = string.Empty;

    [SimpleField(IsFilterable = true, IsFacetable = true)]
    [JsonPropertyName("fileType")]
    public string FileType { get; set; } = string.Empty;

    [SimpleField(IsFilterable = true, IsSortable = true)]
    [JsonPropertyName("created")]
    public DateTime Created { get; set; }

    [SimpleField(IsFilterable = true, IsSortable = true)]
    [JsonPropertyName("modified")]
    public DateTime Modified { get; set; }

    [SimpleField(IsFilterable = true, IsSortable = true)]
    [JsonPropertyName("indexed")]
    public DateTime Indexed { get; set; }

    [SimpleField(IsFilterable = true)]
    [JsonPropertyName("fileSize")]
    public long FileSize { get; set; }

    [SimpleField(IsFilterable = true)]
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [SimpleField(IsFilterable = true, IsFacetable = true)]
    [JsonPropertyName("sourceContainer")]
    public string? SourceContainer { get; set; }

    [SimpleField(IsFilterable = true, IsFacetable = true)]
    [JsonPropertyName("sourceType")]
    public string SourceType { get; set; } = string.Empty; // "blob" or "url"

    [SearchableField(IsSortable = false)]
    [JsonPropertyName("author")]
    public string? Author { get; set; }

    [SimpleField(IsFilterable = true, IsFacetable = true)]
    [JsonPropertyName("language")]
    public string? Language { get; set; }

    [SearchableField]
    [JsonPropertyName("keyPhrases")]
    public string[]? KeyPhrases { get; set; }

    [SimpleField(IsFilterable = true)]
    [JsonPropertyName("hasImages")]
    public bool HasImages { get; set; }

    [SimpleField(IsFilterable = true)]
    [JsonPropertyName("imageCount")]
    public int ImageCount { get; set; }

    // First-class fields for image documents to enable server-side filtering
    [SearchableField(IsFilterable = true)]
    [JsonPropertyName("imageCaption")]
    public string? ImageCaption { get; set; }

    [SearchableField(IsFilterable = true, IsFacetable = true)]
    [JsonPropertyName("imageKeywords")]
    public string[]? ImageKeywords { get; set; }

    // Vector field for semantic search - this is stored persistently in Azure AI Search
    [VectorSearchField(VectorSearchDimensions = 1536, VectorSearchProfileName = "my-vector-profile")]
    [JsonPropertyName("contentVector")]
    public float[]? ContentVector { get; set; }

    // Additional metadata stored as JSON
    [SimpleField(IsFilterable = false)]
    [JsonPropertyName("metadata")]
    public string? Metadata { get; set; }
}

/// <summary>
/// Represents a document during processing pipeline
/// </summary>
public class DocumentToIndex
{
    public string Id { get; set; } = string.Empty;
    public string SourceUrl { get; set; } = string.Empty;
    public string SourceType { get; set; } = string.Empty; // "blob" or "url"
    public string? SourceContainer { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public string FileType { get; set; } = string.Empty;
    public byte[] Content { get; set; } = [];
    public long FileSize { get; set; }
    public DateTime Created { get; set; }
    public DateTime Modified { get; set; }
    public Dictionary<string, object> AdditionalMetadata { get; set; } = new();
}

/// <summary>
/// Result of document processing
/// </summary>
public class ProcessedDocument
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string ExtractedText { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string? Author { get; set; }
    public string? Language { get; set; }
    public string[]? KeyPhrases { get; set; }
    public ExtractedImage[] ExtractedImages { get; set; } = [];
    public float[]? ContentVector { get; set; }
    public DocumentToIndex SourceDocument { get; set; } = new();
}

/// <summary>
/// Represents an image extracted from a document
/// </summary>
public class ExtractedImage
{
    public string Id { get; set; } = string.Empty;
    public string ParentDocumentId { get; set; } = string.Empty;
    public byte[] ImageData { get; set; } = [];
    public string ContentType { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ExtractedText { get; set; } // OCR text
    public string[]? DetectedObjects { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public long FileSize { get; set; }
}

/// <summary>
/// URL processing request
/// </summary>
public class UrlProcessingRequest
{
    [JsonPropertyName("urls")]
    public string[] Urls { get; set; } = [];

    [JsonPropertyName("options")]
    public UrlProcessingOptions? Options { get; set; }
}

/// <summary>
/// Options for URL processing
/// </summary>
public class UrlProcessingOptions
{
    [JsonPropertyName("extractImages")]
    public bool ExtractImages { get; set; } = true;

    [JsonPropertyName("processEmbeddedContent")]
    public bool ProcessEmbeddedContent { get; set; } = true;

    [JsonPropertyName("generateThumbnails")]
    public bool GenerateThumbnails { get; set; } = false;

    [JsonPropertyName("maxFileSize")]
    public long MaxFileSize { get; set; } = 100 * 1024 * 1024; // 100MB default

    [JsonPropertyName("allowedFileTypes")]
    public string[]? AllowedFileTypes { get; set; }
}

/// <summary>
/// URL processing result
/// </summary>
public class UrlProcessingResult
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("documentId")]
    public string? DocumentId { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("processedAt")]
    public DateTime ProcessedAt { get; set; }

    [JsonPropertyName("extractedImages")]
    public int ExtractedImages { get; set; }
}