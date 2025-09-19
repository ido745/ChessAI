using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class AI : MonoBehaviour
{
    const int MAX_PLY = 20;

    // Enum for different types of transposition table entries
    public enum TTEntryType : byte
    {
        Exact = 0,    // PV-node - exact score
        LowerBound = 1, // Beta cutoff - score is at least this value (fail-high)
        UpperBound = 2  // Alpha cutoff - score is at most this value (fail-low)
    }

    // Transposition table entry structure
    [System.Serializable]
    public struct TTEntry
    {
        public ulong zobristKey;    // Full 64-bit key for verification
        public short score;         // Evaluation score
        public byte depth;          // Search depth
        public TTEntryType type;    // Type of bound
        public Move bestMove;       // Best move found
        public byte age;            // For replacement strategy

        public TTEntry(ulong key, short score, byte depth, TTEntryType type, Move bestMove, byte age)
        {
            this.zobristKey = key;
            this.score = score;
            this.depth = depth;
            this.type = type;
            this.bestMove = bestMove;
            this.age = age;
        }

        public bool IsEmpty => zobristKey == 0;
    }

    private struct MoveState
    {
        public ulong enPassantSquare;
        public int castlingRights;
        public int currentCastling;
        public ulong checkMap;
        public ulong[] pinRays;
        public bool[] doubleCheck;
        public ulong[] attackedSquares;
        public bool gameEnded;
    }

    private TranspositionTable tt = new TranspositionTable(64); // 64 MB table

    private Evaluate evaluator;
    [SerializeField] private BoardLogic boardLogic;
    [SerializeField] private GraphicalBoard graphicalBoard;
    [SerializeField] private int aiColor;

    private Stopwatch searchStopwatch;
    private int searchTimeLimitMs;
    private bool aborted;

    private int bestScoreForDebug;


    // Start is called before the first frame update
    // Iterative deepening controller (call from StartThinking)
    public void StartThinking()
    {
        evaluator = GetComponent<Evaluate>();
        aiColor = boardLogic.turn;

        // Age the transposition table for new search
        tt.NextAge();

        Move? bookMove = TryBookMove();
        if (bookMove != null)
        {
            boardLogic.MakeMove((Move)bookMove);
            graphicalBoard.MakeVisualMove((Move)bookMove);
            return;
        }

        searchStopwatch = new Stopwatch();
        searchTimeLimitMs = 2000;
        aborted = false;

        searchStopwatch.Start();

        Move lastBestMove = new Move();
        int depth = 1;
        bestScoreForDebug = -999999;
        while (!aborted && searchStopwatch.ElapsedMilliseconds < searchTimeLimitMs)
        {
            Move candidate = Search(depth);
            if (!aborted)
            {
                lastBestMove = candidate;
                UnityEngine.Debug.Log($"Depth {depth} completed in {searchStopwatch.ElapsedMilliseconds} ms");
            }
            else
            {
                break; // either no move or aborted
            }

            if (bestScoreForDebug > 80000 || bestScoreForDebug < -80000)
            {
                // we've found a mate -- no need to search deeper
                UnityEngine.Debug.Log($"Found a mate in {depth}!");
                break;
            }

            depth++;
        }

        searchStopwatch.Stop();

        if (IsValidMove(lastBestMove))
        {
            boardLogic.MakeMove(lastBestMove);
            graphicalBoard.MakeVisualMove(lastBestMove);
            print($"Final move: {boardLogic.MoveToSAN(lastBestMove)} (depth {depth - 1}, time {searchStopwatch.ElapsedMilliseconds} ms)." +
                $" It will result in a score of {bestScoreForDebug}");
        }
        else
        {
            print("No move found (game over or aborted).");
        }
    }


    private Move Search(int depth)
    {
        Move[] moves = new Move[256];
        int movesCount = boardLogic.GenerateAllMoves(moves, boardLogic.turn);

        if (movesCount == 0)
        {
            if (boardLogic.IsInCheck())
            {
                print($"Checkmate! {1 - boardLogic.turn} wins!");
            }
            else
            {
                print("StaleMate!");
            }
            return new Move(); // Stalemate or checkmate
        }
        if (movesCount == 1)
            return moves[0];

        Move ttMove = tt.GetBestMove(boardLogic.zobristKey);
        OrderMoves(moves, movesCount, ttMove);

        Move bestMove = new Move();

        int bestScore = -999999;
        int originalAlpha = -999999;
        int alpha = -999999;
        int beta = 999999;

        for (int i = 0; i < movesCount; i++)
        {
            Move move = moves[i];
            MoveState state = SaveMoveState();
            boardLogic.MakeMove(move);

            int score = -RecursiveSearch(depth - 1, -beta, -alpha, 1);

            RestoreMoveState(move, state);

            if (score > bestScore)
            {
                bestScore = score;
                bestMove = move;

                if (score > bestScoreForDebug)
                {
                    bestScoreForDebug = score;
                }
            }

            if (score > alpha)
            {
                alpha = score;
            }
            if (score >= beta)
            {
                break; // Beta cutoff
            }
        }

        // Determine entry type for root
        TTEntryType entryType;
        if (bestScore <= originalAlpha)
        {
            entryType = TTEntryType.UpperBound;
        }
        else if (bestScore >= beta)
        {
            entryType = TTEntryType.LowerBound;
        }
        else
        {
            entryType = TTEntryType.Exact;
        }

        // Store in TT
        tt.Store(boardLogic.zobristKey, (short)bestScore, (byte)depth, entryType, bestMove);

        return bestMove;
    }


    private int RecursiveSearch(int depth, int alpha, int beta, int ply)
    {
        int originalAlpha = alpha;

        // Abort if time is up
        if (searchStopwatch.ElapsedMilliseconds >= searchTimeLimitMs)
        {
            aborted = true;
            return 9999 * (boardLogic.turn == aiColor ? -1 : 1); // Return a really bad score - we didn't get to evaluate it.
        }

        // Probe transposition table
        ulong zobristKey = boardLogic.zobristKey;
        if (tt.Probe(zobristKey, (byte)depth, (short)alpha, (short)beta, ply, out short ttScore, out Move ttMove))
        {
            return ttScore; // The score is now correctly adjusted for the current depth
        }

        Move[] moves = new Move[256];
        int movesCount = boardLogic.GenerateAllMoves(moves, boardLogic.turn);

        if (movesCount == 0)
        {
            if (boardLogic.IsInCheck())
                return -90000 + ply; // Mate is less bad the further away it is
            else
                return 0; // stalemate
        }

        //if (boardLogic.IsInCheck() && ply < MAX_PLY - 3)
        //{
        //    //depth += 2; // See opponent's response to the check escape
        //}

        if (depth == 0)
        {
            return QuiescenceSearch(alpha, beta);
        }

        OrderMoves(moves, movesCount, ttMove);

        int bestScore = -999999;
        Move bestMove = new Move();
        bool foundMove = false;

        for (int i = 0; i < movesCount; i++)
        {
            if (aborted) break; // bubble up abort

            Move move = moves[i];
            MoveState state = SaveMoveState();
            boardLogic.MakeMove(move);
            
            int score = -RecursiveSearch(depth - 1, -beta, -alpha, ply + 1);

            RestoreMoveState(move, state);

            if (aborted) return score;

            if (score > bestScore)
            {
                bestScore = score;
                bestMove = move;
                foundMove = true;
            }

            if (score >= beta)
            {
                // Beta cutoff - store as lower bound
                tt.Store(zobristKey, (short)beta, (byte)depth, TTEntryType.LowerBound, move);
                return beta;
            }

            if (score > alpha)
            {
                alpha = score;
            }
        }
        // Determine entry type based on final result
        TTEntryType entryType;
        if (bestScore <= originalAlpha)
        {
            entryType = TTEntryType.UpperBound; // All moves failed low
        }
        else
        {
            entryType = TTEntryType.Exact; // We found a move that raised alpha but didn't cause beta cutoff
        }

        // Always store the result
        tt.Store(zobristKey, (short)bestScore, (byte)depth, entryType, foundMove ? bestMove : new Move());


        return bestScore;
    }


    private int QuiescenceSearch(int alpha, int beta, int depth = 10)
    {
        int originalAlpha = alpha;
        ulong zobristKey = boardLogic.zobristKey;

        // Probe transposition table
        if (tt.Probe(zobristKey, 0, (short)alpha, (short)beta, 0, out short ttScore, out _))
        {
            return ttScore;
        }

        // Time check
        if (searchStopwatch.ElapsedMilliseconds >= searchTimeLimitMs)
        {
            aborted = true;
            return 9999 * (boardLogic.turn == aiColor ? -1 : 1);
        }

        // Depth limit for quiescence
        if (depth <= 0)
        {
            // At maximum depth, just return static evaluation
            return evaluator.GetScore(boardLogic) * (boardLogic.turn == 0 ? 1 : -1);
        }

        // Check for terminal positions first
        Move[] allMoves = new Move[256];
        int allMovesCount = boardLogic.GenerateAllMoves(allMoves, boardLogic.turn);

        if (allMovesCount == 0)
        {
            if (boardLogic.IsInCheck())
                return -90000; // Checkmate
            else
                return 0; // Stalemate
        }

        // Get stand-pat score (static evaluation)
        int standPatScore = evaluator.GetScore(boardLogic) * (boardLogic.turn == 0 ? 1 : -1);

        // Stand-pat cutoff
        if (standPatScore >= beta)
        {
            return beta;
        }

        // Update alpha with stand-pat if it's better
        if (standPatScore > alpha)
        {
            alpha = standPatScore;
        }

        // Delta pruning - if even capturing the opponent's queen wouldn't raise alpha above beta
        const int QUEEN_VALUE = 900;
        if (standPatScore + QUEEN_VALUE + 200 < alpha) // +200 for safety margin
        {
            return alpha;
        }

        // Generate only capture moves
        Move[] captures = new Move[128];
        int captureCount = boardLogic.GenerateAllCaptures(captures, boardLogic.turn);

        // Sort captures by MVV-LVA (Most Valuable Victim - Least Valuable Attacker)
        if (captureCount > 1)
        {
            Array.Sort(captures, 0, captureCount, Comparer<Move>.Create((move1, move2) =>
            {
                int score1 = GetCaptureScore(move1);
                int score2 = GetCaptureScore(move2);
                return score2.CompareTo(score1);
            }));
        }

        for (int i = 0; i < captureCount; i++)
        {
            Move captureMove = captures[i];

            // Delta pruning for individual moves
            int captureValue = GetPieceValue(captureMove.capturedPiece, captureMove.to);
            if (standPatScore + captureValue + 200 < alpha)
            {
                continue; // Skip this capture, it won't improve alpha enough
            }

            MoveState state = SaveMoveState();
            boardLogic.MakeMove(captureMove);

            int score = -QuiescenceSearch(-beta, -alpha, depth - 1);

            RestoreMoveState(captureMove, state);

            if (aborted) return 9999 * (boardLogic.turn == aiColor ? -1 : 1);

            if (score >= beta)
            {
                // Beta cutoff - store as lower bound
                tt.Store(zobristKey, (short)beta, 0, TTEntryType.LowerBound, captureMove);
                return beta;
            }

            if (score > alpha)
            {
                alpha = score;
            }
        }

        // Determine correct entry type for transposition table
        TTEntryType entryType;
        if (alpha <= originalAlpha)
        {
            entryType = TTEntryType.UpperBound; // All moves failed to raise alpha
        }
        else
        {
            entryType = TTEntryType.Exact; // We found a move that improved alpha
        }

        tt.Store(zobristKey, (short)alpha, 0, entryType, new Move());
        return alpha;
    }

    private int GetCaptureScore(Move move)
    {
        if (move.capturedPiece == 0) return 0;

        int victimValue = GetBasicPieceValue(move.capturedPiece);
        int attackerValue = GetBasicPieceValue(move.movedPiece);

        // MVV-LVA: prioritize high-value victims captured by low-value attackers
        return victimValue * 10 - attackerValue;
    }

    private int GetBasicPieceValue(int piece)
    {
        int type = Piece.GetPieceType(piece);
        return type switch
        {
            Piece.Pawn => 100,
            Piece.Knight => 320,
            Piece.Bishop => 330,
            Piece.Rook => 500,
            Piece.Queen => 900,
            Piece.King => 20000,
            _ => 0
        };
    }

    private void OrderMoves(Move[] moves, int moveCount, Move ttMove = new Move())
    {
        // Better check for valid TT move
        bool hasTTMove = IsValidMove(ttMove);

        if (hasTTMove)
        {
            for (int i = 0; i < moveCount; i++)
            {
                if (MovesEqual(moves[i], ttMove))
                {
                    // Swap TT move to front
                    Move temp = moves[0];
                    moves[0] = moves[i];
                    moves[i] = temp;
                    break;
                }
            }
        }

        // Sort the rest
        int startIndex = hasTTMove ? 1 : 0;
        if (startIndex < moveCount)
        {
            Array.Sort(moves, startIndex, moveCount - startIndex, Comparer<Move>.Create((move1, move2) =>
            {
                int score1 = GetMoveScore(move1);
                int score2 = GetMoveScore(move2);
                return score2.CompareTo(score1);
            }));
        }
    }

    // Helper methods for move validation
    private bool IsValidMove(Move move)
    {
        return move.from >= 0 && move.from < 64 &&
               move.to >= 0 && move.to < 64 &&
               move.from != move.to &&
               move.movedPiece != 0;
    }

    private bool MovesEqual(Move move1, Move move2)
    {
        return move1.from == move2.from &&
               move1.to == move2.to &&
               move1.movedPiece == move2.movedPiece &&
               move1.flag == move2.flag &&
               move1.promotionPiece == move2.promotionPiece;
    }

    private int GetMoveScore(Move move)
    {
        int score = 0;

        // Prioritize captures (victim value - attacker value)
        if (move.capturedPiece != 0)
        {
            int victimValue = GetPieceValue(move.capturedPiece, move.to);
            int attackerValue = GetPieceValue(move.movedPiece, move.to);
            score += (victimValue - attackerValue) * 100;
        }

        // Prioritize promotions
        if (move.flag == (int)MoveFlag.Promotion)
            score += 900;
        if (move.flag == (int)MoveFlag.Castling)
            score += 50;
        // You can add more heuristics here later:
        // - Central square moves: score += 10;
        // - Piece development: score += 20;

        return score;
    }

    private int GetPieceValue(int piece, int pos)
    {
        int type = Piece.GetPieceType(piece);
        if (Piece.IsBlack(piece) == 1)
            pos = pos ^ 56;
        return type switch
        {
            Piece.Pawn => 100 + PieceSquareTables.pawnTable[pos],
            Piece.Knight => 320 + PieceSquareTables.knightTable[pos],
            Piece.Bishop => 330 + PieceSquareTables.bishopTable[pos],
            Piece.Rook => 500 + PieceSquareTables.rookTable[pos],
            Piece.Queen => 900 + PieceSquareTables.queenTable[pos],
            Piece.King => 20000 + PieceSquareTables.kingTable[pos],
            _ => 0
        };
    }

    private MoveState SaveMoveState()
    {
        MoveState state = new MoveState();

        state.enPassantSquare = boardLogic.enPassantSquare;
        state.castlingRights = boardLogic.castlingRights;
        state.currentCastling = boardLogic.currentCastling;
        state.checkMap = boardLogic.checkMap;
        state.gameEnded = boardLogic.gameEnded;

        // Deep copy arrays
        state.pinRays = new ulong[64];
        Array.Copy(boardLogic.pinRays, state.pinRays, 64);

        state.doubleCheck = new bool[2];
        Array.Copy(boardLogic.doubleCheck, state.doubleCheck, 2);

        state.attackedSquares = new ulong[2];
        Array.Copy(boardLogic.attackedSquares, state.attackedSquares, 2);

        return state;
    }

    private void RestoreMoveState(Move move, MoveState state)
    {
        // 1. Unmake the physical move on the board
        boardLogic.GetComponent<BoardLogic>().UnmakeMove(move, state.castlingRights, state.enPassantSquare);

        // 2. Restore the entire derived state from the saved struct.
        // This is now the single point of state restoration.
        boardLogic.currentCastling = state.currentCastling;
        boardLogic.checkMap = state.checkMap;
        boardLogic.gameEnded = state.gameEnded;
        Array.Copy(state.pinRays, boardLogic.pinRays, 64);
        Array.Copy(state.doubleCheck, boardLogic.doubleCheck, 2);
        Array.Copy(state.attackedSquares, boardLogic.attackedSquares, 2);
    }

    public Move? TryBookMove()
    {
        string openingsDir = Path.Combine(Application.dataPath, "Scripts/AI scripts/Openings");
        if (!Directory.Exists(openingsDir))
            return null;

        string currentMoves = boardLogic.openingLine.Trim();
        print(currentMoves);

        // Store potential book moves with their details
        List<(Move move, string openingName, int lineLength)> candidateMoves = new List<(Move, string, int)>();

        foreach (string file in Directory.GetFiles(openingsDir, "*.tsv"))
        {
            foreach (string line in File.ReadAllLines(file))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                string[] parts = line.Split('\t');
                if (parts.Length < 3) continue;

                string openingName = parts[1].Trim();
                string moves = parts[2].Trim();

                int index = moves.IndexOf(currentMoves, StringComparison.Ordinal);
                if (index >= 0)
                {
                    string remaining = moves.Substring(index + currentMoves.Length).Trim();
                    if (string.IsNullOrEmpty(remaining)) continue;

                    string[] tokens = remaining.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                    // Skip over move numbers like "4."
                    string nextSan = tokens.FirstOrDefault(t => !t.EndsWith("."));
                    if (nextSan == null) continue;

                    Move? bookMove = FindMoveFromSAN(nextSan);
                    if (bookMove.HasValue)
                    {
                        candidateMoves.Add((bookMove.Value, openingName, moves.Length));
                    }
                }
            }
        }

        if (candidateMoves.Count == 0)
            return null;

        // Sort by line length (most detailed first) and take top 10
        var topCandidates = candidateMoves
            .OrderByDescending(x => x.lineLength)
            .Take(10)
            .ToList();

        // Randomly select from the top candidates
        if (topCandidates.Count > 0)
        {
            int randomIndex = UnityEngine.Random.Range(0, topCandidates.Count);
            var selectedMove = topCandidates[randomIndex];

            UnityEngine.Debug.Log($"Book move found: {boardLogic.MoveToSAN(selectedMove.move)} ({selectedMove.openingName}) - Selected from {topCandidates.Count} top candidates");
            return selectedMove.move;
        }

        return null;
    }

    private Move? FindMoveFromSAN(string san)
    {
        Move[] moves = new Move[256];
        int movesCount = boardLogic.GenerateAllMoves(moves, boardLogic.turn);

        for (int i = 0; i < movesCount; i++)
        {
            Move move = moves[i];
            string moveSan = boardLogic.MoveToSAN(move);

            if (moveSan == san)
                return move;
        }

        return null;
    }
}