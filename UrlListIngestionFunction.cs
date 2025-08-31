using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using HtmlAgilityPack;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Extensions.Logging;
using RagSearch.Models;
using RagSearch.Services;
using System.Linq;

namespace RagSearch.Functions;

public class UrlListIngestionFunction
{
    private readonly ILogger<UrlListIngestionFunction> _logger;
    private readonly IDocumentProcessingService _docProcessor;
    private readonly ISearchService _searchService;
    private readonly BlobServiceClient _blobServiceClient;
    private static readonly HttpClient _http = new HttpClient(new HttpClientHandler
    {
        AllowAutoRedirect = true,
        AutomaticDecompression = System.Net.DecompressionMethods.All
    })
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    public UrlListIngestionFunction(
        ILoggerFactory loggerFactory,
        IDocumentProcessingService docProcessor,
        ISearchService searchService,
        BlobServiceClient blobServiceClient)
    {
        _logger = loggerFactory.CreateLogger<UrlListIngestionFunction>();
        _docProcessor = docProcessor;
        _searchService = searchService;
        _blobServiceClient = blobServiceClient;
    }

    public record IngestUrlListRequest(
        string? UrlListPath = null,
        string? BlobContainer = null,
        string? BlobName = null,
        bool DownloadImages = true,
        bool OcrImages = true,
        int MaxUrls = 200,
        int MaxImagesPerPage = 10);

    [Function("IngestUrlList")] 
    [OpenApiOperation(operationId: "Ingest_Url_List", tags: new[] { "Ingestion" }, Summary = "Ingest URLs from a list", Description = "Reads a text file of URLs (one per line) and ingests each page's content and images.")]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(IngestUrlListRequest), Required = true, Description = "Provide UrlListPath for local file or BlobContainer+BlobName for blob.")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(object), Summary = "Ingestion summary", Description = "Counts and results for processed URLs.")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "Ingest/UrlList")] HttpRequestData req)
    {
        var resp = req.CreateResponse();
        try
        {
            var body = await new StreamReader(req.Body).ReadToEndAsync();
            var options = JsonSerializer.Deserialize<IngestUrlListRequest>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                          ?? new IngestUrlListRequest();

            var urls = await ReadUrlsAsync(options);
            if (urls.Length == 0)
            {
                resp.StatusCode = HttpStatusCode.BadRequest;
                await resp.WriteStringAsync("No URLs provided");
                return resp;
            }

            await _searchService.EnsureIndexExistsAsync();

            int processed = 0, indexed = 0, imagesIndexed = 0;
            var results = new List<UrlProcessingResult>();
            var toIndex = new List<SearchDocument>();

            foreach (var url in urls.Take(Math.Max(1, options.MaxUrls)))
            {
                try
                {
                    var pageBytes = await FetchAsync(url);
                    if (pageBytes == null || pageBytes.Length == 0)
                    {
                        results.Add(new UrlProcessingResult { Url = url, Success = false, Error = "download_failed", ProcessedAt = DateTime.UtcNow });
                        continue;
                    }

                    var html = Encoding.UTF8.GetString(pageBytes);
                    var (plainText, title) = ExtractTextFromHtml(html);

                    var pageDoc = new DocumentToIndex
                    {
                        Id = $"url:{NormalizeId(url)}",
                        SourceUrl = url,
                        SourceType = "url",
                        ContentType = "text/plain",
                        FileType = "html",
                        Content = Encoding.UTF8.GetBytes(plainText),
                        FileSize = pageBytes.LongLength,
                        Created = DateTime.UtcNow,
                        Modified = DateTime.UtcNow
                    };

                    var processedDoc = await _docProcessor.ProcessAsync(pageDoc);

                    var sd = new SearchDocument
                    {
                        Id = pageDoc.Id,
                        Title = !string.IsNullOrWhiteSpace(title) ? title : processedDoc.Title,
                        Content = processedDoc.ExtractedText,
                        Summary = processedDoc.Summary,
                        ContentType = "text",
                        FileType = pageDoc.FileType,
                        Created = pageDoc.Created,
                        Modified = pageDoc.Modified,
                        Indexed = DateTime.UtcNow,
                        FileSize = pageDoc.FileSize,
                        Url = pageDoc.SourceUrl,
                        SourceContainer = null,
                        SourceType = pageDoc.SourceType,
                        Author = processedDoc.Author,
                        Language = processedDoc.Language,
                        KeyPhrases = processedDoc.KeyPhrases,
                        HasImages = false,
                        ImageCount = 0,
                        Metadata = JsonSerializer.Serialize(new { original = pageDoc.AdditionalMetadata })
                    };

                    toIndex.Add(sd);
                    processed++;

                    // Optionally fetch images and index basic image docs (with optional OCR)
                    if (options.DownloadImages)
                    {
                        var minImageBytes = GetMinImageBytes();
                        var imageCandidates = ExtractImageCandidates(html, url).Take(Math.Max(0, options.MaxImagesPerPage)).ToArray();
                        foreach (var cand in imageCandidates)
                        {
                            try
                            {
                                var imgUrl = cand.Url;
                                var imgBytes = await FetchAsync(imgUrl);
                                // Skip if failed or too tiny (likely icons/trackers)
                                if (imgBytes == null || imgBytes.Length == 0 || imgBytes.Length < minImageBytes) continue;

                                var (imgType, imgExt) = SniffImageType(imgBytes, imgUrl);
                                var caption = cand.Caption ?? cand.Alt ?? InferCaptionFromUrl(imgUrl);
                                var urlKeywords = InferKeywordsFromUrl(imgUrl) ?? Array.Empty<string>();

                                // Optional OCR for image content
                                string ocrText = string.Empty;
                                string ocrSummary = string.Empty;
                                string? ocrLanguage = null;
                                string[] ocrKeyPhrases = Array.Empty<string>();
                                if (options.OcrImages && IsOcrSupportedImage(imgType, imgExt))
                                {
                                    try
                                    {
                                        var imgDocToIndex = new DocumentToIndex
                                        {
                                            Id = $"ocr:{NormalizeId(imgUrl)}",
                                            SourceUrl = imgUrl,
                                            SourceType = "url",
                                            ContentType = imgType,
                                            FileType = imgExt,
                                            Content = imgBytes,
                                            FileSize = imgBytes.LongLength,
                                            Created = DateTime.UtcNow,
                                            Modified = DateTime.UtcNow
                                        };
                                        var processedImg = await _docProcessor.ProcessAsync(imgDocToIndex);
                                        ocrText = processedImg.ExtractedText ?? string.Empty;
                                        ocrSummary = processedImg.Summary ?? string.Empty;
                                        ocrLanguage = processedImg.Language;
                                        if (!string.IsNullOrWhiteSpace(ocrText))
                                        {
                                            ocrKeyPhrases = ExtractKeyPhrasesFromText(ocrText, 10);
                                        }
                                    }
                                    catch (Exception exOcr)
                                    {
                                        _logger.LogWarning(exOcr, "OCR failed for image {ImageUrl}", imgUrl);
                                    }
                                }

                                var mergedKeywords = urlKeywords
                                    .Concat(ocrKeyPhrases)
                                    .Select(k => k.Trim().ToLowerInvariant())
                                    .Where(k => !string.IsNullOrWhiteSpace(k))
                                    .Distinct()
                                    .ToArray();
                                var imgDoc = new SearchDocument
                                {
                                    Id = $"urlimg:{NormalizeId(imgUrl)}",
                                    Title = System.IO.Path.GetFileName(imgUrl) ?? "image",
                                    // Prefer OCR summary + caption for better keyword hits
                                    Content = string.Join(' ', new[] { ocrSummary, caption }.Where(s => !string.IsNullOrWhiteSpace(s))),
                                    Summary = $"Image from {url}",
                                    ContentType = "image",
                                    FileType = imgExt,
                                    Created = DateTime.UtcNow,
                                    Modified = DateTime.UtcNow,
                                    Indexed = DateTime.UtcNow,
                                    FileSize = imgBytes.LongLength,
                                    Url = imgUrl,
                                    SourceType = "url",
                                    HasImages = true,
                                    ImageCount = 1,
                                    ImageCaption = caption,
                                    ImageKeywords = mergedKeywords,
                                    // Also populate generic key phrases so they appear in result metadata
                                    KeyPhrases = mergedKeywords,
                                    Language = ocrLanguage,
                                    Metadata = JsonSerializer.Serialize(new { originPage = url, contentType = imgType, caption, alt = cand.Alt, fromOg = cand.FromOg, keywords = mergedKeywords, ocr = options.OcrImages, ocrChars = ocrText?.Length ?? 0, language = ocrLanguage })
                                };

                                toIndex.Add(imgDoc);
                                imagesIndexed++;
                            }
                            catch (Exception exImg)
                            {
                                _logger.LogWarning(exImg, "Failed to ingest image: {ImageUrl}", cand.Url);
                            }
                        }
                    }

                    // Flush periodically
                    if (toIndex.Count >= 10)
                    {
                        var ids = await _searchService.IndexDocumentsAsync(toIndex.ToArray());
                        indexed += ids.Length;
                        toIndex.Clear();
                    }

                    results.Add(new UrlProcessingResult { Url = url, Success = true, DocumentId = sd.Id, ProcessedAt = DateTime.UtcNow, ExtractedImages = imagesIndexed });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to process URL: {Url}", url);
                    results.Add(new UrlProcessingResult { Url = url, Success = false, Error = ex.Message, ProcessedAt = DateTime.UtcNow });
                }
            }

            if (toIndex.Count > 0)
            {
                var ids = await _searchService.IndexDocumentsAsync(toIndex.ToArray());
                indexed += ids.Length;
            }

            resp.StatusCode = HttpStatusCode.OK;
            resp.Headers.Add("Content-Type", "application/json");
            await resp.WriteStringAsync(JsonSerializer.Serialize(new
            {
                processed,
                indexed,
                imagesIndexed,
                results
            }, new JsonSerializerOptions { WriteIndented = true }));
            return resp;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during URL list ingestion");
            resp.StatusCode = HttpStatusCode.InternalServerError;
            await resp.WriteStringAsync("Error during URL list ingestion");
            return resp;
        }
    }

    private async Task<string[]> ReadUrlsAsync(IngestUrlListRequest options)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(options.UrlListPath) && File.Exists(options.UrlListPath))
            {
                var lines = await File.ReadAllLinesAsync(options.UrlListPath);
                return NormalizeUrlLines(lines);
            }
            if (!string.IsNullOrWhiteSpace(options.BlobContainer) && !string.IsNullOrWhiteSpace(options.BlobName))
            {
                var container = _blobServiceClient.GetBlobContainerClient(options.BlobContainer);
                var blob = container.GetBlobClient(options.BlobName);
                if (await blob.ExistsAsync())
                {
                    var dl = await blob.DownloadContentAsync();
                    var text = dl.Value.Content.ToString();
                    var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    return NormalizeUrlLines(lines);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed reading URL list");
        }
        return Array.Empty<string>();
    }

    private static string[] NormalizeUrlLines(IEnumerable<string> lines)
    {
        return lines
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("#"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string NormalizeId(string url)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(url)).TrimEnd('=');
    }

    private static async Task<byte[]?> FetchAsync(string url)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.UserAgent.ParseAdd("RagSearchBot/1.0");
            var res = await _http.SendAsync(req);
            if (!res.IsSuccessStatusCode) return null;
            return await res.Content.ReadAsByteArrayAsync();
        }
        catch
        {
            return null;
        }
    }

    private static (string Text, string Title) ExtractTextFromHtml(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        // Remove scripts/styles
        foreach (var node in doc.DocumentNode.SelectNodes("//script|//style") ?? Enumerable.Empty<HtmlNode>())
        {
            node.Remove();
        }

        var title = doc.DocumentNode.SelectSingleNode("//title")?.InnerText?.Trim() ?? string.Empty;
        var body = doc.DocumentNode.SelectSingleNode("//body") ?? doc.DocumentNode;
        var text = HtmlEntity.DeEntitize(body.InnerText ?? string.Empty);
        // Normalize whitespace
    text = Regex.Replace(text, @"\s+", " ").Trim();
        return (text, title);
    }

    private static IEnumerable<string> ExtractImageUrls(string html, string baseUrl)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        var yielded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // From <img src>
        var imgs = doc.DocumentNode.SelectNodes("//img[@src]") ?? Enumerable.Empty<HtmlNode>();
        foreach (var img in imgs)
        {
            var src = img.GetAttributeValue("src", string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(src)) continue;
            var abs = MakeAbsoluteUrl(baseUrl, src);
            if (ShouldSkipImage(abs)) continue;
            if (yielded.Add(abs)) yield return abs;
        }

        // From <picture><source srcset>
        var sources = doc.DocumentNode.SelectNodes("//picture/source[@srcset]") ?? Enumerable.Empty<HtmlNode>();
        foreach (var s in sources)
        {
            var srcset = s.GetAttributeValue("srcset", string.Empty).Trim();
            var typeAttr = s.GetAttributeValue("type", string.Empty).Trim();
            var chosen = ChooseFromSrcset(srcset, typeAttr);
            if (string.IsNullOrWhiteSpace(chosen)) continue;
            var abs = MakeAbsoluteUrl(baseUrl, chosen);
            if (ShouldSkipImage(abs)) continue;
            if (yielded.Add(abs)) yield return abs;
        }

        // OpenGraph fallback
        var og = doc.DocumentNode.SelectSingleNode("//meta[@property='og:image' or @name='og:image'][@content]");
        if (og != null)
        {
            var content = og.GetAttributeValue("content", string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(content))
            {
                var abs = MakeAbsoluteUrl(baseUrl, content);
                if (!ShouldSkipImage(abs) && yielded.Add(abs)) yield return abs;
            }
        }
    }

    private record ImageCandidate(string Url, string? Alt, string? Caption, bool FromOg);

    private static IEnumerable<ImageCandidate> ExtractImageCandidates(string html, string baseUrl)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        var yielded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // From <img src>
        var imgs = doc.DocumentNode.SelectNodes("//img[@src]") ?? Enumerable.Empty<HtmlNode>();
        foreach (var img in imgs)
        {
            var src = img.GetAttributeValue("src", string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(src)) continue;
            var abs = MakeAbsoluteUrl(baseUrl, src);
            if (ShouldSkipImage(abs)) continue;
            var alt = img.GetAttributeValue("alt", string.Empty).Trim();
            var caption = GetFigcaptionText(img);
            if (yielded.Add(abs)) yield return new ImageCandidate(abs, string.IsNullOrWhiteSpace(alt) ? null : alt, caption, false);
        }

        // From <picture><source srcset>
        var sources = doc.DocumentNode.SelectNodes("//picture/source[@srcset]") ?? Enumerable.Empty<HtmlNode>();
        foreach (var s in sources)
        {
            var srcset = s.GetAttributeValue("srcset", string.Empty).Trim();
            var typeAttr = s.GetAttributeValue("type", string.Empty).Trim();
            var chosen = ChooseFromSrcset(srcset, typeAttr);
            if (string.IsNullOrWhiteSpace(chosen)) continue;
            var abs = MakeAbsoluteUrl(baseUrl, chosen);
            if (ShouldSkipImage(abs)) continue;
            // Try to find <img> within the same <picture> to get alt
            string? alt = null;
            string? caption = null;
            var picture = s.ParentNode;
            if (picture != null)
            {
                var img = picture.SelectSingleNode(".//img");
                if (img != null)
                {
                    var a = img.GetAttributeValue("alt", string.Empty).Trim();
                    alt = string.IsNullOrWhiteSpace(a) ? null : a;
                }
                caption = GetFigcaptionText(picture);
            }
            if (yielded.Add(abs)) yield return new ImageCandidate(abs, alt, caption, false);
        }

        // OpenGraph fallback
        var og = doc.DocumentNode.SelectSingleNode("//meta[@property='og:image' or @name='og:image'][@content]");
        if (og != null)
        {
            var content = og.GetAttributeValue("content", string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(content))
            {
                var abs = MakeAbsoluteUrl(baseUrl, content);
                if (!ShouldSkipImage(abs) && yielded.Add(abs)) yield return new ImageCandidate(abs, null, null, true);
            }
        }
    }

    private static string? GetFigcaptionText(HtmlNode node)
    {
        try
        {
            // Walk up to ancestor <figure> and read <figcaption>
            var current = node;
            while (current != null)
            {
                if (string.Equals(current.Name, "figure", StringComparison.OrdinalIgnoreCase))
                {
                    var cap = current.SelectSingleNode(".//figcaption");
                    var text = cap?.InnerText?.Trim();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        text = HtmlEntity.DeEntitize(text);
                        text = Regex.Replace(text, "\\s+", " ").Trim();
                        return text;
                    }
                    break;
                }
                current = current.ParentNode;
            }
        }
        catch { }
        return null;
    }

    private static bool ShouldSkipImage(string url)
    {
        var u = url.ToLowerInvariant();
        if (u.Contains("favicon") || u.Contains("apple-touch-icon") || u.Contains("sprite") || u.Contains("logo")) return true;
        // Common social/share icons and chrome assets
        if (u.Contains("facebook") || u.Contains("linkedin") || u.Contains("twitter") || u.Contains("grayscale")) return true;
        if (u.Contains("/icons/") || u.Contains("/static/") || u.Contains("/share") || u.Contains("/toolbar") || u.Contains("/badge") || u.Contains("/tracking") || u.Contains("/pixel") || u.Contains("/beacon")) return true;
        try
        {
            var uri = new Uri(url);
            if (string.Equals(uri.Host, "uhf.microsoft.com", StringComparison.OrdinalIgnoreCase)) return true;
        }
        catch { }
        return false;
    }

    private static string ChooseFromSrcset(string srcset, string? type)
    {
        var parts = srcset
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => p.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[0])
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToArray();
        if (parts.Length == 0) return string.Empty;
        string? pick = parts.FirstOrDefault(p => p.Contains("format=avif", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".avif", StringComparison.OrdinalIgnoreCase));
        if (pick == null) pick = parts.FirstOrDefault(p => p.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) || p.Contains("webp", StringComparison.OrdinalIgnoreCase));
        return pick ?? parts[0];
    }

    private static readonly HashSet<string> Stopwords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the","and","for","with","that","this","from","your","you","are","our","was","were","will","can","not","but","has","have","had","into","onto","over","under","about","into","its","it's","a","an","of","to","in","on","by","as","at","or","be","is","it","we","they","them","their","there","then","than","more","most","very","also","how","what","when","where","why"
    };

    private static string[] ExtractKeyPhrasesFromText(string text, int max = 10)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<string>();
        var tokens = Regex.Split(text.ToLowerInvariant(), "[^a-z0-9]+")
                          .Where(t => t.Length >= 3 && !Stopwords.Contains(t))
                          .GroupBy(t => t)
                          .Select(g => (term: g.Key, count: g.Count()))
                          .OrderByDescending(x => x.count)
                          .Take(max)
                          .Select(x => x.term)
                          .ToArray();
        return tokens;
    }

    private static string MakeAbsoluteUrl(string baseUrl, string relative)
    {
        try
        {
            if (Uri.TryCreate(relative, UriKind.Absolute, out var abs)) return abs.ToString();
            var b = new Uri(baseUrl);
            return new Uri(b, relative).ToString();
        }
        catch { return relative; }
    }

    private static int GetMinImageBytes()
    {
        // Default to 8KB to skip tiny social icons, pixels, and trackers
        const int defaultMin = 8 * 1024;
        try
        {
            var s = Environment.GetEnvironmentVariable("MIN_IMAGE_BYTES");
            if (!string.IsNullOrWhiteSpace(s) && int.TryParse(s, out var v) && v > 0)
            {
                return v;
            }
        }
        catch { }
        return defaultMin;
    }

    private static (string ContentType, string Ext) SniffImageType(byte[] bytes, string url)
    {
        // Byte sniff common image types
        if (bytes.Length > 3 && bytes[0] == 0xFF && bytes[1] == 0xD8)
            return ("image/jpeg", "jpg");
        if (bytes.Length > 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
            return ("image/png", "png");
        if (bytes.Length > 4 && bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46)
            return ("image/gif", "gif");
        // AVIF: 'ftypavif' at offset 4
        if (bytes.Length > 12 && Encoding.ASCII.GetString(bytes, 4, 7) == "ftypavif")
            return ("image/avif", "avif");
        // WebP: 'RIFF' then 'WEBP' at offset 8
        if (bytes.Length > 12 && Encoding.ASCII.GetString(bytes, 0, 4) == "RIFF" && Encoding.ASCII.GetString(bytes, 8, 4) == "WEBP")
            return ("image/webp", "webp");
        // fallback from url
        var ext = System.IO.Path.GetExtension(url).Trim('.').ToLowerInvariant();
        var ct = ext switch
        {
            "jpg" or "jpeg" => "image/jpeg",
            "png" => "image/png",
            "gif" => "image/gif",
            "avif" => "image/avif",
            "webp" => "image/webp",
            _ => "application/octet-stream"
        };
        return (ct, string.IsNullOrWhiteSpace(ext) ? ct.Replace("image/","") : ext);
    }

    private static bool IsOcrSupportedImage(string contentType, string ext)
    {
        // Azure Document Intelligence Read supports JPG, PNG, BMP, TIFF, PDF, etc.
        // We’ll skip GIF and WebP/AVIF for OCR to avoid InvalidContent 400s.
        var ct = (contentType ?? string.Empty).ToLowerInvariant();
        var e = (ext ?? string.Empty).ToLowerInvariant();
        if (ct.StartsWith("image/"))
        {
            if (e == "jpg" || e == "jpeg" || e == "png" || e == "bmp" || e == "tif" || e == "tiff") return true;
            if (ct == "image/jpeg" || ct == "image/png" || ct == "image/bmp" || ct == "image/tiff") return true;
            return false; // skip gif/webp/avif
        }
        return false;
    }

    private static string? InferCaptionFromUrl(string url)
    {
        try
        {
            var fileName = System.IO.Path.GetFileNameWithoutExtension(new Uri(url).AbsolutePath);
            if (string.IsNullOrWhiteSpace(fileName)) return null;
            fileName = fileName.Replace('-', ' ').Replace('_', ' ');
            return System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(fileName);
        }
        catch { return null; }
    }

    private static string[]? InferKeywordsFromUrl(string url)
    {
        try
        {
            var tokens = new List<string>();
            var u = new Uri(url);
            var name = System.IO.Path.GetFileNameWithoutExtension(u.AbsolutePath);
            if (!string.IsNullOrWhiteSpace(name)) tokens.AddRange(Regex.Split(name, "[^A-Za-z0-9]+").Where(p => !string.IsNullOrWhiteSpace(p)));
            var segs = u.Segments?.Select(s => s.Trim('/')).Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
            if (segs != null) foreach (var seg in segs) tokens.AddRange(Regex.Split(seg, "[^A-Za-z0-9]+").Where(p => !string.IsNullOrWhiteSpace(p)));
            var kws = tokens.Select(t => t.Trim().ToLowerInvariant()).Where(t => t.Length >= 3).Distinct().Take(8).ToArray();
            return kws.Length > 0 ? kws : null;
        }
        catch { return null; }
    }
}
