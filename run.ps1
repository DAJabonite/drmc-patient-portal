# Ensure dotnet is in PATH for the current session if not already loaded
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    $dotnetPaths = @(
        "C:\Program Files\dotnet",
        "C:\Program Files (x86)\dotnet",
        "$env:USERPROFILE\.dotnet"
    )
    foreach ($p in $dotnetPaths) {
        if (Test-Path "$p\dotnet.exe") {
            $env:PATH = "$p;$env:PATH"
            break
        }
    }
}

$projectDir = Join-Path $PSScriptRoot "src\DrmcPatientPortal"
Set-Location $projectDir
dotnet run
