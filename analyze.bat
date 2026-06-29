@echo off
dotnet publish UCI/ChessEngineUCI.csproj -c Release -r win-x64 --self-contained -o UCI/bin/publish > nul 2>&1
if errorlevel 1 (echo BUILD FAILED & pause & exit /b 1)
if not exist UCI\bin\publish\Openings mkdir UCI\bin\publish\Openings
xcopy /Y /Q Assets\Resources\Openings\*.txt UCI\bin\publish\Openings\ > nul

if "%1"=="replay" (
    UCI\bin\publish\ChessEngineUCI.exe --replay %2 %3
) else if "%1"=="eval-compare" (
    UCI\bin\publish\ChessEngineUCI.exe --eval-compare %2
) else (
    UCI\bin\publish\ChessEngineUCI.exe --analyze
)
