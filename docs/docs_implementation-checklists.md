# Implementation Checklists (mapped 1:1 to functions and docs)

Scope and assumptions
- [x] Single-tenant
- [x] English-only (skip language detection unless content indicates otherwise)
- [x] Default to SimplifiedSearchService, keep AzureSearchService as switchable
- [x] Source of truth files in docs/: functions-reference.md, simplified-search-service.md, url-and-file-indexing.md, search-web-ui-function.md, implementation-progress.md, implementation-complete.md, overview.md

---

Function: Search (/api/Search) — functions-reference.md
- [ ] Add response field: schemaVersion (e.g., "1.1")
- [ ] Add pagination inputs: page (default 1), pageSize (default 10, max 50); return totalResults and page/pageSize
- [ ] Add sort parameter: "relevance" (default) | "lastModified desc"
- [ ] Add explain flag: explain: true to return "why this result" metadata
- [ ] Return why metadata:
  - [ ] For Keyword: matchedTerms, termFrequencies
  - [ ] For Vector: topContributingChunks with chunkIds and similarity
- [ ] Make hybrid weights configurable via request and server defaults:
  - [ ] request.weights.keyword (default 0.6), request.weights.vector (default 0.4), bonusOverlap (default 0.1)
  - [ ] Validate sum and clamp to safe bounds
- [ ] Add filters expansion:
  - [ ] filters.fileTypes (existing)
  - [ ] filters.contentType (e.g., "text/html", "application/pdf")
  - [ ] filters.sourceHost (array)
  - [ ] filters.tags (array of strings)
  - [ ] filters.dateRange.start/end (existing)
- [ ] Safety/timeouts: enforce server-side max execution time, result limits, and defensive truncation of large snippets
- [ ] Telemetry: log query, searchType, latency, resultCount, topScore, correlationId
- [ ] Acceptance criteria:
  - [ ] Backwards-compatible with previous request schema
  - [ ] Returns schemaVersion, totalResults, executionTimeMs, searchType, query, page, pageSize
  - [ ] explain=true yields why fields without significant latency regression (>10% vs baseline)

Docs updates
- [ ] functions-reference.md: Update request/response examples with schemaVersion, pagination, sort, explain, and expanded filters
- [ ] simplified-search-service.md: Document configurable weights and explain output semantics

---

Function: SearchStatus (/api/SearchStatus) — functions-reference.md
- [ ] Include index sizes: documentCount, embeddingCount, on-disk size (if available)
- [ ] Include cache metrics: cacheWarm, cacheAge, cacheHitRate
- [ ] Include latency: avgLatencyMs per searchType (Keyword, Vector, Hybrid, Semantic)
- [ ] Include ingestion: lastIngestTime, ingestLagEstimate, lastErrorCount (windowed)
- [ ] Include build info: serviceType, modelName, modelDim, indexVersion
- [ ] Acceptance criteria:
  - [ ] Single GET returns JSON with above fields
  - [ ] Endpoint remains lightweight (<100ms typical)

Docs updates
- [ ] functions-reference.md: Extend SearchStatus example payload
- [ ] implementation-progress.md: Reference new metrics in monitoring section

---

Function: AddUrlDocument (/api/AddUrlDocument) — functions-reference.md and url-and-file-indexing.md
- [ ] Make idempotent via registry (Azure Table: IngestItems)
- [ ] Normalize URL per rules (lowercase scheme/host, drop fragment, sort query params, remove trackers, canonical link usage)
- [ ] Parameters: { url, title?, force? }
- [ ] If force=true, set reindexRequested or advance reindexGeneration
- [ ] Enqueue crawl job (Azure Queue): { id, partitionKey, rowKey, generation }
- [ ] Return registry entity summary including id, status, nextCrawlAt
- [ ] Acceptance criteria:
  - [ ] Duplicate URL calls upsert same registry row
  - [ ] force=true bypasses conditional GET on next crawl

Docs updates
- [ ] functions-reference.md: Add idempotency behavior and force flag
- [ ] url-and-file-indexing.md: Cross-link normalization and queue contract

---

Function: RebuildIndex (/api/RebuildIndex) — functions-reference.md
- [ ] Support rebuild modes:
  - [ ] soft (recomputes in place)
  - [ ] blueGreen (builds vNext and atomically switches)
- [ ] Options: { force?: bool, mode?: "soft" | "blueGreen" }
- [ ] Rehydrate caches after switch; emit indexVersion
- [ ] Acceptance criteria:
  - [ ] No downtime for Search (reads from current index until switch)
  - [ ] SearchStatus shows new indexVersion after switch

Docs updates
- [ ] functions-reference.md: Describe modes and indexVersion
- [ ] implementation-complete.md: Note zero-downtime reindex capability

---

Function: AddTestDocuments (/api/AddTestDocuments) — functions-reference.md
- [ ] Seed representative corpus:
  - [ ] Varied fileTypes (html, md, pdf placeholder)
  - [ ] Mixed lengths to test chunking
  - [ ] Recent vs old lastModified dates
- [ ] Tag seeded docs (tags:["test"]) for easy cleanup and filtering
- [ ] Acceptance criteria:
  - [ ] Running twice remains idempotent
  - [ ] SearchStatus reflects new counts

Docs updates
- [ ] functions-reference.md: Document test corpus composition
- [ ] implementation-progress.md: Include test coverage notes

---

Function: ClearTestDocuments (/api/ClearTestDocuments) — functions-reference.md
- [ ] Remove only docs tagged test; update persistent storage accordingly
- [ ] Acceptance criteria:
  - [ ] Leaves non-test content untouched
  - [ ] SearchStatus counts decrease accordingly

Docs updates
- [ ] functions-reference.md: Clarify safety constraints

---

Function: TimerExample (convert to CrawlTimer) — functions-reference.md and url-and-file-indexing.md
- [ ] Rename/logically repurpose to CrawlTimer: periodic sweep (e.g., every 5–15 min)
- [ ] Query IngestItems where nextCrawlAt <= now and enqueue jobs
- [ ] Pre-warm caches: optional early-invoked warmup for SimplifiedSearchService at cold start
- [ ] Acceptance criteria:
  - [ ] No duplicate work via ETag-based locking (handled by worker)
  - [ ] Minimal cost impact; timer interval configurable via env

Docs updates
- [ ] functions-reference.md: Replace TimerExample with CrawlTimer
- [ ] url-and-file-indexing.md: Reference timer-based scheduling

---

New Function: CrawlWorker (Queue trigger) — url-and-file-indexing.md
- [ ] Trigger: Azure Queue messages from CrawlTimer/AddUrlDocument
- [ ] Try-lock entity via Azure Table ETag; abandon if conflict
- [ ] HEAD + conditional GET for URLs; respect robots.txt; follow redirects; capture canonicalUrl
- [ ] Compute contentHash/textHash; extract text; discover images (parentPageId link)
- [ ] Index page and child images; update registry fields (status, lastIndexed, imageCount, nextCrawlAt)
- [ ] Error handling: backoff, errorMessage, crawlAttempts++
- [ ] Acceptance criteria:
  - [ ] 304 Not Modified → update timestamps only
  - [ ] Changed content → page reindexed, hashes updated

Docs updates
- [ ] url-and-file-indexing.md: Finalize worker flow and sample payloads
- [ ] functions-reference.md: Add CrawlWorker (internal)

---

New Function: ReindexItem (/api/ReindexItem) — url-and-file-indexing.md and functions-reference.md
- [ ] POST { id | source, force: true } → set reindexRequested or bump generation
- [ ] Return updated entity and queued status
- [ ] Acceptance criteria:
  - [ ] Works for both URL and file items
  - [ ] Safe to call repeatedly

Docs updates
- [ ] functions-reference.md: New endpoint spec and examples
- [ ] url-and-file-indexing.md: Cross-link force mechanisms

---

New Function: SearchUI (GET/POST /api/SearchUI) — search-web-ui-function.md
- [ ] GET returns minimal HTML/JS UI (no keys in browser)
- [ ] POST proxies to server-side Search using same contract
- [ ] Controls: query, searchType, imagesOnly, maxResults, page, sort
- [ ] Rendering: title/link, searchType badge, score, snippet, thumbnail, meta (fileType, lastModified), total results, execution time
- [ ] Error/empty states and basic accessibility
- [ ] Acceptance criteria:
  - [ ] GET anonymous; POST calls server-side Search
  - [ ] "Images only" filter sets filters.fileTypes to common image types
  - [ ] Pagination works with page/pageSize

Docs updates
- [ ] search-web-ui-function.md: Expand acceptance criteria to include pagination and sort
- [ ] functions-reference.md: Add SearchUI to functions table

---

Service: SimplifiedSearchService — simplified-search-service.md
- [ ] Configurable hybrid weights with sane defaults
- [ ] Optional cross-encoder re-ranker on top-k (pluggable; feature flag)
- [ ] Chunking strategy: tune size/overlap; retain section/heading/page metadata
- [ ] Pre-warm caches on timer; document memory footprint and limits
- [ ] Small inverted index persisted to blob for fast cold loads; hydrate to memory on start
- [ ] ANN option (HNSW) roadmap for larger corpora; document trade-offs
- [ ] Acceptance criteria:
  - [ ] Recall/precision improves with re-ranker in A/B test (>5% NDCG@10)
  - [ ] Cold start reduced by cache warmup (>30% faster to first query)

Docs updates
- [ ] simplified-search-service.md: Add weights, explain, cold-start mitigations, re-ranker plug-in notes
- [ ] implementation-complete.md: Mention optional re-ranking stage

---

Service: AzureSearchService — functions-reference.md and implementation-complete.md
- [ ] Ensure parity in request filters and explain fields where feasible
- [ ] Map explain to Azure Search capabilities (e.g., highlights, scoring profiles)
- [ ] Acceptance criteria:
  - [ ] Switching SEARCH_SERVICE_TYPE preserves request/response schema

Docs updates
- [ ] implementation-complete.md: Parity notes and limitations
- [ ] functions-reference.md: Document behavior differences

---

Document: url-and-file-indexing.md (registry and ingestion)
- [ ] Finalize flattened schema names for Azure Table (e.g., httpEtag, httpLastModified, contentHash)
- [ ] Define queue names and message schema
- [ ] Add environment variables section (REGISTRY_TABLE_NAME, intervals, backoff)
- [ ] Add acceptance criteria for normalization, conditional GET, tombstones, and scheduling backoff
- [ ] Add examples for ReindexItem and CrawlTimer/CrawlWorker flows

---

Document: functions-reference.md (API reference)
- [ ] Add/Update endpoints: Search, SearchStatus, AddUrlDocument, RebuildIndex, AddTestDocuments, ClearTestDocuments, SearchUI, ReindexItem, CrawlTimer (timer), CrawlWorker (queue)
- [ ] Update JSON contracts with schemaVersion, pagination, sort, explain, filters
- [ ] Include curl examples for new/updated endpoints

---

Document: search-web-ui-function.md (UI)
- [ ] Update summary/flow to include pagination and sort
- [ ] Show code snippets for building payload with page, pageSize, sort, filters
- [ ] Acceptance criteria updated accordingly

---

Document: implementation-progress.md
- [ ] Add checklist items for: Search contract v1.1, SearchStatus metrics, ingestion registry, CrawlTimer/CrawlWorker, SearchUI
- [ ] Mark items COMPLETE as delivered; link to PRs
- [ ] Include performance deltas (pre/post re-ranker, pre/post warmup)

---

Document: implementation-complete.md
- [ ] Reflect new features as optional enhancements delivered (where applicable)
- [ ] Update cost section if new components materially impact spend (queues/tables are negligible)

---

Document: overview.md
- [ ] Update architecture diagram to include:
  - [ ] IngestItems (Azure Table)
  - [ ] CrawlTimer (Timer) → Queue → CrawlWorker (Queue trigger)
  - [ ] SearchUI (HTTP) calling Search (HTTP)
  - [ ] Blue/green index switch path for RebuildIndex
- [ ] Note Single-tenant/English-only scope

---

Observability and quality (cross-cutting)
- [ ] Add correlationId across requests and ingestion pipeline
- [ ] Log key metrics: ingestion lag, error rates, costs/tokens (if applicable), recall@k, MRR/NDCG
- [ ] Expose subset via SearchStatus; push full telemetry to Application Insights
- [ ] Golden set evaluation job (manual or scheduled) with stored judgments

Security and governance
- [ ] Keep Search GET/POST keyless as intended; require keys for write endpoints (AddUrlDocument, RebuildIndex, ReindexItem, Add/ClearTestDocuments)
- [ ] Secrets via Managed Identity + Key Vault; no secrets in normalizedSource
- [ ] Private networking where applicable (later, if needed)

Performance and cost
- [ ] Batch embeddings; rate-limit Azure OpenAI
- [ ] Constrain pageSize and maxResults to safe defaults
- [ ] Verify memory headroom for in-memory caches; document limits and sharding strategy

Testing
- [ ] Extend PowerShell test suite:
  - [ ] Contract tests for Search (schemaVersion, pagination, explain)
  - [ ] Ingestion idempotency tests (registry + force)
  - [ ] CrawlTimer/CrawlWorker integration tests with mocked HTTP (200/304/404)
  - [ ] SearchUI smoke tests (GET/POST) and rendering assertions (headless)
- [ ] Add chaos tests for queue backlogs and partial outages (retry/backoff)

Rollout plan
- [ ] Feature flags: re-ranker, explain, blue-green rebuild
- [ ] Staged rollout: dev → staging → prod
- [ ] A/B test re-ranker and weight tuning; record metrics in SearchStatus for quick inspection

---

Appendix: Minimal API deltas (for quick reference)

Search request (new optional fields)
- [ ] page: number
- [ ] pageSize: number
- [ ] sort: "relevance" | "lastModified desc"
- [ ] explain: boolean
- [ ] weights: { keyword?: number, vector?: number, bonusOverlap?: number }
- [ ] filters: { fileTypes?: string[], contentType?: string, sourceHost?: string[], tags?: string[], dateRange?: { start?: string, end?: string } }

Search response (new fields)
- [ ] schemaVersion: "1.1"
- [ ] page: number
- [ ] pageSize: number
- [ ] explain: { matchedTerms?: string[], topContributingChunks?: { chunkId: string, score: number }[] } (per result)

SearchStatus response (new fields)
- [ ] documentCount, embeddingCount, cacheWarm, cacheAge, cacheHitRate
- [ ] avgLatencyMsByType: { keyword: number, vector: number, hybrid: number, semantic: number }
- [ ] lastIngestTime, ingestLagEstimate, lastErrorCount
- [ ] serviceType, modelName, modelDim, indexVersion