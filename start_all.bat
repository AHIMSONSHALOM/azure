@echo off
echo =======================================================
echo ProductHub AI Discovery Platform - Startup Sequence
echo =======================================================

echo.
echo [1/3] Setting up Python Environment for AI Services...
cd python_services
if not exist "venv" (
    echo Creating virtual environment...
    python -m venv venv
    call venv\Scripts\activate.bat
    echo Installing requirements...
    pip install -r requirements.txt
) else (
    call venv\Scripts\activate.bat
)

echo.
echo [2/3] Starting FastAPI (Ollama Chatbot Backend)...
start "ProductHub AI Service" cmd /k "python ai_api.py"

cd ..

echo.
echo [3/3] Starting ASP.NET Core MVC Frontend...
start "ProductHub Web App" cmd /k "dotnet run"

echo.
echo =======================================================
echo ALL SERVICES STARTED SUCCESSFULLY!
echo =======================================================
echo.
echo The AI Service is running on http://localhost:8000
echo The Web App will be available shortly...
echo.
echo Press any key to open the application in your browser.
pause > nul
start http://localhost:5207
