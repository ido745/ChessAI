using UnityEngine;

public class Evaluate : MonoBehaviour
{
    // The values for the pieces
    private const int Pawn = 100;
    private const int Knight = 320;
    private const int Bishop = 330;
    private const int Rook = 500;
    private const int Queen = 900;

    public int GetScore(BoardLogic boardLogic)
    {
        int score = 0;

        score += CountMaterial(boardLogic, 0) - CountMaterial(boardLogic, 1);
        score += EvaluatePieceSquareTables(boardLogic, 0) - EvaluatePieceSquareTables(boardLogic, 1);

        if (boardLogic.IsInCheck())
        {
            // The person who's turn it is is in check.
            score += 50 * (boardLogic.turn == 0 ? 1 : -1);
        }
        return score;
    }

    private int CountMaterial(BoardLogic boardLogic, int color)
    {
        int materialScore = 0;
        materialScore += BitScan.PopCount(boardLogic.bitboards[color, Piece.Pawn - 1]) * Pawn;
        materialScore += BitScan.PopCount(boardLogic.bitboards[color, Piece.Knight - 1]) * Knight;
        materialScore += BitScan.PopCount(boardLogic.bitboards[color, Piece.Bishop - 1]) * Bishop;
        materialScore += BitScan.PopCount(boardLogic.bitboards[color, Piece.Rook - 1]) * Rook;
        materialScore += BitScan.PopCount(boardLogic.bitboards[color, Piece.Queen - 1]) * Queen;
        return materialScore;
    }

    private int EvaluatePieceSquareTables(BoardLogic boardLogic, int color)
    {
        int score = 0;

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
                        score += PieceSquareTables.pawnTable[lookupPos];
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
                        score += PieceSquareTables.kingTable[lookupPos];
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
}
