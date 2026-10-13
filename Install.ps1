[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$BizHawkDir)
$ErrorActionPreference = 'Stop'
$taskInstall = (Resolve-Path -LiteralPath $BizHawkDir).Path
if (-not (Test-Path -LiteralPath (Join-Path $taskInstall 'EmuHawk.exe'))) {
    throw 'Choose the extracted BizHawk 2.11.1 folder containing EmuHawk.exe.'
}
$taskPlugin = Join-Path $PSScriptRoot 'GlitchForge.dll'
if (-not (Test-Path -LiteralPath $taskPlugin)) { throw 'GlitchForge.dll is missing from this package.' }
$taskTools = Join-Path $taskInstall 'ExternalTools'
New-Item -ItemType Directory -Force -Path $taskTools | Out-Null
Copy-Item -LiteralPath $taskPlugin -Destination (Join-Path $taskTools 'GlitchForge.dll') -Force
Write-Host 'Installed. Load a ROM in BizHawk, then choose Tools > External Tools > GlitchForge.'
Write-Host 'Tested with stock BizHawk 2.11.1 on Windows. RTC-modified builds may need recompilation.'
