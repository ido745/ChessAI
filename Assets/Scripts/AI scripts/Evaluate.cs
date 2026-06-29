using System;
using System.IO;

public class Evaluate
{
    // The values for the pieces
    private const int Pawn = 100;
    private const int Knight = 320;
    private const int Bishop = 330;
    private const int Rook = 500;
    private const int Queen = 900;

    BoardLogic boardLogic;

    public int GetScore(BoardLogic passedBoardLogic, int alpha = -999999, int beta = 999999)
    {
        boardLogic = passedBoardLogic;

        int us = 0;
        int them = 1 - us;

        int score = CountMaterial(us) - CountMaterial(them);
        score += EvaluatePieceSquareTables(us) - EvaluatePieceSquareTables(them);

        // If score is far outside alpha-beta window, return early
        if (score + 300 < alpha || score - 300 > beta)
            return score;

        score += CountPins(us) - CountPins(them);
        score += EvaluatePawnStructure(us) - EvaluatePawnStructure(them);
        score += EvaluateKingSafety(us) - EvaluateKingSafety(them);
        score += EvaluateMobility(us) - EvaluateMobility(them);
        score += EvaluateDevelopment(us) - EvaluateDevelopment(them);
        score += EvaluateMopUp(boardLogic);

        score += EvaluateConnectedRooks(us) - EvaluateConnectedRooks(them);
        score += EvaluateRookFiles(us) - EvaluateRookFiles(them);

        score += EvaluateBishopPair(us) - EvaluateBishopPair(them);

        int gamePhase = GetGamePhase();

        if (gamePhase > 50) // Opening
            return (int)((float)score * 1.186f);
        else if (gamePhase > 20) // Middle game
            return (int)((float)score * 0.966f);
        else // End game
            return (int)((float)score * 0.81f);
    }

    private int CountMaterial(int color)
    {
        int materialScore = 0;
        materialScore += boardLogic.numOfPieces[color, Piece.Pawn - 1] * Pawn;
        materialScore += boardLogic.numOfPieces[color, Piece.Knight - 1] * Knight;
        materialScore += boardLogic.numOfPieces[color, Piece.Bishop - 1] * Bishop;
        materialScore += boardLogic.numOfPieces[color, Piece.Rook - 1] * Rook;
        materialScore += boardLogic.numOfPieces[color, Piece.Queen - 1] * Queen;
        return materialScore;
    }

    private int EvaluatePieceSquareTables(int color)
    {
        int score = 0;

        int gamePhase = GetGamePhase();

        for (int pieceType = Piece.King; pieceType <= Piece.Queen; pieceType++)
        {
            ulong pieceBitboard = boardLogic.bitboards[color, pieceType - 1];
            while (pieceBitboard != 0)
            {
                int pos = BitScan.TrailingZeroCount(pieceBitboard);
                int lookupPos = pos;

                // Flip the evaluation table for black
                if (color == 1) { lookupPos = pos ^ 56; }

                switch (pieceType)
                {
                    case Piece.Pawn:
                        score += EvaluatePawnTable(lookupPos);
                        break;
                    case Piece.Knight:
                        score += PieceSquareTables.knightTable[lookupPos];
                        break;
                    case Piece.Bishop:
                        score += PieceSquareTables.bishopTable[lookupPos];
                        break;
                    case Piece.Rook:
                        score += PieceSquareTables.rookTable[lookupPos];
                        break;
                    case Piece.Queen:
                        score += PieceSquareTables.queenTable[lookupPos];
                        break;
                    case Piece.King:
                        score += EvaluateKingTable(lookupPos);
                        break;
                    default:
                        break;
                }

                // Remove this piece from the bitboard to process the next one
                pieceBitboard = BitScan.ClearBit(pieceBitboard, pos);
            }
        }

        return score;
    }

    private int EvaluateDevelopment(int color)
    {
        int score = 0;

        // Penalty for unmoved pieces in opening
        if (color == 0) // White
        {
            if ((boardLogic.bitboards[0, Piece.Knight - 1] & (1UL << 1)) != 0) score -= 20; // b1 knight
            if ((boardLogic.bitboards[0, Piece.Knight - 1] & (1UL << 6)) != 0) score -= 20; // g1 knight
            if ((boardLogic.bitboards[0, Piece.Bishop - 1] & (1UL << 2)) != 0) score -= 15; // c1 bishop
            if ((boardLogic.bitboards[0, Piece.Bishop - 1] & (1UL << 5)) != 0) score -= 15; // f1 bishop
        }
        else // Black (similar logic with different squares)
        {
            if ((boardLogic.bitboards[1, Piece.Knight - 1] & (1UL << 57)) != 0) score -= 20;
            if ((boardLogic.bitboards[1, Piece.Knight - 1] & (1UL << 62)) != 0) score -= 20;
            if ((boardLogic.bitboards[1, Piece.Bishop - 1] & (1UL << 58)) != 0) score -= 15;
            if ((boardLogic.bitboards[1, Piece.Bishop - 1] & (1UL << 61)) != 0) score -= 15;
        }

        if (boardLogic.castled[color])
            score += 30;

        return score;
    }

    private int CountPins(int color)
    {
        int score = 0;
        ulong pinnedPieces = 0UL;

        ulong bitboardByColor = (color == 0 ? boardLogic.Wbitboard : boardLogic.Bbitboard);
        // Find all pinned pieces for this color
        while (bitboardByColor != 0)
        {
            int nextIndex = BitScan.TrailingZeroCount(bitboardByColor);
            if (boardLogic.pinRays[nextIndex] != 0UL)
            {
                // we got a pinned piece
                pinnedPieces |= (1UL << nextIndex);
            }

            bitboardByColor &= bitboardByColor - 1;
        }

        // Apply penalties based on piece type (much faster)
        score -= BitScan.PopCount(boardLogic.bitboards[color, Piece.Knight - 1] & pinnedPieces) * 60;
        score -= BitScan.PopCount(boardLogic.bitboards[color, Piece.Bishop - 1] & pinnedPieces) * 40;
        score -= BitScan.PopCount(boardLogic.bitboards[color, Piece.Rook - 1] & pinnedPieces) * 100;
        score -= BitScan.PopCount(boardLogic.bitboards[color, Piece.Queen - 1] & pinnedPieces) * 60;

        return score;
    }

    public int GetGamePhase(BoardLogic passedBoardLogic = null)
    {
        if (passedBoardLogic == null)
            passedBoardLogic = boardLogic;

        // Calculate material (excluding pawns and kings)
        int material = 0;

        // Count material for both sides
        for (int color = 0; color < 2; color++)
        {
            material += BitScan.PopCount(passedBoardLogic.bitboards[color, Piece.Knight - 1]) * 3;
            material += BitScan.PopCount(passedBoardLogic.bitboards[color, Piece.Bishop - 1]) * 3;
            material += BitScan.PopCount(passedBoardLogic.bitboards[color, Piece.Rook - 1]) * 5;
            material += BitScan.PopCount(passedBoardLogic.bitboards[color, Piece.Queen - 1]) * 9;
        }

        return material; // 0 = endgame, 78 = opening (full material)
    }

    private int EvaluateKingTable(int kingPos)
    {
        int gamePhase = GetGamePhase();

        if (gamePhase >= 20)
        {
            // Middlegame: keep king safe
            return PieceSquareTables.kingTable[kingPos];
        }
        else if (gamePhase <= 10)
        {
            // Endgame: centralize king
            return PieceSquareTables.kingEndTable[kingPos];
        }
        else
        {
            // Transition phase: interpolate
            float endgameWeight = (20f - gamePhase) / 10f; // 0.0 to 1.0

            int middlegameScore = PieceSquareTables.kingTable[kingPos];
            int endgameScore = PieceSquareTables.kingEndTable[kingPos];

            return (int)(middlegameScore * (1f - endgameWeight) + endgameScore * endgameWeight);
        }
    }

    private int EvaluatePawnTable(int pawnPos)
    {
        int gamePhase = GetGamePhase();

        if (gamePhase >= 30)
        {
            // Middlegame: keep king safe
            return PieceSquareTables.pawnTable[pawnPos];
        }
        else if (gamePhase <= 20)
        {
            // Endgame: centralize king
            return PieceSquareTables.pawnTableEnd[pawnPos];
        }
        else
        {
            // Transition phase: interpolate
            float endgameWeight = (20f - gamePhase) / 10f; // 0.0 to 1.0

            int middlegameScore = PieceSquareTables.pawnTable[pawnPos];
            int endgameScore = PieceSquareTables.pawnTableEnd[pawnPos];

            return (int)(middlegameScore * (1f - endgameWeight) + endgameScore * endgameWeight);
        }
    }

    private int EvaluateMopUp(BoardLogic boardLogic)
    {
        int whiteMatAdvantage = CountMaterial(0) - CountMaterial(1);
        int blackMatAdvantage = -whiteMatAdvantage;

        int mopUpScore = 0;

        // Only apply mop-up evaluation when one side has significant material advantage
        if (whiteMatAdvantage >= 500) // White is significantly ahead
        {
            mopUpScore += CalculateMopUpScore(0, 1); // White winning vs Black
        }
        else if (blackMatAdvantage >= 500) // Black is significantly ahead
        {
            mopUpScore -= CalculateMopUpScore(1, 0); // Black winning vs White
        }

        return mopUpScore;
    }

    private int CalculateMopUpScore(int strongSide, int weakSide)
    {
        int strongKingPos = BitScan.TrailingZeroCount(boardLogic.bitboards[strongSide, Piece.King - 1]);
        int weakKingPos = BitScan.TrailingZeroCount(boardLogic.bitboards[weakSide, Piece.King - 1]);

        int mopUpScore = 0;

        // 1. Reward bringing the strong king closer to weak king
        int kingDistance = GetManhattanDistance(strongKingPos, weakKingPos);
        mopUpScore += (14 - kingDistance) * 10; // Max 140 points for adjacent kings

        // 2. Reward driving the weak king to the edge
        int weakKingDistanceFromCenter = GetDistanceFromCenter(weakKingPos);
        mopUpScore += weakKingDistanceFromCenter * 15;

        // 4. Bonus for keeping pieces close to the weak king for checkmate
        mopUpScore += EvaluatePieceProximityToWeakKing(boardLogic, strongSide, weakKingPos);

        return mopUpScore;
    }

    private int GetManhattanDistance(int square1, int square2)
    {
        int file1 = square1 % 8, rank1 = square1 / 8;
        int file2 = square2 % 8, rank2 = square2 / 8;
        return Math.Abs(file1 - file2) + Math.Abs(rank1 - rank2);
    }

    private int GetDistanceFromCenter(int square)
    {
        int file = square % 8;
        int rank = square / 8;
        int distanceFromCenterFile = Math.Min(Math.Abs(file - 4), Math.Abs(3 - file));
        int distanceFromCenterRank = Math.Min(Math.Abs(rank - 4), Math.Abs(3 - rank));
        return distanceFromCenterFile + distanceFromCenterRank;
    }

    private int EvaluatePieceProximityToWeakKing(BoardLogic boardLogic, int strongSide, int weakKingPos)
    {
        int proximityScore = 0;

        // Check all strong side pieces and reward them for being close to weak king
        for (int pieceType = Piece.Pawn; pieceType <= Piece.Queen; pieceType++)
        {
            ulong pieceBitboard = boardLogic.bitboards[strongSide, pieceType - 1];

            while (pieceBitboard != 0)
            {
                int piecePos = BitScan.TrailingZeroCount(pieceBitboard);
                int distance = GetManhattanDistance(piecePos, weakKingPos);

                // Closer pieces get more points, with different weights per piece type
                int proximityWeight = pieceType switch
                {
                    Piece.Queen => 5,   // Queen is most important for checkmate
                    Piece.Rook => 4,    // Rooks are crucial for back-rank mates
                    Piece.Bishop => 2,  // Bishops help control squares
                    Piece.Knight => 2,  // Knights for complex mates
                    Piece.Pawn => 1,    // Pawns help cut off escape squares
                    _ => 0
                };

                proximityScore += (8 - distance) * proximityWeight;
                pieceBitboard &= pieceBitboard - 1;
            }
        }

        return proximityScore;
    }

    // bitmaps to check for isolated pawns at each file.
    private ulong[] checkIsolated = new ulong[8]
    {
        0x0202020202020202,
        0x0505050505050505,
        0x0A0A0A0A0A0A0A0A,
        0x1414141414141414,
        0x2828282828282828,
        0x5050505050505050,
        0xA0A0A0A0A0A0A0A0,
        0x4040404040404040
    };

    // bitmaps to check for passed pawns at each file.
    private ulong[] fileMask = new ulong[8]
    {
        0x0101010101010101,
        0x0202020202020202,
        0x0404040404040404,
        0x0808080808080808,
        0x1010101010101010,
        0x2020202020202020,
        0x4040404040404040,
        0x8080808080808080
    };

    private int EvaluatePawnStructure(int color)
    {
        int score = 0;

        ulong pawnBitboard = boardLogic.bitboards[color, Piece.Pawn - 1];
        ulong originalBitboard = pawnBitboard;
        ulong enemyBitboard = (color == 0) ? boardLogic.Bbitboard : boardLogic.Wbitboard;
        
        while (pawnBitboard != 0UL)
        {
            int pos = BitScan.TrailingZeroCount(pawnBitboard);
            int file = pos % 8;

            // Check for isolated pawns.
            if ((originalBitboard & checkIsolated[file]) == 0UL)
                score -= 15;

            // Check for double pawns
            int rank = pos / 8;
            if (rank < 6 && ((1UL << (pos + 8)) & originalBitboard) != 0UL)
                score -= 20;

            // Check for passed pawns
            ulong passedMask = 0UL;
            if (color == 0)
                passedMask = (fileMask[file] | checkIsolated[file]) & (0xFFFFFFFFFFFFFFFFUL << (pos + 8));
            else
                passedMask = (fileMask[file] | checkIsolated[file]) & (0xFFFFFFFFFFFFFFFFUL >> (64 - pos));

            if ((enemyBitboard & passedMask) == 0UL)
            {
                int advancedRank = color == 0 ? rank : 7 - rank;
                score += 30 + (advancedRank * 10);
            } else if ((boardLogic.bitboards[1 - color, Piece.Pawn - 1] & passedMask) == 0UL)
            {
                int advancedRank = color == 0 ? rank : 7 - rank;
                score += 10 + (advancedRank * 10);
            }
            

            // Check for pawn chains
            ulong connectedSquare = Magic.GetPawnCapturesOnly(pos, color);
            if ((connectedSquare & originalBitboard) != 0UL)
                score += 5 * BitScan.PopCount(connectedSquare & originalBitboard);

            pawnBitboard = BitScan.ClearBit(pawnBitboard, pos);
        }
        return score;
    }

    private static readonly int[] kingSafetyTable =
    { 0, 5, 20, 50, 90, 130, 170, 200, 220, 240, 255 };

    private int EvaluateKingSafety(int color)
    {
        int gamePhase = GetGamePhase();
        if (gamePhase < 10) return 0; // pure endgame — king should be active

        // All penalties scale from 0 at gamePhase=10 to full at gamePhase=30+
        float phaseScale = Math.Min(gamePhase, 30) / 30f;

        int score = 0;
        int enemy = 1 - color;
        int kingPos = BitScan.TrailingZeroCount(boardLogic.bitboards[color, Piece.King - 1]);
        ulong kingZone = Magic.GetKingAttacks(kingPos);
        int kingFile = kingPos % 8;
        int kingRank = kingPos / 8;
        ulong allPieces = boardLogic.Wbitboard | boardLogic.Bbitboard;

        // --- Pawn shield (rank-based, phase-scaled) ---
        // Pawns 1 rank in front of king are ideal shields (+20), 2 ranks ahead are weaker (+10)
        for (int f = Math.Max(0, kingFile - 1); f <= Math.Min(7, kingFile + 1); f++)
        {
            ulong ownFilePawns = boardLogic.bitboards[color, Piece.Pawn - 1] & fileMask[f];
            for (int dist = 1; dist <= 2; dist++)
            {
                int checkRank = color == 0 ? kingRank + dist : kingRank - dist;
                if (checkRank < 0 || checkRank > 7) break;
                if ((ownFilePawns & (0xFFUL << (checkRank * 8))) != 0)
                {
                    score += (int)((dist == 1 ? 20 : 10) * phaseScale);
                    break; // only count closest pawn per file
                }
            }
        }

        // --- Open/semi-open files near king (phase-scaled) ---
        for (int f = Math.Max(0, kingFile - 1); f <= Math.Min(7, kingFile + 1); f++)
        {
            bool ownPawn   = (boardLogic.bitboards[color, Piece.Pawn - 1]  & fileMask[f]) != 0;
            bool enemyPawn = (boardLogic.bitboards[enemy, Piece.Pawn - 1] & fileMask[f]) != 0;
            if (!ownPawn && !enemyPawn) score -= (int)(25 * phaseScale); // open file
            else if (!ownPawn)          score -= (int)(15 * phaseScale); // semi-open file
        }

        // --- Pawn storm: enemy pawns advancing toward our king (phase-scaled) ---
        for (int f = Math.Max(0, kingFile - 1); f <= Math.Min(7, kingFile + 1); f++)
        {
            ulong enemyFilePawns = boardLogic.bitboards[enemy, Piece.Pawn - 1] & fileMask[f];
            if (enemyFilePawns == 0) continue;

            bool ownPawnOnFile = (boardLogic.bitboards[color, Piece.Pawn - 1] & fileMask[f]) != 0;

            // Find the most advanced enemy pawn toward our king
            int dist;
            if (color == 0) // white king: black pawns advance toward lower ranks
            {
                int pawnRank = BitScan.TrailingZeroCount(enemyFilePawns) / 8;
                dist = pawnRank - kingRank;
            }
            else // black king: white pawns advance toward higher ranks
            {
                ulong temp = enemyFilePawns;
                int pawnPos = 0;
                while (temp != 0) { pawnPos = BitScan.TrailingZeroCount(temp); temp &= temp - 1; }
                dist = kingRank - (pawnPos / 8);
            }

            if (dist > 0 && dist <= 3)
            {
                int stormPenalty = dist == 1 ? 20 : dist == 2 ? 10 : 5;
                if (!ownPawnOnFile) stormPenalty += 8; // no blocker makes it more dangerous
                score -= (int)(stormPenalty * phaseScale);
            }
        }

        // --- Enemy piece attack weight ---
        int attackWeight = 0;

        ulong b = boardLogic.bitboards[enemy, Piece.Knight - 1];
        while (b != 0)
        {
            int sq = BitScan.TrailingZeroCount(b);
            if ((Magic.GetKnightAttacks(sq) & kingZone) != 0)
                attackWeight += 2;
            b &= b - 1;
        }
        b = boardLogic.bitboards[enemy, Piece.Bishop - 1];
        while (b != 0)
        {
            int sq = BitScan.TrailingZeroCount(b);
            if ((Magic.GetBishopAttacks(sq, allPieces) & kingZone) != 0)
                attackWeight += 2;
            b &= b - 1;
        }
        b = boardLogic.bitboards[enemy, Piece.Rook - 1];
        while (b != 0)
        {
            int sq = BitScan.TrailingZeroCount(b);
            if ((Magic.GetRookAttacks(sq, allPieces) & kingZone) != 0)
                attackWeight += 3;
            b &= b - 1;
        }
        b = boardLogic.bitboards[enemy, Piece.Queen - 1];
        while (b != 0)
        {
            int sq = BitScan.TrailingZeroCount(b);
            if (((Magic.GetBishopAttacks(sq, allPieces) | Magic.GetRookAttacks(sq, allPieces)) & kingZone) != 0)
                attackWeight += 5;
            b &= b - 1;
        }

        // Without the enemy queen, king attacks are far less dangerous
        if (boardLogic.bitboards[enemy, Piece.Queen - 1] == 0)
            attackWeight /= 2;

        // --- King escape squares (only relevant under active pressure) ---
        if (attackWeight >= 3)
        {
            ulong ownPieces = color == 0 ? boardLogic.Wbitboard : boardLogic.Bbitboard;
            int escapeSquares = BitScan.PopCount(kingZone & ~boardLogic.attackedSquares[enemy] & ~ownPieces);
            if (escapeSquares == 0) score -= 40;
            else if (escapeSquares == 1) score -= 15;
        }

        // --- Apply attack penalty (non-linear, phase-scaled) ---
        int idx = Math.Min(attackWeight, kingSafetyTable.Length - 1);
        score -= (int)(kingSafetyTable[idx] * phaseScale);

        return score;
    }


    
    /*private int EvaluateKingSafety(int color)
    {
        int score = 0;
        int kingPos = BitScan.TrailingZeroCount(boardLogic.bitboards[color, Piece.King - 1]);
        ulong aroundKingMask = Magic.GetKingAttacks(kingPos);

        ulong friends = (color == 0) ? boardLogic.Wbitboard : boardLogic.Bbitboard;

        // Pawns are the strongest defenders for the king
        score += 10 * BitScan.PopCount(boardLogic.bitboards[color, Piece.Pawn - 1] & aroundKingMask);
        // Other pieces can also defend the king
        score += 5 * BitScan.PopCount((friends ^ boardLogic.bitboards[color, Piece.Pawn - 1]) & aroundKingMask);

        // Attacks near the king are dangerous
        score -= 5 * BitScan.PopCount(boardLogic.attackedSquares[1 - color] & aroundKingMask);

        return score;
    }*/

    /*
    private int EvaluateKingSafety(int color)
    {
        int gamePhase = GetGamePhase();
        float phaseScale = Math.Min(gamePhase, 30) / 30f;

        int score = 0;
        int kingPos = BitScan.TrailingZeroCount(boardLogic.bitboards[color, Piece.King - 1]);
        ulong aroundKingMask = Magic.GetKingAttacks(kingPos);
        ulong friends = (color == 0) ? boardLogic.Wbitboard : boardLogic.Bbitboard;

        score += 8 * BitScan.PopCount(boardLogic.bitboards[color, Piece.Pawn - 1] & aroundKingMask);
        score += 5 * BitScan.PopCount((friends ^ boardLogic.bitboards[color, Piece.Pawn - 1]) & aroundKingMask);
        score -= 5 * BitScan.PopCount(boardLogic.attackedSquares[1 - color] & aroundKingMask);

        int kingFile = kingPos % 8;
        int kingRank = kingPos / 8;
        
        // Tiny bonuses for pawn shields. +3 per pawn instead of +20.
        for (int f = Math.Max(0, kingFile - 1); f <= Math.Min(7, kingFile + 1); f++)
        {
            ulong ownFilePawns = boardLogic.bitboards[color, Piece.Pawn - 1] & fileMask[f];
            for (int dist = 1; dist <= 2; dist++)
            {
                int checkRank = color == 0 ? kingRank + dist : kingRank - dist;
                if (checkRank >= 0 && checkRank <= 7)
                {
                    if ((ownFilePawns & (1UL << (checkRank * 8 + f))) != 0)
                    {
                        score += (dist == 1) ? 3 : 1; 
                        break;
                    }
                }
            }
        }

        // --- Open/semi-open files near king (phase-scaled) ---
        for (int f = Math.Max(0, kingFile - 1); f <= Math.Min(7, kingFile + 1); f++)
        {
            bool ownPawn   = (boardLogic.bitboards[color, Piece.Pawn - 1]  & fileMask[f]) != 0;
            bool enemyPawn = (boardLogic.bitboards[1 - color, Piece.Pawn - 1] & fileMask[f]) != 0;
            if (!ownPawn && !enemyPawn) score -= (int)(10 * phaseScale); // open file
            else if (!ownPawn)          score -= (int)(5 * phaseScale); // semi-open file
        }

        return score;
    }*/

    private int EvaluateMobility(int color)
    {
        int gamePhase = GetGamePhase();

        if (gamePhase < 50)
            return EvaluateMobilityMiddleEnd(color);
        else
            return EvaluateMobilityOpening(color);
    }

    private int EvaluateMobilityOpening(int color)
    {
        int score = 0;
        ulong ownPieces  = color == 0 ? boardLogic.Wbitboard : boardLogic.Bbitboard;
        ulong allPieces  = boardLogic.Wbitboard | boardLogic.Bbitboard;

        ulong b = boardLogic.bitboards[color, Piece.Knight - 1];
        while (b != 0) { int sq = BitScan.TrailingZeroCount(b);
            score += BitScan.PopCount(Magic.GetKnightAttacks(sq) & ~ownPieces) * 4;
            b &= b - 1; }

        b = boardLogic.bitboards[color, Piece.Bishop - 1];
        while (b != 0) { int sq = BitScan.TrailingZeroCount(b);
            score += BitScan.PopCount(Magic.GetBishopAttacks(sq, allPieces) & ~ownPieces) * 3;
            b &= b - 1; }

        b = boardLogic.bitboards[color, Piece.Rook - 1];
        while (b != 0) { int sq = BitScan.TrailingZeroCount(b);
            score += BitScan.PopCount(Magic.GetRookAttacks(sq, allPieces) & ~ownPieces) * 2;
            b &= b - 1; }

        b = boardLogic.bitboards[color, Piece.Queen - 1];
        while (b != 0) { int sq = BitScan.TrailingZeroCount(b);
            score += BitScan.PopCount((Magic.GetBishopAttacks(sq, allPieces) | Magic.GetRookAttacks(sq, allPieces)) & ~ownPieces) * 1;
            b &= b - 1; }

        return score;
    }


    private int EvaluateMobilityMiddleEnd(int color)
    {
        ulong ownPieces = (color == 0) ? boardLogic.Wbitboard : boardLogic.Bbitboard;
        int mobility = BitScan.PopCount(boardLogic.attackedSquares[color] & ~ownPieces);

        // Weight mobility more in the middlegame
        int gamePhase = GetGamePhase();
        int mobilityWeight = gamePhase > 15 ? 2 : 2;

        return mobility * mobilityWeight;
    }

    private int EvaluateConnectedRooks(int color)
    {
        ulong rookBitboard = boardLogic.bitboards[color, Piece.Rook - 1];
        int score = 0;

        if (BitScan.PopCount(rookBitboard) < 2)
            return 0;

        ulong allPieces = boardLogic.Wbitboard | boardLogic.Bbitboard;

        while (rookBitboard != 0)
        {
            int pos = BitScan.TrailingZeroCount(rookBitboard);

            rookBitboard = BitScan.ClearBit(rookBitboard, pos);

            ulong rookAttacks = Magic.GetRookAttacks(pos, allPieces);

            // Check if this rook connects to another rook
            if ((rookAttacks & rookBitboard) != 0)
                score += 30; // Connected rooks bonus
        }
        return score;
    }

    private int EvaluateRookFiles(int color)
    {
        int score = 0;
        ulong rookBitboard = boardLogic.bitboards[color, Piece.Rook - 1];

        while (rookBitboard != 0)
        {
            int pos = BitScan.TrailingZeroCount(rookBitboard);
            int file = pos % 8;

            ulong fileSquares = fileMask[file];
            bool hasOwnPawns = (boardLogic.bitboards[color, Piece.Pawn - 1] & fileSquares) != 0;
            bool hasEnemyPawns = (boardLogic.bitboards[1 - color, Piece.Pawn - 1] & fileSquares) != 0;

            if (!hasOwnPawns && !hasEnemyPawns)
                score += 30; // Open file
            else if (!hasOwnPawns && hasEnemyPawns)
                score += 15; // Semi-open file

            rookBitboard = BitScan.ClearBit(rookBitboard, pos);
        }
        return score;
    }

    private int EvaluateBishopPair(int color)
    {
        if (BitScan.PopCount(boardLogic.bitboards[color, Piece.Bishop - 1]) >= 2)
            return 30; // Bishop pair is very strong
        return 0;
    }
}
