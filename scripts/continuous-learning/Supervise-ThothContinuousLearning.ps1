param(
    [string]$RunId = "continuous-local",
    [int]$PollSeconds = 10,
    [switch]$Once
)

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
Set-Location $repoRoot
$runDir = Join-Path "data/continuous/runs" $RunId
$statePath = Join-Path $runDir "supervisor-state.json"
$statusPath = Join-Path $runDir "status.json"
$lockPath = Join-Path $runDir "run.lock"

do {
    $pidValue = $null
    if (Test-Path $lockPath) {
        $pidValue = [int](Get-Content $lockPath -Raw)
    }
    $alive = $false
    if ($pidValue) {
        $alive = [bool](Get-Process -Id $pidValue -ErrorAction SilentlyContinue)
    }
    $status = $null
    if (Test-Path $statusPath) {
        $status = Get-Content $statusPath -Raw | ConvertFrom-Json
    }
    $state = [ordered]@{
        runId = $RunId
        processAlive = $alive
        pid = $pidValue
        status = $(if ($status) { $status.state } elseif ($alive) { "starting" } else { "missing" })
        step = $(if ($status) { $status.step } else { $null })
        consumedTokens = $(if ($status) { $status.consumedTokens } else { $null })
        tokensPerSecond = $(if ($status) { $status.tokensPerSecond } else { $null })
        latestCheckpoint = $(if ($status) { $status.latestCheckpoint } else { $null })
        updatedUtc = (Get-Date).ToUniversalTime().ToString("o")
    }
    New-Item -ItemType Directory -Force $runDir | Out-Null
    $state | ConvertTo-Json -Depth 8 | Set-Content -Path $statePath -Encoding UTF8
    if ($Once -or (-not $alive -and $pidValue)) { break }
    Start-Sleep -Seconds $PollSeconds
} while ($true)
