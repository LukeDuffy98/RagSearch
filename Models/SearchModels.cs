using System.Text.Json.Serialization;

namespace RagSearch.Models;

/// <summary>
/// Represents a search request with query parameters and options
/// </summary>
public class SearchRequest
{
    [JsonPropertyName("query")]
    public string Query { get; set; } = string.Empty;

    [JsonPropertyName("searchType")]
    public SearchType SearchType { get; set; } = SearchType.Hybrid;

    [JsonPropertyName("contentTypes")]
    public string[] ContentTypes { get; set; } = ["text", "image"];

    [JsonPropertyName("maxResults")]
    public int MaxResults { get; set; } = 10;

    [JsonPropertyName("filters")]
    public SearchFilters? Filters { get; set; }
}

/// <summary>
/// Search filtering options
/// </summary>
public class SearchFilters
{
    [JsonPropertyName("fileType")]
    public string? FileType { get; set; }

    [JsonPropertyName("dateRange")]
    public DateRange? DateRange { get; set; }

    [JsonPropertyName("sourceContainer")]
    public string? SourceContainer { get; set; }

    [JsonPropertyName("sourceType")]
    public string? SourceType { get; set; } // "blob" or "url"

    [JsonPropertyName("fileSize")]
    public FileSizeRange? FileSize { get; set; }
}

/// <summary>
/// Date range filter
/// </summary>
public class DateRange
{
    [JsonPropertyName("from")]
    public DateTime? From { get; set; }

    [JsonPropertyName("to")]
    public DateTime? To { get; set; }
}

/// <summary>
/// File size range filter (in bytes)
/// </summary>
public class FileSizeRange
{
    [JsonPropertyName("min")]
    public long? Min { get; set; }

    [JsonPropertyName("max")]
    public long? Max { get; set; }
}

/// <summary>
/// Search type enumeration
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SearchType
{
    Keyword,
    Vector,
    Hybrid
}

/// <summary>
/// Represents a search response with results and metadata
/// </summary>
public class SearchResponse
{
    [JsonPropertyName("query")]
    public string Query { get; set; } = string.Empty;

    [JsonPropertyName("totalResults")]
    public int TotalResults { get; set; }

    [JsonPropertyName("results")]
    public SearchResult[] Results { get; set; } = [];

    [JsonPropertyName("executionTime")]
    public double ExecutionTimeMs { get; set; }

    [JsonPropertyName("searchType")]
    public SearchType SearchType { get; set; }
}

/// <summary>
/// Individual search result item
/// </summary>
public class SearchResult
{
    [JsonPropertyName("score")]
    public double Score { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty; // "text", "image", "document"

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public SearchResultContent Content { get; set; } = new();
}

/// <summary>
/// Content and metadata for a search result
/// </summary>
public class SearchResultContent
{
    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("metadata")]
    public SearchResultMetadata Metadata { get; set; } = new();
}

/// <summary>
/// Metadata for a search result
/// </summary>
public class SearchResultMetadata
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("author")]
    public string? Author { get; set; }

    [JsonPropertyName("created")]
    public DateTime? Created { get; set; }

    [JsonPropertyName("modified")]
    public DateTime? Modified { get; set; }

    [JsonPropertyName("fileSize")]
    public long? FileSize { get; set; }

    [JsonPropertyName("contentType")]
    public string? ContentType { get; set; }

    [JsonPropertyName("sourceContainer")]
    public string? SourceContainer { get; set; }

    [JsonPropertyName("sourceType")]
    public string? SourceType { get; set; } // "blob" or "url"

    [JsonPropertyName("language")]
    public string? Language { get; set; }

    [JsonPropertyName("keyPhrases")]
    public string[]? KeyPhrases { get; set; }
}