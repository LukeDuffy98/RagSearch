# Search Architecture Overview

## Current Approach
- Uses in-memory cache and full scan for keyword search (SimplifiedSearchService).
- Keyword search matches query words against title, summary, and content of each document.
- Vector and hybrid search use Azure OpenAI embeddings for semantic similarity.
- Suitable for small to medium datasets (hundreds to a few thousand documents).

## Performance
- Fast for small datasets.
- As the index grows, keyword search may slow down due to full scan and string matching.

## Long-term Scaling Options
- **Inverted Index:**
  - Build a mapping from terms to document IDs for fast lookup.
  - Reduces the need to scan all documents for each query.
  - Can be implemented in-memory or persisted to disk/blob.
- **Azure AI Search:**
  - Offloads indexing and search to a managed cloud service.
  - Supports full-text, vector, and hybrid search at scale.
  - Handles paging, filtering, and relevance ranking efficiently.
  - Recommended for large datasets (10,000+ documents) or production workloads.

## Recommendation
- For small projects, current approach is sufficient.
- For larger datasets or production, migrate to Azure AI Search or implement an inverted index for keyword search.

---
This file provides a high-level overview and future direction for search scalability in RagSearch.
