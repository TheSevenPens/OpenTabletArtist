@echo off
title Driver UX Experiment

echo Starting .NET Bridge...
start "Bridge" cmd /c "cd /d %~dp0bridge && dotnet run"

echo Starting Svelte Frontend...
start "Frontend" cmd /c "cd /d %~dp0frontend && call %USERPROFILE%\miniconda3\condabin\conda.bat activate node1 && npm run dev"

echo.
echo  Bridge:   http://localhost:5000
echo  Frontend: http://localhost:5188
echo.
echo Close this window to stop both servers.
pause
