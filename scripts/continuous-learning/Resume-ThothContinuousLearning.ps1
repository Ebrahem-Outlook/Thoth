param(
    [string]$RunId = "continuous-local"
)

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
Set-Location $repoRoot
$runDir = Join-Path "data/continuous/runs" $RunId
$stopPath = Join-Path $runDir "STOP_REQUESTED"
if (Test-Path $stopPath) {
    Remove-Item -LiteralPath $stopPath -Force
}

$settingsPath = Join-Path $runDir "run-settings.json"
if (Test-Path $settingsPath) {
    $settings = Get-Content $settingsPath -Raw | ConvertFrom-Json
    $args = @{
        RunId = $RunId
        Config = $settings.config
        TokenizerPath = $settings.tokenizerPath
        StopAfterTokens = [int]$settings.stopAfterTokens
        StopAfterHours = [double]$settings.stopAfterHours
        MaxCpuPercent = [double]$settings.maxCpuPercent
        RamFloorGb = [double]$settings.ramFloorGb
        DiskFloorGb = [double]$settings.diskFloorGb
        SpoolMaxGb = [double]$settings.spoolMaxGb
        TrainerThreads = [int]$settings.trainerThreads
        IngestWorkers = [int]$settings.ingestWorkers
        StepsPerCycle = [int]$settings.stepsPerCycle
        Context = [int]$settings.context
        Layers = [int]$settings.layers
        Width = [int]$settings.width
        Heads = [int]$settings.heads
        Ffn = [int]$settings.ffn
    }
    if ($settings.offline) { $args["Offline"] = $true }
    if ($settings.rehearsal) { $args["Rehearsal"] = $true }
    & (Join-Path $PSScriptRoot "Start-ThothContinuousLearning.ps1") @args
} else {
    & (Join-Path $PSScriptRoot "Start-ThothContinuousLearning.ps1") -RunId $RunId
}
