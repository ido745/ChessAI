using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class AI : MonoBehaviour
{
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

    private Evaluate evaluator;
    [SerializeField] private BoardLogic boardLogic;
    [SerializeField] private GraphicalBoard graphicalBoard;


    // Start is called before the first frame update
    public void StartThinking()
    {
        print($"the turn at the beginning of the ai turn is: {boardLogic.turn}");
        evaluator = GetComponent<Evaluate>();
        Lookup();
    }

    private void Lookup()
    {
        Move? bookMove = TryBookMove();
        if (bookMove != null)
        {
            boardLogic.MakeMove((Move)bookMove);
            graphicalBoard.MakeVisualMove((Move)bookMove);
            return; // Skip engine search
        }

        // For every move, find it's evaluation at a certain depth.
        int depth = 3;

        Move[] moves = new Move[256];
        int movesCount = boardLogic.GenerateAllMoves(moves, boardLogic.turn);
        int bestScore = -999999;

        if (movesCount == 0)
        {
            if (boardLogic.IsInCheck())
            {
                print($"Checkmate! {1 - boardLogic.turn} wins!");
                return; // Checkmate
            }
            else
            {
                print("StaleMate!");
                return; // Stalemate
            }
        }

        Move bestMove = new Move(-1, -1, -1);

        // Define the initial alpha and beta for the root search
        int alpha = -999999;
        int beta = 999999;
        print($"current score: {evaluator.GetScore(boardLogic)}");

        for (int i = 0; i < movesCount; i++)
        {
            Move move = moves[i];
            MoveState state = SaveMoveState();
            boardLogic.MakeMove(move);

            int score = -RecursiveLookup(depth - 1, -beta, -alpha);
            if (score > bestScore)
            {
                bestScore = score;
                bestMove = move;
            }

            RestoreMoveState(move, state);
        }

        print($"best move: from {bestMove.from} to {bestMove.to}. it will result in a score of {bestScore}");

        boardLogic.MakeMove(bestMove);
        graphicalBoard.MakeVisualMove(bestMove);
    }

    private int RecursiveLookup(int depth, int alpha, int beta)
    {
        if (depth == 0)
        {
            return QuiescenceSearch(alpha, beta);
        }

        Move[] moves = new Move[256];
        int movesCount = boardLogic.GenerateAllMoves(moves, boardLogic.turn);

        // Check for terminal positions
        if (movesCount == 0)
        {
            if (boardLogic.IsInCheck())
                return -999999; // Checkmate
            else
                return 0; // Stalemate
        }

        for (int i = 0; i < movesCount; i++)
        {
            Move move = moves[i];
            MoveState state = SaveMoveState();
            boardLogic.MakeMove(move);

            int score = -RecursiveLookup(depth - 1, -beta, -alpha);

            RestoreMoveState(move, state);

            if (score >= beta)
            {
                return beta;
            }
            if (score > alpha)
            {
                alpha = score;
            }
        }
        return alpha;
    }

    private int QuiescenceSearch(int alpha, int beta, int depth = 4)
    {
        if (depth <= 0)
            return evaluator.GetScore(boardLogic) * (boardLogic.turn == 0 ? 1 : -1);

        // 1. Get the static evaluation of the current position.
        // This is the "stand-pat" score, assuming we don't make any more captures.
        int standPatScore = evaluator.GetScore(boardLogic) * (boardLogic.turn == 0 ? 1 : -1);

        // 2. Check for beta cutoff. If the static eval is already too high,
        // we can prune immediately.
        if (standPatScore >= beta)
        {
            return beta;
        }
        if (standPatScore > alpha)
        {
            alpha = standPatScore;
        }

        // 3. Generate ONLY capture moves. You will need to modify your
        // GenerateAllMoves function or create a new GenerateCaptures function.
        Move[] captures = new Move[128];
        int captureCount = boardLogic.GenerateAllCaptures(captures, boardLogic.turn); // A new function you'll write

        for (int i = 0; i < captureCount; i++)
        {
            Move captureMove = captures[i];

            MoveState state = SaveMoveState();
            boardLogic.MakeMove(captureMove);

            // Recursively call QuiescenceSearch. Note there is no depth countdown.
            int score = -QuiescenceSearch(-beta, -alpha, depth - 1);

            RestoreMoveState(captureMove, state);

            if (score >= beta)
            {
                return beta; // Beta cutoff
            }
            if (score > alpha)
            {
                alpha = score; // New best move
            }
        }

        return alpha; // Return the best score found, which is at least the stand-pat score.
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