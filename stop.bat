@echo off
setlocal

set "ROOT=%~dp0"
set "PG_CONTAINER=omnichannel-postgres"

echo Stopping Omnichannel backend and frontend...

taskkill /F /T /FI "WINDOWTITLE eq Omnichannel-Backend*"  >nul 2>&1
taskkill /F /T /FI "WINDOWTITLE eq Omnichannel-Frontend*" >nul 2>&1

rem Free the known ports (stops the actual dotnet/node server processes even if
rem the window-title match above missed them, e.g. window was renamed).
for %%P in (5068 7184 4200) do (
    powershell -NoProfile -Command "Get-NetTCPConnection -LocalPort %%P -State Listen -ErrorAction SilentlyContinue | Select-Object -ExpandProperty OwningProcess -Unique | ForEach-Object { Stop-Process -Id $_ -Force -ErrorAction SilentlyContinue }"
)

rem Close any leftover launcher console windows by matching their command line
rem (window-title matching is unreliable in some sessions, e.g. remote/non-interactive ones).
powershell -NoProfile -Command "Get-CimInstance Win32_Process | Where-Object { $_.Name -eq 'cmd.exe' -and ($_.CommandLine -match 'Omnichannel\.Api' -or $_.CommandLine -match '\\web\\') } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }"

echo Stopping local Postgres (%PG_CONTAINER%)...
docker compose -f "%ROOT%docker-compose.yml" stop >nul

echo Done.
endlocal
