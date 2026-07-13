param(
    [string]$RunId = "manual-local-run",
    [int]$WaitSeconds = 20
)

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
Set-Location $repoRoot
$runDir = Join-Path "data/runs" $RunId
$lockPath = Join-Path $runDir "run.lock"
$stopPath = Join-Path $runDir "STOP_REQUESTED"

function Close-ProcessMainWindow {
    param([System.Diagnostics.Process]$Process)
    if ($Process.MainWindowHandle -ne 0) {
        return $Process.CloseMainWindow()
    }
    return $false
}

New-Item -ItemType File -Force $stopPath | Out-Null
if (-not (Test-Path $lockPath)) {
    Write-Host "No lock found for run $RunId"
    exit 0
}

$pidValue = [int](Get-Content $lockPath -Raw)
$process = Get-Process -Id $pidValue -ErrorAction SilentlyContinue
if (-not $process) {
    Write-Host "Process is already stopped."
    exit 0
}

Close-ProcessMainWindow -Process $process | Out-Null
$process.WaitForExit($WaitSeconds * 1000) | Out-Null
if (-not $process.HasExited) {
    Stop-Process -Id $pidValue -Force
}
Write-Host "Stop requested for run $RunId"
