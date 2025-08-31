param(
  [string]$FunctionsBaseUrl = "http://localhost:7071",
  [string]$Container = "docs",
  [string]$LocalFilePath = "$PSScriptRoot/sample.txt",
  [string]$UrlToIngest = "https://learn.microsoft.com/en-us/copilot/security/prompting-tips"
)

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

$connString = "UseDevelopmentStorage=true"
if(Test-AzCli){
  Write-Info "Ensuring container '$Container' exists (az CLI)"
  az storage container create --name $Container --connection-string $connString | Out-Null

  $blobName = [IO.Path]::GetFileName($LocalFilePath)
  Write-Info "Uploading $blobName to container '$Container' via az CLI"
  az storage blob upload --file $LocalFilePath --container-name $Container --name $blobName --overwrite --connection-string $connString | Out-Null
  Write-Ok "Upload completed"
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

$title = ($urlResp.ParsedHtml.title 2>$null)
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
    Write-Host ("[{0}] {1}  score={2:n3}" -f $i, ($r.content.metadata.title ?? $r.summary ?? $r.url), $r.score)
  }
}

Write-Info "Search: keyword on 'Azure Functions'"
$s1 = Invoke-RestMethod -Uri "$FunctionsBaseUrl/api/Search?q=Azure+Functions&type=keyword&maxResults=5" -Method GET
Write-Host "Found: $($s1.totalResults)"; Show-TopResults $s1 5

Write-Info "Search: keyword on 'Security Copilot' (from URL)"
$s2 = Invoke-RestMethod -Uri "$FunctionsBaseUrl/api/Search?q=Security+Copilot&type=keyword&maxResults=5" -Method GET
Write-Host "Found: $($s2.totalResults)"; Show-TopResults $s2 5

Write-Info "Search: vector on 'prompting tips'"
$s3Body = @{ query = "prompting tips"; searchType = "Vector"; maxResults = 5 } | ConvertTo-Json
$s3 = Invoke-RestMethod -Uri "$FunctionsBaseUrl/api/Search" -Method POST -Body $s3Body -ContentType "application/json"
Write-Host "Found: $($s3.totalResults)"; Show-TopResults $s3 5

Write-Ok "Test run complete."
