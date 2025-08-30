@echo off
echo Starting Azurite and Azure Functions...
echo.

REM Create azurite directory if it doesn't exist
if not exist "azurite" mkdir azurite

echo Starting Azurite in background...
start "Azurite" cmd /c "azurite --silent --location azurite --debug azurite\debug.log"

REM Wait for Azurite to start
timeout /t 3 /nobreak >nul

echo Starting Azure Functions...
echo Press Ctrl+C to stop the functions
echo.

func start
