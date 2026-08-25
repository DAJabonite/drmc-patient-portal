# Runs the DRMC Patient Portal
$projectDir = Join-Path $PSScriptRoot "src\DrmcPatientPortal"
Set-Location $projectDir
dotnet run
