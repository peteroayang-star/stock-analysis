@echo off
echo Starting AKShare Python service...
start "AKShare" python "%~dp0akshare_server.py"
echo Waiting for Python service...
timeout /t 3 /nobreak >nul
echo Starting Web app...
start "StockAnalysis" cmd /k "cd /d "%~dp0StockAnalysis.Web" && dotnet run"
echo.
echo AKShare: http://127.0.0.1:5100
echo Web: http://localhost:5110
pause
