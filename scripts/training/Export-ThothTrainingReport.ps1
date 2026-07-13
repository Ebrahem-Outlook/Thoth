param(
    [string]$RunId = "manual-local-run"
)

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
Set-Location $repoRoot
$runDir = Join-Path "data/runs" $RunId
$statePath = Join-Path $runDir "supervisor-state.json"
$proofPath = Join-Path $runDir "learning-proof.json"
$reportPath = Join-Path $runDir "TRAINING_REPORT.md"

$state = if (Test-Path $statePath) { Get-Content $statePath -Raw | ConvertFrom-Json } else { $null }
$proof = if (Test-Path $proofPath) { Get-Content $proofPath -Raw | ConvertFrom-Json } else { $null }

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add("# Thoth Training Report")
$lines.Add("")
$generatedUtc = (Get-Date).ToUniversalTime().ToString("o")
$lines.Add("- Run ID: ``$RunId``")
$lines.Add("- Generated UTC: ``$generatedUtc``")
if ($state) {
    $lines.Add("- Status: ``$($state.status)``")
    $lines.Add("- Last step: ``$($state.lastStep)``")
    $lines.Add("- Last loss: ``$($state.lastLoss)``")
    $lines.Add("- Tokens/sec: ``$($state.tokensPerSecond)``")
    $lines.Add("- Latest checkpoint: ``$($state.latestCheckpoint)``")
}
if ($proof) {
    $lines.Add("- Parameters: ``$($proof.parameterCount)``")
    $lines.Add("- Corpus tokens: ``$($proof.corpusTokens)``")
    $lines.Add("- Loss delta: ``$($proof.lossDelta)``")
}
$lines.Add("")
$lines.Add("Raw checkpoints, logs, and generated samples remain local under `data/runs/` and are not committed.")

$lines | Set-Content -Path $reportPath -Encoding UTF8
Write-Host "Report written to $reportPath"
