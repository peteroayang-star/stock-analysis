@echo off
echo ============================================
echo   Stock Analysis System - Starting...
echo ============================================
echo.
echo [1/2] Starting AKShare data service (port 5100)...
start "AKShare-DataService" cmd /k "cd /d %~dp0 && python akshare_server.py"
echo       Waiting for data service to be ready...
powershell -Command "Start-Sleep -Seconds 8"

echo.
echo [2/2] Starting Web app (port 5110)...
start "StockAnalysis-Web" cmd /k "cd /d %~dp0StockAnalysis.Web && dotnet run"
echo       Waiting for Web app to be ready...
powershell -Command "Start-Sleep -Seconds 5"

echo.
echo ============================================
echo   Startup complete!
echo   Data service: http://127.0.0.1:5100
echo   Web app:      http://localhost:5110
echo.
echo   Make sure the AKShare window shows:
echo   "Running on http://127.0.0.1:5100"
echo   If not, check: pip install akshare flask
echo ============================================
pause
