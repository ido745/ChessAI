@echo off
REM ── compare_replay.bat ────────────────────────────────────────────────────
REM Builds two binaries: one with the bug fixes, one without (unfixed SearchEngine.cs).
REM Then runs the replay tool on both so you can see if the blunder is reproduced
REM on the unfixed engine and fixed on the new one.
REM
REM Usage:  .\compare_replay.bat  path\to\game.pgn
REM ─────────────────────────────────────────────────────────────────────────

if "%1"=="" (
    echo Usage: compare_replay.bat path\to\game.pgn
    pause & exit /b 1
)
set PGN=%1

echo ====================================================
echo  Step 1: Building UNFIXED engine (reverting SearchEngine.cs)
echo ====================================================
git stash push -- "Assets/Scripts/AI scripts/SearchEngine.cs" > nul 2>&1
if errorlevel 1 (
    echo ERROR: git stash failed. Make sure you are in a git repo.
    pause & exit /b 1
)

dotnet publish UCI/ChessEngineUCI.csproj -c Release -r win-x64 --self-contained -o UCI/bin/unfixed > nul 2>&1
if errorlevel 1 (
    echo BUILD FAILED (unfixed engine^)
    git stash pop > nul 2>&1
    pause & exit /b 1
)
if not exist UCI\bin\unfixed\Openings mkdir UCI\bin\unfixed\Openings > nul 2>&1
xcopy /Y /Q Assets\Resources\Openings\*.txt UCI\bin\unfixed\Openings\ > nul

echo Built unfixed engine.
echo.

echo ====================================================
echo  Step 2: Restoring fixes and building FIXED engine
echo ====================================================
git stash pop > nul 2>&1
if errorlevel 1 (
    echo ERROR: git stash pop failed.
    pause & exit /b 1
)

dotnet publish UCI/ChessEngineUCI.csproj -c Release -r win-x64 --self-contained -o UCI/bin/publish > nul 2>&1
if errorlevel 1 (
    echo BUILD FAILED (fixed engine^)
    pause & exit /b 1
)
if not exist UCI\bin\publish\Openings mkdir UCI\bin\publish\Openings > nul 2>&1
xcopy /Y /Q Assets\Resources\Openings\*.txt UCI\bin\publish\Openings\ > nul

echo Built fixed engine.
echo.

echo ====================================================
echo  UNFIXED ENGINE — replay (look for Rd2 on move 25)
echo ====================================================
UCI\bin\unfixed\ChessEngineUCI.exe --replay %PGN%

echo.
echo ====================================================
echo  FIXED ENGINE — replay (should play Bxd4 on move 25)
echo ====================================================
UCI\bin\publish\ChessEngineUCI.exe --replay %PGN%

pause
