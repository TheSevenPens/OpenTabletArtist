@echo off
echo Stopping Bridge...
taskkill /FI "WINDOWTITLE eq Bridge*" /F >nul 2>&1
taskkill /IM Bridge.exe /F >nul 2>&1

echo Stopping Frontend...
taskkill /FI "WINDOWTITLE eq Frontend*" /F >nul 2>&1

echo Done.
