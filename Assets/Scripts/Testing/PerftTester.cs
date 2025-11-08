using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using static BitScan;

public class PerftTester : MonoBehaviour
{
    private BoardLogic boardLogic;

    // Test positions with expected results
    private readonly TestPosition[] testPositions = {
        // Starting position
        new TestPosition(
            "8/2p5/3p4/KP5r/1R3p1k/8/4P1P1/8 w - - 0 1",
            "Engame Position",
            new long[] { 1, 14, 191, 2812, 43238, 674624 }
        ),
        new TestPosition(
            "r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - ",
            "Kiwipete Position",
            new long[] { 1, 48, 2039, 97862, 4085603, 193690690 }
        ),
        new TestPosition(
            "r3k2r/Pppp1ppp/1b3nbN/nP6/BBP1P3/q4N2/Pp1P2PP/R2Q1RK1 w kq - 0 1",
            "Fourth Position",
            new long[] { 1, 6, 264, 9467, 422333, 15833292 }
        ),
        new TestPosition(
            "rnbq1k1r/pp1Pbppp/2p5/8/2B5/8/PPP1NnPP/RNBQK2R w KQ - 1 8",
            "Fifth Position",
            new long[] { 1, 44, 1486, 62379, 2103487, 89941194 }
        )
        //new TestPosition(
        //    "r4rk1/1pp1qppp/p1np1n2/2b1p1B1/2B1P1b1/P1NP1N2/1PP1QPPP/R4RK1 w - - 0 10",
        //    "Sixth Position",
        //    new long[] { 1, 46, 2079, 89890, 3894594, 164075551 }
        //)
    };

    private struct TestPosition
    {
        public string fen;
        public string name;
        public long[] expectedNodes;

        public TestPosition(string fen, string name, long[] expectedNodes)
        {
            this.fen = fen;
            this.name = name;
            this.expectedNodes = expectedNodes;
        }
    }

    private struct PerftResult
    {
        public long nodes;
        public float timeMs;

        public PerftResult(long nodes, float timeMs)
        {
            this.nodes = nodes;
            this.timeMs = timeMs;
        }
    }

    private struct MoveNodeCount
    {
        public Move move;
        public long nodeCount;

        public MoveNodeCount(Move move, long nodeCount)
        {
            this.move = move;
            this.nodeCount = nodeCount;
        }
    }

    //void Start()
    //{
    //    boardLogic = GetComponent<BoardLogic>();
    //    if (boardLogic == null)
    //    {
    //        UnityEngine.Debug.LogError("BoardLogic component not found!");
    //        return;
    //    }

    //    // Wait a frame to ensure everything is initialized
    //    Invoke(nameof(RunPerftTests), 0.1f);
    //}

    private void RunPerftTests()
    {
        UnityEngine.Debug.Log("=== CHESS ENGINE PERFT TESTS ===\n");

        for (int posIndex = 0; posIndex < testPositions.Length; posIndex++)
        {
            TestPosition testPos = testPositions[posIndex];
            UnityEngine.Debug.Log($"Testing {testPos.name}");
            UnityEngine.Debug.Log($"FEN: {testPos.fen}\n");

            // Set up the position
            boardLogic.ParseFEN(testPos.fen);
            boardLogic.attackCalculator.FindPinsAndChecks(boardLogic.turn);
            boardLogic.attackCalculator.UpdateAttacksMap(0);
            boardLogic.attackCalculator.UpdateAttacksMap(1);

            // Test depths
            int maxDepth = Math.Min(testPos.expectedNodes.Length - 1, 5);
            for (int depth = 1; depth <= maxDepth; depth++)
            {
                PerftResult result;

                // For depths up to 3, show detailed move breakdown
                if (depth == 5)
                {
                    result = RunPerftWithBreakdown(depth);
                }
                else
                {
                    result = RunPerft(depth);
                }

                bool passed = true;
                if (depth <= testPos.expectedNodes.Length - 1)
                {
                    long expected = testPos.expectedNodes[depth];
                    passed = result.nodes == expected;

                    string status = passed ? "PASS" : "FAIL";
                    UnityEngine.Debug.Log($"Depth {depth}: {result.nodes:N0} nodes ({status}) - Expected: {expected:N0}");
                }
                else
                {
                    UnityEngine.Debug.Log($"Depth {depth}: {result.nodes:N0} nodes");
                }

                // Print detailed breakdown
                PrintPerftBreakdown(result, depth);

                if (!passed)
                {
                    UnityEngine.Debug.LogError($"PERFT FAILED at depth {depth} for {testPos.name}!");
                }
            }

            UnityEngine.Debug.Log(new string('-', 60));
        }

        UnityEngine.Debug.Log("=== PERFT TESTS COMPLETED ===");
    }

    private PerftResult RunPerftWithBreakdown(int depth)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();

        PerftResult result = new PerftResult();

        if (depth == 0)
        {
            stopwatch.Stop();
            return new PerftResult(1, stopwatch.ElapsedMilliseconds);
        }

        List<MoveNodeCount> moveBreakdown = new List<MoveNodeCount>();
        List<Move> moves = GenerateAllMoves();

        UnityEngine.Debug.Log($"\nMove breakdown for depth {depth}:");
        UnityEngine.Debug.Log("================================");

        foreach (Move move in moves)
        {
            BoardState state = SaveBoardState();
            boardLogic.moveExecuter.MakeMove(move);

            long nodeCount;
            if (depth == 1)
            {
                nodeCount = 1;
            }
            else
            {
                PerftResult subResult = PerftRecursive(depth - 1);
                nodeCount = subResult.nodes;
            }

            RestoreBoardState(state);

            result.nodes += nodeCount;
            moveBreakdown.Add(new MoveNodeCount(move, nodeCount));

            // Print this move's contribution
            string moveStr = FormatMove(move);
            UnityEngine.Debug.Log($"{moveStr}: {nodeCount}");
        }

        UnityEngine.Debug.Log($"\nTotal nodes: {result.nodes}\n");

        stopwatch.Stop();
        result.timeMs = stopwatch.ElapsedMilliseconds;

        return result;
    }

    private PerftResult RunPerft(int depth)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();

        PerftResult result = new PerftResult();

        if (depth == 0)
        {
            stopwatch.Stop();
            return new PerftResult(1, stopwatch.ElapsedMilliseconds);
        }

        result = PerftRecursive(depth);

        stopwatch.Stop();
        result.timeMs = stopwatch.ElapsedMilliseconds;

        return result;
    }

    private PerftResult PerftRecursive(int depth)
    {
        PerftResult result = new PerftResult();

        if (depth == 0)
        {
            return new PerftResult(1, 0);
        }

        List<Move> moves = GenerateAllMoves();

        if (depth == 1)
        {
            // At depth 1, just count the moves
            result.nodes = moves.Count;
            return result;
        }

        // For deeper searches, recursively count
        foreach (Move move in moves)
        {
            BoardState state = SaveBoardState();
            boardLogic.moveExecuter.MakeMove(move);

            PerftResult subResult = PerftRecursive(depth - 1);
            result.nodes += subResult.nodes;

            RestoreBoardState(state);
        }

        return result;
    }

    private string FormatMove(Move move)
    {
        string from = SquareToAlgebraic(move.from);
        string to = SquareToAlgebraic(move.to);

        string moveStr = from + to;

        // Add promotion piece if applicable
        if (move.flag == 2) // Promotion
        {
            char promotionPiece = ' ';
            int pieceType = Piece.GetPieceType(move.promotionPiece);
            switch (pieceType)
            {
                case Piece.Queen: promotionPiece = 'q'; break;
                case Piece.Rook: promotionPiece = 'r'; break;
                case Piece.Bishop: promotionPiece = 'b'; break;
                case Piece.Knight: promotionPiece = 'n'; break;
            }
            moveStr += promotionPiece;
        }

        return moveStr;
    }

    private string SquareToAlgebraic(int square)
    {
        int file = square % 8;
        int rank = square / 8;
        return ((char)('a' + file)).ToString() + (rank + 1).ToString();
    }

    private List<Move> GenerateAllMoves()
    {
        List<Move> allMoves = new List<Move>();

        // Generate moves for all pieces of the current color
        for (int square = 0; square < 64; square++)
        {
            int piece = boardLogic.board[square];
            if (piece == 0) continue;

            int pieceColor = Piece.IsBlack(piece);
            if (pieceColor != boardLogic.turn) continue;

            // Generate moves for this piece
            List<Move> pieceMoves = boardLogic.moveCalculator.GenerateListMoves(square, piece);

            // For promotions, generate all possible promotion pieces
            foreach (Move move in pieceMoves)
            {
                if (move.flag == 2) // Promotion
                {
                    int color = Piece.GetColor(piece);
                    allMoves.Add(new Move(move.from, move.to, move.movedPiece, move.capturedPiece, move.flag, Piece.Queen | color));
                    allMoves.Add(new Move(move.from, move.to, move.movedPiece, move.capturedPiece, move.flag, Piece.Rook | color));
                    allMoves.Add(new Move(move.from, move.to, move.movedPiece, move.capturedPiece, move.flag, Piece.Bishop | color));
                    allMoves.Add(new Move(move.from, move.to, move.movedPiece, move.capturedPiece, move.flag, Piece.Knight | color));
                }
                else
                {
                    allMoves.Add(move);
                }
            }
        }

        return allMoves;
    }

    private struct BoardState
    {
        public int[] board;
        public ulong[,] bitboards;
        public ulong Wbitboard;
        public ulong Bbitboard;
        public ulong enPassantSquare;
        public short turn;
        public int castlingRights;
        public int currentCastling;
        public ulong[] attackedSquares;
        public ulong checkMap;
        public ulong[] pinRays;
        public bool[] doubleCheck;
        public bool gameEnded;
    }

    private BoardState SaveBoardState()
    {
        BoardState state = new BoardState();

        state.board = new int[64];
        Array.Copy(boardLogic.board, state.board, 64);

        state.bitboards = new ulong[2, 6];
        for (int i = 0; i < 2; i++)
            for (int j = 0; j < 6; j++)
                state.bitboards[i, j] = boardLogic.bitboards[i, j];

        state.Wbitboard = boardLogic.Wbitboard;
        state.Bbitboard = boardLogic.Bbitboard;
        state.enPassantSquare = boardLogic.enPassantSquare;
        state.turn = boardLogic.turn;
        state.castlingRights = boardLogic.castlingRights;
        state.currentCastling = boardLogic.currentCastling;

        state.attackedSquares = new ulong[2];
        Array.Copy(boardLogic.attackedSquares, state.attackedSquares, 2);

        state.checkMap = boardLogic.checkMap;

        state.pinRays = new ulong[64];
        Array.Copy(boardLogic.pinRays, state.pinRays, 64);

        state.doubleCheck = new bool[2];
        Array.Copy(boardLogic.doubleCheck, state.doubleCheck, 2);

        state.gameEnded = boardLogic.gameEnded;

        return state;
    }

    private void RestoreBoardState(BoardState state)
    {
        Array.Copy(state.board, boardLogic.board, 64);

        for (int i = 0; i < 2; i++)
            for (int j = 0; j < 6; j++)
                boardLogic.bitboards[i, j] = state.bitboards[i, j];

        boardLogic.Wbitboard = state.Wbitboard;
        boardLogic.Bbitboard = state.Bbitboard;
        boardLogic.enPassantSquare = state.enPassantSquare;
        boardLogic.turn = state.turn;
        boardLogic.castlingRights = state.castlingRights;
        boardLogic.currentCastling = state.currentCastling;

        Array.Copy(state.attackedSquares, boardLogic.attackedSquares, 2);
        boardLogic.checkMap = state.checkMap;
        Array.Copy(state.pinRays, boardLogic.pinRays, 64);
        Array.Copy(state.doubleCheck, boardLogic.doubleCheck, 2);
        boardLogic.gameEnded = state.gameEnded;
    }

    private void PrintPerftBreakdown(PerftResult result, int depth)
    {
        float avgTimePerNode = result.nodes > 0 ? result.timeMs * 1000.0f / result.nodes : 0;
        UnityEngine.Debug.Log($"  Time: {result.timeMs:F2}ms | Nodes/sec: {(result.nodes / Math.Max(result.timeMs / 1000.0f, 0.001f)):N0} | " +
                             $"Avg per node: {avgTimePerNode:F3}μs\n");
    }
}