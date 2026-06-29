using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

// Disable stdout buffering so testing frameworks receive output immediately
Console.OutputEncoding = System.Text.Encoding.UTF8;
var stdout = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
Console.SetOut(stdout);

if (args.Length > 0 && args[0] == "--analyze")
{
    RunAnalysisTool();
    return;
}
if (args.Length >= 2 && args[0] == "--replay")
{
    int startMove = args.Length >= 3 && int.TryParse(args[2], out int m) ? m : 1;
    RunReplayTool(args[1], startMove);
    return;
}
if (args.Length >= 1 && args[0] == "--eval-compare")
{
    string posFile = args.Length >= 2 && args[1] != "" ? args[1] : "eval_positions.fen";
    // Also look next to the exe so it works from any working directory
    if (!File.Exists(posFile))
    {
        string nearExe = Path.Combine(AppContext.BaseDirectory, posFile);
        if (File.Exists(nearExe)) posFile = nearExe;
    }
    string sfPath  = args.Length >= 3 ? args[2]
                   : @"C:\Users\yairm\stockfish-windows-x86-64-avx2\stockfish\stockfish-windows-x86-64-avx2.exe";
    RunEvalCompareTool(posFile, sfPath);
    return;
}

// Load opening book before creating the real board (so board is the last Instance set)
string openingsFolder = Path.Combine(AppContext.BaseDirectory, "Openings");
OpeningBook book = OpeningBook.Load(openingsFolder, MoveToUCI);
Console.Error.WriteLine($"Opening book: {book.PositionCount} positions loaded.");

BoardLogic board = new BoardLogic();
SearchEngine engine = new SearchEngine(1000, new Evaluate());
engine.ResetAI();
engine.InfoCallback = Console.WriteLine;

// Output bestmove (with optional ponder move) when any timed search completes
engine.OnSearchComplete = (move) =>
{
    string ponderStr = IsValidMove(engine.PonderMove) ? $" ponder {MoveToUCI(engine.PonderMove)}" : "";
    Console.WriteLine($"bestmove {MoveToUCI(move)}{ponderStr}");
};

// Saved time params — reused by ponderhit to calculate move time
int savedWtime = -1, savedBtime = -1, savedWinc = 0, savedBinc = 0;
// UCI move history from last position command, for opening book lookup
string positionMoves = "";

string? line;
while ((line = Console.ReadLine()) != null)
{
    line = line.Trim();
    if (line == string.Empty) continue;

    if (line == "uci")
    {
        Console.WriteLine("id name ChessAI");
        Console.WriteLine("id author Ido");
        Console.WriteLine("option name Hash type spin default 64 min 1 max 512");
        Console.WriteLine("option name MoveTime type spin default 1000 min 100 max 30000");
        Console.WriteLine("option name Ponder type check default true");
        Console.WriteLine("uciok");
    }
    else if (line == "isready")
    {
        Console.WriteLine("readyok");
    }
    else if (line == "ucinewgame")
    {
        board = new BoardLogic();
        engine.ResetAI();
        board.positionCounter.Clear();
        board.positionHistory.Clear();
    }
    else if (line.StartsWith("position"))
    {
        ParsePosition(line, ref board, engine);
        positionMoves = ExtractMoves(line);
    }
    else if (line.StartsWith("go"))
    {
        if (!line.Contains("ponder"))
        {
            string? bookMove = book.TryGetMove(positionMoves);
            if (bookMove != null)
            {
                Console.WriteLine($"bestmove {bookMove}");
                continue;
            }
        }
        ParseGo(line, board, engine, ref savedWtime, ref savedBtime, ref savedWinc, ref savedBinc);
    }
    else if (line == "ponderhit")
    {
        // Check book first — if the ponder position is still in book, play instantly.
        string? ponderBookMove = book.TryGetMove(positionMoves);
        if (ponderBookMove != null)
        {
            engine.StopSearch();
            Console.WriteLine($"bestmove {ponderBookMove}");
        }
        else
        {
            int myTime = board.turn == 0 ? savedWtime : savedBtime;
            int myInc  = board.turn == 0 ? savedWinc  : savedBinc;
            const int OVERHEAD_MS = 200;
            int safeTime = Math.Max(myTime - OVERHEAD_MS, 0);
            int timeMs = myTime > 0 ? safeTime / 40 + (int)(myInc * 0.75) : 1000;
            timeMs = Math.Min(timeMs, safeTime / 10);
            timeMs = Math.Max(timeMs, 50);
            engine.PonderHit(timeMs);
        }
    }
    else if (line == "stop")
    {
        Move best = engine.StopSearch();
        if (IsValidMove(best))
        {
            string ponderStr = IsValidMove(engine.PonderMove) ? $" ponder {MoveToUCI(engine.PonderMove)}" : "";
            Console.WriteLine($"bestmove {MoveToUCI(best)}{ponderStr}");
        }
    }
    else if (line == "quit")
    {
        engine.StopSearch();
        break;
    }
    else if (line.StartsWith("setoption"))
    {
        // options advertised but not yet wired up — ignore safely
    }
}

// ─── Position ───────────────────────────────────────────────────────────────

static void ParsePosition(string command, ref BoardLogic board, SearchEngine engine)
{
    // Formats:
    //   position startpos
    //   position startpos moves e2e4 e7e5 ...
    //   position fen <FEN> moves ...

    string[] tokens = command.Split(' ');
    int idx = 1;

    if (idx >= tokens.Length) return;

    if (tokens[idx] == "startpos")
    {
        board = new BoardLogic();
        board.positionCounter.Clear();
        board.positionHistory.Clear();
        idx++;
    }
    else if (tokens[idx] == "fen")
    {
        idx++;
        // FEN is up to 6 space-separated fields, terminated by "moves" or end
        int fenStart = idx;
        while (idx < tokens.Length && tokens[idx] != "moves") idx++;
        string fen = string.Join(" ", tokens, fenStart, idx - fenStart);

        board = new BoardLogic();
        board.positionCounter.Clear();
        board.positionHistory.Clear();
        board.ParseFEN(fen);
        board.attackCalculator.FindPinsAndChecks(board.turn);
        board.attackCalculator.UpdateAttacksMap(0);
        board.attackCalculator.UpdateAttacksMap(1);
    }

    if (idx < tokens.Length && tokens[idx] == "moves")
    {
        idx++;
        while (idx < tokens.Length)
        {
            ApplyUCIMove(tokens[idx], board);
            idx++;
        }
    }
}

static void ApplyUCIMove(string uciMove, BoardLogic board)
{
    if (uciMove.Length < 4) return;

    int from = ParseSquare(uciMove[..2]);
    int to   = ParseSquare(uciMove[2..4]);
    int promotionType = uciMove.Length == 5 ? ParsePromotionType(uciMove[4]) : -1;

    Move[] moves = new Move[256];
    int count = board.moveCalculator.GenerateAllMoves(moves, board.turn);

    for (int i = 0; i < count; i++)
    {
        Move m = moves[i];
        if (m.from != from || m.to != to) continue;

        // For promotions, match the piece type
        if (m.flag == (int)MoveFlag.Promotion)
        {
            if (promotionType == -1 || Piece.GetPieceType(m.promotionPiece) != promotionType)
                continue;
        }

        board.moveExecuter.MakeMove(m);
        return;
    }
}

// ─── Go ─────────────────────────────────────────────────────────────────────

static void ParseGo(string command, BoardLogic board, SearchEngine engine,
                    ref int savedWtime, ref int savedBtime, ref int savedWinc, ref int savedBinc)
{
    string[] tokens = command.Split(' ');
    int timeMs = -1;
    int wtime = -1, btime = -1, winc = 0, binc = 0;
    bool isPonder = false;

    for (int i = 1; i < tokens.Length; i++)
    {
        switch (tokens[i])
        {
            case "ponder":   isPonder = true; break;
            case "movetime" when i + 1 < tokens.Length: timeMs = int.Parse(tokens[i + 1]); break;
            case "wtime"    when i + 1 < tokens.Length: wtime  = int.Parse(tokens[i + 1]); break;
            case "btime"    when i + 1 < tokens.Length: btime  = int.Parse(tokens[i + 1]); break;
            case "winc"     when i + 1 < tokens.Length: winc   = int.Parse(tokens[i + 1]); break;
            case "binc"     when i + 1 < tokens.Length: binc   = int.Parse(tokens[i + 1]); break;
        }
    }

    // Save time params so ponderhit can use them later
    savedWtime = wtime; savedBtime = btime;
    savedWinc  = winc;  savedBinc  = binc;

    if (isPonder)
    {
        engine.StartPondering(board);
        return;
    }

    if (timeMs == -1)
    {
        int myTime = board.turn == 0 ? wtime : btime;
        int myInc  = board.turn == 0 ? winc  : binc;
        if (myTime > 0)
        {
            const int OVERHEAD_MS = 200; // reserve for network + lichess-bot Python + move transmission
            int safeTime = Math.Max(myTime - OVERHEAD_MS, 0);
            // /40 instead of /30: budget for 40 more moves (safer in blitz/bullet)
            timeMs = safeTime / 40 + (int)(myInc * 0.75);
            // Never burn more than 10% of remaining clock on one move
            timeMs = Math.Min(timeMs, safeTime / 10);
            timeMs = Math.Max(timeMs, 50); // always think at least 50ms
        }
        else
        {
            timeMs = 1000;
        }
    }

    engine.StartSearch(board, timeMs);
    // bestmove is output via engine.OnSearchComplete when the search finishes
}

// ─── Helpers ────────────────────────────────────────────────────────────────

static int ParseSquare(string sq)
{
    int file = sq[0] - 'a';
    int rank = sq[1] - '1';
    return rank * 8 + file;
}

static string MoveToUCI(Move move)
{
    string result = $"{(char)('a' + move.from % 8)}{move.from / 8 + 1}" +
                    $"{(char)('a' + move.to   % 8)}{move.to   / 8 + 1}";

    if (move.flag == (int)MoveFlag.Promotion)
    {
        result += Piece.GetPieceType(move.promotionPiece) switch
        {
            Piece.Queen  => "q",
            Piece.Rook   => "r",
            Piece.Bishop => "b",
            Piece.Knight => "n",
            _ => "q"
        };
    }

    return result;
}

static int ParsePromotionType(char c) => c switch
{
    'q' => Piece.Queen,
    'r' => Piece.Rook,
    'b' => Piece.Bishop,
    'n' => Piece.Knight,
    _   => Piece.Queen
};

static bool IsValidMove(Move m) => m.from != m.to;

static string ExtractMoves(string command)
{
    int idx = command.IndexOf(" moves ", StringComparison.Ordinal);
    return idx < 0 ? "" : command[(idx + 7)..].Trim();
}

// ─── Analysis Tool ───────────────────────────────────────────────────────────

static void RunAnalysisTool()
{
    const string SEP  = "─────────────────────────────────────────────────";
    const string SEP2 = "═════════════════════════════════════════════════";
    const int TOP_N = 5;
    const string START_FEN = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    Console.WriteLine("╔═════════════════════════════════════╗");
    Console.WriteLine("║     Chess Engine Analysis Tool      ║");
    Console.WriteLine("╚═════════════════════════════════════╝");
    Console.WriteLine();

    Console.Write("FEN (Enter for start): ");
    string fenInput = Console.ReadLine()?.Trim() ?? "";
    string fen = string.IsNullOrEmpty(fenInput) ? START_FEN : fenInput;

    Console.Write("Seconds to think: ");
    if (!int.TryParse(Console.ReadLine()?.Trim(), out int seconds) || seconds < 1)
        seconds = 5;

    int totalMs  = seconds * 1000;
    int perMoveMs = totalMs / TOP_N;

    // Set up board
    BoardLogic board = new BoardLogic();
    board.ParseFEN(fen);
    board.attackCalculator.FindPinsAndChecks(board.turn);
    board.attackCalculator.UpdateAttacksMap(0);
    board.attackCalculator.UpdateAttacksMap(1);

    // Print board
    Console.WriteLine();
    Console.WriteLine(SEP);
    PrintBoard(board);
    Console.WriteLine($"  Turn: {(board.turn == 0 ? "White" : "Black")}");

    // Static evaluation (no search)
    var evalOnly = new Evaluate();
    int staticEval = evalOnly.GetScore(board) * (board.turn == 0 ? 1 : -1);
    Console.WriteLine($"  Static eval: {FormatScore(staticEval)}");
    Console.WriteLine(SEP);
    Console.WriteLine();

    var engine = new SearchEngine(perMoveMs, new Evaluate());
    engine.ResetAI();

    var results = new List<(string san, string uci, int score, int depth, int seldepth, float ttHit, float nps, string pv)>();

    for (int i = 0; i < TOP_N; i++)
    {
        Console.WriteLine($"  Candidate {i + 1}/{TOP_N} — {perMoveMs}ms");
        Console.WriteLine(SEP);

        // Print depth-by-depth progress
        engine.InfoCallback = line =>
        {
            // Reformat the info line for readability
            string[] parts = line.Split(' ');
            string depth   = GetToken(parts, "depth");
            string selD    = GetToken(parts, "seldepth");
            string scoreStr = FormatInfoScore(parts);
            string nodes   = GetToken(parts, "nodes");
            string npsStr  = GetToken(parts, "nps");
            Console.WriteLine($"    depth {depth,2}  seldepth {selD,2}  score {scoreStr,12}  nodes {nodes,9}  nps {npsStr}");
        };

        Move best = engine.GetBestMove(board, perMoveMs);

        if (!IsValidMove(best)) break;

        string san = board.MoveToSAN(best);
        string uci = MoveToUCI(best);
        int score  = engine.LastBestScore;
        int dep    = engine.LastDepthReached;
        int selDep = engine.SelDepth;
        float tt   = engine.LastTtHitRate;
        float nps  = engine.LastNps;
        string pv  = engine.GetPVLine();

        results.Add((san, uci, score, dep, selDep, tt, nps, pv));

        Console.WriteLine(SEP);
        Console.WriteLine($"  → {san} ({uci})   {FormatScore(score)}   depth {dep}   seldepth {selDep}");
        Console.WriteLine($"    TT hit rate: {tt:F1}%   NPS: {nps:N0}");
        Console.WriteLine($"    PV: {pv}");
        Console.WriteLine();

        engine.ExcludeRootMove(best.from, best.to);
    }

    engine.ClearRootExclusions();

    // Summary
    Console.WriteLine(SEP2);
    Console.WriteLine("  SUMMARY");
    Console.WriteLine(SEP2);
    for (int i = 0; i < results.Count; i++)
    {
        var r = results[i];
        string arrow = i == 0 ? "★" : " ";
        Console.WriteLine($"  {arrow} {i + 1}. {r.san,-8} {FormatScore(r.score),14}   depth {r.depth}");
        Console.WriteLine($"       PV: {r.pv}");
    }
    Console.WriteLine(SEP2);
    Console.WriteLine();
}

static void PrintBoard(BoardLogic board)
{
    // Piece type constants: King=1,Pawn=2,Knight=3,Bishop=4,Rook=5,Queen=6
    char[] pieceChars = { ' ', 'K', 'P', 'N', 'B', 'R', 'Q' };
    Console.WriteLine();
    for (int rank = 7; rank >= 0; rank--)
    {
        Console.Write($"  {rank + 1} ");
        for (int file = 0; file < 8; file++)
        {
            int sq = rank * 8 + file;
            int piece = board.board[sq];
            if (piece == 0) { Console.Write(". "); continue; }
            char c = pieceChars[Piece.GetPieceType(piece)];
            Console.Write(Piece.IsBlack(piece) == 1 ? char.ToLower(c) + " " : c + " ");
        }
        Console.WriteLine();
    }
    Console.WriteLine("    a b c d e f g h");
    Console.WriteLine();
}

static string FormatScore(int score)
{
    if (score >  90000) return $"+M{(100000 - score + 1) / 2}";
    if (score < -90000) return $"-M{(100000 + score) / 2}";
    return $"{score / 100.0:+0.00;-0.00} pawns";
}

static string FormatInfoScore(string[] parts)
{
    int mateIdx = Array.IndexOf(parts, "mate");
    if (mateIdx >= 0 && mateIdx + 1 < parts.Length)
        return $"mate {parts[mateIdx + 1]}";
    int cpIdx = Array.IndexOf(parts, "cp");
    if (cpIdx >= 0 && cpIdx + 1 < parts.Length && int.TryParse(parts[cpIdx + 1], out int cp))
        return FormatScore(cp);
    return "?";
}

static string GetToken(string[] parts, string key)
{
    int idx = Array.IndexOf(parts, key);
    return (idx >= 0 && idx + 1 < parts.Length) ? parts[idx + 1] : "?";
}

// ─── Replay Tool ─────────────────────────────────────────────────────────────
// Replays a PGN game move by move.  At each position where it's the engine's
// turn (White), we run a real search with warm TT (same as during the game)
// and compare what the engine chooses with what was actually played.

static void RunReplayTool(string pgnFile, int searchFromMove = 1)
{
    const string SEP = "─────────────────────────────────────────────────";

    if (!File.Exists(pgnFile))
    {
        Console.WriteLine($"File not found: {pgnFile}");
        return;
    }

    string pgn = File.ReadAllText(pgnFile);
    string[] sanMoves = ExtractPGNMoves(pgn);
    if (sanMoves.Length == 0)
    {
        Console.WriteLine("No moves found in PGN.");
        return;
    }

    Console.Write("Seconds per move for replay (Enter for 6): ");
    if (!int.TryParse(Console.ReadLine()?.Trim(), out int seconds) || seconds < 1) seconds = 6;
    int timeMs = seconds * 1000;

    // Load opening book — same as UCI mode so book moves are skipped (no TT warming),
    // exactly replicating real game conditions.
    string openingsFolder = Path.Combine(AppContext.BaseDirectory, "Openings");
    OpeningBook book = OpeningBook.Load(openingsFolder, MoveToUCI);
    Console.Error.WriteLine($"Opening book: {book.PositionCount} positions loaded.");

    Console.WriteLine();
    Console.WriteLine($"Replaying {sanMoves.Length} moves from: {Path.GetFileName(pgnFile)}");
    Console.WriteLine(SEP);

    BoardLogic board = new BoardLogic();
    board.positionCounter.Clear();
    board.positionHistory.Clear();

    var engine = new SearchEngine(timeMs, new Evaluate());
    engine.ResetAI();
    engine.InfoCallback = _ => {};  // suppress per-depth output

    int moveNum = 1;
    bool white = true;
    int mismatches = 0;
    string positionMoves = ""; // UCI move history for book lookup

    foreach (string san in sanMoves)
    {
        string cleanSan = san.TrimEnd('+', '#');
        Move? gameMove = FindMoveFromSAN(board, cleanSan);

        if (gameMove == null)
        {
            Console.WriteLine($"  Could not parse move: {san}  (stopping)");
            break;
        }

        string label = $"{moveNum}{(white ? "." : "...")}";

        if (white)
        {
            bool skipSearch = moveNum < searchFromMove || book.TryGetMove(positionMoves) != null;

            if (skipSearch)
            {
                string reason = moveNum < searchFromMove ? $"pre-search (from move {searchFromMove})" : "book";
                Console.WriteLine($"  {label,-8} actual={san,-10} [{reason}]");
            }
            else
            {
                Move engineMove = engine.GetBestMove(board, timeMs);
                string engineSan = IsValidMove(engineMove) ? board.MoveToSAN(engineMove) : "?";
                int engineScore = engine.LastBestScore;
                int engineDepth = engine.LastDepthReached;

                bool match = engineSan.TrimEnd('+', '#') == cleanSan;
                string flag = match ? "" : "  ◄ DIFFERS";
                if (!match) mismatches++;

                Console.WriteLine($"  {label,-8} actual={san,-10} engine={engineSan,-10} " +
                                  $"score={FormatScore(engineScore),14}  depth={engineDepth}{flag}");
            }
        }

        // Update UCI history for book lookups (both colours)
        string gameMoveUci = MoveToUCI((Move)gameMove);
        positionMoves = positionMoves.Length == 0 ? gameMoveUci : positionMoves + " " + gameMoveUci;

        board.moveExecuter.MakeMove((Move)gameMove);
        if (!white) moveNum++;
        white = !white;
    }

    Console.WriteLine(SEP);
    Console.WriteLine($"  Done. {mismatches} mismatch(es) out of {(sanMoves.Length + 1) / 2} engine moves.");
}

static string[] ExtractPGNMoves(string pgn)
{
    // Strip header lines
    var lines = pgn.Split('\n');
    var body = string.Join(" ", lines.Where(l => !l.TrimStart().StartsWith("[")));

    // Remove annotations in braces and NAG symbols
    body = Regex.Replace(body, @"\{[^}]*\}", "");
    body = Regex.Replace(body, @"\$\d+", "");

    // Remove result tokens
    body = Regex.Replace(body, @"(1-0|0-1|1/2-1/2|\*)", "");

    // Tokenise; drop move numbers (e.g. "1." "2..." "10.")
    var tokens = body.Split(new[] {' ', '\t', '\r', '\n'}, StringSplitOptions.RemoveEmptyEntries)
                     .Where(t => !Regex.IsMatch(t, @"^\d+\.+$"))
                     .ToArray();
    return tokens;
}

static Move? FindMoveFromSAN(BoardLogic board, string san)
{
    Move[] moves = new Move[256];
    int count = board.moveCalculator.GenerateAllMoves(moves, board.turn);
    for (int i = 0; i < count; i++)
    {
        string ms = board.MoveToSAN(moves[i]).TrimEnd('+', '#');
        if (ms == san) return moves[i];
    }
    return null;
}

// ─── Eval Compare Tool ────────────────────────────────────────────────────────
// Compares our static evaluation against Stockfish's across a set of test
// positions and reports the top 10 positions with the largest discrepancy,
// plus a per-theme breakdown so you can see which strategic concept we mis-eval.

static void RunEvalCompareTool(string positionsFile, string stockfishPath)
{
    const string SEP2 = "═════════════════════════════════════════════════════════════════";

    if (!File.Exists(positionsFile))
    { Console.WriteLine($"Positions file not found: {positionsFile}"); return; }
    if (!File.Exists(stockfishPath))
    { Console.WriteLine($"Stockfish not found: {stockfishPath}"); return; }

    var fens = ReadFenPositions(positionsFile);
    Console.WriteLine($"Loaded {fens.Count} positions from {Path.GetFileName(positionsFile)}");
    Console.WriteLine("Starting Stockfish...");

    Process? sf = StartStockfishProcess(stockfishPath);
    if (sf == null) { Console.WriteLine("Failed to start Stockfish."); return; }

    var results = new List<(string fen, string desc, int ourEval, int sfEval, int diff, string phase)>();
    int skipped = 0;

    for (int i = 0; i < fens.Count; i++)
    {
        var (fen, desc) = fens[i];
        Console.Write($"\r  [{i + 1,3}/{fens.Count}] {desc[..Math.Min(40, desc.Length)]}...    ");

        // Our static eval — normalised to white's perspective (positive = white winning)
        BoardLogic board = new BoardLogic();
        board.ParseFEN(fen);
        board.attackCalculator.FindPinsAndChecks(board.turn);
        board.attackCalculator.UpdateAttacksMap(0);
        board.attackCalculator.UpdateAttacksMap(1);
        // GetScore() always returns from White's perspective (us=0 hardcoded).
        // Do NOT apply the turn flip here — Stockfish eval is also white's perspective,
        // and we need both on the same scale for a fair comparison.
        int ourEval = new Evaluate().GetScore(board);

        // Stockfish static eval (in centipawns, white's perspective)
        int sfEval = GetStockfishStaticEval(sf, fen);
        if (sfEval == int.MinValue) { skipped++; continue; }

        results.Add((fen, desc, ourEval, sfEval, sfEval - ourEval, GetPhaseLabel(board)));
    }

    try { sf.StandardInput.WriteLine("quit"); sf.WaitForExit(2000); } catch {}

    Console.WriteLine($"\r  Done. {results.Count} analyzed, {skipped} skipped.                        ");
    Console.WriteLine();

    // Sort by absolute discrepancy — worst first
    results.Sort((a, b) => Math.Abs(b.diff).CompareTo(Math.Abs(a.diff)));

    Console.WriteLine(SEP2);
    Console.WriteLine($"  STATIC EVAL COMPARISON — Top {Math.Min(10, results.Count)} Discrepancies");
    Console.WriteLine($"  Our Engine vs Stockfish  ({results.Count} positions analyzed)");
    Console.WriteLine(SEP2);
    Console.WriteLine();

    for (int i = 0; i < Math.Min(10, results.Count); i++)
    {
        var r = results[i];
        string dir = r.diff > 0
            ? $"SF says {r.diff}cp MORE in White's favour"
            : $"SF says {-r.diff}cp MORE in Black's favour";
        Console.WriteLine($"#{i + 1}  |diff| = {Math.Abs(r.diff),4}cp");
        Console.WriteLine($"    Our eval : {FormatScore(r.ourEval)}");
        Console.WriteLine($"    SF  eval : {FormatScore(r.sfEval)}");
        Console.WriteLine($"    {dir}");
        Console.WriteLine($"    Phase    : {r.phase}");
        Console.WriteLine($"    Desc     : {r.desc}");
        Console.WriteLine($"    FEN      : {r.fen}");

        // Redraw board for this position
        BoardLogic bd = new BoardLogic();
        bd.ParseFEN(r.fen);
        bd.attackCalculator.FindPinsAndChecks(bd.turn);
        bd.attackCalculator.UpdateAttacksMap(0);
        bd.attackCalculator.UpdateAttacksMap(1);
        PrintBoard(bd);
        Console.WriteLine();
    }

    // Phase breakdown + directional statistics
    Console.WriteLine(SEP2);
    Console.WriteLine("  STATISTICS BY GAME PHASE");
    Console.WriteLine(SEP2);
    Console.WriteLine($"  {"Phase",-14} | {"N",3} | {"Avg |err|",9} | {"Avg err (signed)",16} | {"Optimal ×",9}");
    Console.WriteLine($"  {"",14}   {"",3}   {"",9}   {"(+)=SF higher",16}   {"(least squares)",9}");
    Console.WriteLine($"  {new string('─', 65)}");

    foreach (string phaseName in new[] { "Opening", "Middlegame", "Endgame", "ALL" })
    {
        var group = phaseName == "ALL"
            ? results
            : results.Where(r => r.phase == phaseName).ToList();
        if (group.Count == 0) continue;

        // Avg |err|: magnitude of error regardless of direction
        double avgAbsDiff = group.Average(r => Math.Abs(r.diff));

        // Avg signed diff (sfEval - ourEval): positive = SF higher (we undervalue),
        // negative = we higher (we overvalue)
        double avgSignedDiff = group.Average(r => (double)r.diff);

        // Optimal multiplier via least-squares regression through the origin:
        // minimises sum((k * ourEval - sfEval)^2)  →  k = sum(our*sf) / sum(our^2)
        // Uses ALL positions (not just same-sign) so nothing cancels artificially.
        double sumXY = group.Sum(r => (double)r.ourEval * r.sfEval);
        double sumXX = group.Sum(r => (double)r.ourEval * r.ourEval);
        string scaleStr = sumXX > 1 ? $"{sumXY / sumXX:F3}×" : "n/a";

        string signStr = avgSignedDiff >= 0
            ? $"+{avgSignedDiff,5:F0}cp (SF>us)"
            : $"{avgSignedDiff,6:F0}cp (us>SF)";
        string label = phaseName == "ALL" ? "─── Overall ───" : phaseName;
        Console.WriteLine($"  {label,-14} | {group.Count,3} | {avgAbsDiff,6:F0}cp    | {signStr,-16}     | {scaleStr}");
    }

    Console.WriteLine(SEP2);
    Console.WriteLine();
    Console.WriteLine("  Avg err (signed): positive means SF scores the position higher (we undervalue).");
    Console.WriteLine("  Optimal ×: multiply our eval by this to minimise avg squared error vs SF (1.0 = perfect).");
}

static List<(string fen, string desc)> ReadFenPositions(string file)
{
    var result = new List<(string, string)>();
    int lineNum = 0;
    foreach (var rawLine in File.ReadAllLines(file))
    {
        lineNum++;
        int hashIdx = rawLine.IndexOf(" # ");
        string code = hashIdx >= 0 ? rawLine[..hashIdx].Trim() : rawLine.Trim();
        string desc = hashIdx >= 0 ? rawLine[(hashIdx + 3)..].Trim() : "position";

        if (string.IsNullOrEmpty(code) || code.StartsWith("#")) continue;

        var parts = code.Split(' ');
        if (parts.Length < 4 || !parts[0].Contains('/')) continue;

        // Validate: each side must have exactly 1 king, ≤16 pieces total
        string ranks = parts[0];
        int wKings = ranks.Count(c => c == 'K');
        int bKings = ranks.Count(c => c == 'k');
        int wPieces = ranks.Count(c => char.IsUpper(c));
        int bPieces = ranks.Count(c => char.IsLower(c));

        if (wKings != 1 || bKings != 1 || wPieces > 16 || bPieces > 16 || wPieces < 2 || bPieces < 2)
        {
            Console.Error.WriteLine(
                $"  [line {lineNum}] Skipping invalid FEN (W={wPieces} B={bPieces} kings W={wKings} B={bKings}): {desc}");
            continue;
        }

        string fen = parts.Length >= 6 ? code : code + " 0 1";
        result.Add((fen, desc));
    }
    return result;
}

// Phase based on remaining non-pawn/non-king material (mirrors GetGamePhase in Evaluate.cs)
// Knight=3, Bishop=4, Rook=5, Queen=6 per Piece.cs constants
static string GetPhaseLabel(BoardLogic board)
{
    int gp = 0;
    for (int sq = 0; sq < 64; sq++)
    {
        int p = board.board[sq];
        if (p == 0) continue;
        gp += Piece.GetPieceType(p) switch {
            3 => 3,  // Knight
            4 => 3,  // Bishop
            5 => 5,  // Rook
            6 => 9,  // Queen
            _ => 0
        };
    }
    return gp >= 50 ? "Opening" : gp >= 16 ? "Middlegame" : "Endgame";
}

static Process? StartStockfishProcess(string path)
{
    try
    {
        var sf = new Process();
        sf.StartInfo.FileName = path;
        sf.StartInfo.UseShellExecute = false;
        sf.StartInfo.RedirectStandardInput  = true;
        sf.StartInfo.RedirectStandardOutput = true;
        sf.StartInfo.RedirectStandardError  = true;
        sf.StartInfo.CreateNoWindow = true;
        sf.Start();
        sf.StandardInput.AutoFlush = true;

        sf.StandardInput.WriteLine("uci");
        string? line;
        while ((line = sf.StandardOutput.ReadLine()) != null)
            if (line == "uciok") break;
        return sf;
    }
    catch { return null; }
}

static int GetStockfishStaticEval(Process sf, string fen)
{
    sf.StandardInput.WriteLine($"position fen {fen}");
    sf.StandardInput.WriteLine("eval");
    sf.StandardInput.WriteLine("isready"); // sentinel — readyok arrives after eval output

    int score = int.MinValue;
    string? line;
    while ((line = sf.StandardOutput.ReadLine()) != null)
    {
        // "Final evaluation" (NNUE Stockfish) or "Total evaluation" (classical)
        // Example: "Final evaluation       +0.34 (white side)"
        // Skip lines with "none" (position in check or no eval)
        if (!line.Contains("none") &&
            (line.Contains("Final evaluation") || line.Contains("Total evaluation")))
        {
            var m = Regex.Match(line, @"([+-]?\d+(?:\.\d+)?)");
            if (m.Success && double.TryParse(m.Groups[1].Value,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out double val))
            {
                score = (int)(val * 100); // pawns → centipawns
            }
        }

        if (line == "readyok") break;
    }
    return score;
}
