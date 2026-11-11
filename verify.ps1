param([Parameter(Mandatory=$true)][string]$PythonPath,[switch]$SkipDesktopSmoke)
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
Push-Location $projectRoot
try {
    & "$projectRoot\build.ps1"
    & $PythonPath "$projectRoot\tests\run_addon_tests.py"
    if ($LASTEXITCODE -ne 0) { throw 'Lua 5.1 tests failed.' }
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    if (-not (Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
    & $compiler /nologo /codepage:65001 /langversion:5 /r:System.Drawing.dll "/out:$projectRoot\bin\CrossProtocolTests.exe" "$projectRoot\src\Core.cs" "$projectRoot\tests\CrossProtocolTests.cs"
    if ($LASTEXITCODE -ne 0) { throw 'Cross-language test compilation failed.' }
    & "$projectRoot\bin\CrossProtocolTests.exe" "$projectRoot\test-output\lua-packet.bin"
    if ($LASTEXITCODE -ne 0) { throw 'Lua to C# protocol test failed.' }
    & "$projectRoot\bin\CombatTelemetryTests.exe" "$projectRoot\test-output\lua-combat-core.bin" "$projectRoot\test-output\lua-combat-packet.bin"
    if ($LASTEXITCODE -ne 0) { throw 'Lua to C# WC3 matched-packet protocol test failed.' }
    if (-not $SkipDesktopSmoke) {
        $captureReportPath = Join-Path $projectRoot 'bin\test-output\smoke-test.json'
        if (Test-Path -LiteralPath $captureReportPath) { Remove-Item -LiteralPath $captureReportPath -Force }
        $probe = Start-Process -FilePath "$projectRoot\bin\IntegrationTests.exe" -ArgumentList '--smoke-test' -WindowStyle Hidden -Wait -PassThru
        if ($probe.ExitCode -ne 0) { throw 'Actual screen capture smoke test failed. Inspect bin/test-output/smoke-error.txt.' }
        $captureReport = Get-Content -LiteralPath $captureReportPath -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($captureReport.screen_capture -ne $true) { throw 'Desktop capture is unavailable; a window-rendered fallback does not pass the actual screen-capture check. Inspect smoke-test.json or use -SkipDesktopSmoke and verify UI flows separately.' }
        $preview = Start-Process -FilePath "$projectRoot\bin\IntegrationTests.exe" -ArgumentList '--ui-preview' -WindowStyle Hidden -Wait -PassThru
        if ($preview.ExitCode -ne 0) { throw 'UI preview failed.' }
        $routePreview = Start-Process -FilePath "$projectRoot\bin\IntegrationTests.exe" -ArgumentList '--route-ui-preview' -WindowStyle Hidden -Wait -PassThru
        if ($routePreview.ExitCode -ne 0) { throw 'Route UI preview failed.' }
        $routeFlow = Start-Process -FilePath "$projectRoot\bin\RouteUiTests.exe" -WindowStyle Hidden -Wait -PassThru
        if ($routeFlow.ExitCode -ne 0) { throw 'Offline route UI integration failed. Inspect bin/test-output/route-ui-tests.json.' }
        $farmPreview = Start-Process -FilePath "$projectRoot\bin\IntegrationTests.exe" -ArgumentList '--farm-ui-preview' -WindowStyle Hidden -Wait -PassThru
        if ($farmPreview.ExitCode -ne 0) { throw 'Farm UI preview failed.' }
        $farmFlow = Start-Process -FilePath "$projectRoot\bin\FarmUiTests.exe" -WindowStyle Hidden -Wait -PassThru
        if ($farmFlow.ExitCode -ne 0) { throw 'Offline farm UI integration failed. Inspect bin/test-output/farm-ui-tests.json.' }
    }
    if ($SkipDesktopSmoke) {
        Write-Output 'VERIFICATION_OK: C# domain suites, Lua51 and WP1/WC3 cross-language protocol passed. Desktop and UI checks were skipped. No real game was tested.'
    } else {
        Write-Output 'VERIFICATION_OK: C# domain suites, Lua51, WP1/WC3 protocol, desktop capture and route/farm UI checks passed. No real game was tested.'
    }
} finally { Pop-Location }
