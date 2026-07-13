param(
    [string]$RunId = "manual-local-run"
)

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
Set-Location $repoRoot
$runDir = Join-Path "data/runs" $RunId
$runJson = Join-Path $runDir "run.json"

if (-not (Test-Path $runJson)) {
    throw "Cannot resume: run.json not found for $RunId"
}

$run = Get-Content $runJson -Raw | ConvertFrom-Json
$latestCheckpoint = Get-ChildItem (Join-Path $runDir "checkpoints") -Directory -Filter "step-*" -ErrorAction SilentlyContinue |
    Sort-Object Name |
    Select-Object -Last 1
if (-not $latestCheckpoint) {
    throw "Cannot resume: no checkpoint found for $RunId"
}

Write-Host "Latest checkpoint: $($latestCheckpoint.FullName)"
Write-Host "Relaunching from latest checkpoint with the same config."
& (Join-Path $PSScriptRoot "Start-ThothTraining.ps1") `
    -RunId "$RunId-resume-$(Get-Date -Format yyyyMMddHHmmss)" `
    -DataPath $run.dataPath `
    -Steps $run.steps `
    -Context $run.context `
    -Layers $run.layers `
    -Width $run.width `
    -Heads $run.heads `
    -Ffn $run.ffn `
    -ResumeCheckpoint (Join-Path $latestCheckpoint.FullName "model.bin")
