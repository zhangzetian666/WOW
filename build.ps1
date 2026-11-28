param([switch]$Package,[switch]$QuickCheck,[string]$Python)
$ErrorActionPreference='Stop'
if($Package -and $QuickCheck){throw 'QuickCheck does not create a package.'}
& (Join-Path $PSScriptRoot 'build-fishing.ps1') -Package:$Package -TestOnly:$QuickCheck -Python $Python
