using System;
using System.IO;

// Disable stdout buffering so testing frameworks receive output immediately
Console.OutputEncoding = System.Text.Encoding.UTF8;
var stdout = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
Console.SetOut(stdout);

BoardLogic board = new BoardLogic();
SearchEngine1 engine = new SearchEngine1(1000, new Evaluate1());
engine.ResetAI();
engine.InfoCallback = Console.WriteLine;

string? line;
while ((line = Console.ReadLine()) != null)
{
    line = line.Trim();
    if (line == string.Empty) continue;

    if (line == "uci")
    {
        Console.WriteLine("id name ChessAI-Old");
        Console.WriteLine("id author Yair");
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
    }
    else if (line.StartsWith("go"))
    {
        ParseGo(line, board, engine);
    }
    else if (line == "quit")
    {
        break;
    }
}

static void ParsePosition(string command, ref BoardLogic board, SearchEngine1 engine)
{
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

        if (m.flag == (int)MoveFlag.Promotion)
        {
            if (promotionType == -1 || Piece.GetPieceType(m.promotionPiece) != promotionType)
                continue;
        }

        board.moveExecuter.MakeMove(m);
        return;
    }
}

static void ParseGo(string command, BoardLogic board, SearchEngine1 engine)
{
    string[] tokens = command.Split(' ');
    int timeMs = -1;
    int wtime = -1, btime = -1, winc = 0, binc = 0;

    for (int i = 1; i < tokens.Length - 1; i++)
    {
        switch (tokens[i])
        {
            case "movetime": timeMs = int.Parse(tokens[i + 1]); break;
            case "wtime":    wtime  = int.Parse(tokens[i + 1]); break;
            case "btime":    btime  = int.Parse(tokens[i + 1]); break;
            case "winc":     winc   = int.Parse(tokens[i + 1]); break;
            case "binc":     binc   = int.Parse(tokens[i + 1]); break;
        }
    }

    if (timeMs == -1)
    {
        int myTime = board.turn == 0 ? wtime : btime;
        int myInc  = board.turn == 0 ? winc  : binc;

        if (myTime > 0)
            timeMs = myTime / 30 + (int)(myInc * 0.8);
        else
            timeMs = 1000;
    }

    Move best = engine.GetBestMove(board, timeMs);
    Console.WriteLine($"bestmove {MoveToUCI(best)}");
}

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
