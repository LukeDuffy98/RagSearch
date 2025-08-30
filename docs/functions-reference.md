# RagSearch Functions Reference Guide

This document provides detailed information about each function in the RagSearch Azure Functions project, including their triggers, bindings, configuration, and usage examples.

## 📋 Functions Overview

| Function | Trigger Type | Schedule/Route | Purpose |
|----------|--------------|----------------|---------|   
| `Search` | HTTP | `/api/Search` | Semantic search with keyword, vector, and hybrid modes |
| `SearchStatus` | HTTP | `/api/SearchStatus` | Get index statistics and health information |
| `AddTestDocuments` | HTTP | `/api/AddTestDocuments` | Add sample documents for testing |
| `ClearTestDocuments` | HTTP | `/api/ClearTestDocuments` | Remove test documents from index |
| `RebuildIndex` | HTTP | `/api/RebuildIndex` | Rebuild search index from storage |
| `AddUrlDocument` | HTTP | `/api/AddUrlDocument` | Ingest content from web URLs |
| `HttpExample` | HTTP | `/api/HttpExample` | Example HTTP function for reference |
| `TimerExample` | Timer | Every 5 minutes | Example scheduled background processing |

---

## 🔍 Search Function

### Overview
The `Search` function is the core search API that provides keyword, vector, and hybrid search capabilities using either SimplifiedSearchService (with Azure OpenAI embeddings) or Azure AI Search service.

### Function Signature
```csharp
[Function("Search")]
public async Task<HttpResponseData> Run(
    [HttpTrigger(AuthorizationLevel.Function, "get", "post")] HttpRequestData req)
```

### Search Types Supported

| Search Type | Description | Use Case |
|-------------|-------------|----------|
| **Keyword** | Traditional text matching | Exact term searches, names, IDs |
| **Vector** | Semantic similarity using embeddings | Concept-based search, natural language |
| **Hybrid** | Combines keyword + vector (60/40 weighted) | Best of both approaches |
| **Semantic** | Enhanced vector search with boost | Understanding intent and context |

### Request Format

#### POST Request Body
```json
{
  "query": "azure functions deployment best practices",
  "searchType": "Hybrid",
  "maxResults": 10,
  "filters": {
    "fileTypes": ["pdf", "docx"],
    "dateRange": {
      "start": "2024-01-01",
      "end": "2024-12-31"
    }
  }
}
```

#### GET Request (Simple)
```http
GET /api/Search?query=azure%20functions&searchType=Vector&maxResults=5
```

### Response Format
```json
{
  "results": [
    {
      "id": "doc-123",
      "content": {
        "text": "Azure Functions is a serverless compute service...",
        "metadata": {
          "title": "Azure Functions Overview",
          "source": "https://docs.microsoft.com/azure-functions",
          "lastModified": "2024-08-30T12:00:00Z"
        }
      },
      "score": 0.8547,
      "keywordScore": 0.750,
      "vectorScore": 0.892
    }
  ],
  "totalResults": 25,
  "executionTimeMs": 264,
  "searchType": "Hybrid",
  "query": "azure functions deployment best practices"
}
```

### Usage Examples

#### Vector Search for Concepts
```bash
curl -X POST http://localhost:7071/api/Search \
  -H "Content-Type: application/json" \
  -d '{
    "query": "How do I improve my conversations with AI assistants",
    "searchType": "Vector",
    "maxResults": 3
  }'
```