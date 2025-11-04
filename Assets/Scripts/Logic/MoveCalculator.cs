using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MoveCalculator
{
    private BoardLogic boardLogic;

    public MoveCalculator(BoardLogic boardLogic)
    {
        this.boardLogic = boardLogic;
    }

    public ulong GenerateMoves(int pos, int piece, bool ignoreOtherKing = false)
    {
        if (pos < 0 || pos >= 64) return 0UL;
        if (piece == 0) return 0UL;

        int color = Piece.IsBlack(piece);
        int type = Piece.GetPieceType(piece);
        ulong allOccupancy = boardLogic.Wbitboard | boardLogic.Bbitboard;
        ulong friendly = (color == 0) ? boardLogic.Wbitboard : boardLogic.Bbitboard;
        ulong attacks = 0UL;

        if (ignoreOtherKing) { allOccupancy &= (~boardLogic.bitboards[1 - color, Piece.King - 1]); }

        // If we're in double check, only the king can move
        if (boardLogic.doubleCheck[color] && type != Piece.King)
            return 0UL;

        switch (type)
        {
            // Sliding pieces
            case Piece.Rook:
                attacks = Magic.GetRookAttacks(pos, allOccupancy);
                break;
            case Piece.Bishop:
                attacks = Magic.GetBishopAttacks(pos, allOccupancy);
                break;
            case Piece.Queen:
                attacks = Magic.GetRookAttacks(pos, allOccupancy)
                        | Magic.GetBishopAttacks(pos, allOccupancy);
                break;
            // Other regular pieces
            case Piece.Knight:
                attacks = Magic.GetKnightAttacks(pos);
                break;
            case Piece.King:
                bool kingSideCastle = ((0b0010 << (2 * color)) & boardLogic.currentCastling) != 0;
                bool queenSideCastle = ((0b0001 << (2 * color)) & boardLogic.currentCastling) != 0;
                attacks = Magic.GetKingAttacks(pos, kingSideCastle, queenSideCastle);
                break;
            case Piece.Pawn:
                attacks = Magic.GetPawnAttacks(pos, friendly, allOccupancy & (~friendly), color, boardLogic.enPassantSquare);
                break;
            default:
                return 0;  // Use separate logic for non-sliders
        }

        // Filter out moves to your own pieces, then add them
        if (ignoreOtherKing)
            return attacks;
        ulong valid = attacks & ~friendly;

        // Not only Pseudo moves, but we need to find out what we can actually do.
        if (type == Piece.King)
        {
            valid &= ~boardLogic.attackedSquares[1 - color];
            return valid;
        }

        // Add the pin mask
        if (boardLogic.pinRays[pos] != 0UL)
            valid &= boardLogic.pinRays[pos];

        if (boardLogic.checkMap != 0UL)
        {
            // We are in check. We must protect the king.
            if (type == Piece.Pawn && (boardLogic.checkMap & boardLogic.Bbitboard) == 
                (boardLogic.checkMap & boardLogic.bitboards[1 - color, Piece.Pawn - 1]))
                // If the check was given by double push, we can en passant to capture the pawn.
                valid &= (boardLogic.checkMap | boardLogic.enPassantSquare);
            else
                valid &= boardLogic.checkMap;
        }

        return valid;
    }

    public int FindFlag(int pieceType, int from, int to)
    {
        pieceType = Piece.GetPieceType(pieceType);
        // Castling check
        if (pieceType == Piece.King && Mathf.Abs(to - from) == 2)
            return 1; // Castling

        if (pieceType == Piece.Pawn)
        {
            // Promotion check
            if ((to >= 56 && to <= 63) || (to >= 0 && to <= 7))
                return 2; // Promotion

            // En passant capture check
            if ((boardLogic.enPassantSquare & (1UL << to)) != 0 && boardLogic.enPassantSquare != 0)
            {
                return 3; // En passant
            }

            // Double pawn move check
            if (Mathf.Abs(to - from) == 16)
            {
                return 4; // Double pawn move
            }
        }

        return 0; // Normal move
    }

    public List<Move> GenerateListMoves(int pos, int piece)
    {
        List<Move> moves = new List<Move>();
        int type = Piece.GetPieceType(piece);
        ulong valid = GenerateMoves(pos, piece);

        while (valid != 0)
        {
            ulong lsb = valid & (ulong)-(long)valid;
            int to = BitScan.TrailingZeroCount(lsb);
            valid ^= lsb;

            int flag = FindFlag(type, pos, to);
            moves.Add(new Move(pos, to, piece, boardLogic.board[to], flag)); // Use 'piece', not 'type'
        }

        return moves;
    }

    public int GenerateAllMoves(Move[] moveList, int color)
    {
        int moveCount = 0;

        // Iterate through each piece type, from Pawn to King
        for (int pieceType = Piece.King; pieceType <= Piece.Queen; pieceType++)
        {
            ulong pieceBitboard;
            try
            {
                pieceBitboard = boardLogic.bitboards[color, pieceType - 1];
            }
            catch (System.Exception)
            {
                Debug.Log($"color: {color}, pieceType: {pieceType}");
                throw;
            }
            

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
                ulong validDestinations = GenerateMoves(from, piece);

                // Loop through each valid destination square
                while (validDestinations != 0)
                {
                    ulong lsb = validDestinations & (ulong)-(long)validDestinations;
                    int to = BitScan.TrailingZeroCount(lsb);

                    // Determine move details
                    int capturedPiece = boardLogic.board[to]; // Will be 0 if it's not a capture
                    int flag = FindFlag(piece, from, to); // Use the actual piece, not pieceType

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

    public int GenerateAllCaptures(Move[] moveList, int color)
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
                ulong validDestinations = GenerateMoves(from, piece);

                // Loop through each valid destination square
                while (validDestinations != 0)
                {
                    ulong lsb = validDestinations & (ulong)-(long)validDestinations;
                    int to = BitScan.TrailingZeroCount(lsb);

                    // Determine move details
                    int capturedPiece = boardLogic.board[to]; // Will be 0 if it's not a capture
                    if (capturedPiece == 0)
                    {
                        validDestinations ^= lsb;
                        continue;
                    }

                    int flag = FindFlag(piece, from, to); // Use the actual piece, not pieceType

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
}
