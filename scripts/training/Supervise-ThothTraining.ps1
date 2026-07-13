param(
    [string]$RunId = "manual-local-run",
    [int]$PollSeconds = 30,
    [switch]$Once
)

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
Set-Location $repoRoot
$runDir = Join-Path "data/runs" $RunId
$statePath = Join-Path $runDir "supervisor-state.json"
$lockPath = Join-Path $runDir "run.lock"
$stopPath = Join-Path $runDir "STOP_REQUESTED"

function Read-State {
    if (Test-Path $statePath) {
        return Get-Content $statePath -Raw | ConvertFrom-Json
    }
    return [pscustomobject]@{ runId = $RunId; status = "unknown" }
}

function Write-State($state) {
    $state | ConvertTo-Json -Depth 10 | Set-Content -Path $statePath -Encoding UTF8
}

do {
    $state = Read-State
    $pidValue = $null
    if (Test-Path $lockPath) {
        $pidValue = [int](Get-Content $lockPath -Raw)
    }

    $alive = $false
    if ($pidValue) {
        $alive = [bool](Get-Process -Id $pidValue -ErrorAction SilentlyContinue)
    }

    $latestMetric = $null
    $trainLog = Join-Path $runDir "train.jsonl"
    if (Test-Path $trainLog) {
        $lastLine = Get-Content $trainLog -Tail 1
        if ($lastLine) {
            $latestMetric = $lastLine | ConvertFrom-Json
        }
    }

    $status = if ((Test-Path $stopPath) -and -not $alive) { "stopped" } elseif (Test-Path $stopPath) { "stopping" } elseif ($alive) { "running" } else { "exited" }
    $state | Add-Member -Force NoteProperty status $status
    $state | Add-Member -Force NoteProperty trainingPid $pidValue
    $state | Add-Member -Force NoteProperty processAlive $alive
    $state | Add-Member -Force NoteProperty lastUpdateUtc ((Get-Date).ToUniversalTime().ToString("o"))
    $state | Add-Member -Force NoteProperty freeDiskBytes ([int64](Get-PSDrive -Name C).Free)
    $state | Add-Member -Force NoteProperty availableRamBytes ([int64]((Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory * 1024))

    if ($latestMetric) {
        $state | Add-Member -Force NoteProperty lastStep $latestMetric.step
        $state | Add-Member -Force NoteProperty lastLoss $latestMetric.loss
        $state | Add-Member -Force NoteProperty tokensPerSecond $latestMetric.tokensPerSecond
        $state | Add-Member -Force NoteProperty managedMemoryBytes $latestMetric.managedMemoryBytes
        if ([double]::IsNaN([double]$latestMetric.loss) -or [double]::IsInfinity([double]$latestMetric.loss)) {
            $state | Add-Member -Force NoteProperty status "failed"
            $state | Add-Member -Force NoteProperty failureReason "non-finite loss"
            if ($pidValue) { Stop-Process -Id $pidValue -Force -ErrorAction SilentlyContinue }
        }
    }

    $latestCheckpoint = Get-ChildItem (Join-Path $runDir "checkpoints") -Directory -Filter "step-*" -ErrorAction SilentlyContinue |
        Sort-Object Name |
        Select-Object -Last 1
    if ($latestCheckpoint) {
        $state | Add-Member -Force NoteProperty latestCheckpoint $latestCheckpoint.FullName
    }

    Write-State $state
    if (-not $alive -and $pidValue -and (Test-Path $lockPath)) {
        Remove-Item -LiteralPath $lockPath -Force -ErrorAction SilentlyContinue
    }

    if ($Once -or (-not $alive -and $pidValue)) { break }
    Start-Sleep -Seconds $PollSeconds
} while ($true)
