# SearchUI Function (Proposed)

A lightweight browser-based front end to run searches against the existing Search function and display results with rich metadata (score, search type, summary, and thumbnails).

This function does not introduce new search logic. Instead, it provides:
- A GET endpoint that serves a minimal HTML/JS UI.
- A POST endpoint that accepts the same request contract as the existing Search function and returns the same response format, allowing the UI to call server-side without exposing keys in the client.

## Summary

- Name: SearchUI
- Trigger: HTTP
- Routes:
  - GET /api/SearchUI → returns HTML/JS UI
  - POST /api/SearchUI → executes a search (server-side) using the same pipeline as the Search function
- Purpose: Provide a simple, user-friendly UI to run Keyword, Vector, Hybrid, or Semantic searches; toggle “Images only”; and visualize results (score, type, summary, thumbnail).

## Typical User Flow

1. User opens /api/SearchUI in a browser.
2. Enters a query, picks a search type (Keyword/Vector/Hybrid/Semantic), toggles “Images only,” and sets max results.
3. UI sends POST to /api/SearchUI with the same JSON contract used by /api/Search.
4. UI renders a list of results with:
   - Title + link to source
   - Search type
   - Score
   - Summary/snippet
   - Thumbnail (if available)
   - Metadata (e.g., file type, last modified)
   - Execution time and total results at the top

## Security and Auth

- GET /api/SearchUI: AuthorizationLevel.Anonymous (to serve the HTML)
- POST /api/SearchUI: AuthorizationLevel.Function (default), but the HTML form calls this endpoint from the same origin without embedding keys. The function executes search server-side via the same service pipeline used by Search.

If you prefer a single anonymous experience (for demos), you can set POST /api/SearchUI to AuthorizationLevel.Anonymous as well. For production, keep POST protected and consider adding an App Service Authentication provider or a simple auth gate.

## Request (POST /api/SearchUI)

Same as the Search function’s request format. Example:

```json
{
  "query": "azure functions deployment best practices",
  "searchType": "Hybrid",
  "maxResults": 10,
  "filters": {
    "fileTypes": ["pdf", "docx"]
  }
}
```

### Images-only Option

When the “Images only” checkbox is selected in the UI, the POST payload should include a filter that narrows results to images. Two common approaches:

- File type filter:
  ```json
  {
    "filters": {
      "fileTypes": ["jpg", "jpeg", "png", "gif", "webp"]
    }
  }
  ```

- Or, if your index includes a contentType field:
  ```json
  {
    "filters": {
      "contentTypePrefixes": ["image/"]
    }
  }
  ```

Pick the approach that aligns with your existing index schema.

## Response

Identical to the Search function’s response. Example:

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
          "lastModified": "2024-08-30T12:00:00Z",
          "thumbnailUrl": "https://example.com/thumbs/doc-123.png",
          "fileType": "pdf"
        }
      },
      "score": 0.8547,
      "keywordScore": 0.75,
      "vectorScore": 0.892
    }
  ],
  "totalResults": 25,
  "executionTimeMs": 264,
  "searchType": "Hybrid",
  "query": "azure functions deployment best practices"
}
```

The UI will use:
- searchType and executionTimeMs for summary status
- content.metadata.title and .source for title/link
- score (and/or keywordScore/vectorScore) for display badge
- content.text for the snippet/summary
- content.metadata.thumbnailUrl (if present) for thumbnails

## Minimal UI Spec

Controls:
- Text input: query
- Select: searchType (Keyword | Vector | Hybrid | Semantic)
- Checkbox: imagesOnly
- Number: maxResults (1–50, default 10)
- Submit button: Search

Results list:
- For each result:
  - Title (link to metadata.source)
  - Badge with search type
  - Score (rounded)
  - Optional thumbnail (metadata.thumbnailUrl)
  - Snippet/summary from content.text
  - Optional metadata (fileType, lastModified)

Header summary:
- “X results in Y ms”
- Query echoed back

## Example HTML (served by GET /api/SearchUI)

This is a minimal example. In production, you may want better styling and input validation.

```html
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <title>RagSearch UI</title>
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <style>
    body { font-family: system-ui, Arial, sans-serif; margin: 1.5rem; }
    .row { display: flex; gap: 0.75rem; flex-wrap: wrap; align-items: center; }
    .results { margin-top: 1rem; }
    .card { border: 1px solid #ddd; border-radius: 8px; padding: 0.75rem; margin-bottom: 0.75rem; display: grid; grid-template-columns: 96px 1fr; gap: 0.75rem; }
    .thumb { width: 96px; height: 96px; object-fit: cover; background: #f6f6f6; border: 1px solid #eee; border-radius: 4px; }
    .meta { color: #666; font-size: 0.9rem; }
    .badge { display: inline-block; padding: 0.2rem 0.5rem; border-radius: 999px; background: #eef; color: #225; font-size: 0.8rem; margin-left: 0.5rem; }
    .score { color: #444; font-size: 0.85rem; margin-left: 0.5rem; }
    .status { margin-top: 0.5rem; color: #333; }
    .error { color: #a00; margin-top: 0.5rem; }
    label { font-size: 0.9rem; }
    input[type="text"] { width: min(680px, 90vw); }
  </style>
</head>
<body>
  <h1>RagSearch UI</h1>
  <form id="searchForm">
    <div class="row">
      <label>Query
        <input type="text" id="query" placeholder="Search…" required />
      </label>
      <label>Type
        <select id="searchType">
          <option>Keyword</option>
          <option>Vector</option>
          <option selected>Hybrid</option>
          <option>Semantic</option>
        </select>
      </label>
      <label>
        <input type="checkbox" id="imagesOnly" />
        Images only
      </label>
      <label>Max results
        <input type="number" id="maxResults" min="1" max="50" value="10" />
      </label>
      <button type="submit">Search</button>
    </div>
  </form>

  <div class="status" id="status"></div>
  <div class="error" id="error"></div>

  <div class="results" id="results"></div>

  <script>
    const form = document.getElementById('searchForm');
    const statusEl = document.getElementById('status');
    const errorEl = document.getElementById('error');
    const resultsEl = document.getElementById('results');

    function buildPayload() {
      const query = document.getElementById('query').value.trim();
      const searchType = document.getElementById('searchType').value;
      const imagesOnly = document.getElementById('imagesOnly').checked;
      const maxResults = Math.min(50, Math.max(1, Number(document.getElementById('maxResults').value || 10)));

      const payload = { query, searchType, maxResults };

      if (imagesOnly) {
        payload.filters = {
          fileTypes: ["jpg", "jpeg", "png", "gif", "webp"]
        };
      }
      return payload;
    }

    function escapeHtml(s) {
      return s.replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
    }

    function resultCard(r, searchType) {
      const title = r?.content?.metadata?.title || r?.id || 'Untitled';
      const href = r?.content?.metadata?.source || '#';
      const score = (r?.score ?? 0).toFixed(3);
      const snippet = r?.content?.text ? r.content.text.slice(0, 280) : '';
      const thumb = r?.content?.metadata?.thumbnailUrl || '';
      const fileType = r?.content?.metadata?.fileType || '';
      const lastModified = r?.content?.metadata?.lastModified || '';

      return `
        <div class="card">
          <div>${thumb ? `<img class="thumb" src="${thumb}" alt="thumbnail" />` : `<div class="thumb"></div>`}</div>
          <div>
            <div>
              <a href="${href}" target="_blank" rel="noopener">${escapeHtml(title)}</a>
              <span class="badge">${escapeHtml(searchType)}</span>
              <span class="score">score: ${score}</span>
            </div>
            <div class="meta">${escapeHtml([fileType, lastModified].filter(Boolean).join(" • "))}</div>
            <div>${escapeHtml(snippet)}</div>
          </div>
        </div>
      `;
    }

    form.addEventListener('submit', async (e) => {
      e.preventDefault();
      errorEl.textContent = '';
      resultsEl.innerHTML = '';
      statusEl.textContent = 'Searching…';

      const payload = buildPayload();

      try {
        const res = await fetch('/api/SearchUI', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(payload)
        });
        if (!res.ok) throw new Error(`HTTP ${res.status}`);
        const data = await res.json();

        statusEl.textContent = `${data.totalResults ?? 0} results in ${data.executionTimeMs ?? '-'} ms for “${payload.query}”`;
        const cards = (data.results || []).map(r => resultCard(r, data.searchType || payload.searchType)).join('');
        resultsEl.innerHTML = cards || '<div>No results.</div>';
      } catch (err) {
        console.error(err);
        errorEl.textContent = 'Search failed. Please try again.';
        statusEl.textContent = '';
      }
    });
  </script>
</body>
</html>
```

## Function Signature (C# outline)

Single function handling both GET (UI) and POST (server-side search):

```csharp
[Function("SearchUI")]
public async Task<HttpResponseData> Run(
    [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequestData req)
{
    if (req.Method.Equals("GET", StringComparison.OrdinalIgnoreCase))
    {
        // return HTML (string or embedded resource)
    }
    else
    {
        // parse JSON body (same contract as Search)
        // invoke the same search pipeline/service used by the Search function
        // return JSON response identical to /api/Search
    }
}
```

Notes:
- If you prefer to keep POST protected, change AuthorizationLevel to Function and use your preferred auth mechanism on the client or restrict access to trusted networks.
- Reuse the existing SimplifiedSearchService and/or Azure AI Search client to avoid duplicating logic.

## Acceptance Criteria

- UI loads at GET /api/SearchUI without requiring a key.
- POST /api/SearchUI accepts the same request contract as /api/Search and returns the same response schema.
- UI controls:
  - Query input
  - Search type select (Keyword, Vector, Hybrid, Semantic)
  - “Images only” checkbox
  - Max results control
- UI renders:
  - Total results and execution time
  - Each result with title (link), search type badge, score, summary/snippet, and thumbnail when available
- “Images only” narrows results to image content via filters consistent with the index schema.

## Integration Notes

- Link this function from docs/functions-reference.md under the functions table:
  - SearchUI | HTTP | /api/SearchUI | Browser-based UI for querying indexes and viewing results
- No changes required to the Search function contract; this UI simply consumes it server-side.
