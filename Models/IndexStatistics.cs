namespace RagSearch.Models;

/// <summary>
/// Simple index statistics that doesn't depend on Azure Search types
/// </summary>
public class IndexStatistics
{
    public long DocumentCount { get; set; }
    public long StorageSize { get; set; }
    public DateTime LastUpdated { get; set; }
    public string IndexType { get; set; } = string.Empty;
}