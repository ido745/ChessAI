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
    //string FEN = "r3kb1r/pppnn1pp/8/8/4N3/5Q2/P2R1KPq/2R5 w kq - ";
    public ulong zobristKey;

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
    private MoveCalculator moveCalculator;
    private MoveToNotationConverter moveToNotationConverter;
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
        moveCalculator = new MoveCalculator(this);
        moveToNotationConverter = new MoveToNotationConverter(this);

        // Set the pieces on the board
        ParseFEN(FEN);

        boardDrawer = transform.parent.gameObject;
        boardDrawer.GetComponent<GraphicalBoard>().DrawPieces(board);
        attackCalculator.UpdateAttacksMap(0);
        attackCalculator.UpdateAttacksMap(1);
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

        zobristKey = GetZobristKey();
    }

    public ulong GetZobristKey()
    {
        zobristKey = 0UL;

        // Hash pieces
        for (int i = 0; i < 64; i++)
        {
            if (board[i] != 0) // Only hash non-empty squares
            {
                int pieceType = Piece.GetPieceType(board[i]);
                int color = Piece.IsBlack(board[i]);

                zobristKey ^= Zobrist.pieceKeys[color, pieceType - 1, i];
            }
        }

        // Hash castling rights
        zobristKey ^= Zobrist.castlingKeys[currentCastling]; // current castling rights?

        // Hash en passant square
        if (enPassantSquare != 0UL)
        {
            int file = BitScan.TrailingZeroCount(enPassantSquare) % 8;
            zobristKey ^= Zobrist.enPassantFileKey[file];
        }

        // Hash side to move
        if (turn == 1)
        {
            zobristKey ^= Zobrist.blackToMoveKey;
        }

        return zobristKey;
    }

    public ulong GenerateMoves(int pos, int piece, bool ignoreOtherKing = false)
    {
        
        return moveCalculator.GenerateMoves(pos, piece, ignoreOtherKing);
    }

    public int FindFlag(int pieceType, int from, int to)
    {
        return moveCalculator.FindFlag(pieceType, from, to);
    }

    public List<Move> GenerateListMoves(int pos, int piece)
    {
        return moveCalculator.GenerateListMoves(pos, piece);
    }

    public int GenerateAllMoves(Move[] moveList, int color)
    {
        return moveCalculator.GenerateAllMoves(moveList, color);
    }

    public int GenerateAllCaptures(Move[] moveList, int color)
    {
        return moveCalculator.GenerateAllCaptures(moveList, color);
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
        return moveToNotationConverter.MoveToNotation(move);
    }

    public string MoveToSAN(Move move)
    {
        return moveToNotationConverter.MoveToSAN(move);
    }
}