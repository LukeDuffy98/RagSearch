# URL and File Indexing Strategy

Purpose:
- Avoid unnecessary re-indexing of URLs and files.
- Provide a durable registry of all known sources with change detection.
- Assign stable IDs to relate pages, images, and derived content.
- Allow a switch to force a reindex.
- Fit cleanly into RagSearch’s existing functions and search schema.

This document defines normalization, storage (Azure Table Storage), entity schema, and the indexing workflow.

---

## 1) Goals and Principles

- Stable Identity: Every URL or file path maps to a stable ID used across:
  - Parent page documents
  - Child images (via parentPageId)
  - Any other derived artifacts (chunks, thumbnails)
- Change-Aware: Re-crawl only when content likely changed using:
  - HTTP ETag, Last-Modified, Content-Length
  - Blob/File ETag and properties
  - Content hashes (text and/or bytes)
- Idempotent: Multiple registrations of the same input should upsert the same registry record.
- Observable: Track status, attempts, and last success/error.
- Overridable: A forceReindex switch to bypass change checks.

---

## 2) Storage: Azure Table Storage (recommended)

Create a table: IngestItems

Partitioning:
- For URLs: PartitionKey = host (e.g., "contoso.com")
- For files: PartitionKey = scheme (e.g., "file", "blob", "share") or a storage account/container name

RowKey:
- A short, collision-resistant ID derived from the normalized source key (see normalization).
  - Example: base32_sha256(normalizedSource) e.g., "k7gxn3…"

Why Azure Table?
- Ultra-cheap, highly available, easy concurrency control via ETag.
- Native fit for key-value + metadata without heavy indexing needs.

---

## 3) Normalization and ID Rules

Normalization is critical to avoid duplicates.

For URLs (normalizedSource):
- Lowercase scheme and host.
- Remove fragment (#…).
- Sort query parameters alphabetically; drop known trackers (utm_*, gclid, fbclid, msclkid).
- Remove trailing slash when not the root ("/path/" → "/path").
- Resolve relative URLs to absolute before storing.
- Prefer canonical link if available: <link rel="canonical" href="...">.
- Decode punycode to Unicode for display, but store ASCII form for ID consistency.

For file paths (normalizedSource):
- Use an absolute, canonical path or URI:
  - Windows: drive-normalized, case-normalized, forward slashes
  - Blob: "https://{account}.blob.core.windows.net/{container}/{path}?sv=…" → store canonical URI without SAS (persist container/path; do not store secrets)
- Remove trailing slashes, normalize repeated slashes.

Stable ID:
- id = $"{type}_{shortHash(normalizedSource)}"
  - type ∈ {"url","file"}
  - Example: "url_k7gxn3…" or "file_3p4s8…"
- Also store parentPageId for child objects (e.g., images) using this id.

---

## 4) Entity Schema (Azure Table)

Table: IngestItems

- PartitionKey: string
- RowKey: string
- ETag: system-managed (for optimistic concurrency)
- id: string (same as "{type}_{hash}")
- type: "url" | "file"
- normalizedSource: string (URL or canonical file URI)
- displaySource: string (pretty form for UI)
- sourceHost: string (for URLs; same as PartitionKey)
- parentPageId: optional (used only if this item itself is a child; generally empty here)
- status: "Pending" | "Crawled" | "Indexed" | "Error" | "OutOfScope" | "Gone"
- reindexRequested: bool
- reindexGeneration: int (monotonic counter; increment to force work)
- http:
  - etag: string (ETag header from last successful GET)
  - lastModified: string ISO (HTTP Last-Modified)
  - contentLength: long
  - statusCode: int
  - redirectChain: string (JSON array)
  - robotsAllowed: bool
  - canonicalUrl: string (if discovered)
- file:
  - etag: string (blob/file ETag)
  - lastModified: string ISO
  - length: long
  - contentType: string
- content:
  - contentHash: string (sha256 of normalized text/bytes used for index)
  - textHash: string (sha256 of extracted text, if applicable)
  - language: string (iso)
  - mimeType: string
- linkage:
  - lastIndexedDocId: string (ID of document in the search index)
  - imageCount: int (number of child images indexed)
- scheduling:
  - firstSeen: string ISO
  - lastChecked: string ISO
  - lastChanged: string ISO
  - lastIndexed: string ISO
  - crawlAttempts: int
  - errorMessage: string
  - nextCrawlAt: string ISO
  - crawlIntervalSeconds: int (dynamic backoff)

Notes:
- Nested properties can be flattened for Azure Table (httpEtag, httpLastModified, etc.) if you prefer a single-level schema.

Example (flattened):

```json
{
  "PartitionKey": "contoso.com",
  "RowKey": "k7gxn3",
  "id": "url_k7gxn3",
  "type": "url",
  "normalizedSource": "https://contoso.com/blog/post-1",
  "displaySource": "https://contoso.com/blog/post-1",
  "status": "Indexed",
  "reindexRequested": false,
  "reindexGeneration": 3,
  "httpEtag": "\"W/\\\"a1b2c3\\\"\"",
  "httpLastModified": "2025-08-20T12:00:00Z",
  "httpContentLength": 45231,
  "httpStatusCode": 200,
  "httpRobotsAllowed": true,
  "httpCanonicalUrl": "https://contoso.com/blog/post-1",
  "contentHash": "sha256:8e2f…",
  "textHash": "sha256:9f1a…",
  "mimeType": "text/html",
  "language": "en",
  "lastChecked": "2025-08-30T21:00:00Z",
  "lastChanged": "2025-08-20T12:00:00Z",
  "lastIndexed": "2025-08-30T21:01:12Z",
  "crawlAttempts": 5,
  "imageCount": 3,
  "lastIndexedDocId": "page_0b1a…9c",
  "nextCrawlAt": "2025-09-02T06:00:00Z",
  "crawlIntervalSeconds": 259200
}
```

---

## 5) Change Detection and Skip Logic

URLs:
- Prefer HEAD to get ETag/Last-Modified/Content-Length.
- Use conditional GET (If-None-Match, If-Modified-Since).
- If 304 Not Modified → skip download and indexing; update lastChecked and nextCrawlAt.
- If 200 OK and ETag/Last-Modified/length changed → re-download and reprocess.
- If response redirects, follow; update canonicalUrl and redirectChain.
- Respect robots.txt; set httpRobotsAllowed=false and mark OutOfScope if disallowed.

Files (local/Blob):
- Use SDK-provided properties (ETag, LastModified, ContentLength).
- If ETag unchanged → skip.
- For large binaries, compute streaming hash only when metadata unavailable or has changed.

Content-level checks:
- After fetch, compute contentHash (bytes or normalized text) and textHash (if applicable).
- If hashes unchanged → skip full re-index; update timestamps.

Backoff/scheduling:
- If unchanged for N consecutive checks, increase crawlIntervalSeconds (e.g., 1 day → 3 → 7 → 14).
- On change/event, reset interval to baseline.

Tombstones:
- If 404/410 consistently across M checks, set status="Gone" and optionally remove index doc.

---

## 6) Force Reindex

Two mechanisms:
- reindexRequested: bool (single-use; crawler clears it after processing)
- reindexGeneration: int (crawler reprocesses if generation > last processed generation)

API touchpoints:
- Add optional force=true to:
  - AddUrlDocument
  - RebuildIndex
  - New endpoint: POST /api/ReindexItem { id | source, force: true }
Effect:
- Set reindexRequested=true or increment reindexGeneration.

Crawler behavior:
- If reindexRequested or generation advanced → bypass conditional checks and re-fetch.

---

## 7) Pipeline Overview

1) Register
- Upsert into IngestItems with normalizedSource and computed id.
- Initialize status="Pending", firstSeen, nextCrawlAt=now.

2) Crawl/Fetch
- Respect robots.txt for URLs.
- Use HEAD + conditional GET.
- On success, capture headers and content bytes.

3) Extract
- If text/html:
  - Extract text, canonicalUrl, language.
  - Discover images per docs/image-handling.md.
  - Compute hashes (content/text).
- If application/pdf/docx/etc.:
  - Extract text and compute hashes.

4) Index
- Upsert page doc in search index; capture lastIndexedDocId.
- For images, call image pipeline; use parentPageId = id of this registry item.

5) Update Registry
- status="Indexed" (or "Crawled" if deferred)
- lastChanged (from ETag/Last-Modified or hash change)
- lastIndexed, imageCount, errorMessage=null
- Set nextCrawlAt based on strategy.

6) Errors
- Increment crawlAttempts.
- status="Error".
- Set nextCrawlAt with exponential backoff (and Retry-After if provided).
- Preserve errorMessage (truncated) for observability.

---

## 8) Linking Pages and Images

- Page registry item id → parentPageId used by all child images.
- Images include:
  - parentPageId
  - parentPageUrl
  - source (origin page URL)
- Search API can group images under their parent using parentPageId as a join key (nestImages=true).

---

## 9) Function Integrations

Existing:
- AddUrlDocument: extend to:
  - Normalize URL, upsert IngestItems, optionally set force.
  - Enqueue a crawl job (Azure Queue Storage).
- RebuildIndex: can recompute registry and re-queue.
- Search: add nestImages=true to attach child images.

New (optional):
- ReindexItem (HTTP): force reindex by id or source.
- CrawlTimer (Timer): periodic sweep to enqueue due items where nextCrawlAt <= now.
- CrawlWorker (Queue trigger): processes crawl jobs with concurrency control via Table ETag.

Concurrency:
- Use Azure Table ETag on the IngestItems entity:
  - Read entity, check eligibility, set a transient lock field (e.g., lockedUntil) and update via ETag.
  - If ETag mismatch, another worker won; abandon.

---

## 10) Additional Suggestions

- Canonical groups: Maintain contentGroupId = shortHash(host + canonical path without query) to coalesce http/https and trivial query variants.
- Snapshot storage: Optionally store raw HTML/PDF in Blob with a retention policy for reproducibility.
- Language detection: Persist language once detected; skip re-detect unless contentHash changed.
- Access control: For private sources, don’t persist secrets (e.g., SAS tokens) in normalizedSource; store separately in Key Vault or app settings.
- Metrics: Count of indexed items, change rate, error rate; surface via SearchStatus.
- Purge policy: Configurable TTL for Gone items; optionally delete index docs after grace period.

---

## 11) Environment Variables

- REGISTRY_TABLE_NAME = IngestItems
- REGISTRY_PARTITION_STRATEGY = host|scheme
- CRAWL_BASE_INTERVAL_SECONDS = 86400
- CRAWL_MAX_INTERVAL_SECONDS = 1209600
- CRAWL_ERROR_BACKOFF_BASE = 600
- TRACKING_PARAM_PREFIXES = utm_, gclid, fbclid, msclkid
- FORCE_REINDEX_ON_CANONICAL_CHANGE = true

---

## 12) Pseudocode (C# Outline)

```csharp
public async Task<IngestItem> RegisterAsync(string source, string type, bool force = false)
{
    var normalized = NormalizeSource(source, type); // applies URL/file rules
    var id = $"{type}_{ShortHash(normalized)}";
    var (pk, rk) = KeysFromSource(normalized, type);

    var entity = await table.GetOrDefaultAsync(pk, rk);
    if (entity == null)
    {
        entity = NewEntity(pk, rk, id, type, normalized);
    }

    entity.displaySource = PrettySource(source);
    entity.status = entity.status is "Indexed" or "Crawled" ? entity.status : "Pending";
    entity.firstSeen ??= Now();
    entity.nextCrawlAt = Now();
    if (force)
    {
        entity.reindexRequested = true;
        entity.reindexGeneration = (entity.reindexGeneration ?? 0) + 1;
    }

    await table.UpsertAsync(entity, mode: MergeOrReplace);
    await queue.EnqueueAsync(new CrawlJob { Id = id, PartitionKey = pk, RowKey = rk, Generation = entity.reindexGeneration });
    return entity;
}

public async Task ProcessCrawlAsync(CrawlJob job)
{
    var entity = await table.RetrieveAsync(job.PartitionKey, job.RowKey);
    if (!TryLock(entity)) return; // ETag-based lock

    try
    {
        if (!ShouldProcess(entity)) return;

        var fetch = await FetchAsync(entity); // Uses HEAD/conditional GET or blob properties
        if (fetch.NotModified && !entity.reindexRequested)
        {
            entity.lastChecked = Now();
            entity.nextCrawlAt = ScheduleNext(entity, unchanged: true);
            await table.ReplaceAsync(entity);
            return;
        }

        var extract = await ExtractAsync(fetch);
        var changed = extract.Hash != entity.contentHash;

        if (changed || entity.reindexRequested)
        {
            var docId = await IndexAsync(extract, entity.id);
            await IndexImagesAsync(extract, entity.id); // uses parentPageId = entity.id

            entity.lastIndexedDocId = docId;
            entity.imageCount = extract.ImageCount;
            entity.contentHash = extract.Hash;
            entity.textHash = extract.TextHash;
            entity.status = "Indexed";
            entity.lastIndexed = Now();
        }

        entity.reindexRequested = false;
        entity.lastChecked = Now();
        entity.lastChanged = changed ? Now() : entity.lastChanged;
        entity.nextCrawlAt = ScheduleNext(entity, unchanged: !changed);
        await table.ReplaceAsync(entity);
    }
    catch (Exception ex)
    {
        entity.status = "Error";
        entity.errorMessage = Truncate(ex.Message, 1024);
        entity.crawlAttempts = (entity.crawlAttempts ?? 0) + 1;
        entity.nextCrawlAt = Backoff(entity);
        await table.ReplaceAsync(entity);
    }
}
```

---

## 13) Acceptance Criteria

- Registry created (Azure Table IngestItems) with partition and row key strategy.
- Stable IDs assigned for all sources; used as parentPageId for images.
- Conditional fetch used (ETag/Last-Modified/Content-Length or blob ETag) to skip unchanged items.
- Force reindex available via flag or generation bump and exposed via an HTTP endpoint.
- Redirects and canonical URLs handled; tracking parameters normalized.
- Errors are recorded with backoff; gone pages are tombstoned.
- Images can be nested under their parent page in search results through parentPageId linkage.

---