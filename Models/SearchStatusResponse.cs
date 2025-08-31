using System.Text.Json.Serialization;

namespace RagSearch.Models;

public class SearchStatusResponse
{
    [JsonPropertyName("indexName")] public string IndexName { get; set; } = string.Empty;
    [JsonPropertyName("documentCount")] public long DocumentCount { get; set; }
    [JsonPropertyName("storageSize")] public long StorageSize { get; set; }
    [JsonPropertyName("status")] public string Status { get; set; } = string.Empty;
    [JsonPropertyName("lastUpdated")] public DateTime LastUpdated { get; set; }
    [JsonPropertyName("persistentStorage")] public bool PersistentStorage { get; set; }
    [JsonPropertyName("features")] public SearchStatusFeatures Features { get; set; } = new();
}

public class SearchStatusFeatures
{
    [JsonPropertyName("keywordSearch")] public bool KeywordSearch { get; set; }
    [JsonPropertyName("vectorSearch")] public bool VectorSearch { get; set; }
    [JsonPropertyName("hybridSearch")] public bool HybridSearch { get; set; }
    [JsonPropertyName("semanticSearch")] public bool SemanticSearch { get; set; }
}
