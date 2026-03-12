using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using static BitScan;

public class OptimizedPerftTester : MonoBehaviour
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
        )
        //new TestPosition(
        //    "rnbq1k1r/pp1Pbppp/2p5/8/2B5/8/PPP1NnPP/RNBQK2R w KQ - 1 8",
        //    "Fifth Position",
        //    new long[] { 1, 44, 1486, 62379, 2103487, 89941194 }
        //),
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

    // Lightweight state tracking for move unmaking
    private struct MoveState
    {
        public ulong zobristKey;
        public ulong enPassantSquare;
        public int castlingRights;
        public int currentCastling;
        public ulong checkMap;
        public ulong[] pinRays;
        public bool[] doubleCheck;
        public ulong[] attackedSquares;
        public bool gameEnded;
    }

    void Start()
    {
        boardLogic = BoardLogic.Instance;
        //Invoke(nameof(RunPerftTests), 0.1f);
    }

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
                if (depth == -1)
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
        Move[] moves = new Move[256];
        int moveCount = GenerateAllMoves(moves, (int)boardLogic.turn);
        //List<Move> moves = GenerateAllMoves();

        UnityEngine.Debug.Log($"\nMove breakdown for depth {depth}:");
        UnityEngine.Debug.Log("================================");

        for (int i = 0; i < moveCount; i++)
        {
            Move move = moves[i];
            MoveState state = SaveMoveState();

            ulong keyBefore = boardLogic.zobristKey;
            boardLogic.moveExecuter.MakeMove(move);

            ulong keyAfterMove = boardLogic.zobristKey;
            int epFile = (boardLogic.enPassantSquare != 0UL) ? BitScan.TrailingZeroCount(boardLogic.enPassantSquare) % 8 : -1;
            ulong recalculatedKey = Zobrist.GetZobristKey(boardLogic.board, boardLogic.currentCastling, epFile, boardLogic.turn);

            if (keyAfterMove != recalculatedKey)
            {
                UnityEngine.Debug.LogError($"ZOBRIST KEY MISMATCH after move {FormatMove(move)}!");
                UnityEngine.Debug.LogError($"  Incremental: {keyAfterMove}");
                UnityEngine.Debug.LogError($"  Recalculated: {recalculatedKey}");
                UnityEngine.Debug.LogError($"  Depth: {depth}");
            }

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

            RestoreMoveState(move, state);

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

        Move[] moves = new Move[256];
        int moveCount = GenerateAllMoves(moves, (int)boardLogic.turn);
        //List<Move> moves = GenerateAllMoves();

        if (depth == 1)
        {
            // At depth 1, just count the moves
            result.nodes = moveCount;
            return result;
        }

        // For deeper searches, recursively count
        for (int i = 0; i < moveCount; i++)
        {
            Move move = moves[i];
            MoveState state = SaveMoveState();

            ulong keyBefore = boardLogic.zobristKey;
            boardLogic.moveExecuter.MakeMove(move);

            ulong keyAfterMove = boardLogic.zobristKey;
            int epFile = (boardLogic.enPassantSquare != 0UL) ? BitScan.TrailingZeroCount(boardLogic.enPassantSquare) % 8 : -1;
            ulong recalculatedKey = Zobrist.GetZobristKey(boardLogic.board, boardLogic.currentCastling, epFile, boardLogic.turn);

            if (keyAfterMove != recalculatedKey)
            {
                UnityEngine.Debug.LogError($"ZOBRIST KEY MISMATCH after move {FormatMove(move)}!");
                UnityEngine.Debug.LogError($"  Incremental: {keyAfterMove}");
                UnityEngine.Debug.LogError($"  Recalculated: {recalculatedKey}");
                UnityEngine.Debug.LogError($"  Depth: {depth}");
            }

            PerftResult subResult = PerftRecursive(depth - 1);
            result.nodes += subResult.nodes;

            RestoreMoveState(move, state);
        }

        return result;
    }

    private MoveState SaveMoveState()
    {
        MoveState state = new MoveState();

        state.zobristKey = boardLogic.zobristKey;
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
        boardLogic.moveExecuter.UnmakeMove(move, state.castlingRights, state.enPassantSquare, state.zobristKey);

        // 2. Restore the entire derived state from the saved struct.
        // This is now the single point of state restoration.
        boardLogic.currentCastling = state.currentCastling;
        boardLogic.checkMap = state.checkMap;
        boardLogic.gameEnded = state.gameEnded;
        Array.Copy(state.pinRays, boardLogic.pinRays, 64);
        Array.Copy(state.doubleCheck, boardLogic.doubleCheck, 2);
        Array.Copy(state.attackedSquares, boardLogic.attackedSquares, 2);
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

    public int GenerateAllMoves(Move[] moveList, int color)
    {
        int moveCount = 0;

        // Iterate through each piece type, from Pawn to King
        for (int pieceType = Piece.King; pieceType <= Piece.Queen; pieceType++)
        {
            ulong pieceBitboard = boardLogic.bitboards[color, pieceType - 1];

            // Loop through each piece of the current type on the board
            while (pieceBitboard != 0)
            {
                int from = BitScan.TrailingZeroCount(pieceBitboard);

                // Get the actual piece from the board array instead of constructing it
                int piece = boardLogic.board[from];

                // Verify this piece belongs to the current color (safety check)
                if (piece == 0 || Piece.IsBlack(piece) != color)
                {
                    // Remove this piece from the bitboard and continue
                    pieceBitboard = BitScan.ClearBit(pieceBitboard, from);
                    continue;
                }

                // Get the bitboard of all legal destination squares for this piece
                ulong validDestinations = boardLogic.moveCalculator.GenerateMoves(from, piece);

                // Loop through each valid destination square
                while (validDestinations != 0)
                {
                    ulong lsb = validDestinations & (ulong)-(long)validDestinations;
                    int to = BitScan.TrailingZeroCount(lsb);

                    // Determine move details
                    int capturedPiece = boardLogic.board[to]; // Will be 0 if it's not a capture
                    int flag = boardLogic.moveCalculator.FindFlag(piece, from, to); // Use the actual piece, not pieceType

                    // --- Handle Promotions ---
                    if (flag == (int)MoveFlag.Promotion)
                    {
                        int promotionColor = color == 1 ? Piece.Black : Piece.White;

                        // Add a move for each possible promotion piece
                        moveList[moveCount++] = new Move(from, to, piece, capturedPiece, flag, Piece.Queen | promotionColor);
                        moveList[moveCount++] = new Move(from, to, piece, capturedPiece, flag, Piece.Rook | promotionColor);
                        moveList[moveCount++] = new Move(from, to, piece, capturedPiece, flag, Piece.Bishop | promotionColor);
                        moveList[moveCount++] = new Move(from, to, piece, capturedPiece, flag, Piece.Knight | promotionColor);
                    }
                    // --- Handle all other move types ---
                    else
                    {
                        moveList[moveCount++] = new Move(from, to, piece, capturedPiece, flag);
                    }

                    // Remove this destination from the bitboard to process the next one
                    validDestinations ^= lsb;
                }

                // Remove this piece from the bitboard to process the next one
                pieceBitboard = BitScan.ClearBit(pieceBitboard, from);
            }
        }

        return moveCount;
    }

    private void PrintPerftBreakdown(PerftResult result, int depth)
    {
        float avgTimePerNode = result.nodes > 0 ? result.timeMs * 1000.0f / result.nodes : 0;
        UnityEngine.Debug.Log($"  Time: {result.timeMs:F2}ms | Nodes/sec: {(result.nodes / Math.Max(result.timeMs / 1000.0f, 0.001f)):N0} | " +
                             $"Avg per node: {avgTimePerNode:F3}μs\n");
    }
}