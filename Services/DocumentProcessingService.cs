using System.Text;
using Azure;
using Azure.AI.FormRecognizer.DocumentAnalysis;
using Microsoft.Extensions.Logging;
using RagSearch.Models;

namespace RagSearch.Services;

public interface IDocumentProcessingService
{
    Task<ProcessedDocument> ProcessAsync(DocumentToIndex document, CancellationToken cancellationToken = default);
}

/// <summary>
/// Processes documents using Azure Document Intelligence (prebuilt-read) to extract text and basic metadata.
/// Supports PDFs, images, and Office files (DOCX/PPTX) for OCR/text extraction.
/// </summary>
public class DocumentProcessingService : IDocumentProcessingService
{
    private readonly DocumentAnalysisClient _diClient;
    private readonly ILogger<DocumentProcessingService> _logger;

    public DocumentProcessingService(DocumentAnalysisClient diClient, ILogger<DocumentProcessingService> logger)
    {
        _diClient = diClient ?? throw new ArgumentNullException(nameof(diClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ProcessedDocument> ProcessAsync(DocumentToIndex document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        var processed = new ProcessedDocument
        {
            Id = document.Id,
            Title = GetTitleFromUrlOrId(document),
            SourceDocument = document
        };

        if (document.Content == null || document.Content.Length == 0)
        {
            _logger.LogWarning("Document {Id} has no content to process", document.Id);
            return processed;
        }

        try
        {
            // For plain text and HTML-like content, skip DI and use lightweight path
            var ct = (document.ContentType ?? string.Empty).ToLowerInvariant();
            var ft = (document.FileType ?? string.Empty).ToLowerInvariant();
            bool isTextual = ct.StartsWith("text/") || ft is "txt" or "md" or "csv" or "html" or "htm";

            if (isTextual)
            {
                processed.ExtractedText = TryDecodeText(document.Content);
                processed.Summary = BuildSummary(processed.ExtractedText);
                processed.ExtractedImages = Array.Empty<ExtractedImage>();
                return processed;
            }

            _logger.LogInformation("Analyzing document {Id} with prebuilt-read (size: {Size} bytes, type: {Type})", document.Id, document.Content.Length, document.ContentType);

            using var stream = new MemoryStream(document.Content);
            var operation = await _diClient.AnalyzeDocumentAsync(WaitUntil.Completed, "prebuilt-read", stream, cancellationToken: cancellationToken);
            var result = operation.Value;

            // Consolidate content
            processed.ExtractedText = result.Content ?? string.Empty;

            // Basic language detection if available
            if (result.Languages?.Count > 0)
            {
                processed.Language = result.Languages[0].Locale;
            }

            // Very lightweight summary: first 400 characters or first line
            processed.Summary = BuildSummary(processed.ExtractedText);

            // Image signal: if original file is an image, set flags
            processed.ExtractedImages = Array.Empty<ExtractedImage>();

            return processed;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to analyze document {Id} using Document Intelligence", document.Id);
            // Best-effort fallback: try naive text extraction for UTF8 text files
            processed.ExtractedText = TryDecodeText(document.Content);
            processed.Summary = BuildSummary(processed.ExtractedText);
            return processed;
        }
    }

    private static string GetTitleFromUrlOrId(DocumentToIndex doc)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(doc.SourceUrl))
            {
                var fileName = doc.SourceUrl.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault();
                if (!string.IsNullOrEmpty(fileName)) return fileName;
            }
        }
        catch { /* ignore */ }
        return doc.Id;
    }

    private static string BuildSummary(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var trimmed = text.Trim();
        if (trimmed.Length <= 400) return trimmed;
        return trimmed.Substring(0, 400) + "…";
    }

    private static string TryDecodeText(byte[] bytes)
    {
        try
        {
            // Attempt UTF8 without BOM, fallback to ASCII
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            try { return Encoding.ASCII.GetString(bytes); } catch { return string.Empty; }
        }
    }
}
