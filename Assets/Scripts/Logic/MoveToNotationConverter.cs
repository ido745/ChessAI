using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MoveToNotationConverter
{
    private BoardLogic boardLogic;

    public MoveToNotationConverter(BoardLogic boardLogic)
    {
        this.boardLogic = boardLogic;
    }

    public string MoveToNotation(Move move)
    {
        int fromFile = move.from % 8;
        int fromRank = move.from / 8;
        int toFile = move.to % 8;
        int toRank = move.to / 8;

        string files = "abcdefgh";
        string pieceChar = "";

        int pieceType = Piece.GetPieceType(move.movedPiece);

        switch (pieceType)
        {
            case Piece.Pawn:
                pieceChar = "";
                break;
            case Piece.Knight:
                pieceChar = "N";
                break;
            case Piece.Bishop:
                pieceChar = "B";
                break;
            case Piece.Rook:
                pieceChar = "R";
                break;
            case Piece.Queen:
                pieceChar = "Q";
                break;
            case Piece.King:
                pieceChar = "K";
                break;
        }

        // Castling
        if (move.flag == (int)MoveFlag.Castling)
        {
            if (toFile == 6) return "O-O";     // King-side
            if (toFile == 2) return "O-O-O";   // Queen-side
        }

        // Pawn captures
        if (pieceType == Piece.Pawn && move.capturedPiece != 0)
        {
            return $"{files[fromFile]}x{files[toFile]}{toRank + 1}";
        }

        // Normal capture
        if (move.capturedPiece != 0)
        {
            return $"{pieceChar}x{files[toFile]}{toRank + 1}";
        }

        // Normal move
        return $"{pieceChar}{files[toFile]}{toRank + 1}";
    }

    public string MoveToSAN(Move move)
    {
        int from = move.from;
        int to = move.to;
        int piece = move.movedPiece;
        int captured = move.capturedPiece;
        int promotion = move.promotionPiece;

        string san = "";

        // Handle castling
        if (Piece.GetPieceType(piece) == Piece.King)
        {
            if (Mathf.Abs(to - from) == 2)
            {
                return to > from ? "O-O" : "O-O-O";
            }
        }

        // Get piece letter (empty for pawns)
        string pieceLetter = Piece.GetPieceType(piece) == Piece.Pawn ? "" :
            Piece.GetPieceType(piece) switch
            {
                Piece.Knight => "N",
                Piece.Bishop => "B",
                Piece.Rook => "R",
                Piece.Queen => "Q",
                Piece.King => "K",
                _ => ""
            };

        // Determine capture symbol
        string captureSymbol = captured != 0 ? "x" : "";

        // For pawn captures, include the file of the pawn
        if (Piece.GetPieceType(piece) == Piece.Pawn && captureSymbol != "")
        {
            pieceLetter = ((char)('a' + (from % 8))).ToString();
        }

        // Disambiguation: check if other pieces of same type can move to 'to'
        if (pieceLetter != "" && captureSymbol != "")
        {
            string disambiguation = "";
            Move[] moves = new Move[256];
            int count = boardLogic.moveCalculator.GenerateAllMoves(moves, Piece.IsBlack(piece));

            foreach (var m in moves)
            {
                if (m.from == from) continue;
                if (Piece.GetPieceType(m.movedPiece) != Piece.GetPieceType(piece)) continue;
                if (m.to == to)
                {
                    // Same piece type can move to the same square -> add disambiguation
                    disambiguation = ((char)('a' + (from % 8))).ToString();
                    break;
                }
            }

            pieceLetter += disambiguation;
        }

        // Add destination square
        string toSquare = $"{(char)('a' + (to % 8))}{(to / 8) + 1}";

        san += pieceLetter + captureSymbol + toSquare;

        // Add promotion if exists
        if (promotion != 0)
        {
            string promoLetter = Piece.GetPieceType(promotion) switch
            {
                Piece.Queen => "Q",
                Piece.Rook => "R",
                Piece.Bishop => "B",
                Piece.Knight => "N",
                _ => ""
            };
            san += "=" + promoLetter;
        }

        return san;
    }
}
