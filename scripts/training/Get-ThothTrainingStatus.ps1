param(
    [string]$RunId = "manual-local-run"
)

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
Set-Location $repoRoot
$statePath = Join-Path "data/runs/$RunId" "supervisor-state.json"

if (-not (Test-Path $statePath)) {
    throw "No supervisor state found for run $RunId"
}

$state = Get-Content $statePath -Raw | ConvertFrom-Json
Write-Host "Run: $($state.runId)"
Write-Host "Status: $($state.status)"
Write-Host "PID: $($state.trainingPid)"
Write-Host "Step: $($state.lastStep)"
Write-Host "Loss: $($state.lastLoss)"
Write-Host "Tokens/sec: $($state.tokensPerSecond)"
Write-Host "RAM available: $($state.availableRamBytes)"
Write-Host "Free disk: $($state.freeDiskBytes)"
Write-Host "Checkpoint: $($state.latestCheckpoint)"
Write-Host "Updated: $($state.lastUpdateUtc)"
