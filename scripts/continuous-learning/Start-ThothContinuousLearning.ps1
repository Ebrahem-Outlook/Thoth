param(
    [string]$RunId = "continuous-local",
    [string]$Config = "config/continuous-learning/sources.json",
    [string]$TokenizerPath = "data/tokenizers/local-bpe-8k",
    [int]$StopAfterTokens = 0,
    [double]$StopAfterHours = 0,
    [switch]$Offline,
    [switch]$Rehearsal,
    [double]$MaxCpuPercent = 96,
    [double]$RamFloorGb = 2,
    [double]$DiskFloorGb = 25,
    [double]$SpoolMaxGb = 1,
    [int]$TrainerThreads = [Math]::Max(1, [Environment]::ProcessorCount - 2),
    [int]$IngestWorkers = 1,
    [int]$StepsPerCycle = 1,
    [int]$Context = 64,
    [int]$Layers = 1,
    [int]$Width = 64,
    [int]$Heads = 4,
    [int]$Ffn = 256
)

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
Set-Location $repoRoot

$runDir = Join-Path "data/continuous/runs" $RunId
$lockPath = Join-Path $runDir "run.lock"
$stdout = Join-Path $runDir "stdout.log"
$stderr = Join-Path $runDir "stderr.log"
$commandPath = Join-Path $runDir "start-command.txt"
$settingsPath = Join-Path $runDir "run-settings.json"

if (Test-Path $lockPath) {
    throw "Continuous learning lock exists: $lockPath"
}

New-Item -ItemType Directory -Force $runDir | Out-Null
$freeDisk = (Get-PSDrive -Name C).Free
if ($freeDisk -lt ([int64]($DiskFloorGb * 1GB))) {
    throw "Refusing to start: free disk is below requested floor of $DiskFloorGb GB."
}

$argsList = @(
    "run", "--project", "src\Thoth.Cli", "-c", "Release", "--no-build", "--",
    "continuous", "start",
    "--run-id", $RunId,
    "--config", $Config,
    "--tokenizer", $TokenizerPath,
    "--stop-after-tokens", "$StopAfterTokens",
    "--stop-after-hours", "$StopAfterHours",
    "--max-cpu-percent", "$MaxCpuPercent",
    "--ram-floor-gb", "$RamFloorGb",
    "--disk-floor-gb", "$DiskFloorGb",
    "--spool-max-gb", "$SpoolMaxGb",
    "--trainer-threads", "$TrainerThreads",
    "--ingest-workers", "$IngestWorkers",
    "--steps-per-cycle", "$StepsPerCycle",
    "--context", "$Context",
    "--layers", "$Layers",
    "--width", "$Width",
    "--heads", "$Heads",
    "--ffn", "$Ffn",
    "--no-model-growth"
)
if ($Offline) { $argsList += "--offline" }
if ($Rehearsal) { $argsList += "--rehearsal" }

Set-Content -Path $commandPath -Encoding UTF8 -Value ("dotnet " + ($argsList -join " "))

$settings = [ordered]@{
    runId = $RunId
    config = $Config
    tokenizerPath = $TokenizerPath
    stopAfterTokens = $StopAfterTokens
    stopAfterHours = $StopAfterHours
    offline = [bool]$Offline
    rehearsal = [bool]$Rehearsal
    maxCpuPercent = $MaxCpuPercent
    ramFloorGb = $RamFloorGb
    diskFloorGb = $DiskFloorGb
    spoolMaxGb = $SpoolMaxGb
    trainerThreads = $TrainerThreads
    ingestWorkers = $IngestWorkers
    stepsPerCycle = $StepsPerCycle
    context = $Context
    layers = $Layers
    width = $Width
    heads = $Heads
    ffn = $Ffn
}
$settings | ConvertTo-Json -Depth 8 | Set-Content -Path $settingsPath -Encoding UTF8

$process = Start-Process -FilePath "dotnet" -ArgumentList $argsList -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru -WindowStyle Hidden

$supervisorArgs = @(
    "-NoProfile", "-ExecutionPolicy", "Bypass",
    "-File", (Join-Path $PSScriptRoot "Supervise-ThothContinuousLearning.ps1"),
    "-RunId", $RunId
)
$supervisor = Start-Process -FilePath "powershell" -ArgumentList $supervisorArgs -PassThru -WindowStyle Hidden

Write-Host "Started continuous learning run $RunId"
Write-Host "Orchestrator PID: $($process.Id)"
Write-Host "Supervisor PID: $($supervisor.Id)"
Write-Host "Status: scripts\continuous-learning\Get-ThothContinuousLearningStatus.ps1 -RunId $RunId"
Write-Host "Stop: scripts\continuous-learning\Stop-ThothContinuousLearning.ps1 -RunId $RunId"
Write-Host "Resume: scripts\continuous-learning\Resume-ThothContinuousLearning.ps1 -RunId $RunId"
