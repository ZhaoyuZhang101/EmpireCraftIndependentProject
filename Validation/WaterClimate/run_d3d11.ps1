param(
 [Parameter(Mandatory=$true)][string]$UnityEditor,
 [string]$ProjectPath = (Resolve-Path (Join-Path $PSScriptRoot "../..")),
 [string]$LogPath = (Join-Path $PSScriptRoot "unity-d3d11.log")
)
$ErrorActionPreference = "Stop"
if (!(Test-Path $UnityEditor)) { throw "Unity Editor executable was not found: $UnityEditor" }
$arguments = @("-batchmode", "-force-d3d11", "-projectPath", $ProjectPath,
 "-executeMethod", "Orbis.Editor.FlatWaterClimateValidation.RunD3D11Batch", "-logFile", $LogPath)
# Intentionally NOT -nographics: it would not verify the reported D3D11 failure.
& $UnityEditor @arguments
$code = $LASTEXITCODE
if ($code -ne 0) { throw "Unity validation failed ($code). Read $LogPath" }
Write-Host "Unity exited successfully. Read Validation/WaterClimate/unity-native-report.json for the API and scope."
