using System;
using System.Collections.Generic;
using static BitScan;

// Generating and making moves, implementing game logic.
public class BoardLogic
{
    // Hybrid approach: board to check what piece is on a certin position, bitboard for move generation etc.
    public int[] board = new int[64];
    public ulong[,] bitboards = new ulong[2, 6];   // For each color, for each type.
                                                   // 1 - King, 2 - pawn, 3 - knight, 4 - bishop, 5 - rook, 6 - queen.
    public int[,] numOfPieces = new int[2,6]{ { 0, 0, 0, 0, 0, 0}, { 0, 0, 0, 0, 0, 0 } };
    public ulong Wbitboard;
    public ulong Bbitboard;
    public ulong enPassantSquare = 0;

    // Helper classes
    public readonly MoveExecuter moveExecuter;
    public readonly MoveCalculator moveCalculator;
    public readonly AttackCalculator attackCalculator;

    private readonly MoveToNotationConverter moveToNotationConverter;
    //private GameObject boardDrawer;

    public bool gameEnded = false;
    public int winner;
    public bool normalStarting = true;

    private string FEN = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
    //private string FEN = "8/6pk/6pp/8/7q/8/8/3K4 w - - - 0 1";
    public ulong zobristKey;

    public short turn = 0;     // 0 - white, 1 - black.

    public int castlingRights = 15; // 15 = 1111  - all sides can castle.
                                    // Format: kqKQ. e.g. 1001 - black can castle king side and white can castle queen side.
    public int currentCastling = 15;
    public bool[] castled = new bool[2];

    // For fast legal move generation - keep bitboards for the white and black attacking squares.
    public ulong[] attackedSquares = new ulong[2];

    // CheckMap is only used if the king's in check, to find the squares a piece can go to, to block the check.
    public ulong checkMap = 0UL;
    // Maps each square to the pinned ray (if there is any).
    public ulong[] pinRays = new ulong[64];
    public bool[] doubleCheck = new bool[] { false, false };

    public string openingLine = "";   // PGN-style history string
    private Stack<string> openingHistory = new Stack<string>(); // for easy undo

    public int halfMoveClock = 0; // Counts moves for 50-move rule
    public List<ulong> positionHistory = new List<ulong>(); 
    
    public Dictionary<ulong, int> positionCounter = new Dictionary<ulong, int>();

    public BoardLogic()
    {
        moveExecuter = new MoveExecuter(this);
        attackCalculator = new AttackCalculator(this);
        moveCalculator = new MoveCalculator(this);
        moveToNotationConverter = new MoveToNotationConverter(this);

        Instance = this;  // was in Awake()

        // was in Start():
        normalStarting = (FEN == "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1");
        ParseFEN(FEN);
        attackCalculator.UpdateAttacksMap(0);
        attackCalculator.UpdateAttacksMap(1);
    }

    // Make it a singleton - only one instance of BoardLogic.
    public static BoardLogic Instance { get; private set; }

    // private void Awake()
    // {
    //     // If there is already an instance and it is not this one, destroy this object
    //     if (Instance != null && Instance != this)
    //     {
    //         Destroy(gameObject);
    //         return;
    //     }

    //     Instance = this;
    // }

    // // Start is called before the first frame update
    // private void Start()
    // {
    //     normalStarting = (FEN == "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1");

    //     // Set the pieces on the board
    //     ParseFEN(FEN);

    //     boardDrawer = transform.parent.gameObject;
    //     boardDrawer.GetComponent<GraphicalBoard>().DrawPieces(board);
    //     attackCalculator.UpdateAttacksMap(0);
    //     attackCalculator.UpdateAttacksMap(1);
    // }

    public void ParseFEN(string fen)
    {
        // 1. Reset current board state
        board = new int[64];
        bitboards = new ulong[2, 6];
        numOfPieces = new int[2, 6] { { 0, 0, 0, 0, 0, 0 }, { 0, 0, 0, 0, 0, 0 } };
        Wbitboard = 0;
        Bbitboard = 0;
        enPassantSquare = 0;
        castlingRights = 0; // Will be built from the FEN string
        castled = new bool[2] { false, false };

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
            numOfPieces[isBlack, type - 1] += 1;

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

        int enPassantFile = (enPassantSquare != 0UL) ? BitScan.TrailingZeroCount(enPassantSquare) % 8 : -1;
        zobristKey = Zobrist.GetZobristKey(board, currentCastling, enPassantFile, turn);

        // Increment the counter of this position
        positionCounter.Clear();
        positionCounter[zobristKey] = 1;
    }

    public void addMoveToNotation(Move move)
    {
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

    public void popMoveFromNotation()
    {
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

    public void ResetBoard()
    {
        turn = 0;
        gameEnded = false;
        winner = -1;

        castlingRights = 15; 
        currentCastling = 15;

        attackedSquares = new ulong[2];

        checkMap = 0UL;
        pinRays = new ulong[64];
        doubleCheck = new bool[] { false, false };
        castled = new bool[2] { false, false };

        openingLine = "";
        openingHistory = new Stack<string>();

        halfMoveClock = 0;
        positionHistory = new List<ulong>();
        normalStarting = true;
        numOfPieces = new int[2, 6] { { 0, 0, 0, 0, 0, 0 }, { 0, 0, 0, 0, 0, 0 } };

        ParseFEN(FEN);

        attackCalculator.UpdateAttacksMap(0);
        attackCalculator.UpdateAttacksMap(1);
    }

    public bool IsInCheck()
    {
        // Checks if the player who's turn it is to play is in check
        return (checkMap != 0UL);
    }

    public void EndGame()
    {
        gameEnded = true;
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

    public bool IsDraw()
    {
        int checkForWhite = attackCalculator.CheckForEndGame(0);
        int checkForBlack = attackCalculator.CheckForEndGame(1);
        
        return (checkForWhite == 3 || checkForWhite == 2 || checkForBlack == 2);
    }

    private string MoveToNotation(Move move)
    {
        return moveToNotationConverter.MoveToNotation(move);
    }

    public string MoveToSAN(Move move)
    {
        return moveToNotationConverter.MoveToSAN(move);
    }
}