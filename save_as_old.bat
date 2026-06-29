@echo off
echo Building current SearchEngine.cs as the OLD engine...
dotnet publish UCI/ChessEngineUCI.csproj -c Release -r win-x64 --self-contained -o UCI/bin/publish
if errorlevel 1 (echo BUILD FAILED & pause & exit /b 1)

if not exist UCI\engines\Old mkdir UCI\engines\Old
copy /Y UCI\bin\publish\ChessEngineUCI.exe UCI\engines\Old\ChessEngineUCI_Old.exe
copy /Y UCI\bin\publish\ChessEngineUCI.dll UCI\engines\Old\ChessEngineUCI.dll
if not exist UCI\engines\Old\Openings mkdir UCI\engines\Old\Openings
xcopy /Y /Q Assets\Resources\Openings\*.txt UCI\engines\Old\Openings\ > nul

echo.
echo Saved to engines\Old\  -- now paste in the FIXED SearchEngine.cs and run rebuild_and_test.bat
pause
