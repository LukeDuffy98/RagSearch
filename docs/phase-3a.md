# Phase 3A: Advanced Document Processing

This phase adds:

- PDF/Word/PowerPoint processing via Azure Document Intelligence (prebuilt-read)
- Image OCR (images are processed via the same read model)
- Batch ingestion from Azure Blob Storage

## Configuration

Add these settings to local.settings.json (or App Settings):

- AZURE_DOCUMENTINTELLIGENCE_ENDPOINT
- AZURE_DOCUMENTINTELLIGENCE_API_KEY

OpenAI is still required for embeddings:

- AZURE_OPENAI_ENDPOINT
- AZURE_OPENAI_API_KEY

## API

POST /api/Ingest/BlobsBatch

Body example:

{
  "container": "docs",
  "prefix": "optional/subfolder/",
  "allowedExtensions": [".pdf", ".docx", ".pptx", ".png", ".jpg"],
  "maxFiles": 25
}

Response includes counts of processed and indexed items.

## Notes

- Uses the "prebuilt-read" model
- Unsupported files are skipped; errors are logged and processing continues
- Indexing works with both implementations (simplified or Azure Search)
 - If the source container does not exist, it will be created automatically