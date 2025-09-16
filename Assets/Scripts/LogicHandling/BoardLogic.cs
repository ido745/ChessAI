using System;
using System.Collections.Generic;
using UnityEditor.Rendering.LookDev;
using UnityEngine;
using static BitScan;

public enum MoveFlag
{
    Normal = 0,
    Castling = 1,
    Promotion = 2,
    EnPassant = 3,
    DoublePawnMove = 4
}

public struct Move
{
    public int from;
    public int to;
    public int movedPiece;
    public int capturedPiece;

    public int flag; // 0 - normal, 1 - castling, 2 - promotion, 3 - en passant, 4 - double move.
    public int promotionPiece;

    public Move(int from, int to, int moved, int captured = 0, int flag = 0, int promotionPiece = 0)
    {
        this.from = from;
        this.to = to;
        this.movedPiece = moved;
        this.capturedPiece = captured;
        this.flag = flag;

        this.promotionPiece = promotionPiece;
    }
}

// Generating and making moves, implementing game logic.
public class BoardLogic : MonoBehaviour
{
    // Hybrid approach: board to check what piece is on a certin position, bitboard for move generation etc.
    public int[] board = new int[64];
    public ulong[,] bitboards = new ulong[2, 6];   // For each color, for each type.
    public ulong Wbitboard;
    public ulong Bbitboard;
    public ulong enPassantSquare = 0;
    // 1 - King, 2 - pawn, 3 - knight, 4 - bishop, 5 - rook, 6 - queen.

    string FEN = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
    //string FEN = "r4rk1/1pp1qppp/p1np1n2/2b1p1B1/2B1P1b1/P1NP1N2/1PP1QPPP/R4RK1 w - - 0 10";
    public short turn = 0;     // 0 - white, 1 - black.

    public int castlingRights = 15; // 15 = 1111  - all sides can castle.
                                    // Format: kqKQ. e.g. 1001 - black can castle king side and white can castle queen side.
    public int currentCastling = 15;

    // For fast legal move generation - keep bitboards for the white and black attacking squares.
    public ulong[] attackedSquares = new ulong[2];

    // CheckMap is only used if the king's in check, to find the squares a piece can go to, to block the check.
    public ulong checkMap = 0UL;
    // Maps each square to the pinned ray (if there is any).
    public ulong[] pinRays = new ulong[64];
    public bool[] doubleCheck = new bool[] { false, false };

    // Helper classes
    private MoveExecuter moveExecuter;
    private AttackCalculator attackCalculator;
    GameObject boardDrawer;

    public bool gameEnded;

    public string openingLine = "";   // PGN-style history string
    private Stack<string> openingHistory = new Stack<string>(); // for easy undo

    // Start is called before the first frame update
    private void Start()
    {
        // Initialize helper classes
        moveExecuter = new MoveExecuter(this);
        attackCalculator = new AttackCalculator(this);

        // Set the pieces on the board
        ParseFEN(FEN);

        boardDrawer = transform.parent.gameObject;
        boardDrawer.GetComponent<GraphicalBoard>().DrawPieces(board);
        attackCalculator.UpdateAttacksMap(0);
        attackCalculator.UpdateAttacksMap(1);
    }

    public ulong GenerateMoves(int pos, int piece, bool ignoreOtherKing = false)
    {
        if (pos < 0 || pos >= 64) return 0UL;
        if (piece == 0) return 0UL;

        int color = Piece.IsBlack(piece);
        int type = Piece.GetPieceType(piece);
        ulong allOccupancy = Wbitboard | Bbitboard;
        ulong friendly = (color == 0) ? Wbitboard : Bbitboard;
        ulong attacks = 0UL;

        if (ignoreOtherKing) { allOccupancy &= (~bitboards[1 - color, Piece.King - 1]); }

        // If we're in double check, only the king can move
        if (doubleCheck[color] && type != Piece.King)
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
                bool kingSideCastle = ((0b0010 << (2 * color)) & currentCastling) != 0;
                bool queenSideCastle = ((0b0001 << (2 * color)) & currentCastling) != 0;
                attacks = Magic.GetKingAttacks(pos, kingSideCastle, queenSideCastle);
                break;
            case Piece.Pawn:
                attacks = Magic.GetPawnAttacks(pos, friendly, allOccupancy & (~friendly), color, enPassantSquare);
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
            valid &= ~attackedSquares[1 - color];
            return valid;
        }

        // Add the pin mask
        if (pinRays[pos] != 0UL)
            valid &= pinRays[pos];

        if (checkMap != 0UL)
        {
            // We are in check. We must protect the king.
            if (type == Piece.Pawn && (checkMap & Bbitboard) == (checkMap & bitboards[1 - color, Piece.Pawn - 1]))
                // If the check was given by double push, we can en passant to capture the pawn.
                valid &= (checkMap | enPassantSquare);
            else
                valid &= checkMap;
        }
        
        return valid;
    }

    public int FindFlag(int pieceType, int from, int to)
    {
        pieceType = Piece.GetPieceType(pieceType);
        // Castling check
        if (pieceType == Piece.King && Math.Abs(to - from) == 2)
            return 1; // Castling

        if (pieceType == Piece.Pawn)
        {
            // Promotion check
            if ((to >= 56 && to <= 63) || (to >= 0 && to <= 7))
                return 2; // Promotion

            // En passant capture check
            if ((enPassantSquare & (1UL << to)) != 0 && enPassantSquare != 0)
            {
                return 3; // En passant
            }

            // Double pawn move check
            if (Math.Abs(to - from) == 16)
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
            moves.Add(new Move(pos, to, piece, board[to], flag)); // Use 'piece', not 'type'
        }

        return moves;
    }

    public int GenerateAllMoves(Move[] moveList, int color)
    {
        int moveCount = 0;

        // Iterate through each piece type, from Pawn to King
        for (int pieceType = Piece.King; pieceType <= Piece.Queen; pieceType++)
        {
            ulong pieceBitboard = bitboards[color, pieceType - 1];

            // Loop through each piece of the current type on the board
            while (pieceBitboard != 0)
            {
                int from = BitScan.TrailingZeroCount(pieceBitboard);

                // Get the actual piece from the board array instead of constructing it
                int piece = board[from];

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
                    int capturedPiece = board[to]; // Will be 0 if it's not a capture
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
            ulong pieceBitboard = bitboards[color, pieceType - 1];

            // Loop through each piece of the current type on the board
            while (pieceBitboard != 0)
            {
                int from = BitScan.TrailingZeroCount(pieceBitboard);

                // Get the actual piece from the board array instead of constructing it
                int piece = board[from];

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
                    int capturedPiece = board[to]; // Will be 0 if it's not a capture
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

    public void MakeMove(Move move)
    {
        moveExecuter.MakeMove(move);

        string notation = MoveToNotation(move);

        // Add move number for White
        if (turn == 1) // White made a move
        {
            int moveNumber = (openingHistory.Count / 2) + 1;
            notation = moveNumber + ". " + notation;
        }

        openingLine += notation + " ";
        openingHistory.Push(notation);
    }

    public void UnmakeMove(Move move, int previousCastlingRights, ulong previousEnPassantSquare)
    {
        moveExecuter.UnmakeMove(move, previousCastlingRights, previousEnPassantSquare);

        if (openingHistory.Count > 0)
        {
            string last = openingHistory.Pop();

            // Remove the last added notation from the string
            if (openingLine.EndsWith(last + " "))
            {
                openingLine = openingLine.Substring(0, openingLine.Length - (last.Length + 1));
            }
        }
    }


    public void FindPinsAndChecks(int color)
    {
        attackCalculator.FindPinsAndChecks(color);
    }

    public void UpdateAttacksMap(int color)
    {
        attackCalculator.UpdateAttacksMap(color);
    }

    public bool IsInCheck()
    {
        // Checks if the player who's turn it is to play is in check
        return (checkMap != 0UL);
    }

    public void UpdateCastlingRights()
    {
        if ((bitboards[0, Piece.King - 1] & 0x0000000000000010) == 0)
        {
            // King is not in place -- no castling.
            castlingRights &= 0b1100;
        }
        if ((bitboards[1, Piece.King - 1] & 0x1000000000000000) == 0)
        {
            // King is not in place -- no castling.
            castlingRights &= 0b0011;
        }

        if ((bitboards[1, Piece.Rook - 1] & 0x8000000000000000) == 0)
        {
            // No king side castling for black
            castlingRights &= ~0b1000;
        }
        if ((bitboards[1, Piece.Rook - 1] & 0x0100000000000000) == 0)
        {
            // No queen side castling for black
            castlingRights &= ~0b0100;
        }

        if ((bitboards[0, Piece.Rook - 1] & 0x0000000000000080) == 0)
        {
            // No king side castling for white
            castlingRights &= ~0b0010;
        }
        if ((bitboards[0, Piece.Rook - 1] & 0x0000000000000001) == 0)
        {
            // No queen side castling for white
            castlingRights &= ~0b0001;
        }
    }

    public void DebugShowSquares(ulong squares, Color squareColor) => boardDrawer.GetComponent<GraphicalBoard>().DebugShowSquares(squares, squareColor);

    private string MoveToNotation(Move move)
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


    public void ParseFEN(string fen)
    {
        // 1. Reset current board state
        board = new int[64];
        bitboards = new ulong[2, 6];
        Wbitboard = 0;
        Bbitboard = 0;
        enPassantSquare = 0;
        castlingRights = 0; // Will be built from the FEN string

        string[] fenParts = fen.Split(' ');

        // 2. Parse Piece Placement (Field 1)
        string piecePlacement = fenParts[0];
        int file = 0, rank = 7;

        foreach (char c in piecePlacement)
        {
            if (c == '/')
            {
                file = 0;
                rank--;
                continue;
            }

            if (char.IsDigit(c))
            {
                file += (int)char.GetNumericValue(c);
                continue;
            }

            int piece = Piece.GetPieceFromChar(c);
            int pos = rank * 8 + file;
            board[pos] = piece;

            int type = Piece.GetPieceType(piece);
            int isBlack = Piece.IsBlack(piece);
            bitboards[isBlack, type - 1] = SetBit(bitboards[isBlack, type - 1], pos);

            file++;
        }

        // Aggregate white and black bitboards
        for (int i = 0; i < bitboards.GetLength(1); i++)
        {
            Wbitboard |= bitboards[0, i];
            Bbitboard |= bitboards[1, i];
        }

        // 3. Parse Active Color (Field 2)
        // fenParts[1] is 'w' or 'b'
        turn = (fenParts[1] == "w") ? (short)0 : (short)1;

        // 4. Parse Castling Availability (Field 3)
        // Note: Your code uses the bit order: black-king, black-queen, white-king, white-queen (bk bq wk wq)
        // 0b1000 = bk, 0b0100 = bq, 0b0010 = wk, 0b0001 = wq
        string castlingStr = fenParts[2];
        if (castlingStr.Contains('K')) castlingRights |= 0b0010; // White King-side
        if (castlingStr.Contains('Q')) castlingRights |= 0b0001; // White Queen-side
        if (castlingStr.Contains('k')) castlingRights |= 0b1000; // Black King-side
        if (castlingStr.Contains('q')) castlingRights |= 0b0100; // Black Queen-side

        // Sync currentCastling with the parsed rights
        currentCastling = castlingRights;

        // 5. Parse En Passant Target Square (Field 4) - Optional but highly recommended
        string enPassantStr = fenParts[3];
        if (enPassantStr != "-")
        {
            int epFile = enPassantStr[0] - 'a';
            int epRank = int.Parse(enPassantStr[1].ToString()) - 1;
            int epSquareIndex = epRank * 8 + epFile;
            enPassantSquare = 1UL << epSquareIndex;
        }

        // IMPORTANT: Do NOT call UpdateCastlingRights() here anymore, 
        // as we have already set the rights directly from the FEN string.
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
            if (Math.Abs(to - from) == 2)
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
            int count = GenerateAllMoves(moves, Piece.IsBlack(piece));

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