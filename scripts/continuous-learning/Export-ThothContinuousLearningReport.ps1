param(
    [string]$RunId = "continuous-local"
)

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
Set-Location $repoRoot
dotnet run --project src\Thoth.Cli -c Release --no-build -- continuous report --run-id $RunId
