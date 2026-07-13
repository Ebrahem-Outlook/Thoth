param(
    [string]$RunId = "manual-local-run",
    [string]$DataPath = "data/splits/instruction/train/learning-proof-owned.jsonl",
    [int]$Steps = 20,
    [int]$Context = 128,
    [int]$Layers = 2,
    [int]$Width = 128,
    [int]$Heads = 4,
    [int]$Ffn = 512,
    [string]$TokenizerPath = "",
    [int]$MaxCorpusTokens = 10000000,
    [int]$GradAccum = 1,
    [int]$CheckpointEvery = 1000,
    [string]$ResumeCheckpoint = ""
)

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
Set-Location $repoRoot

$runDir = Join-Path "data/runs" $RunId
$lockPath = Join-Path $runDir "run.lock"
$stdout = Join-Path $runDir "stdout.log"
$stderr = Join-Path $runDir "stderr.log"
$statePath = Join-Path $runDir "supervisor-state.json"
$commandPath = Join-Path $runDir "start-command.txt"

if (Test-Path $lockPath) {
    throw "Run lock exists: $lockPath"
}

New-Item -ItemType Directory -Force $runDir | Out-Null
New-Item -ItemType Directory -Force (Join-Path $runDir "samples") | Out-Null
New-Item -ItemType Directory -Force (Join-Path $runDir "evaluation") | Out-Null
New-Item -ItemType Directory -Force (Join-Path $runDir "checkpoints") | Out-Null

$freeDisk = (Get-PSDrive -Name C).Free
if ($freeDisk -lt 10GB) {
    throw "Refusing to start: free disk below 10GB."
}

$argsList = @(
    "run", "--project", "src\Thoth.Cli", "-c", "Release", "--no-build", "--",
    "model", "learning-proof",
    "--data", $DataPath,
    "--run-id", $RunId,
    "--run-dir", $runDir,
    "--steps", "$Steps",
    "--context", "$Context",
    "--layers", "$Layers",
    "--width", "$Width",
    "--heads", "$Heads",
    "--ffn", "$Ffn",
    "--max-corpus-tokens", "$MaxCorpusTokens",
    "--grad-accum", "$GradAccum",
    "--checkpoint-every", "$CheckpointEvery"
)
if ($TokenizerPath) {
    $argsList += @("--tokenizer", $TokenizerPath)
}
if ($ResumeCheckpoint) {
    $argsList += @("--resume-checkpoint", $ResumeCheckpoint)
}

Set-Content -Path $commandPath -Encoding UTF8 -Value ("dotnet " + ($argsList -join " "))

$metadata = [ordered]@{
    runId = $RunId
    status = "starting"
    createdUtc = (Get-Date).ToUniversalTime().ToString("o")
    dataPath = $DataPath
    runDirectory = (Resolve-Path $runDir).Path
    steps = $Steps
    context = $Context
    layers = $Layers
    width = $Width
    heads = $Heads
    ffn = $Ffn
    tokenizerPath = $TokenizerPath
    maxCorpusTokens = $MaxCorpusTokens
    gradAccum = $GradAccum
    checkpointEvery = $CheckpointEvery
    resumeCheckpoint = $ResumeCheckpoint
    freeDiskBytesAtStart = [int64]$freeDisk
}
$metadata | ConvertTo-Json -Depth 8 | Set-Content -Path (Join-Path $runDir "run.json") -Encoding UTF8
$metadata | ConvertTo-Json -Depth 8 | Set-Content -Path $statePath -Encoding UTF8

$process = Start-Process -FilePath "dotnet" -ArgumentList $argsList -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru -WindowStyle Hidden
Set-Content -Path $lockPath -Encoding UTF8 -Value $process.Id

$state = [ordered]@{}
foreach ($key in $metadata.Keys) {
    $state[$key] = $metadata[$key]
}
$state["status"] = "running"
$state["trainingPid"] = $process.Id
$state["lastUpdateUtc"] = (Get-Date).ToUniversalTime().ToString("o")
$state | ConvertTo-Json -Depth 8 | Set-Content -Path $statePath -Encoding UTF8

$supervisorArgs = @(
    "-NoProfile", "-ExecutionPolicy", "Bypass",
    "-File", (Join-Path $PSScriptRoot "Supervise-ThothTraining.ps1"),
    "-RunId", $RunId
)
$supervisor = Start-Process -FilePath "powershell" -ArgumentList $supervisorArgs -PassThru -WindowStyle Hidden

Write-Host "Started run $RunId"
Write-Host "Training PID: $($process.Id)"
Write-Host "Supervisor PID: $($supervisor.Id)"
Write-Host "Status: scripts\training\Get-ThothTrainingStatus.ps1 -RunId $RunId"
Write-Host "Stop: scripts\training\Stop-ThothTraining.ps1 -RunId $RunId"
Write-Host "Resume: scripts\training\Resume-ThothTraining.ps1 -RunId $RunId"
