using System;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using static BitScan;
using static UnityEngine.RuleTile.TilingRuleOutput;

public class MoveExecuter
{
    private BoardLogic boardLogic;

    public MoveExecuter(BoardLogic boardLogic)
    {
        this.boardLogic = boardLogic;
    }

    public void MakeMove(Move move)
    {
        // 1. Update board state
        boardLogic.board[move.from] = 0;
        boardLogic.board[move.to] = move.movedPiece;

        // 2. Update piece bitboards
        int pieceType = Piece.GetPieceType(move.movedPiece);
        int color = Piece.IsBlack(move.movedPiece);

        // Move the piece in its specific bitboard
        boardLogic.bitboards[color, pieceType - 1] = MoveBit(boardLogic.bitboards[color, pieceType - 1], move.from, move.to);

        // Update color occupancy bitboards
        UpdateColorOccupancy(color, move.from, move.to);

        // 3. Handle captures
        HandleCapture(move);

        // 4. Handle special moves
        HandleSpecialMove(move);

        // 5. Update en passant square
        if (move.flag == 4) // Double pawn move
        {
            boardLogic.enPassantSquare = 1UL << ((move.from + move.to) / 2);
        }
        else
        {
            boardLogic.enPassantSquare = 0UL;
        }

        // 6. Update game state
        boardLogic.turn = (short)(1 - boardLogic.turn);
        int movingColor = Piece.IsBlack(move.movedPiece);
        
        // Update attack maps and check/pin detection
        UpdateCastlingRights(move, pieceType, movingColor);

        boardLogic.UpdateAttacksMap(movingColor);
        boardLogic.FindPinsAndChecks(1 - movingColor);

        UpdateCastlingRights(move, pieceType, movingColor);
    }

    private void UpdateColorOccupancy(int color, int from, int to)
    {
        if (color == 1) // Black
        {
            boardLogic.Bbitboard = MoveBit(boardLogic.Bbitboard, from, to);
            boardLogic.Wbitboard = ClearBit(boardLogic.Wbitboard, to); // Clear captured white piece if any
        }
        else // White
        {
            boardLogic.Wbitboard = MoveBit(boardLogic.Wbitboard, from, to);
            boardLogic.Bbitboard = ClearBit(boardLogic.Bbitboard, to); // Clear captured black piece if any
        }
    }

    private void HandleCapture(Move move)
    {
        if (move.capturedPiece == 0) return;

        int capturedType = Piece.GetPieceType(move.capturedPiece);
        int capturedColor = Piece.IsBlack(move.capturedPiece);

        // Remove captured piece from its bitboard
        boardLogic.bitboards[capturedColor, capturedType - 1] = ClearBit(boardLogic.bitboards[capturedColor, capturedType - 1], move.to);
    }

    public void UpdateCastlingRights(Move move, int pieceType, int color)
    {
        if (pieceType == Piece.King) boardLogic.castlingRights &= 0b1100 >> (2 * color);

        else if (pieceType == Piece.Rook)
        {
            if (move.from == 0 || move.from == 56)
            {
                // Queen side moved
                boardLogic.castlingRights &= ~(0b0001 << (2 * color));
            }else if (move.from == 7 ||  move.from == 63)
            {
                // King side moved
                boardLogic.castlingRights &= ~(0b0010 << (2 * color));
            }
        }
        else
        {
            if (move.to == 0)
                boardLogic.castlingRights &= ~0b0001;
            else if (move.to == 56)
                boardLogic.castlingRights &= ~0b0100;
            else if (move.to == 7)
                boardLogic.castlingRights &= ~0b0010;
            else if (move.to == 63)
                boardLogic.castlingRights &= ~0b1000;
        }

        // Look for checks on the kings.
        boardLogic.currentCastling = boardLogic.castlingRights;
        if ((boardLogic.bitboards[0, Piece.King - 1] & boardLogic.attackedSquares[1]) != 0)
            boardLogic.currentCastling &= 0b1100;
        if ((boardLogic.bitboards[1, Piece.King - 1] & boardLogic.attackedSquares[0]) != 0)
            boardLogic.currentCastling &= 0b0011;

        // Look for checks between the king and rooks.
        bool wKingSideCheck = (boardLogic.attackedSquares[1] & 0x0000000000000060) != 0;
        bool wQueenSideCheck = (boardLogic.attackedSquares[1] & 0x000000000000000C) != 0;

        bool bKingSideCheck = (boardLogic.attackedSquares[0] & 0x6000000000000000) != 0;
        bool bQueenSideCheck = (boardLogic.attackedSquares[0] & 0x0C00000000000000) != 0;

        if (wQueenSideCheck)
            boardLogic.currentCastling &= ~0b0001;
        if (bQueenSideCheck)
            boardLogic.currentCastling &= ~0b0100;
        if (wKingSideCheck)
            boardLogic.currentCastling &= ~0b0010;
        if (bKingSideCheck)
            boardLogic.currentCastling &= ~0b1000;

        ulong piecesBitboard = (boardLogic.Wbitboard | boardLogic.Bbitboard);

        // Look for pieces in the way.
        bool wKingSideBlocked = (piecesBitboard & 0x0000000000000060) != 0;
        bool wQueenSideBlocked = (piecesBitboard & 0x000000000000000E) != 0;

        bool bKingSideBlocked = (piecesBitboard & 0x6000000000000000) != 0;
        bool bQueenSideBlocked = (piecesBitboard & 0x0E00000000000000) != 0;

        if (wQueenSideBlocked)
            boardLogic.currentCastling &= ~0b0001;
        if (bQueenSideBlocked)
            boardLogic.currentCastling &= ~0b0100;
        if (wKingSideBlocked)
            boardLogic.currentCastling &= ~0b0010;
        if (bKingSideBlocked)
            boardLogic.currentCastling &= ~0b1000;

        // Look for enemy pieces in the way

        //Debug.Log("Current castling rights:");
        //PrintBinary((ulong)boardLogic.currentCastling);
        //Debug.Log("Castling rights:");
        //PrintBinary((ulong)boardLogic.castlingRights);

    }
    private void HandleSpecialMove(Move move)
    {
        switch ((MoveFlag)move.flag)
        {
            case MoveFlag.Castling:
                MakeCastling(move);
                break;
            case MoveFlag.EnPassant:
                MakeEnPassantCapture(move);
                break;
            case MoveFlag.Promotion:
                MakePromotion(move);
                break;
            case MoveFlag.Normal:
            case MoveFlag.DoublePawnMove:
            default:
                // No special handling needed
                break;
        }
    }

    private void MakeEnPassantCapture(Move move)
    {
        int movingColor = Piece.IsBlack(move.movedPiece);
        int capturedPawnSquare = movingColor == 1 ? move.to + 8 : move.to - 8;

        // Remove the captured pawn from the board
        boardLogic.board[capturedPawnSquare] = 0;

        // Remove from enemy pawn bitboard and color occupancy
        int enemyColor = 1 - movingColor;
        boardLogic.bitboards[enemyColor, Piece.Pawn - 1] = ClearBit(boardLogic.bitboards[enemyColor, Piece.Pawn - 1], capturedPawnSquare);

        if (enemyColor == 1)
            boardLogic.Bbitboard = ClearBit(boardLogic.Bbitboard, capturedPawnSquare);
        else
            boardLogic.Wbitboard = ClearBit(boardLogic.Wbitboard, capturedPawnSquare);
    }

    private void MakeCastling(Move move)
    {
        bool isKingSide = (move.to - move.from) == 2;
        int color = Piece.IsBlack(move.movedPiece);

        // Calculate rook positions
        int rookFrom, rookTo;
        if (isKingSide)
        {
            rookFrom = color == 0 ? 7 : 63;  // h1 or h8
            rookTo = color == 0 ? 5 : 61;    // f1 or f8
        }
        else
        {
            rookFrom = color == 0 ? 0 : 56;  // a1 or a8
            rookTo = color == 0 ? 3 : 59;    // d1 or d8
        }

        // Move the rook
        int rookPiece = boardLogic.board[rookFrom];
        boardLogic.board[rookFrom] = 0;
        boardLogic.board[rookTo] = rookPiece;

        // Update rook bitboards
        boardLogic.bitboards[color, Piece.Rook - 1] = MoveBit(boardLogic.bitboards[color, Piece.Rook - 1], rookFrom, rookTo);

        if (color == 1)
            boardLogic.Bbitboard = MoveBit(boardLogic.Bbitboard, rookFrom, rookTo);
        else
            boardLogic.Wbitboard = MoveBit(boardLogic.Wbitboard, rookFrom, rookTo);
    }

    private void MakePromotion(Move move)
    {
        // Implement promotion logic here
        // Remove pawn, add promoted piece
        int oldType = move.movedPiece;
        int newType = move.promotionPiece;

        // Update the values in the bitboards
        ulong pawnbit = boardLogic.bitboards[Piece.IsBlack(oldType), Piece.GetPieceType(oldType) - 1];
        boardLogic.bitboards[Piece.IsBlack(oldType), Piece.GetPieceType(oldType) - 1] = BitScan.ClearBit(pawnbit, move.to);

        ulong promotedBit = boardLogic.bitboards[Piece.IsBlack(newType), Piece.GetPieceType(newType) - 1];
        boardLogic.bitboards[Piece.IsBlack(newType), Piece.GetPieceType(newType) - 1] = BitScan.SetBit(promotedBit, move.to);

        // Update board array
        boardLogic.board[move.to] = newType;
        boardLogic.board[move.from] = 0;

        // Update attack maps and checks
        boardLogic.UpdateAttacksMap(Piece.IsBlack(newType));
        boardLogic.FindPinsAndChecks(1 - Piece.IsBlack(newType));
    }

    // Helper method to move a bit from one position to another in a bitboard
    private ulong MoveBit(ulong bitboard, int from, int to) => SetBit(ClearBit(bitboard, from), to);

    public void UnmakeMove(Move move)
    {
        int fromSquare = move.from;
        int toSquare = move.to;
        int movedPiece = move.movedPiece;
        int capturedPiece = move.capturedPiece;
        int flag = move.flag;
        int promotionPiece = move.promotionPiece;

        int color = Piece.IsBlack(movedPiece);
        int pieceType = Piece.GetPieceType(movedPiece);

        // Handle special moves first
        switch (flag)
        {
            case 1: // Castling
                UnmakeCastling(fromSquare, toSquare, color);
                return;

            case 2: // Promotion
                UnmakePromotion(fromSquare, toSquare, movedPiece, capturedPiece, promotionPiece);
                return;

            case 3: // En passant
                UnmakeEnPassant(fromSquare, toSquare, movedPiece);
                return;

            case 4: // Double pawn move
                // Just a normal unmove, en passant square is restored by game state
                break;
        }

        // Normal move unmake
        UnmakeNormalMove(fromSquare, toSquare, movedPiece, capturedPiece);
    }

    private void UnmakeNormalMove(int fromSquare, int toSquare, int movedPiece, int capturedPiece)
    {
        int color = Piece.IsBlack(movedPiece);
        int pieceType = Piece.GetPieceType(movedPiece);

        // Move piece back to original square
        boardLogic.board[fromSquare] = movedPiece;
        boardLogic.board[toSquare] = capturedPiece; // Restore captured piece (or 0 if no capture)

        // Update bitboards - move piece back
        boardLogic.bitboards[color, pieceType - 1] = ClearBit(boardLogic.bitboards[color, pieceType - 1], toSquare);
        boardLogic.bitboards[color, pieceType - 1] = SetBit(boardLogic.bitboards[color, pieceType - 1], fromSquare);

        // If there was a captured piece, restore it to the bitboards
        if (capturedPiece != 0)
        {
            int capturedColor = Piece.IsBlack(capturedPiece);
            int capturedType = Piece.GetPieceType(capturedPiece);
            boardLogic.bitboards[capturedColor, capturedType - 1] = SetBit(boardLogic.bitboards[capturedColor, capturedType - 1], toSquare);
        }

        // Update aggregate bitboards
        UpdateAggregateBitboards();
    }

    private void UnmakeCastling(int fromSquare, int toSquare, int color)
    {
        // Determine if this was kingside or queenside castling
        bool kingSide = toSquare > fromSquare;

        if (kingSide)
        {
            // Kingside castling
            int rookFromSquare = (color == 0) ? 63 : 7;   // h1 or h8
            int rookToSquare = (color == 0) ? 61 : 5;     // f1 or f8
            int kingOriginal = (color == 0) ? 60 : 4;     // e1 or e8

            // Move king back
            boardLogic.board[kingOriginal] = boardLogic.board[toSquare];
            boardLogic.board[toSquare] = 0;

            // Move rook back
            boardLogic.board[rookFromSquare] = boardLogic.board[rookToSquare];
            boardLogic.board[rookToSquare] = 0;

            // Update king bitboard
            boardLogic.bitboards[color, Piece.King - 1] = ClearBit(boardLogic.bitboards[color, Piece.King - 1], toSquare);
            boardLogic.bitboards[color, Piece.King - 1] = SetBit(boardLogic.bitboards[color, Piece.King - 1], kingOriginal);

            // Update rook bitboard
            boardLogic.bitboards[color, Piece.Rook - 1] = ClearBit(boardLogic.bitboards[color, Piece.Rook - 1], rookToSquare);
            boardLogic.bitboards[color, Piece.Rook - 1] = SetBit(boardLogic.bitboards[color, Piece.Rook - 1], rookFromSquare);
        }
        else
        {
            // Queenside castling
            int rookFromSquare = (color == 0) ? 56 : 0;   // a1 or a8
            int rookToSquare = (color == 0) ? 59 : 3;     // d1 or d8
            int kingOriginal = (color == 0) ? 60 : 4;     // e1 or e8

            // Move king back
            boardLogic.board[kingOriginal] = boardLogic.board[toSquare];
            boardLogic.board[toSquare] = 0;

            // Move rook back
            boardLogic.board[rookFromSquare] = boardLogic.board[rookToSquare];
            boardLogic.board[rookToSquare] = 0;

            // Update king bitboard
            boardLogic.bitboards[color, Piece.King - 1] = ClearBit(boardLogic.bitboards[color, Piece.King - 1], toSquare);
            boardLogic.bitboards[color, Piece.King - 1] = SetBit(boardLogic.bitboards[color, Piece.King - 1], kingOriginal);

            // Update rook bitboard
            boardLogic.bitboards[color, Piece.Rook - 1] = ClearBit(boardLogic.bitboards[color, Piece.Rook - 1], rookToSquare);
            boardLogic.bitboards[color, Piece.Rook - 1] = SetBit(boardLogic.bitboards[color, Piece.Rook - 1], rookFromSquare);
        }

        UpdateAggregateBitboards();
    }

    private void UnmakePromotion(int fromSquare, int toSquare, int originalPawn, int capturedPiece, int promotionPiece)
    {
        int color = Piece.IsBlack(originalPawn);
        int promotedPieceType = Piece.GetPieceType(promotionPiece);

        // Remove promoted piece from target square
        boardLogic.board[toSquare] = capturedPiece; // Restore captured piece (or 0)
        boardLogic.board[fromSquare] = originalPawn; // Restore original pawn

        // Update bitboards - remove promoted piece
        boardLogic.bitboards[color, promotedPieceType - 1] = ClearBit(boardLogic.bitboards[color, promotedPieceType - 1], toSquare);

        // Restore pawn
        boardLogic.bitboards[color, Piece.Pawn - 1] = SetBit(boardLogic.bitboards[color, Piece.Pawn - 1], fromSquare);

        // If there was a captured piece, restore it
        if (capturedPiece != 0)
        {
            int capturedColor = Piece.IsBlack(capturedPiece);
            int capturedType = Piece.GetPieceType(capturedPiece);
            boardLogic.bitboards[capturedColor, capturedType - 1] = SetBit(boardLogic.bitboards[capturedColor, capturedType - 1], toSquare);
        }

        UpdateAggregateBitboards();
    }

    private void UnmakeEnPassant(int fromSquare, int toSquare, int movedPawn)
    {
        int color = Piece.IsBlack(movedPawn);
        int capturedPawnSquare;
        int capturedPawn;

        if (color == 0) // White pawn
        {
            capturedPawnSquare = toSquare - 8; // Black pawn was on rank below
            capturedPawn = Piece.Pawn | Piece.Black;
        }
        else // Black pawn
        {
            capturedPawnSquare = toSquare + 8; // White pawn was on rank above
            capturedPawn = Piece.Pawn | Piece.White;
        }

        // Move pawn back
        boardLogic.board[fromSquare] = movedPawn;
        boardLogic.board[toSquare] = 0;

        // Restore captured pawn
        boardLogic.board[capturedPawnSquare] = capturedPawn;

        // Update bitboards
        boardLogic.bitboards[color, Piece.Pawn - 1] = ClearBit(boardLogic.bitboards[color, Piece.Pawn - 1], toSquare);
        boardLogic.bitboards[color, Piece.Pawn - 1] = SetBit(boardLogic.bitboards[color, Piece.Pawn - 1], fromSquare);

        // Restore captured pawn bitboard
        int capturedColor = 1 - color;
        boardLogic.bitboards[capturedColor, Piece.Pawn - 1] = SetBit(boardLogic.bitboards[capturedColor, Piece.Pawn - 1], capturedPawnSquare);

        UpdateAggregateBitboards();
    }

    private void UpdateAggregateBitboards()
    {
        boardLogic.Wbitboard = 0;
        boardLogic.Bbitboard = 0;

        for (int i = 0; i < 6; i++)
        {
            boardLogic.Wbitboard |= boardLogic.bitboards[0, i];
            boardLogic.Bbitboard |= boardLogic.bitboards[1, i];
        }
    }

    // Helper methods (you might already have these)
    private static ulong SetBit(ulong bitboard, int square)
    {
        return bitboard | (1UL << square);
    }

    private static ulong ClearBit(ulong bitboard, int square)
    {
        return bitboard & ~(1UL << square);
    }
}