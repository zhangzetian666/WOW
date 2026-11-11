param([switch]$Package)
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework compiler not found.' }
$bin = Join-Path $projectRoot 'bin'
New-Item -ItemType Directory -Path $bin -Force | Out-Null
$common = @('/nologo','/codepage:65001','/langversion:5','/optimize+','/r:System.Drawing.dll','/r:System.Windows.Forms.dll','/r:System.Web.Extensions.dll')
$sources = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' | ForEach-Object { $_.FullName })
& $compiler @common /target:winexe "/win32manifest:$projectRoot\src\app.manifest" "/out:$bin\WoWProbe.exe" @sources
if ($LASTEXITCODE -ne 0) { throw 'Application compilation failed.' }
& $compiler @common /target:exe "/out:$bin\CoreTests.exe" "$projectRoot\src\Core.cs" "$projectRoot\tests\CoreTests.cs"
if ($LASTEXITCODE -ne 0) { throw 'Core test compilation failed.' }
& $compiler @common /target:exe "/out:$bin\NativeTests.exe" "$projectRoot\src\Core.cs" "$projectRoot\src\Native.cs" "$projectRoot\tests\NativeTests.cs"
if ($LASTEXITCODE -ne 0) { throw 'Native test compilation failed.' }
& "$bin\CoreTests.exe"
if ($LASTEXITCODE -ne 0) { throw 'Core tests failed.' }
& "$bin\NativeTests.exe"
if ($LASTEXITCODE -ne 0) { throw 'Native tests failed.' }
$routeSources = @("$projectRoot\src\Core.cs","$projectRoot\src\Routes.cs","$projectRoot\src\Navigation.cs","$projectRoot\src\RouteSession.cs")
foreach ($testName in @('RoutesTests','NavigationTests','RouteSessionTests')) {
    & $compiler @common /target:exe "/out:$bin\$testName.exe" @routeSources "$projectRoot\tests\$testName.cs"
    if ($LASTEXITCODE -ne 0) { throw "$testName compilation failed." }
    & "$bin\$testName.exe"
    if ($LASTEXITCODE -ne 0) { throw "$testName failed." }
}
$farmSources = $routeSources + @("$projectRoot\src\CombatTelemetry.cs","$projectRoot\src\BattleProfiles.cs","$projectRoot\src\Farming.cs","$projectRoot\src\FarmSimulator.cs","$projectRoot\src\FarmSession.cs")
foreach ($testName in @('CombatTelemetryTests','BattleProfileTests','FarmingTests','FarmSessionTests')) {
    & $compiler @common /target:exe "/out:$bin\$testName.exe" @farmSources "$projectRoot\tests\$testName.cs"
    if ($LASTEXITCODE -ne 0) { throw "$testName compilation failed." }
    & "$bin\$testName.exe"
    if ($LASTEXITCODE -ne 0) { throw "$testName failed." }
}
& $compiler @common /target:winexe /main:WoWProbe.Smoke "/win32manifest:$projectRoot\src\app.manifest" "/out:$bin\IntegrationTests.exe" @sources "$projectRoot\tests\Smoke.cs"
if ($LASTEXITCODE -ne 0) { throw 'Integration test compilation failed.' }
& $compiler @common /target:winexe /main:RouteUiTests "/win32manifest:$projectRoot\src\app.manifest" "/out:$bin\RouteUiTests.exe" @sources "$projectRoot\tests\RouteUiTests.cs"
if ($LASTEXITCODE -ne 0) { throw 'Route UI test compilation failed.' }
& $compiler @common /target:winexe /main:FarmUiTests "/win32manifest:$projectRoot\src\app.manifest" "/out:$bin\FarmUiTests.exe" @sources "$projectRoot\tests\FarmUiTests.cs"
if ($LASTEXITCODE -ne 0) { throw 'Farm UI test compilation failed.' }
Write-Output "BUILD_OK $bin\WoWProbe.exe"
if ($Package) {
    $dist = Join-Path $projectRoot 'dist'
    $release = Join-Path $dist 'WoWProbe-Phase3'
    New-Item -ItemType Directory -Path $release -Force | Out-Null
    Copy-Item -LiteralPath "$bin\WoWProbe.exe" -Destination $release -Force
    Copy-Item -LiteralPath "$projectRoot\README.md" -Destination "$release\使用说明.txt" -Force
    Copy-Item -LiteralPath "$projectRoot\docs\实机测试记录.csv" -Destination $release -Force
    Copy-Item -LiteralPath "$projectRoot\docs\verification.md" -Destination "$release\开发侧验证记录.md" -Force
    Copy-Item -LiteralPath "$projectRoot\docs\phase2-testing.md" -Destination "$release\路线录制与巡航说明.txt" -Force
    Copy-Item -LiteralPath "$projectRoot\docs\phase3-testing.md" -Destination "$release\战斗与恢复说明.txt" -Force
    Copy-Item -LiteralPath "$projectRoot\addon\WoWProbe" -Destination $release -Recurse -Force
    $routeExamples = Join-Path $release 'routes\examples'
    New-Item -ItemType Directory -Path $routeExamples -Force | Out-Null
    Copy-Item -Path "$projectRoot\routes\examples\*.json" -Destination $routeExamples -Force
    $profileExamples = Join-Path $release 'profiles\examples'
    New-Item -ItemType Directory -Path $profileExamples -Force | Out-Null
    Copy-Item -Path "$projectRoot\profiles\examples\*.json" -Destination $profileExamples -Force
    $zip = Join-Path $dist 'WoWProbe-Phase3.zip'
    Compress-Archive -LiteralPath $release -DestinationPath $zip -Force
    (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash | Set-Content -LiteralPath "$zip.sha256" -Encoding ASCII
    Write-Output "PACKAGE_OK $zip"
}
