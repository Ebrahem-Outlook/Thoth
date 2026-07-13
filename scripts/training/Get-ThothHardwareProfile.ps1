param(
    [string]$OutputPath = "data/runs/hardware-profile.json"
)

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
Set-Location $repoRoot

New-Item -ItemType Directory -Force (Split-Path $OutputPath -Parent) | Out-Null

$cliJson = dotnet run --project src\Thoth.Cli -c Release --no-build -- hardware inspect --json
$cli = $cliJson | ConvertFrom-Json
$os = Get-CimInstance Win32_OperatingSystem
$cpu = Get-CimInstance Win32_Processor | Select-Object -First 1
$drive = Get-PSDrive -Name C

$profile = [ordered]@{
    capturedUtc = (Get-Date).ToUniversalTime().ToString("o")
    cpuName = [string]$cli.cpuName
    physicalCores = if ($cpu.NumberOfCores) { [int]$cpu.NumberOfCores } else { 0 }
    logicalCores = [int]$cli.logicalCpuCores
    totalRamBytes = [int64]$os.TotalVisibleMemorySize * 1024
    availableRamBytes = [int64]$os.FreePhysicalMemory * 1024
    freeDiskBytes = [int64]$drive.Free
    cpuArchitecture = [string]$cli.architecture
    torchBackend = [string]$cli.torch.device
    cudaAvailable = [bool]$cli.torch.cudaAvailable
    processMemoryBytes = [int64](Get-Process -Id $PID).WorkingSet64
}

$profile | ConvertTo-Json -Depth 8 | Set-Content -Path $OutputPath -Encoding UTF8
Write-Host "Hardware profile written to $OutputPath"
