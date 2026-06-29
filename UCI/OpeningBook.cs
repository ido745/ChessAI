using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public class OpeningBook
{
    // Maps UCI move history → (UCI move → frequency across book lines)
    private readonly Dictionary<string, Dictionary<string, int>> _entries = new();
    private readonly Random _rng = new();

    public int PositionCount => _entries.Count;

    public static OpeningBook Load(string folder, Func<Move, string> moveToUci)
    {
        var book = new OpeningBook();
        const string startFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

        // Temporary board just for replaying book lines — discarded after loading
        BoardLogic tempBoard = new BoardLogic();

        foreach (string file in new[] { "a.txt", "b.txt", "c.txt", "d.txt", "e.txt" })
        {
            string path = Path.Combine(folder, file);
            if (!File.Exists(path)) continue;

            foreach (string rawLine in File.ReadAllLines(path))
            {
                string[] parts = rawLine.Split('\t');
                if (parts.Length < 3 || parts[0] == "eco") continue;

                // Reset board to starting position
                tempBoard.ParseFEN(startFen);
                tempBoard.positionHistory.Clear();
                tempBoard.attackCalculator.FindPinsAndChecks(tempBoard.turn);
                tempBoard.attackCalculator.UpdateAttacksMap(0);
                tempBoard.attackCalculator.UpdateAttacksMap(1);

                string[] tokens = parts[2].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var history = new List<string>();

                foreach (string token in tokens)
                {
                    if (token.EndsWith('.') || token == "*") continue;
                    string san = token.TrimEnd('+', '#', '!', '?');
                    if (san.Length == 0) continue;

                    Move[] candidates = new Move[256];
                    int count = tempBoard.moveCalculator.GenerateAllMoves(candidates, tempBoard.turn);

                    bool matched = false;
                    for (int i = 0; i < count; i++)
                    {
                        if (tempBoard.MoveToSAN(candidates[i]).TrimEnd('+', '#') != san) continue;

                        string uci = moveToUci(candidates[i]);
                        string key = string.Join(" ", history);

                        if (!book._entries.TryGetValue(key, out var dict))
                        {
                            dict = new Dictionary<string, int>();
                            book._entries[key] = dict;
                        }
                        // Count how many book lines include this move at this position.
                        // Mainstream moves (e5, c5, Nf6 ...) appear in hundreds of lines;
                        // dubious ones (g5, h5 ...) appear in only a handful.
                        dict[uci] = dict.TryGetValue(uci, out int n) ? n + 1 : 1;

                        tempBoard.moveExecuter.MakeMove(candidates[i]);
                        history.Add(uci);
                        matched = true;
                        break;
                    }

                    if (!matched) break;
                }
            }
        }

        return book;
    }

    // Returns a frequency-weighted random book move, or null if not in book.
    // Moves that appear in more book lines are proportionally more likely to be chosen,
    // so mainstream theory is heavily favoured over obscure/dubious variations.
    public string? TryGetMove(string uciHistory)
    {
        string key = uciHistory.Trim();
        if (!_entries.TryGetValue(key, out var moves) || moves.Count == 0)
            return null;

        int total = moves.Values.Sum();
        int r = _rng.Next(total);
        foreach (var (uci, weight) in moves)
        {
            r -= weight;
            if (r < 0) return uci;
        }
        return moves.Keys.Last();
    }
}
