using System;
using System.Collections.Generic;
using System.Diagnostics;

public class SearchEngine
{
    const int INFINITY = 1_000_000;
    const int MATE_SCORE = 100_000;
    const int MAX_PLY = 128;    // raised to 128 instead of 20

    private int TIME_LIMIT = 2000;

    public SearchEngine(int timeLimitMs, Evaluate evaluator)
    {
        this.TIME_LIMIT = timeLimitMs;
        this.evaluator = evaluator;
    }

    public SearchEngine(Evaluate evaluator)
    {
        this.TIME_LIMIT = 2000;
        this.evaluator = evaluator;
    }

    private int aiColor;

    private Evaluate evaluator;
    private BoardLogic boardLogic;

    private TranspositionTable tt = new TranspositionTable(64); // 64 MB table
    private Dictionary<ulong, int> evalCache = new Dictionary<ulong, int>(); // Cached evaluations for positions

    private Stopwatch searchStopwatch;
    private int searchTimeLimitMs;
    private bool aborted;
    private int seldepth = 0;
    private int currentSearchDepth = 0;

    private int bestScoreForDebug;

    private Move[,] killerMoves = new Move[MAX_PLY, 2]; // Two killer moves per ply
    private int[,] historyTable = new int[64, 64]; // [from][to] square history scores
    private ulong[][] allocatedPinRays = new ulong[MAX_PLY][];
    private bool[][] allocatedDoubleCheck = new bool[MAX_PLY][];
    private ulong[][] allocatedAttackedSquares = new ulong[MAX_PLY][];

    private Move[][] moveList = new Move[MAX_PLY][];
    private Move[][] captureList = new Move[MAX_PLY][]; // for q-search

    private Move[,] pvTable = new Move[MAX_PLY, MAX_PLY];
    private int[] pvLength = new int[MAX_PLY];

    private Move[,] savedPVTable = new Move[MAX_PLY, MAX_PLY];
    private int[] savedPVLength = new int[MAX_PLY];

    private int[] moveScores = new int[256];

    // Public debugging information
    public int CurrentDepth => currentSearchDepth;
    public int SelDepth => seldepth;
    public int LastDepthReached { get; private set; }
    public float LastNps { get; private set; }
    public float LastTtHitRate { get; private set; }


    private void InitializeMoveStatePool()
    {
        for (int i = 0; i < MAX_PLY; i++)
        {
            allocatedPinRays[i] = new ulong[64];
            allocatedDoubleCheck[i] = new bool[2];
            allocatedAttackedSquares[i] = new ulong[2];
        }
    }

    private void InitializeMovePools()
    {
        for (int i = 0; i < MAX_PLY; i++)
        {
            moveList[i] = new Move[256];
            captureList[i] = new Move[128];
        }
    }

    public void ResetAI()
    {
        Array.Clear(pvTable, 0, pvTable.Length);
        Array.Clear(pvLength, 0, pvLength.Length);
        Array.Clear(savedPVTable, 0, savedPVTable.Length);
        Array.Clear(savedPVLength, 0, savedPVLength.Length);
        Array.Clear(moveScores, 0, moveScores.Length);
        Array.Clear(killerMoves, 0, killerMoves.Length);
        Array.Clear(historyTable, 0, historyTable.Length);

        aborted = false;
        seldepth = 0;
        currentSearchDepth = 0;

        tt.Clear();
        evalCache.Clear();

        InitializeMoveStatePool();
        InitializeMovePools();

        // if (BookManager.Instance != null)
        //     bookManager = BookManager.Instance;
        // if (BoardLogic.Instance != null)
        //     boardLogic = BoardLogic.Instance;

        //boardLogic.positionCounter.Clear();
    }

    private int nodesSearched = 0;
    private int ttProbes = 0;

    public Move GetBestMove(BoardLogic board)
    {
        this.boardLogic = board;
        evalCache.Clear();
        aiColor = boardLogic.turn;
        tt.NextAge();
        Array.Clear(historyTable, 0, historyTable.Length);

        return SearchOnBackgroundThread();
    }


    private Move SearchOnBackgroundThread()
    {
        nodesSearched = 0;
        ttProbes = 0;
        seldepth = 0;
        tt.Clear();

        searchStopwatch = new Stopwatch();
        searchTimeLimitMs = TIME_LIMIT;
        aborted = false;
        searchStopwatch.Start();

        Move lastBestMove = new Move();

        int scoreForDebug = evaluator.GetScore(boardLogic);
        //print($"STATIC EVAL: {scoreForDebug}");

        Array.Clear(pvLength, 0, pvLength.Length);

        int depth = 0;
        bestScoreForDebug = -INFINITY;
        int previousScore = 0;

        while (!aborted && searchStopwatch.ElapsedMilliseconds < searchTimeLimitMs)
        {
            depth++;
            currentSearchDepth = depth;
            // For depths 5+, use aspiration windows
            int score;
            Move candidate;

            if (depth >= 5 && evaluator.GetGamePhase(boardLogic) <= 60)
            {
                // We don't want to use a narrow window right in the opening.
                (candidate, score) = SearchWithAspirationWindows(depth, previousScore);
            }
            else
            {
                // Search with full window
                candidate = Search(depth, -INFINITY, INFINITY);
                score = bestScoreForDebug;
            }

            if (aborted || !IsValidMove(candidate))
                break;

            // Update the best move and score
            lastBestMove = candidate;
            previousScore = score;

            // Save the pvTable for this depth - used for move ordering in next depths.
            Array.Copy(pvTable, savedPVTable, pvTable.Length);
            Array.Copy(pvLength, savedPVLength, pvLength.Length);
        }

        float nps = nodesSearched / (TIME_LIMIT / 1000f);
        float ttHitRate = (ttProbes > 0) ? (tt.Hits * 100f / ttProbes) : 0;

        // Update public debugging information
        LastDepthReached = depth;
        LastNps = nps;
        LastTtHitRate = ttHitRate;

        searchStopwatch.Stop();
        return lastBestMove;
    }

    private (Move bestMove, int score) SearchWithAspirationWindows(int depth, int previousScore)
    {
        // Start with a narrow window around the previous score
        int delta = 25;
        int alpha = previousScore - delta;
        int beta = previousScore + delta;

        int failHighCount = 0;
        int failLowCount = 0;

        while (true)
        {
            Move candidate = Search(depth, alpha, beta);
            int score = bestScoreForDebug;

            if (aborted)
            {
                return (candidate, score);
            }

            // Check if we failed high
            if (score >= beta)
            {
                failHighCount++;

                // Widen the window upwards
                beta = Math.Min(beta + delta * (1 << failHighCount), INFINITY);

                // If we've failed high multiple times, just use infinite window
                if (failHighCount >= 3)
                {
                    beta = INFINITY;
                }
                continue;
            }

            // Check if we failed low
            if (score <= alpha)
            {
                failLowCount++;

                // Widen the window downward
                alpha = Math.Max(alpha - delta * (1 << failLowCount), -INFINITY);

                // Also widen beta slightly to avoid another fail-high immediately
                beta = score + delta;

                // If we've failed low multiple times, just use infinite window
                if (failLowCount >= 3)
                {
                    alpha = -INFINITY;
                    beta = INFINITY;
                }

                continue; // Re-search with wider window
            }

            // Success! no fails
            return (candidate, score);
        }
    }

    private Move Search(int depth, int alpha, int beta)
    {
        if (aborted) return new Move();

        Move[] moves = moveList[0];
        int movesCount = boardLogic.moveCalculator.GenerateAllMoves(moves, boardLogic.turn);

        if (movesCount == 0)
        {
            // if (boardLogic.IsInCheck())
            // {
            //     print($"Checkmate! {1 - boardLogic.turn} wins!");
            // }
            // else
            // {
            //     print("StaleMate!");
            // }
            return new Move(); // Stalemate or checkmate
        }
        if (movesCount == 1)
            return moves[0];

        Move ttMove = tt.GetBestMove(boardLogic.zobristKey);
        Move pvMove = (savedPVLength[0] > 0) ? savedPVTable[0, 0] : new Move();
        OrderMoves(moves, movesCount, ttMove, pvMove, 0);

        Move bestMove = new Move();

        int bestScore = -INFINITY;
        int originalAlpha = alpha;

        // Get score for each move from the root node
        for (int i = 0; i < movesCount; i++)
        {
            Move move = moves[i];
            MoveState state = SaveMoveState(0);
            boardLogic.moveExecuter.MakeMove(move);
            nodesSearched++;

            int score = -RecursiveSearch(depth - 1, -beta, -alpha, 1, true);

            //print($"Move {boardLogic.MoveToSAN(move)} -> {score}");
            RestoreMoveState(move, state);

            if (score > bestScore)
            {
                bestScore = score;
                bestMove = move;

                if (score > bestScoreForDebug)
                {
                    bestScoreForDebug = score;
                }

                // PV handling at root - this ply's table depends on the next ply's table (at deeper depth)
                pvLength[0] = pvLength[1] + 1;
                pvTable[0, 0] = move;
                for (int j = 0; j < pvLength[1]; j++)
                {
                    pvTable[0, j + 1] = pvTable[1, j];
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


    private int RecursiveSearch(int depth, int alpha, int beta, int ply, bool allowNullMove)
    {
        // Update seldepth variable for debugging
        if (ply > seldepth)
            seldepth = ply;

        int originalAlpha = alpha;

        // Reached maximum ply - return the static evaluation
        if (ply == MAX_PLY)
            return GetCachedEvaluation();

        // Abort if time is up
        if (searchStopwatch.ElapsedMilliseconds >= searchTimeLimitMs)
        {
            aborted = true;
            //return GetCachedEvaluation();
            return -MATE_SCORE * (boardLogic.turn == aiColor ? 1 : -1); // Return a really bad score - we didn't get to evaluate it.
        }

        // Check for threefold repetition
        ulong zobristKey = boardLogic.zobristKey;

        if (boardLogic.positionHistory != null && boardLogic.positionHistory.Count > 0)
        {
            ulong currentKey = boardLogic.zobristKey;
            int count = boardLogic.positionCounter.TryGetValue(boardLogic.zobristKey, out int v) ? v : 0;
            
            if (count >= 3)
            {
                return 50; // draws are not interesting - a bit of penalty.
            }
            else if (count == 2)
            {
                return GetCachedEvaluation() - 50 * (boardLogic.turn == 0 ? -1 : 1);
            }
        }
        if (boardLogic.attackCalculator.HasInsufficientMaterial())
            return 0;

        // Probe transposition table
        ttProbes++;
        if (tt.Probe(zobristKey, (byte)depth, (short)alpha, (short)beta, ply, out short ttScore, out Move ttMove))
        {
            return ttScore;
        }

        Move[] moves = moveList[ply];
        int movesCount = boardLogic.moveCalculator.GenerateAllMoves(moves, boardLogic.turn);

        if (movesCount == 0)
        {
            if (boardLogic.IsInCheck())
                return -MATE_SCORE + ply; // Mate is less bad the further away it is
            else
                return 0; // stalemate
        }

        if (boardLogic.IsInCheck())
        {
            depth += 1;
        }

        if (depth == 0)
        {
            // Continue the search for captures, to not end up biased towards whoever captured last
            return QuiescenceSearch(alpha, beta);
        }

        // Null move pruning
        bool inCheck = boardLogic.IsInCheck();
        ulong fiendlyBitboard = (boardLogic.turn == 0) ? boardLogic.Wbitboard : boardLogic.Bbitboard;
        bool hasNonPawnMaterial = ((fiendlyBitboard & ~boardLogic.bitboards[boardLogic.turn, Piece.Pawn - 1]) != 0UL);

        if (allowNullMove &&
            !inCheck &&
            hasNonPawnMaterial &&
            depth >= 3 &&
            beta - alpha > 1)
        {
            // Make a "null move" - just switch turns
            MoveState nullState = SaveMoveState(ply);
            boardLogic.turn = (short)(1 - boardLogic.turn);
            boardLogic.zobristKey ^= Zobrist.blackToMoveKey;

            // Clear en passant if it exists
            if (boardLogic.enPassantSquare != 0)
            {
                int epSquare = BitScan.TrailingZeroCount(boardLogic.enPassantSquare);
                boardLogic.zobristKey ^= Zobrist.enPassantFileKey[epSquare % 8];
                boardLogic.enPassantSquare = 0;
            }

            // Search with reduced depth
            int R = depth >= 6 ? 3 : 2;
            int nullScore = -RecursiveSearch(depth - 1 - R, -beta, -beta + 1, ply + 1, false);

            // Restore the position
            boardLogic.turn = (short)(1 - boardLogic.turn);
            boardLogic.zobristKey = nullState.zobristKey;
            boardLogic.enPassantSquare = nullState.enPassantSquare;

            RestoreMoveState(new Move(0, 0, 0), nullState);

            // If null move caused a beta cutoff, prune this branch
            if (nullScore >= beta)
            {
                // Null moves can preduce false mate scores - if it looks like a mate, ignore it.
                if (nullScore > MATE_SCORE - MAX_PLY)
                    return beta;
                return nullScore;
            }
        }

        // Reverse futility pruning
        if (!inCheck &&
            depth <= 3 &&
            beta - alpha <= 1)
        {
            int staticEval = GetCachedEvaluation();
            int margin = 70 * depth;
            if (staticEval - margin >= beta)
                return staticEval - margin;
        }

        // If previous depths saved a move at this ply - get this move and order it first
        Move pvMove = new Move();
        if (savedPVLength[0] > ply)
        {
            pvMove = savedPVTable[0, ply];
        }

        OrderMoves(moves, movesCount, ttMove, pvMove, ply);

        int bestScore = -INFINITY;
        Move bestMove = new Move();

        for (int i = 0; i < movesCount; i++)
        {
            if (aborted) break; // bubble up abort

            Move move = moves[i];
            MoveState state = SaveMoveState(ply);
            bool wasInCheck = boardLogic.IsInCheck();

            boardLogic.moveExecuter.MakeMove(move);
            nodesSearched++;

            int score = SearchLMR(move, i, depth, alpha, beta, ply, wasInCheck);

            RestoreMoveState(move, state);

            if (aborted) return score;

            // Found a new best move - update it
            if (score > bestScore)
            {
                bestScore = score;
                bestMove = move;
            }

            if (score >= beta)
            {
                // Beta cutoff - store as lower bound
                tt.Store(zobristKey, (short)beta, (byte)depth, TTEntryType.LowerBound, move);

                // Update killer moves for quiet moves
                if (move.capturedPiece == 0)
                {
                    UpdateKillerMove(move, ply);

                    // Update history table
                    historyTable[move.from, move.to] += depth * depth;
                }

                return beta;
            }

            if (score > alpha)
            {
                alpha = score;

                // ---- PV Handling ----
                pvLength[ply] = pvLength[ply + 1] + 1;
                pvTable[ply, 0] = move;
                for (int j = 0; j < pvLength[ply + 1]; j++)
                {
                    pvTable[ply, j + 1] = pvTable[ply + 1, j];
                }
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
        tt.Store(zobristKey, (short)bestScore, (byte)depth, entryType, bestMove);


        return bestScore;
    }

    private int SearchLMR(Move move, int moveIndex, int depth, int alpha, int beta,
    int ply, bool wasInCheck)
    {
        bool canReduce = moveIndex >= 3 &&                      // The move appears late in the ordering
                         depth >= 3 &&                          // The search is deep
                         move.capturedPiece == 0 &&             // We're not capturing anything
                         move.flag != (int)MoveFlag.Promotion &&// Not promoting
                         !boardLogic.IsInCheck() &&             // We're not checking anyone
                         !wasInCheck &&                         // We weren't in check before moving
                         evaluator.GetGamePhase(boardLogic) <= 60; // This is not the beginning of the game

        if (canReduce)
        {
            // Calculate reduction
            double baseReduction = Math.Log(depth) * Math.Log(moveIndex) / 2.5;
            int reduction = (int)Math.Ceiling(baseReduction);
            reduction = Math.Max(1, Math.Min(reduction, depth - 2));

            // Scout search with reduced depth
            int score = -RecursiveSearch(depth - 1 - reduction, -alpha - 1, -alpha, ply + 1, true);

            // If scout failed high, do full-depth full-window search
            if (score > alpha)
                score = -RecursiveSearch(depth - 1, -beta, -alpha, ply + 1, true);

            return score;
        }
        else
        {
            // Full depth, full window search
            return -RecursiveSearch(depth - 1, -beta, -alpha, ply + 1, true);
        }
    }


    private int QuiescenceSearch(int alpha, int beta, int qPly = 0)
    {
        int ply = currentSearchDepth + qPly;
        if (ply > seldepth)
            seldepth = ply;

        if (ply >= MAX_PLY) // Cut off the search
        {
            return GetCachedEvaluation();
        }

        pvLength[ply] = 0;

        int originalAlpha = alpha;
        ulong zobristKey = boardLogic.zobristKey;

        // Probe transposition table
        ttProbes++;
        if (tt.Probe(zobristKey, 0, (short)alpha, (short)beta, 0, out short ttScore, out _))
        {
            return ttScore;
        }

        // Time check
        if (searchStopwatch.ElapsedMilliseconds >= searchTimeLimitMs)
        {
            aborted = true;
            return GetCachedEvaluation();
            //return -INFINITY * (boardLogic.turn == aiColor ? 1 : -1);
        }

        // Generate only capture moves
        Move[] captures = captureList[qPly];
        int captureCount = boardLogic.moveCalculator.GenerateAllCaptures(captures, boardLogic.turn);

        int allMovesCount = -1;
        if (captureCount == 0)
        {
            allMovesCount = boardLogic.moveCalculator.GenerateAllMoves(null, boardLogic.turn);
        }

        if (allMovesCount == 0)
        {
            if (boardLogic.IsInCheck())
                return -MATE_SCORE + ply; // Checkmate
            else
                return 0; // Stalemate
        }

        // Get stand-pat score (static evaluation)
        int standPatScore = GetCachedEvaluation();

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


        // Sort captures by MVV-LVA (Most Valuable Victim - Least Valuable Attacker)
        Array.Sort(captures, 0, captureCount, Comparer<Move>.Create((move1, move2) =>
        {
            int score1 = GetCaptureScore(move1);
            int score2 = GetCaptureScore(move2);
            return score2.CompareTo(score1);
        }));

        for (int i = 0; i < captureCount; i++)
        {
            Move captureMove = captures[i];

            int captureValue = GetPieceValue(captureMove.capturedPiece, captureMove.to);
            if (standPatScore + captureValue < alpha - 200)
            {
                continue; // Skip if even the capture can't raise alpha
            }

            MoveState state = SaveMoveState(ply);
            boardLogic.moveExecuter.MakeMove(captureMove);
            nodesSearched++;

            int score = -QuiescenceSearch(-beta, -alpha, qPly + 1);

            RestoreMoveState(captureMove, state);

            if (aborted) return GetCachedEvaluation();

            if (score >= beta)
            {
                // Store score as lower bound
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

    private void UpdateKillerMove(Move move, int ply)
    {
        if (ply >= MAX_PLY)
            return;

        // Don't store the same move twice
        if (MovesEqual(killerMoves[ply, 0], move))
            return;

        // Shift moves: killer1 becomes killer2, new move becomes killer1
        killerMoves[ply, 1] = killerMoves[ply, 0];
        killerMoves[ply, 0] = move;
    }

    private int GetCachedEvaluation()
    {
        if (evalCache.TryGetValue(boardLogic.zobristKey, out int cachedScore))
            return cachedScore * (boardLogic.turn == 0 ? 1 : -1);

        int score = evaluator.GetScore(boardLogic);
        evalCache[boardLogic.zobristKey] = score;
        return score * (boardLogic.turn == 0 ? 1 : -1);
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

    private void OrderMoves(Move[] moves, int moveCount, Move ttMove = new Move(), Move pvMove = new Move(), int ply = 0)
    {
        // 1. Single pass to score all moves
        for (int i = 0; i < moveCount; i++)
        {
            Move m = moves[i];
            int score;

            if (MovesEqual(m, pvMove))
            {
                score = 20_000_000; // 1. PV move is highest priority
            }
            else if (MovesEqual(m, ttMove))
            {
                score = 19_000_000; // 2. TT move is next
            }
            else if (m.capturedPiece != 0)
            {
                // Add SEE (Static Exchange Evaluation) check here
                int seeScore = StaticExchangeEvaluation(m);
                if (seeScore < 0)
                {
                    // Losing capture - deprioritize
                    score = 1_000_000 + seeScore;
                }
                else
                {
                    score = 10_000_000 + GetCaptureScore(m);
                }
            }
            else // Quiet moves
            {
                if (ply < MAX_PLY)
                {
                    if (MovesEqual(m, killerMoves[ply, 0]))
                    {
                        score = 9_000_000; // 4. First killer move
                    }
                    else if (MovesEqual(m, killerMoves[ply, 1]))
                    {
                        score = 8_000_000; // 5. Second killer move
                    }
                    else
                    {
                        score = historyTable[m.from, m.to]; // 6. History heuristic
                    }
                }
                else
                {
                    score = historyTable[m.from, m.to]; // 6. History heuristic
                }
            }

            // Negate score for descending sort
            moveScores[i] = -score;
        }

        Array.Sort(moveScores, moves, 0, moveCount);
    }

    // Added
    private int StaticExchangeEvaluation(Move move)
    {
        if (move.capturedPiece == 0) return 0;

        int gain = GetBasicPieceValue(move.capturedPiece);
        int risk = GetBasicPieceValue(move.movedPiece);

        // Simple SEE: if the target square is defended and our piece is more valuable,
        // it's likely a bad trade
        bool targetDefended = IsSquareAttacked(move.to, 1 - boardLogic.turn);

        if (targetDefended && risk > gain)
        {
            return gain - risk; // Negative score for losing trades
        }

        return gain; // Positive for winning trades
    }

    private bool IsSquareAttacked(int square, int byColor)
    {
        ulong squareBit = 1UL << square;
        return (boardLogic.attackedSquares[byColor] & squareBit) != 0;
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

    private MoveState SaveMoveState(int ply)
    {
        MoveState state = new MoveState();

        state.zobristKey = boardLogic.zobristKey;
        state.enPassantSquare = boardLogic.enPassantSquare;
        state.castlingRights = boardLogic.castlingRights;
        state.currentCastling = boardLogic.currentCastling;
        state.checkMap = boardLogic.checkMap;
        state.gameEnded = boardLogic.gameEnded;
        state.halfMoveClock = boardLogic.halfMoveClock;

        // Deep copy arrays
        state.castled = new bool[2];
        Array.Copy(boardLogic.castled, state.castled, 2);
        Array.Copy(boardLogic.pinRays, allocatedPinRays[ply], 64);

        Array.Copy(boardLogic.doubleCheck, allocatedDoubleCheck[ply], 2);

        Array.Copy(boardLogic.attackedSquares, allocatedAttackedSquares[ply], 2);

        state.pinRays = allocatedPinRays[ply];
        state.doubleCheck = allocatedDoubleCheck[ply];
        state.attackedSquares = allocatedAttackedSquares[ply];

        return state;
    }

    private void RestoreMoveState(Move move, MoveState state)
    {
        // 1. Unmake the physical move on the board
        if (move.from != 0 || move.to != 0 || move.movedPiece != 0)
            boardLogic.moveExecuter.UnmakeMove(move, state.castlingRights, state.enPassantSquare, state.zobristKey);

        // 2. Restore the entire derived state from the saved struct.
        // This is now the single point of state restoration.
        boardLogic.currentCastling = state.currentCastling;
        boardLogic.castlingRights = state.castlingRights;
        boardLogic.checkMap = state.checkMap;
        boardLogic.gameEnded = state.gameEnded;
        boardLogic.halfMoveClock = state.halfMoveClock;
        Array.Copy(state.castled, boardLogic.castled, 2);
        Array.Copy(state.pinRays, boardLogic.pinRays, 64);
        Array.Copy(state.doubleCheck, boardLogic.doubleCheck, 2);
        Array.Copy(state.attackedSquares, boardLogic.attackedSquares, 2);
    }


    public string GetPVLine()
    {
        if (savedPVLength[0] == 0) return "";
        List<string> sanMoves = new List<string>();
        for (int i = 0; i < savedPVLength[0]; i++)
        {
            sanMoves.Add(boardLogic.MoveToSAN(savedPVTable[0, i]));
        }
        return string.Join(" ", sanMoves);
    }
}