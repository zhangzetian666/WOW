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
    & "$projectRoot\bin\OperationsTelemetryTests.exe" "$projectRoot\test-output\lua-operations-core.bin" "$projectRoot\test-output\lua-operations-combat.bin" "$projectRoot\test-output\lua-operations-packet.bin"
    if ($LASTEXITCODE -ne 0) { throw 'Lua to C# WO4 three-packet protocol or pixel geometry check failed.' }
    if (-not $SkipDesktopSmoke) {
        $captureReportPath = Join-Path $projectRoot 'bin\test-output\smoke-test.json'
        if (Test-Path -LiteralPath $captureReportPath) { Remove-Item -LiteralPath $captureReportPath -Force }
        $probe = Start-Process -FilePath "$projectRoot\bin\IntegrationTests.exe" -ArgumentList '--smoke-test' -WindowStyle Hidden -Wait -PassThru
        if ($probe.ExitCode -ne 0) { throw 'Actual screen capture smoke test failed. Inspect bin/test-output/smoke-error.txt.' }
        $captureReport = Get-Content -LiteralPath $captureReportPath -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($captureReport.screen_capture -ne $true) { throw 'Desktop capture is unavailable; a window-rendered fallback does not pass the actual screen-capture check. Inspect smoke-test.json or use -SkipDesktopSmoke and verify UI flows separately.' }
        $preview = Start-Process -FilePath "$projectRoot\bin\IntegrationTests.exe" -ArgumentList '--ui-preview' -WindowStyle Hidden -Wait -PassThru
        if ($preview.ExitCode -ne 0) { throw 'UI preview failed.' }
        & "$projectRoot\bin\QuickRouteUiTests.exe"
        if ($LASTEXITCODE -ne 0) { throw 'Current client route UI checks failed.' }

    }
    if ($SkipDesktopSmoke) {
        Write-Output 'VERIFICATION_OK: All C# domain suites including portable plans, checkpoints, feedback and preflight, Lua51, WP1/WC3/WO4 protocol passed. Desktop and current client UI checks were skipped. No real game was tested; stress duration is virtual.'
    } else {
        Write-Output 'VERIFICATION_OK: C# domain suites, Lua51, WP1/WC3/WO4 protocol, desktop capture and current client UI checks passed. No real game was tested; stress duration is virtual.'
    }
} finally { Pop-Location }
