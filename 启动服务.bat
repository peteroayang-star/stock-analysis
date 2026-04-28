@echo off
echo 启动 AKShare Python 服务...
start "AKShare服务" python "D:\创新项目\通达信 + 风险评分系统\akshare_server.py"

echo 等待 Python 服务启动...
timeout /t 3 /nobreak >nul

echo 启动 Web 应用...
start "StockAnalysis Web" cmd /k "cd /d \"D:\创新项目\通达信 + 风险评分系统\StockAnalysis.Web\" && dotnet run"

echo.
echo 服务启动中...
echo AKShare: http://127.0.0.1:5100
echo Web应用: http://localhost:5110
pause
