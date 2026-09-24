param(
 [Parameter(Mandatory=$true)][string]$UnityEditor,
 [string]$ProjectPath = (Resolve-Path (Join-Path $PSScriptRoot "../..")),
 [string]$LogPath = (Join-Path $PSScriptRoot "unity-d3d11.log")
)
$ErrorActionPreference = "Stop"
if (!(Test-Path $UnityEditor)) { throw "Unity Editor executable was not found: $UnityEditor" }
$project = (Resolve-Path $ProjectPath).Path
$log = [System.IO.Path]::GetFullPath($LogPath)
$reportPath = Join-Path $project "Validation/Perf3_2/unity-native-report.json"
# Start-Process with -Wait is deliberate: a GUI executable must not return a
# premature shell success. Quote paths because ArgumentList joins its elements.
$arguments = @("-batchmode", "-force-d3d11", "-projectPath", ('"' + $project + '"'),
 "-executeMethod", "Orbis.Editor.FlatRenderValidation.RunD3D11Batch", "-logFile", ('"' + $log + '"'))
# Intentionally NOT -nographics: it would not verify the reported D3D11 failure.
$started = [DateTime]::UtcNow
$process = Start-Process -FilePath $UnityEditor -ArgumentList $arguments -Wait -PassThru
if ($process.ExitCode -ne 0) { throw "Unity validation failed ($($process.ExitCode)). Read $log" }
if (!(Test-Path $reportPath)) { throw "Unity produced no native report. Read $log" }
$report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
$reportTime = [DateTime]::Parse($report.utc).ToUniversalTime()
if ($reportTime -lt $started.AddSeconds(-2)) { throw "Native report is stale; this run did not produce a new report. Read $log" }
if ($report.status -ne "PASS" -or $report.graphicsAPI -ne "Direct3D11" -or !$report.nativeShadersExecuted) {
 throw "Native report did not pass Direct3D11 validation. Read $reportPath and $log"
}
Write-Host "Native default-pass/core/UXML validation passed on Direct3D11. Play Mode and visual/FPS acceptance remain separate."
