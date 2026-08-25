@echo off
set "PATH=C:\Program Files\dotnet;C:\Program Files (x86)\dotnet;%PATH%"
cd /d "%~dp0src\DrmcPatientPortal"
dotnet run
