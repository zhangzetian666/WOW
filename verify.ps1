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
    if (-not $SkipDesktopSmoke) {
        $probe = Start-Process -FilePath "$projectRoot\bin\IntegrationTests.exe" -ArgumentList '--smoke-test' -WindowStyle Hidden -Wait -PassThru
        if ($probe.ExitCode -ne 0) { throw 'Actual screen capture smoke test failed. Inspect bin/test-output/smoke-error.txt.' }
        $preview = Start-Process -FilePath "$projectRoot\bin\IntegrationTests.exe" -ArgumentList '--ui-preview' -WindowStyle Hidden -Wait -PassThru
        if ($preview.ExitCode -ne 0) { throw 'UI preview failed.' }
        $routePreview = Start-Process -FilePath "$projectRoot\bin\IntegrationTests.exe" -ArgumentList '--route-ui-preview' -WindowStyle Hidden -Wait -PassThru
        if ($routePreview.ExitCode -ne 0) { throw 'Route UI preview failed.' }
        $routeFlow = Start-Process -FilePath "$projectRoot\bin\RouteUiTests.exe" -WindowStyle Hidden -Wait -PassThru
        if ($routeFlow.ExitCode -ne 0) { throw 'Offline route UI integration failed. Inspect bin/test-output/route-ui-tests.json.' }
    }
    Write-Output 'VERIFICATION_OK: Core, native boundaries, Lua51 and cross-language protocol passed. No real game was tested.'
} finally { Pop-Location }
