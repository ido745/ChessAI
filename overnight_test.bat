@echo off
echo Running overnight match (50 games, tc=120+3)...
C:\Users\yairm\cutechess-1.5.1-win64\cutechess-cli.exe ^
  -engine cmd="C:\Users\yairm\ChessAI\UCI\engines\New\ChessEngineUCI.exe" name=New proto=uci ponder ^
  -engine cmd="C:\Users\yairm\ChessAI\UCI\engines\Old\ChessEngineUCI_Old.exe" name=Old proto=uci ^
  -each tc=120+3 ^
  -games 50 ^
  -concurrency 1 ^
  -repeat ^
  -resign movecount=5 score=800 ^
  -openings file="C:\Users\yairm\ChessAI\UCI\openings.pgn" format=pgn policy=round ^
  -pgnout "C:\Users\yairm\ChessAI\match.pgn"
pause
