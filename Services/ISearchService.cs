using RagSearch.Models;

namespace RagSearch.Services;

/// <summary>
/// Interface for search service implementations
/// Supports both simplified (blob storage) and enterprise (Azure AI Search) implementations
/// </summary>
public interface ISearchService
{
    /// <summary>
    /// Ensures the search index exists and is ready for use
    /// </summary>
    /// <returns>True if index exists or was created successfully</returns>
    Task<bool> EnsureIndexExistsAsync();

    /// <summary>
    /// Indexes a single document with vector embeddings
    /// </summary>
    /// <param name="document">Document to index</param>
    /// <returns>Document ID that was indexed</returns>
    Task<string> IndexDocumentAsync(SearchDocument document);

    /// <summary>
    /// Indexes multiple documents in batch with embeddings
    /// </summary>
    /// <param name="documents">Documents to index</param>
    /// <returns>Array of document IDs that were successfully indexed</returns>
    Task<string[]> IndexDocumentsAsync(SearchDocument[] documents);

    /// <summary>
    /// Performs search with support for keyword, vector, and hybrid search
    /// </summary>
    /// <param name="request">Search request with query and options</param>
    /// <returns>Search response with results and metadata</returns>
    Task<SearchResponse> SearchAsync(SearchRequest request);

    /// <summary>
    /// Deletes a document from the search index
    /// </summary>
    /// <param name="documentId">ID of document to delete</param>
    /// <returns>True if document was deleted successfully</returns>
    Task<bool> DeleteDocumentAsync(string documentId);

    /// <summary>
    /// Gets statistics about the search index
    /// </summary>
    /// <returns>Index statistics including document count and storage size</returns>
    Task<IndexStatistics> GetIndexStatisticsAsync();

    /// <summary>
    /// Rebuilds the entire search index (deletes all existing data)
    /// </summary>
    /// <returns>True if index was rebuilt successfully</returns>
    Task<bool> RebuildIndexAsync();
}