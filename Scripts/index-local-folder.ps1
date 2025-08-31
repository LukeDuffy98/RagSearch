param(
  [Parameter(Mandatory = $true)][string]$Path,
  [string]$Container = "localdocs",
  [string]$FunctionsBaseUrl = "http://localhost:7071",
  [string[]]$AllowedExtensions = @(".pdf", ".docx", ".pptx", ".txt", ".png", ".jpg", ".jpeg"),
  [string]$Prefix,
  [string]$ConnectionString,
  [switch]$UseUploadFunctionOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Write-Info($m){ Write-Host $m -ForegroundColor Cyan }
function Write-Ok($m){ Write-Host $m -ForegroundColor Green }
function Write-Warn($m){ Write-Host $m -ForegroundColor Yellow }
function Write-Err($m){ Write-Host $m -ForegroundColor Red }

# Resolve and validate folder
$Path = [System.IO.Path]::GetFullPath($Path)
if(-not (Test-Path -LiteralPath $Path -PathType Container)){
  Write-Err "Folder not found: $Path"
  exit 1
}
Write-Info "Indexing local folder: $Path"

# Check Functions host is up
$health = "$FunctionsBaseUrl/api/SearchStatus"
try {
  $resp = Invoke-WebRequest -UseBasicParsing -Uri $health -TimeoutSec 5 -Method GET
  if(-not ($resp.StatusCode -ge 200 -and $resp.StatusCode -lt 500)) { throw "Unexpected status $($resp.StatusCode)" }
  Write-Ok "Functions host reachable at $FunctionsBaseUrl"
} catch {
  Write-Err "Functions host not reachable at $FunctionsBaseUrl. Start it (VS Code task: 'func: 4' or .\\Scripts\\start-dev.ps1) and retry."
  exit 1
}

# Azurite dev store connection string (overrideable)
if([string]::IsNullOrWhiteSpace($ConnectionString)){
  $Conn = "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFe7Z+Zf2n0NRZ6IFSuFZfVZgZ6r5hAZdZ4hZ0Z0Z0Z0Z0Z0Z0Z0Z0Z0Z0Z==;BlobEndpoint=http://127.0.0.1:10000/devstoreaccount1;QueueEndpoint=http://127.0.0.1:10001/devstoreaccount1;TableEndpoint=http://127.0.0.1:10002/devstoreaccount1"
} else {
  $Conn = $ConnectionString
}

function Test-AzCli { return [bool](Get-Command az -ErrorAction SilentlyContinue) }

function Try-AzCliUpload {
  param([string]$SrcPath, [string]$DestContainer)
  Write-Info "Ensuring container '$DestContainer' exists (az CLI)"
  try {
    az storage container create --name $DestContainer --connection-string $Conn | Out-Null
  } catch {
    Write-Warn "az container create failed: $($_.Exception.Message)"
    return $false
  }
  Write-Info "Uploading folder to container '$DestContainer' via az CLI (recursive)"
  try {
    az storage blob upload-batch -d $DestContainer -s $SrcPath --connection-string $Conn --overwrite | Out-Null
    Write-Ok "Upload complete via az CLI"
    return $true
  } catch {
    Write-Warn "az blob upload-batch failed: $($_.Exception.Message)"
    return $false
  }
}

# Upload files
if($UseUploadFunctionOnly.IsPresent){
  Write-Warn "UseUploadFunctionOnly specified. Skipping az CLI. Uploading via UploadBlob function."
  $useFunctionUpload = $true
}
elseif(Test-AzCli){
  $useFunctionUpload = -not (Try-AzCliUpload -SrcPath $Path -DestContainer $Container)
  if(-not $useFunctionUpload){
    # az CLI upload succeeded; skip function upload
    $useFunctionUpload = $false
  } else {
    Write-Warn "Falling back to UploadBlob function."
  }
}
else {
  Write-Warn "az CLI not found. Using UploadBlob function."
  $useFunctionUpload = $true
}

if($useFunctionUpload){
  Write-Warn "az CLI not found or disabled. Falling back to per-file upload via UploadBlob function."
  $files = Get-ChildItem -LiteralPath $Path -Recurse -File | Where-Object { $AllowedExtensions -contains ([IO.Path]::GetExtension($_.FullName)) }
  if(-not $files){ Write-Warn "No files with allowed extensions found in $Path" }
  foreach($f in $files){
    $rel = Resolve-Path -LiteralPath $f.FullName | ForEach-Object { $_.Path }
    Write-Info "Uploading: $rel"
    $bytes = [IO.File]::ReadAllBytes($rel)
    $b64 = [Convert]::ToBase64String($bytes)
  $relPath = $f.FullName.Substring($Path.Length)
  # Normalize to blob name: strip leading back/forward slashes, then convert backslashes to forward slashes
  $name = ($relPath -replace '^[\\/]+','') -replace '\\','/'
    $ct = switch([IO.Path]::GetExtension($f.Name).ToLowerInvariant()){
      ".pdf" { "application/pdf" }
      ".docx" { "application/vnd.openxmlformats-officedocument.wordprocessingml.document" }
      ".pptx" { "application/vnd.openxmlformats-officedocument.presentationml.presentation" }
      ".txt" { "text/plain" }
      ".png" { "image/png" }
      ".jpg" { "image/jpeg" }
      ".jpeg" { "image/jpeg" }
      default { "application/octet-stream" }
    }
    $body = @{ container = $Container; name = $name; contentBase64 = $b64; contentType = $ct } | ConvertTo-Json
  Invoke-RestMethod -Uri "$FunctionsBaseUrl/api/UploadBlob" -Method POST -Body $body -ContentType "application/json" | Out-Null
  }
  Write-Ok "Upload complete via UploadBlob function"
}

# Trigger ingestion
Write-Info "Triggering batch ingestion from container '$Container'"
$body2 = @{ container = $Container; prefix = $Prefix; allowedExtensions = $AllowedExtensions; maxFiles = 100000 } | ConvertTo-Json
$ing = Invoke-RestMethod -Uri "$FunctionsBaseUrl/api/Ingest/BlobsBatch" -Method POST -Body $body2 -ContentType "application/json"
Write-Ok ("Ingestion complete: processed={0} indexed={1}" -f $ing.processed, $ing.indexed)

# Optional: show quick status
try {
  $status = Invoke-RestMethod -Uri "$FunctionsBaseUrl/api/SearchStatus" -Method GET
  Write-Host ("Index now has {0} documents (storage ~{1} bytes)" -f $status.documentCount, $status.storageSize)
} catch { }
