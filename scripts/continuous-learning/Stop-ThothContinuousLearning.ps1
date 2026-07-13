param(
    [string]$RunId = "continuous-local",
    [int]$WaitSeconds = 60
)

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
Set-Location $repoRoot
dotnet run --project src\Thoth.Cli -c Release --no-build -- continuous stop --run-id $RunId

$runDir = Join-Path "data/continuous/runs" $RunId
$lockPath = Join-Path $runDir "run.lock"
$deadline = (Get-Date).AddSeconds($WaitSeconds)
while ((Get-Date) -lt $deadline) {
    if (-not (Test-Path $lockPath)) {
        Write-Host "Continuous learning stopped cleanly."
        exit 0
    }
    Start-Sleep -Seconds 2
}

Write-Host "Stop was requested but the run lock still exists: $lockPath"
exit 1
