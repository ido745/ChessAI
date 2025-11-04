using System.Drawing;
using UnityEngine;
using static BitScan;

public class AttackCalculator
{
    private BoardLogic boardLogic;

    public AttackCalculator(BoardLogic boardLogic)
    {
        this.boardLogic = boardLogic;
    }

    public void FindPinsAndChecks(int color)
    {
        boardLogic.pinRays = new ulong[64];
        boardLogic.checkMap = 0UL;
        boardLogic.doubleCheck[color] = false;
        int kingPos = TrailingZeroCount(boardLogic.bitboards[color, Piece.King - 1]);

        // Look at pieces that can pin straight ahead and diagonally.
        ulong rookOrQueen = boardLogic.bitboards[1 - color, Piece.Rook - 1] | boardLogic.bitboards[1 - color, Piece.Queen - 1];
        ulong bishopOrQueen = boardLogic.bitboards[1 - color, Piece.Bishop - 1] | boardLogic.bitboards[1 - color, Piece.Queen - 1];

        ulong kingRookRays = Magic.GetRookAttacks(kingPos, rookOrQueen);
        ulong kingBishopRays = Magic.GetBishopAttacks(kingPos, bishopOrQueen);

        ulong potentialRookPinners = kingRookRays & rookOrQueen;
        ulong potentialBishopPinners = kingBishopRays & bishopOrQueen;

        ulong allPotentialPinners = potentialRookPinners | potentialBishopPinners;

        ulong friendly = (color == 0) ? boardLogic.Wbitboard : boardLogic.Bbitboard;

        ulong knightCheck = Magic.GetKnightAttacks(kingPos) & boardLogic.bitboards[1 - color, Piece.Knight - 1];
        ulong pawnCheck = Magic.GetPawnCapturesOnly(kingPos, color) & boardLogic.bitboards[1 - color, Piece.Pawn - 1];

        if (knightCheck != 0)
            boardLogic.checkMap |= knightCheck;
        if (pawnCheck != 0)
            boardLogic.checkMap |= pawnCheck;

        // Loop through each potential attacker one by one
        while (allPotentialPinners != 0)
        {
            // Get the least significant bit (LSB)
            ulong lsb = allPotentialPinners & (ulong)-(long)allPotentialPinners;
            int pinnerSq = BitScan.TrailingZeroCount(lsb);

            // Remove this bit from our working set
            allPotentialPinners ^= lsb;  // This is the correct way to remove LSB

            ulong pinnerBitboard = 1UL << pinnerSq;

            ulong ray = 0UL;
            if ((pinnerBitboard & kingRookRays) != 0)
                ray = Magic.GetRookAttacks(pinnerSq, boardLogic.bitboards[color, Piece.King - 1]) & kingRookRays;
            else
                ray = Magic.GetBishopAttacks(pinnerSq, boardLogic.bitboards[color, Piece.King - 1]) & kingBishopRays;

            ulong blockers_on_ray = ray & (boardLogic.Wbitboard | boardLogic.Bbitboard);
            int num_blockers = BitScan.PopCount(blockers_on_ray);

            if (num_blockers == 0)
            {
                // There are no pieces between the king and the attacker.
                // RESULT: It is a CHECK from the piece at pinnerSq.
                if (boardLogic.checkMap != 0)
                {
                    // We got checked more than one direction. This is a double check.
                    boardLogic.doubleCheck[color] = true;
                }

                boardLogic.checkMap |= ray | pinnerBitboard;
                boardLogic.currentCastling &= (0b1100 >> 2*color);
            }
            else if (num_blockers == 1)
            {
                // There is exactly one piece blocking. We must check its color.
                bool is_blocker_friendly = ((blockers_on_ray & friendly) != 0UL);
                
                if (!is_blocker_friendly)
                {
                    // Not a check, not a pin.
                    continue;
                }
                // The single blocking piece is one of ours.
                // RESULT: It is a PIN.
                int pinnedPieceSq = BitScan.TrailingZeroCount(blockers_on_ray);

                // The pin ray includes the squares between the king and pinner, plus the pinner itself.
                boardLogic.pinRays[pinnedPieceSq] = ray | pinnerBitboard;
            }
            // Even if there are two pieces, there is the extreme case where en passant could reveal a check on the king.
            else if (num_blockers == 2)
            {
                ulong enPassantMaker = (color == 0) ? boardLogic.enPassantSquare >> 8 : boardLogic.enPassantSquare << 8;
                ulong pawnBesidesMaker = Magic.GetPawnCapturesOnly(BitScan.TrailingZeroCount(boardLogic.enPassantSquare), 1 - color) & boardLogic.bitboards[color, Piece.Pawn - 1];
                // We are only interested in a rook's pin. A bishop could not reveal a check by en passant.
                bool isMakerOnRay = (enPassantMaker & boardLogic.bitboards[1 - color, Piece.Pawn - 1] & ray) != 0;
                bool isPawnBesidesMakerOnRay = (pawnBesidesMaker & boardLogic.bitboards[color, Piece.Pawn - 1] & ray) != 0;
                if ((pinnerBitboard & kingRookRays) != 0 && isMakerOnRay && isPawnBesidesMakerOnRay)
                {
                    if (pawnBesidesMaker != 0)
                    {
                        // There's a pawn that can en passant and reveal a check
                        boardLogic.pinRays[BitScan.TrailingZeroCount(pawnBesidesMaker)] = ~boardLogic.enPassantSquare;
                    }
                }
            }
        }
    }

    public int CheckForEndGame(int color)
    {
        if (IsDraw())
        {
            boardLogic.gameEnded = true;
            boardLogic.winner = -1;
            return 3;
        }
        if (IsStalemate(color))
        {
            // this color has no moves to make. he's either stalemated or checkmated.
            boardLogic.gameEnded = true;
            if (boardLogic.checkMap != 0)
            {
                boardLogic.winner = 1 - color;
                OnCheckmate(color);
                return 1;
            }
            boardLogic.winner = -1;
            OnStalemate();
            return 2;
        }
        return 0;
    }

    // Also fix the UpdateAttacksMap method to prevent infinite loops
    public void UpdateAttacksMap(int color) // White = 0, Black = 1.
    {
        ulong map = 0UL;

        for (int i = 0; i < boardLogic.board.Length; i++)
        {
            int piece = boardLogic.board[i];
            if (piece == 0 || Piece.IsBlack(piece) != color)
                continue;
            if (Piece.GetPieceType(piece) == Piece.Pawn)
            {
                map |= Magic.GetPawnCapturesOnly(i, Piece.IsBlack(piece));
                continue;
            }

            map |= boardLogic.moveCalculator.GenerateMoves(i, piece, true);
        }

        //boardLogic.transform.parent.GetComponent<GraphicalBoard>().DebugShowSquares(map, Color.cyan);
        boardLogic.attackedSquares[color] = map;
    }

    private bool IsStalemate(int color)
    {
        // Go through all pieces of the current color
        for (int pieceType = 1; pieceType <= 6; pieceType++)
        {
            ulong currentBitboard = boardLogic.bitboards[color, pieceType - 1];

            while (currentBitboard != 0)
            {
                int index = BitScan.TrailingZeroCount(currentBitboard);

                if (boardLogic.moveCalculator.GenerateMoves(index, pieceType | 0b01000 << color) != 0)
                {
                    return false;
                }

                currentBitboard = BitScan.ClearBit(currentBitboard, index);
            }
        }

        // Stalemate was found
        return true;
    }

    public bool IsDraw()
    {
        // --- 1. Fifty-Move Rule ---
        if (boardLogic.halfMoveClock >= 100)
        {
            return true;
        }

        // --- 2. Threefold Repetition ---
        if (boardLogic.positionHistory != null && boardLogic.positionHistory.Count > 0)
        {
            ulong currentKey = boardLogic.zobristKey;
            int count = 0; // Don't start at 1, count only from history

            // Count occurrences in history (NOT including current position yet)
            foreach (ulong key in boardLogic.positionHistory)
            {
                if (key == currentKey)
                    count++;
            }

            if (count >= 3)
            {
                return true;
            }
        }

        // --- 3. Insufficient Mating Material ---
        if (HasInsufficientMaterial())
        {
            return true;
        }

        return false;
    }

    private bool HasInsufficientMaterial()
    {
        // Count pieces for both sides
        int whiteBishops = BitScan.PopCount(boardLogic.bitboards[0, Piece.Bishop - 1]);
        int blackBishops = BitScan.PopCount(boardLogic.bitboards[1, Piece.Bishop - 1]);
        int whiteKnights = BitScan.PopCount(boardLogic.bitboards[0, Piece.Knight - 1]);
        int blackKnights = BitScan.PopCount(boardLogic.bitboards[1, Piece.Knight - 1]);
        int whiteRooks = BitScan.PopCount(boardLogic.bitboards[0, Piece.Rook - 1]);
        int blackRooks = BitScan.PopCount(boardLogic.bitboards[1, Piece.Rook - 1]);
        int whiteQueens = BitScan.PopCount(boardLogic.bitboards[0, Piece.Queen - 1]);
        int blackQueens = BitScan.PopCount(boardLogic.bitboards[1, Piece.Queen - 1]);
        int whitePawns = BitScan.PopCount(boardLogic.bitboards[0, Piece.Pawn - 1]);
        int blackPawns = BitScan.PopCount(boardLogic.bitboards[1, Piece.Pawn - 1]);

        // Any pawns, rooks, or queens instantly means sufficient material.
        if (whitePawns + blackPawns + whiteRooks + blackRooks + whiteQueens + blackQueens > 0)
            return false;

        // K vs K
        if (whiteBishops + whiteKnights + blackBishops + blackKnights == 0)
            return true;

        // K+B vs K
        if ((whiteBishops == 1 && whiteKnights == 0 && blackBishops + blackKnights == 0) ||
            (blackBishops == 1 && blackKnights == 0 && whiteBishops + whiteKnights == 0))
            return true;

        // K+N vs K
        if ((whiteKnights == 1 && whiteBishops == 0 && blackBishops + blackKnights == 0) ||
            (blackKnights == 1 && blackBishops == 0 && whiteBishops + whiteKnights == 0))
            return true;

        // K+B vs K+B (same color bishops)
        if (whiteBishops == 1 && blackBishops == 1 && whiteKnights + blackKnights == 0)
        {
            int whiteBishopSquare = BitScan.TrailingZeroCount(boardLogic.bitboards[0, Piece.Bishop - 1]);
            int blackBishopSquare = BitScan.TrailingZeroCount(boardLogic.bitboards[1, Piece.Bishop - 1]);

            // Check if bishops are on same color squares
            // A square is light if (rank + file) is even
            bool whiteOnLight = ((whiteBishopSquare / 8 + whiteBishopSquare % 8) % 2 == 0);
            bool blackOnLight = ((blackBishopSquare / 8 + blackBishopSquare % 8) % 2 == 0);

            if (whiteOnLight == blackOnLight)
                return true;
        }

        return false;
    }


    private void OnCheckmate(int checkmatedColor)
    {
        // Handle checkmate logic here
        string winner = checkmatedColor == 0 ? "Black" : "White";
        string loser = checkmatedColor == 0 ? "White" : "Black";

        //Debug.Log($"Game Over! {winner} wins by checkmate!");

        // You might want to:
        // 1. Update game state
        // 2. Show victory screen
        // 3. Disable further moves
        // 4. Update statistics

        // Example: Disable further input
        // boardLogic.gameEnded = true;

        // Example: Show UI
        // boardLogic.GetComponent<GraphicalBoard>().ShowCheckmateUI(winner);
    }

    private void OnStalemate()
    {
        //Debug.Log("Game Over! Draw by stalemate!");

        // Handle stalemate logic:
        // 1. Update game state
        // 2. Show draw screen
        // 3. Disable further moves

        // Example: Show UI
        // boardLogic.GetComponent<GraphicalBoard>().ShowStalemateUI();
    }
}