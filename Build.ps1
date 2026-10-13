[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$BizHawkDir)
$ErrorActionPreference = 'Stop'
$taskInstall = (Resolve-Path -LiteralPath $BizHawkDir).Path
& dotnet build (Join-Path $PSScriptRoot 'src\GlitchForge.csproj') -c Release --nologo "-p:BizHawkDir=$taskInstall"
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'src\bin\Release\net48\GlitchForge.dll') -Destination (Join-Path $PSScriptRoot 'GlitchForge.dll') -Force
& dotnet build (Join-Path $PSScriptRoot 'launcher\GlitchForge.Launcher.csproj') -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Launcher build failed.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'launcher\bin\Release\net48\GlitchForge.exe') -Destination (Join-Path $PSScriptRoot 'GlitchForge.exe') -Force
Write-Host 'Built GlitchForge.exe and GlitchForge.dll. Run the EXE to install and launch.'
