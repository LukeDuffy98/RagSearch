# Ingest a list of URLs (content and images) via the Ingest/UrlList function
param(
  [string]$FunctionsBaseUrl = "http://localhost:7071",
  [string]$UrlListPath = "",
  [string]$BlobContainer = "",
  [string]$BlobName = "",
  [switch]$NoImages,
  [int]$MaxUrls = 50,
  [int]$MaxImagesPerPage = 5
)

function Write-Info($m){ Write-Host "[INFO] $m" -ForegroundColor Cyan }
function Write-Ok($m){ Write-Host "[ OK ] $m" -ForegroundColor Green }
function Write-Err($m){ Write-Host "[ERR ] $m" -ForegroundColor Red }

if([string]::IsNullOrWhiteSpace($UrlListPath) -and ([string]::IsNullOrWhiteSpace($BlobContainer) -or [string]::IsNullOrWhiteSpace($BlobName))){
  Write-Err "Provide -UrlListPath for a local text file OR -BlobContainer and -BlobName for a blob-hosted list."; exit 1
}

$body = @{ 
  urlListPath   = $UrlListPath
  blobContainer = $BlobContainer
  blobName      = $BlobName
  downloadImages = -not $NoImages
  maxUrls = $MaxUrls
  maxImagesPerPage = $MaxImagesPerPage
} | ConvertTo-Json

Write-Info "Posting to $FunctionsBaseUrl/api/Ingest/UrlList"
try {
  $resp = Invoke-RestMethod -Uri "$FunctionsBaseUrl/api/Ingest/UrlList" -Method POST -Body $body -ContentType "application/json"
  Write-Ok "Processed=$($resp.processed) Indexed=$($resp.indexed) ImagesIndexed=$($resp.imagesIndexed)"
  if($resp.results){
    $fail = @($resp.results | Where-Object { -not $_.success })
    if($fail.Count -gt 0){ Write-Info "Failures:`n$($fail | ConvertTo-Json -Depth 5)" }
  }
}
catch{
  Write-Err "Request failed: $_"
  exit 1
}
