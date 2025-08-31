# Image Handling and Ingestion Guidelines

Purpose:
- Prevent indexing of tiny/utility images (icons, favicons, logos, sprites).
- Standardize stored/served image formats to PNG or JPEG.
- Preserve provenance by recording the source web page of each image, enabling images to be nested/grouped under the parent page in search results.

This document defines thresholds, metadata schema, and an ingestion/normalization pipeline to meet the above goals.

---

## 1) Filtering Rules (exclude small/utility images)

Default thresholds (configurable via environment variables):

- Minimum dimensions:
  - MIN_IMAGE_WIDTH = 128 px
  - MIN_IMAGE_HEIGHT = 128 px
- Minimum area:
  - MIN_IMAGE_AREA = 16384 px² (128 × 128)
- Minimum file size:
  - MIN_IMAGE_BYTES = 5_000 bytes
- Aspect ratio outliers (likely banners/sprites):
  - MAX_ASPECT_RATIO = 6.0 (exclude if width/height > 6.0 or height/width > 6.0)
- Filename/url patterns to exclude (case-insensitive substring match):
  - ICON/LOGO/SPRITE/FAVICON, e.g.:
    - favicon, apple-touch-icon, mask-icon, icon-, -icon, logo, brandmark, sprite, -sprite, social-*, og-image if too small
- MIME types:
  - Accept: image/jpeg, image/png, image/webp, image/gif (only first frame), image/bmp, image/tiff
  - Exclude: image/svg+xml (vector; often icons/logos); consider converting only if explicitly enabled and meets size rules

If any of the following is true, skip indexing:
- width < MIN_IMAGE_WIDTH OR height < MIN_IMAGE_HEIGHT
- (width × height) < MIN_IMAGE_AREA
- content-length < MIN_IMAGE_BYTES (if available)
- extreme aspect ratio (ratio > MAX_ASPECT_RATIO)
- filename/url includes excluded terms above

Tip: Treat Open Graph or Twitter Card images specially. Many pages use og:image for meaningful previews; still apply thresholds but avoid filename heuristics for og-image if it passes size checks.

---

## 2) Standardization Policy (output formats and sizing)

Target formats:
- Prefer JPEG for photographic images
- Prefer PNG when:
  - Transparency (alpha) is present
  - Line art, UI, charts, or screenshots with sharp edges/text (optional heuristic)
- Fallback: If original is already JPEG/PNG and meets quality thresholds, keep as-is

Normalization steps:
- Read original; detect transparency
- Resize to cap the longest edge to MAX_IMAGE_EDGE (default 1920 px) while preserving aspect ratio (only downscale; do not upscale)
- Strip metadata (EXIF/XMP) except essential attributes (orientation)
- Convert:
  - If has alpha → PNG (24/32-bit)
  - Else → JPEG (quality 85, progressive)
- Generate thumbnail:
  - THUMB_MAX_EDGE = 320 px (JPEG or PNG matching main format; JPEG recommended to keep size low)
- Compute hashes:
  - SHA256 of normalized bytes (contentHash)
  - Perceptual hash (pHash) for de-duplication

Recommended default settings (configurable):
- MAX_IMAGE_EDGE = 1920
- JPEG_QUALITY = 85
- CREATE_THUMBNAIL = true
- THUMB_MAX_EDGE = 320
- STRIP_EXIF = true

---

## 3) Provenance and Metadata Schema

Each indexed image document should include:

- id: string (stable; e.g., hash of originImageUrl or contentHash)
- type: "image"
- content:
  - text: Optional generated/nearby caption or alt text (used for text/vector search)
  - metadata:
    - title: Optional human title or derived from caption
    - source: originPageUrl (the parent web page URL)
    - parentPageUrl: same as source (explicit alias for clarity)
    - parentPageId: stable ID for the parent page (e.g., hash of parentPageUrl)
    - imageUrl: originImageUrl (original image src URL)
    - originalFormat: e.g., "webp", "png", "jpg"
    - standardizedFormat: "png" or "jpg"
    - width: normalized width (px)
    - height: normalized height (px)
    - originalWidth: original width (px) if known
    - originalHeight: original height (px) if known
    - sizeBytes: normalized bytes length
    - thumbUrl or thumbStoragePath: if thumbnails are stored
    - contentHash: SHA256 of normalized image
    - pHash: perceptual hash (string/hex)
    - altText: from <img alt="...">
    - caption: from <figure><figcaption> or nearby text
    - position: optional DOM xPath/CSS selector or index on page
    - lastCrawled: ISO timestamp

Example document:

```json
{
  "id": "img_7f2e...d1",
  "type": "image",
  "content": {
    "text": "Diagram of the ingestion pipeline for Azure Functions",
    "metadata": {
      "title": "Ingestion Pipeline Diagram",
      "source": "https://contoso.com/blog/azure-functions-ingestion",
      "parentPageUrl": "https://contoso.com/blog/azure-functions-ingestion",
      "parentPageId": "page_0b1a...9c",
      "imageUrl": "https://contoso.com/assets/img/pipeline-diagram.png",
      "originalFormat": "png",
      "standardizedFormat": "jpg",
      "width": 1600,
      "height": 900,
      "originalWidth": 2400,
      "originalHeight": 1350,
      "sizeBytes": 142380,
      "thumbUrl": "https://cdn.contoso.net/thumbs/img_7f2e...d1.jpg",
      "contentHash": "sha256:8e2f...4b",
      "pHash": "a1b2c3d4e5...",
      "altText": "Ingestion pipeline diagram",
      "caption": "High-level pipeline for data ingestion",
      "position": "domPath:/html/body/main/article/figure[1]/img",
      "lastCrawled": "2025-08-30T22:40:00Z"
    }
  },
  "score": 0.0
}
```

---

## 4) Nesting Images Under Parent Pages in Search Results

- Ensure image docs store parentPageId and parentPageUrl.
- Ensure parent page docs store their own id equal to parentPageId (or have a mapping field).
- To nest images when returning a page result:
  - Option A: Post-process results: for each parent page hit, query up to N images using parentPageId and attach as children.
  - Option B: Pre-join: index a lightweight list of child image references on the parent page doc, updated during ingestion.
- Include the parent page URL and id in the image metadata so the Search API can group them at response time if requested (e.g., query param nestImages=true).

---

## 5) Web Page Extraction Rules

When crawling a web page:
- Collect images from:
  - <img src>, srcset (pick largest reasonable candidate), <picture><source>
  - Open Graph/Twitter card metadata (og:image, twitter:image) if present
- Resolve relative URLs to absolute
- Capture:
  - alt attribute
  - nearby caption (<figure><figcaption>) or nearest <p> preceding/succeeding
  - DOM position for optional ordering
- Skip images failing filtering rules (Section 1)
- Download once; use content-length to early-skip tiny files
- Normalize to PNG/JPEG per Section 2
- De-duplicate by:
  - Same originImageUrl already processed
  - Same contentHash or near-duplicate by pHash (distance threshold, e.g. Hamming <= 5)

---

## 6) Suggested Index Fields

If adding to an existing index, consider these fields:

- id: keyword (key)
- type: keyword
- content.text: searchable (full text)
- content.metadata.title: searchable/keyword
- content.metadata.source: keyword (facetable)
- content.metadata.parentPageUrl: keyword
- content.metadata.parentPageId: keyword (filterable, facetable)
- content.metadata.imageUrl: keyword
- content.metadata.standardizedFormat: keyword (filterable)
- content.metadata.width/height/sizeBytes: numeric (filterable/range)
- content.metadata.contentHash/pHash: keyword
- content.metadata.altText/caption: searchable
- content.metadata.lastCrawled: dateTime (filterable/sortable)

---

## 7) Configuration (Environment Variables)

- IMAGE_MIN_WIDTH (default 128)
- IMAGE_MIN_HEIGHT (default 128)
- IMAGE_MIN_AREA (default 16384)
- IMAGE_MIN_BYTES (default 5000)
- IMAGE_MAX_ASPECT_RATIO (default 6.0)
- IMAGE_MAX_EDGE (default 1920)
- IMAGE_JPEG_QUALITY (default 85)
- IMAGE_CREATE_THUMB (default true)
- IMAGE_THUMB_MAX_EDGE (default 320)
- IMAGE_STRIP_EXIF (default true)
- IMAGE_ENABLE_SVG (default false)

---

## 8) Implementation Outline (C# Pseudocode)

Below is an outline using SixLabors.ImageSharp for cross-platform image processing in Azure Functions.

```csharp
public sealed class ImageIngestionService
{
    public bool ShouldSkipByUrl(string url)
    {
        var u = url.ToLowerInvariant();
        string[] bad = { "favicon", "apple-touch-icon", "mask-icon", "sprite", "-sprite", "logo", "brandmark", "/icons/", "/emoji/" };
        return bad.Any(b => u.Contains(b));
    }

    public async Task<ImageIngestResult?> IngestAsync(ImageSource src, CancellationToken ct)
    {
        // src contains: originPageUrl, originImageUrl, bytes/stream, contentType, alt, caption, position, originalWidth/Height if known

        if (ShouldSkipByUrl(src.OriginImageUrl)) return null;

        // Early skip by headers if available
        if (src.ContentLength.HasValue && src.ContentLength.Value < MinBytes) return null;

        using var image = await Image.LoadAsync<Rgba32>(src.Stream, ct);
        int w = image.Width, h = image.Height;
        if (w < MinWidth || h < MinHeight || (w * h) < MinArea) return null;

        double ar = (double)Math.Max(w, h) / Math.Min(w, h);
        if (ar > MaxAspectRatio) return null;

        // Resize down to MAX_EDGE
        var maxEdge = Math.Max(w, h);
        if (maxEdge > MaxImageEdge)
        {
            double scale = (double)MaxImageEdge / maxEdge;
            image.Mutate(ctx => ctx.Resize((int)(w * scale), (int)(h * scale)));
            w = image.Width; h = image.Height;
        }

        bool hasAlpha = HasAnyAlpha(image);
        byte[] normalized;
        string fmt;

        if (hasAlpha)
        {
            // PNG
            using var ms = new MemoryStream();
            var encoder = new PngEncoder { ColorType = PngColorType.RgbWithAlpha };
            await image.SaveAsync(ms, encoder, ct);
            normalized = ms.ToArray();
            fmt = "png";
        }
        else
        {
            // JPEG
            using var ms = new MemoryStream();
            var encoder = new JpegEncoder { Quality = JpegQuality /* e.g., 85 */ };
            await image.SaveAsync(ms, encoder, ct);
            normalized = ms.ToArray();
            fmt = "jpg";
        }

        // Optional: strip EXIF by re-encoding without metadata (ImageSharp encoders above already drop most)
        // Compute hashes
        string sha256 = ComputeSha256(normalized);
        string pHash = ComputePerceptualHash(image); // implement or use a lib

        // Thumbnail
        string? thumbUrl = null;
        if (CreateThumb)
        {
            using var clone = image.Clone(x => x.Resize(new ResizeOptions
            {
                Mode = ResizeMode.Max,
                Size = new Size(ThumbMaxEdge, ThumbMaxEdge)
            }));
            using var tms = new MemoryStream();
            if (fmt == "jpg") await clone.SaveAsJpegAsync(tms, new JpegEncoder { Quality = 80 }, ct);
            else await clone.SaveAsPngAsync(tms, new PngEncoder(), ct);

            var thumbBytes = tms.ToArray();
            thumbUrl = await StoreAsync(thumbBytes, $"{sha256}_thumb.{fmt}", ct);
        }

        // Store normalized main image and build doc
        var mainUrl = await StoreAsync(normalized, $"{sha256}.{fmt}", ct);
        var parentPageId = StableIdFromUrl(src.OriginPageUrl);

        return new ImageIngestResult
        {
            Document = new
            {
                id = $"img_{sha256}",
                type = "image",
                content = new
                {
                    text = SelectBestText(src.AltText, src.Caption),
                    metadata = new
                    {
                        title = src.Title ?? src.AltText,
                        source = src.OriginPageUrl,
                        parentPageUrl = src.OriginPageUrl,
                        parentPageId = parentPageId,
                        imageUrl = src.OriginImageUrl,
                        originalFormat = src.OriginalFormat,
                        standardizedFormat = fmt,
                        width = w,
                        height = h,
                        originalWidth = src.OriginalWidth,
                        originalHeight = src.OriginalHeight,
                        sizeBytes = normalized.Length,
                        thumbUrl = thumbUrl,
                        contentHash = $"sha256:{sha256}",
                        pHash = pHash,
                        altText = src.AltText,
                        caption = src.Caption,
                        position = src.Position,
                        lastCrawled = DateTimeOffset.UtcNow
                    }
                }
            },
            StorageUrl = mainUrl
        };
    }
}
```

Notes:
- Use SixLabors.ImageSharp (recommended for cross-platform Azure Functions).
- For perceptual hashing, you can implement a DCT-based pHash or use a library; store as hex string.
- Storage: write to your blob container; return public or signed URLs as needed.

---

## 9) Integration with Existing Functions

- If you already ingest pages via AddUrlDocument:
  - Add an option includeImages=true to fetch, filter, normalize, and index images alongside the page.
  - Write image docs to the same index or a dedicated images index. If separate, keep parentPageId consistent across both.
- Search API:
  - Add nestImages=true option to attach top-k images to each page result by parentPageId.
  - Or provide a separate endpoint /api/SearchImages that filters type:image.

---

## 10) Acceptance Criteria

- Small images (icons, logos, sprites, favicons) are not indexed given the default thresholds.
- All stored/served images are standardized to JPEG or PNG following transparency and quality rules.
- Each image record includes:
  - originImageUrl, parentPageUrl, parentPageId
  - standardizedFormat, dimensions, hashes
  - alt/caption when available
- Duplicates are avoided using URL, SHA256, and pHash checks.
- Thumbnails exist for UI previews (if enabled).
- Images can be nested under parent pages in search results using parentPageId.

---