param([switch]$SkipBrowserInstall)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repositoryRoot
try {
    # Release avoids locking the user's usual Debug development server.
    dotnet build DrmcPatientPortal.slnx -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
    if (-not $SkipBrowserInstall) {
        & "$repositoryRoot/tests/DrmcPatientPortal.BrowserTests/bin/Release/net10.0/playwright.ps1" install chromium firefox webkit
        if ($LASTEXITCODE -ne 0) { throw 'Playwright browser installation failed.' }
    }
    dotnet test DrmcPatientPortal.slnx -c Release --no-build --logger trx --results-directory output/playwright/test-results
    if ($LASTEXITCODE -ne 0) { throw 'Validation failed; inspect output/playwright and the test results.' }
}
finally {
    Pop-Location
}
