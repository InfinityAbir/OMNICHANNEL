@echo off
setlocal

set "ROOT=%~dp0"
set "BACKEND_DIR=%ROOT%src\Omnichannel.Api"
set "FRONTEND_DIR=%ROOT%web"
set "PG_CONTAINER=omnichannel-postgres"
set "BACKEND_PORT=5068"
set "FRONTEND_PORT=4200"

echo Checking Docker...
docker version --format "{{.Server.Version}}" >nul 2>&1
if errorlevel 1 (
    echo Starting Docker Desktop...
    if not exist "C:\Program Files\Docker\Docker\Docker Desktop.exe" (
        echo ERROR: Docker Desktop is not installed at the expected location.
        goto :docker_failure
    )
    start "" "C:\Program Files\Docker\Docker\Docker Desktop.exe" >nul 2>&1
    echo Waiting for the Docker daemon...
    powershell -NoProfile -Command "$deadline = (Get-Date).AddSeconds(120); while ((Get-Date) -lt $deadline) { docker version --format '{{.Server.Version}}' *> $null; if ($LASTEXITCODE -eq 0) { exit 0 }; Start-Sleep -Seconds 3 }; exit 1"
    if errorlevel 1 goto :docker_failure
)

docker version --format "{{.Server.Version}}" >nul 2>&1
if errorlevel 1 goto :docker_failure

echo Starting local Postgres (docker compose)...
docker compose -f "%ROOT%docker-compose.yml" up -d
if errorlevel 1 goto :container_failure

echo Waiting for Postgres to accept connections...
powershell -NoProfile -Command "$deadline = (Get-Date).AddSeconds(60); while ((Get-Date) -lt $deadline) { if (Test-NetConnection -ComputerName localhost -Port 5432 -InformationLevel Quiet -WarningAction SilentlyContinue) { break }; Start-Sleep -Seconds 2 }"
powershell -NoProfile -Command "if (-not (Test-NetConnection -ComputerName localhost -Port 5432 -InformationLevel Quiet -WarningAction SilentlyContinue)) { exit 1 }"
if errorlevel 1 (
    echo ERROR: Postgres did not accept connections on localhost:5432.
    docker logs --tail 30 %PG_CONTAINER%
    goto :failed
)

echo Freeing backend/frontend ports in case a previous run is still holding them
echo (avoids stale-build file locks so this always builds the latest code)...
for %%P in (%BACKEND_PORT% 7184 %FRONTEND_PORT%) do (
    powershell -NoProfile -Command "Get-NetTCPConnection -LocalPort %%P -State Listen -ErrorAction SilentlyContinue | Select-Object -ExpandProperty OwningProcess -Unique | ForEach-Object { Stop-Process -Id $_ -Force -ErrorAction SilentlyContinue }"
)
taskkill /F /IM Omnichannel.Api.exe >nul 2>&1

echo Starting backend (Omnichannel API)...
start "Omnichannel-Backend" cmd /k "cd /d "%BACKEND_DIR%" && set ASPNETCORE_ENVIRONMENT=Development && set ASPNETCORE_URLS=http://localhost:%BACKEND_PORT% && dotnet run"

echo Starting frontend (Angular inbox UI)...
start "Omnichannel-Frontend" cmd /k "cd /d "%FRONTEND_DIR%" && npm start"

echo.
echo Postgres:  localhost:5432   (container %PG_CONTAINER%, database omnichannel)
echo Backend:   http://localhost:%BACKEND_PORT%
echo Frontend:  http://localhost:%FRONTEND_PORT%
echo.
echo Waiting for the frontend dev server to come up...
powershell -NoProfile -Command "$deadline = (Get-Date).AddSeconds(120); while ((Get-Date) -lt $deadline) { if (Test-NetConnection -ComputerName localhost -Port %FRONTEND_PORT% -InformationLevel Quiet -WarningAction SilentlyContinue) { break }; Start-Sleep -Seconds 2 }"

echo Opening the frontend in your browser...
start "" "http://localhost:%FRONTEND_PORT%"

echo.
echo Both services are launching in separate windows. Run stop.bat to stop them.

endlocal
exit /b 0

:docker_failure
echo.
echo ERROR: Docker Desktop is running but its daemon cannot be reached.
echo Open Docker Desktop, wait for it to show "Engine running", then run start.bat again.
echo If it still fails, run "docker version" in a normal PowerShell session to repair Docker access.
goto :failed

:container_failure
echo.
echo ERROR: Postgres container could not be started via docker compose. The error above identifies the cause.
goto :failed

:failed
endlocal
exit /b 1
