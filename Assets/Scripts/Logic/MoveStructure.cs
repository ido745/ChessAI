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

public struct MoveState
{
    public ulong zobristKey;
    public ulong enPassantSquare;
    public int castlingRights;
    public int currentCastling;
    public ulong checkMap;
    public ulong[] pinRays;
    public bool[] doubleCheck;
    public ulong[] attackedSquares;
    public bool gameEnded;
    public bool[] castled;
    public int halfMoveClock;
}
