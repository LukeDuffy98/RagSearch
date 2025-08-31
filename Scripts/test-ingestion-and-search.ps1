param(
  [string]$FunctionsBaseUrl = "http://localhost:7071",
  [string]$Container = "docs",
  [string]$LocalFilePath = "$PSScriptRoot/sample.txt",
  [string]$UrlToIngest = "https://learn.microsoft.com/en-us/copilot/security/prompting-tips",
  [switch]$ShowImages
)

# Default ShowImages to true when not explicitly provided
if(-not $PSBoundParameters.ContainsKey('ShowImages')){ $ShowImages = $true }

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Write-Info($msg){ Write-Host $msg -ForegroundColor Cyan }
function Write-Ok($msg){ Write-Host $msg -ForegroundColor Green }
function Write-Warn($msg){ Write-Host $msg -ForegroundColor Yellow }
function Write-Err($msg){ Write-Host $msg -ForegroundColor Red }

Write-Info "Phase 3A test: upload a file, ingest a URL, then run searches"

# 0) Check Functions host is up
$healthUrls = @(
  "$FunctionsBaseUrl/api/SearchStatus",
  "$FunctionsBaseUrl"
)
$isUp = $false
foreach($u in $healthUrls){
  try {
    $resp = Invoke-WebRequest -UseBasicParsing -Uri $u -Method GET -TimeoutSec 5
    if ($resp.StatusCode -ge 200 -and $resp.StatusCode -lt 500){
      $isUp = $true; break
    }
  } catch { }
}
if(-not $isUp){
  Write-Err "Functions host not reachable at $FunctionsBaseUrl. Start it first (task: 'func: 4')."
  return
}
Write-Ok "Functions host reachable."

# 1) Prepare a local sample file
# Expand $PSScriptRoot if it appeared literally from caller
if ($LocalFilePath -match '\$PSScriptRoot') {
  $LocalFilePath = $ExecutionContext.InvokeCommand.ExpandString($LocalFilePath)
}
# If still not rooted, make it relative to this script folder
if (-not [IO.Path]::IsPathRooted($LocalFilePath)) {
  if ($PSScriptRoot) { $LocalFilePath = Join-Path $PSScriptRoot $LocalFilePath }
}
if(-not (Test-Path -Path $LocalFilePath)){
  Write-Info "Creating sample file at $LocalFilePath"
  @"
This is a sample document about Azure Functions and semantic search.
It demonstrates ingestion of a plain text file and subsequent search.
"@ | Set-Content -Encoding UTF8 -Path $LocalFilePath
} else {
  Write-Info "Using existing file: $LocalFilePath"
}

# 2) Upload file to Blob Storage container using az CLI if available
function Test-AzCli {
  if (Get-Command az -ErrorAction SilentlyContinue) { return $true } else { return $false }
}

# Build a connection string for Azurite if using local dev storage
$devConn = "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFe7Z+Zf2n0NRZ6IFSuFZfVZgZ6r5hAZdZ4hZ0Z0Z0Z0Z0Z0Z0Z0Z0Z0Z0Z0Z==;BlobEndpoint=http://127.0.0.1:10000/devstoreaccount1;QueueEndpoint=http://127.0.0.1:10001/devstoreaccount1;TableEndpoint=http://127.0.0.1:10002/devstoreaccount1"
$connString = $devConn
if(Test-AzCli){
  Write-Info "Ensuring container '$Container' exists (az CLI)"
  try {
    az storage container create --name $Container --connection-string $connString | Out-Null
  } catch {
    Write-Warn "az container create failed. Will fallback to UploadBlob function for upload."
  }

  $blobName = [IO.Path]::GetFileName($LocalFilePath)
  Write-Info "Uploading $blobName to container '$Container' via az CLI"
  $azUploaded = $false
  try {
    az storage blob upload --file $LocalFilePath --container-name $Container --name $blobName --overwrite --connection-string $connString | Out-Null
    $azUploaded = $true
  } catch {
    Write-Warn "az blob upload failed. Falling back to UploadBlob function."
  }

  if (-not $azUploaded) {
    $bytes = [IO.File]::ReadAllBytes($LocalFilePath)
    $b64 = [Convert]::ToBase64String($bytes)
    $upBody = @{ container = $Container; name = $blobName; contentBase64 = $b64; contentType = "text/plain" } | ConvertTo-Json
    $upResp = Invoke-RestMethod -Uri "$FunctionsBaseUrl/api/UploadBlob" -Method POST -Body $upBody -ContentType "application/json"
    Write-Ok "Upload via function completed: $($upResp.url)"
  } else {
    Write-Ok "Upload completed"
  }
} else {
  Write-Warn "az CLI not found. Skipping upload step. Place your file in the container manually or install Azure CLI."
}

# 3) Trigger batch ingestion from the container
Write-Info "Triggering batch ingestion from container '$Container'"
$ingestBody = @{ container = $Container; allowedExtensions = @(".txt", ".pdf", ".docx", ".pptx"); maxFiles = 25 } | ConvertTo-Json
$ingestResp = Invoke-RestMethod -Uri "$FunctionsBaseUrl/api/Ingest/BlobsBatch" -Method POST -Body $ingestBody -ContentType "application/json"
Write-Ok "Ingestion complete: processed=$($ingestResp.processed) indexed=$($ingestResp.indexed) containerCreated=$($ingestResp.containerCreated)"

# 4) Fetch URL content and add as a document via AddUrlDocument
Write-Info "Downloading URL content: $UrlToIngest"
$urlResp = Invoke-WebRequest -UseBasicParsing -Uri $UrlToIngest -Method GET
$urlText = $urlResp.Content

# Parse <title> from HTML content without relying on ParsedHtml
$title = $null
if ($urlText) {
  $m = [regex]::Match($urlText, '<title>(.*?)</title>', 'IgnoreCase')
  if ($m.Success) { $title = $m.Groups[1].Value.Trim() }
}
if([string]::IsNullOrWhiteSpace($title)){ $title = "URL Document" }

$urlDoc = @{ Title = $title; Url = $UrlToIngest; Content = $urlText } | ConvertTo-Json -Depth 5
$addResp = Invoke-RestMethod -Uri "$FunctionsBaseUrl/api/AddUrlDocument" -Method POST -Body $urlDoc -ContentType "application/json"
Write-Ok "URL document indexed: id=$($addResp.documentId) title='$($addResp.title)'"

# 5) Run a few searches
function Show-TopResults($results, $take = 3){
  if(-not $results){ Write-Host "(no results)"; return }
  $i = 0
  foreach($r in $results.results){
    $i++; if($i -gt $take){ break }
  $title = $r.content.metadata.title
  if([string]::IsNullOrWhiteSpace($title)){ $title = $r.summary }
  if([string]::IsNullOrWhiteSpace($title)){ $title = $r.url }
  $imgFlag = if($r.content.metadata.hasImages){ " hasImages=$($r.content.metadata.hasImages) imageCount=$($r.content.metadata.imageCount)" } else { "" }
  Write-Host ("[{0}] {1}  score={2:n3}{3}" -f $i, $title, $r.score, $imgFlag)
  }
}

function Show-Images($label, $results, $take = 1){
  if(-not $results){ return }
  Write-Info "Images for: $label"
  $i = 0
  $printed = $false
  foreach($r in $results.results){
    $i++; if($i -gt $take){ break }
    $meta = $r.content.metadata
    if($meta.hasImages){
      $printed = $true
      Write-Host ("  [{0}] hasImages={1} imageCount={2}" -f $i, $meta.hasImages, $meta.imageCount)
      if($meta.images){
        Write-Host "    images:"; $meta.images | ForEach-Object { Write-Host "      - $_" }
      }
      if($meta.imagesDetailed){
        Write-Host "    imagesDetailed:"
        foreach($img in $meta.imagesDetailed){
          $ct = if($img.contentType){$img.contentType}else{""}
          $sz = if($img.fileSize){$img.fileSize}else{""}
          $cap = if($img.caption){$img.caption}else{""}
          $kws = if($img.keywords){ ($img.keywords -join ', ') } else {""}
          Write-Host "      - url=$($img.url) contentType=$ct fileSize=$sz"
          if($cap -or $kws){
            if($cap){ Write-Host "          caption: $cap" }
            if($kws){ Write-Host "          keywords: $kws" }
          }
        }
      }
    }
  }
  if(-not $printed){ Write-Host "  (no image details in top $take results)" }
}

Write-Info "Search: keyword on 'Azure Functions'"
$s1 = Invoke-RestMethod -Uri "$FunctionsBaseUrl/api/Search?q=Azure+Functions&type=keyword&maxResults=5" -Method GET
Write-Host "Found: $($s1.totalResults)"; Show-TopResults $s1 5

Write-Info "Search: keyword on 'Security Copilot' (from URL)"
$s2 = Invoke-RestMethod -Uri "$FunctionsBaseUrl/api/Search?q=Security+Copilot&type=keyword&maxResults=5" -Method GET
Write-Host "Found: $($s2.totalResults)"; Show-TopResults $s2 5
if($ShowImages){ Show-Images "Security Copilot" $s2 5 }

Write-Info "Search: vector on 'prompting tips'"
$s3Body = @{ query = "prompting tips"; searchType = "Vector"; maxResults = 5 } | ConvertTo-Json
$s3 = Invoke-RestMethod -Uri "$FunctionsBaseUrl/api/Search" -Method POST -Body $s3Body -ContentType "application/json"
Write-Host "Found: $($s3.totalResults)"; Show-TopResults $s3 5
if($ShowImages){ Show-Images "prompting tips" $s3 5 }

Write-Ok "Test run complete."
