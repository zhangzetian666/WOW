param([switch]$TestOnly,[switch]$Package)
$ErrorActionPreference='Stop'
$root=$PSScriptRoot
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$bin=Join-Path $root 'bin'
New-Item -ItemType Directory -Path $bin -Force | Out-Null
$sources=@(Get-ChildItem -LiteralPath (Join-Path $root 'src') -Filter '*.cs' | ForEach-Object FullName)
$common=@('/nologo','/codepage:65001','/langversion:5','/optimize+','/r:System.Drawing.dll','/r:System.Windows.Forms.dll','/r:System.Web.Extensions.dll','/r:System.IO.Compression.dll','/r:System.IO.Compression.FileSystem.dll')
foreach($test in @('FishingTests','FishingSimulatorTests','FishingSessionTests','FishingAudioFormatTests','FishingPerceptionTests','FishingExtrasTests')) {
    $testFile=Join-Path $root ('tests\'+$test+'.cs')
    & $compiler @common /target:exe ('/main:'+$test) ('/out:'+(Join-Path $bin ($test+'.exe'))) @sources (Join-Path $root 'tests\FishingFixtures.cs') $testFile
    if($LASTEXITCODE -ne 0){throw "$test compilation failed"}
    Push-Location $root
    try { & (Join-Path $bin ($test+'.exe')); if($LASTEXITCODE -ne 0){throw "$test failed"} } finally {Pop-Location}
}
if($TestOnly){Write-Output 'FISHING_TESTS_OK';return}
& $compiler @common /target:winexe /main:WoWProbe.FishingProgram ('/win32manifest:'+(Join-Path $root 'src\app.manifest')) ('/out:'+(Join-Path $bin 'WoWFishing.exe')) @sources
if($LASTEXITCODE -ne 0){throw 'Fishing client compilation failed'}
if($Package){
    $output=Join-Path $root 'dist\WoWFishing'
    New-Item -ItemType Directory -Path $output -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $bin 'WoWFishing.exe') -Destination $output -Force
    foreach($folder in @('assets','addon')){Copy-Item -LiteralPath (Join-Path $root $folder) -Destination $output -Recurse -Force}
    Copy-Item -LiteralPath (Join-Path $root 'README.md') -Destination $output -Force
    Compress-Archive -Path (Join-Path $output '*') -DestinationPath (Join-Path $root 'dist\WoWFishing-latest.zip') -Force
}
Write-Output 'FISHING_BUILD_OK'
