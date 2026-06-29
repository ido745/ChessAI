@echo off
echo Building...
dotnet publish UCI/ChessEngineUCI.csproj -c Release -r win-x64 --self-contained -o UCI/bin/publish
if errorlevel 1 (echo BUILD FAILED & pause & exit /b 1)

echo Copying...
copy /Y UCI\bin\publish\ChessEngineUCI.exe UCI\engines\New\ChessEngineUCI.exe
copy /Y UCI\bin\publish\ChessEngineUCI.dll UCI\engines\New\ChessEngineUCI.dll
if not exist UCI\bin\publish\Openings mkdir UCI\bin\publish\Openings
xcopy /Y /Q Assets\Resources\Openings\*.txt UCI\bin\publish\Openings\
if not exist UCI\engines\New\Openings mkdir UCI\engines\New\Openings
xcopy /Y /Q Assets\Resources\Openings\*.txt UCI\engines\New\Openings\

echo Running match...
C:\Users\yairm\cutechess-1.5.1-win64\cutechess-cli.exe -engine cmd="C:\Users\yairm\ChessAI\UCI\engines\New\ChessEngineUCI.exe" name=New proto=uci ponder -engine cmd="C:\Users\yairm\ChessAI\UCI\engines\Old\ChessEngineUCI_Old.exe" name=Old proto=uci -each tc=30+0 -games 30 -concurrency 1 -resign movecount=5 score=800 -openings file="C:\Users\yairm\ChessAI\UCI\openings.pgn" format=pgn policy=round -pgnout "C:\Users\yairm\ChessAI\match.pgn"
