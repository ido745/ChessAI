using System.Collections.Generic;

public static class Piece
{
    public const int None = 0;
    public const int King = 1;
    public const int Pawn = 2;
    public const int Knight = 3;
    public const int Bishop = 4;
    public const int Rook = 5;
    public const int Queen = 6;

    public const int White = 8;
    public const int Black = 16;

    public static int GetPieceType(int piece) => piece & 0b111;
    public static int GetColor(int piece) => piece & 0b11000;
    public static int IsBlack(int piece) => piece >> 4;

    public static int GetPieceFromChar(char c)
    {
        int color = char.IsUpper(c) ? Piece.White : Piece.Black;

        switch (char.ToLower(c))
        {
            case 'k': return Piece.King | color;
            case 'p': return Piece.Pawn | color;
            case 'n': return Piece.Knight | color;
            case 'b': return Piece.Bishop | color;
            case 'r': return Piece.Rook | color;
            case 'q': return Piece.Queen | color;
            default: return Piece.None;
        }
    }
}
