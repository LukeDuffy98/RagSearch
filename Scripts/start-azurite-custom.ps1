param(
  [int]$BlobPort = 11000,
  [int]$QueuePort = 11001,
  [int]$TablePort = 11002,
  [string]$Location = "azurite",
  [switch]$KillOnBusy
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Write-Info($m){ Write-Host $m -ForegroundColor Cyan }
function Write-Ok($m){ Write-Host $m -ForegroundColor Green }
function Write-Warn($m){ Write-Host $m -ForegroundColor Yellow }
function Write-Err($m){ Write-Host $m -ForegroundColor Red }

if(-not (Get-Command azurite -ErrorAction SilentlyContinue)){
  Write-Err "Azurite CLI not found. Install with: npm install -g azurite"
  exit 1
}

if(-not (Test-Path $Location)){ New-Item -ItemType Directory -Path $Location | Out-Null }

Write-Info "Starting Azurite on ports: blob=$BlobPort queue=$QueuePort table=$TablePort"

# Check for listeners and optionally kill
foreach($p in @($BlobPort,$QueuePort,$TablePort)){
  $conn = Get-NetTCPConnection -LocalPort $p -State Listen -ErrorAction SilentlyContinue
  if($conn){
    $proc = Get-Process -Id $conn.OwningProcess -ErrorAction SilentlyContinue
    if($KillOnBusy -and $proc){
      Write-Warn "Port $p in use by PID $($proc.Id) ($($proc.ProcessName)). Stopping it."
      $proc.Kill()
      Start-Sleep -Seconds 1
    } else {
      Write-Err "Port $p is in use by PID $($proc.Id) ($($proc.ProcessName)). Use -KillOnBusy or choose different ports."
      exit 1
    }
  }
}

Start-Process -FilePath "azurite" -ArgumentList @(
  "--silent",
  "--location", $Location,
  "--debug", (Join-Path $Location "debug.log"),
  "--blobHost", "127.0.0.1",
  "--queueHost", "127.0.0.1",
  "--tableHost", "127.0.0.1",
  "--blobPort", $BlobPort,
  "--queuePort", $QueuePort,
  "--tablePort", $TablePort
) | Out-Null

# Wait for readiness
for($i=0;$i -lt 10;$i++){
  try {
    Invoke-WebRequest -UseBasicParsing -Uri "http://127.0.0.1:$BlobPort" -TimeoutSec 2 -Method GET | Out-Null
    Write-Ok "Azurite Blob endpoint ready at http://127.0.0.1:$BlobPort"
    break
  } catch { Start-Sleep -Milliseconds 500 }
}

Write-Ok "Azurite started. Use this connection string in local.settings.json and scripts:"
$conn = "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFe7Z+Zf2n0NRZ6IFSuFZfVZgZ6r5hAZdZ4hZ0Z0Z0Z0Z0Z0Z0Z0Z0Z0Z0Z==;BlobEndpoint=http://127.0.0.1:$BlobPort/devstoreaccount1;QueueEndpoint=http://127.0.0.1:$QueuePort/devstoreaccount1;TableEndpoint=http://127.0.0.1:$TablePort/devstoreaccount1"
Write-Host $conn -ForegroundColor Gray
