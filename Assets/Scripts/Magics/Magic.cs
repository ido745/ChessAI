using System.Diagnostics;
using System.Drawing;
using static PrecomputedMagics;

public static class Magic
{
    public static readonly ulong[] RookMask;
    public static readonly ulong[] BishopMask;
    public static readonly ulong[][] RookAttacks;
    public static readonly ulong[][] BishopAttacks;

    static Magic()
    {
        RookMask = new ulong[64];
        BishopMask = new ulong[64];
        RookAttacks = new ulong[64][];
        BishopAttacks = new ulong[64][];

        //MagicHelper.GenerateRayTable();

        for (int sq = 0; sq < 64; sq++)
        {
            RookMask[sq] = MagicHelper.CreateRookMovementMask(sq);
            BishopMask[sq] = MagicHelper.CreateBishopMovementMask(sq);
        }

        for (int sq = 0; sq < 64; sq++)
        {
            RookAttacks[sq] = BuildTable(sq, true, RookMask[sq], RookMagics[sq], RookShifts[sq]);
            BishopAttacks[sq] = BuildTable(sq, false, BishopMask[sq], BishopMagics[sq], BishopShifts[sq]);
        }
    }

    static ulong[] BuildTable(int square, bool rook, ulong mask, ulong magic, int shift)
    {
        int bits = 64 - shift;
        int size = 1 << bits;
        var table = new ulong[size];

        foreach (ulong blockers in MagicHelper.CreateAllBlockerBitboards(mask))
        {
            int index = (int)((blockers * magic) >> shift);
            table[index] = MagicHelper.LegalMoveBitboardFromBlockers(square, blockers, rook);
        }
        return table;
    }

    public static ulong GetSliderAttacks(int square, ulong blockers, bool ortho) =>
        ortho ? GetRookAttacks(square, blockers) : GetBishopAttacks(square, blockers);

    public static ulong GetRookAttacks(int square, ulong blockers)
    {
        ulong key = ((blockers & RookMask[square]) * RookMagics[square]) >> RookShifts[square];
        return RookAttacks[square][key];
    }

    public static ulong GetBishopAttacks(int square, ulong blockers)
    {
        ulong key = ((blockers & BishopMask[square]) * BishopMagics[square]) >> BishopShifts[square];
        return BishopAttacks[square][key];
    }

    public static ulong GetKnightAttacks(int square)
    {
        return knightMoves[square];
    }

    public static ulong GetKingAttacks(int square, bool _castleKing = false, bool _castleQueen = false)
    {
        ulong castleSquares = 0UL;
        if (_castleKing) 
        {
            castleSquares |= (1UL << (square+2));
        }
        if (_castleQueen)
        {
            castleSquares |= (1UL << (square - 2));
        }
        return (kingMoves[square] | castleSquares);
    }

    // File masks to prevent wrap-around on captures
    const ulong NOT_A_FILE = 0xfefefefefefefefeUL;
    const ulong NOT_H_FILE = 0x7f7f7f7f7f7f7f7fUL;

    // Rank masks to identify pawns on their starting ranks
    const ulong RANK_2_MASK = 0x000000000000FF00UL;
    const ulong RANK_7_MASK = 0x00FF000000000000UL;

    public static ulong GetPawnAttacks(int square, ulong friendly, ulong enemy, int isBlack, ulong enPassantTarget)
    {
        // 1. Create a bitboard for the single pawn at the given square
        ulong pawn = 1UL << square;
        ulong allPieces = friendly | enemy;
        ulong empty = ~allPieces;

        ulong singlePush;
        ulong doublePush;
        ulong attacks;

        if (isBlack == 0) // White Pawn
        {
            singlePush = (pawn << 8) & empty;

            // Double push: only if the pawn is on rank 2 and the single push was valid
            doublePush = ((pawn & RANK_2_MASK) << 16) & empty & (empty << 8);

            // Captures
            ulong attacksWest = (pawn & NOT_A_FILE) << 7;
            ulong attacksEast = (pawn & NOT_H_FILE) << 9;
            attacks = attacksWest | attacksEast;
        }
        else // Black Pawn
        {
            singlePush = (pawn >> 8) & empty;

            // Double push
            doublePush = ((pawn & RANK_7_MASK) >> 16) & empty & (empty >> 8);

            // Captures
            ulong attacksWest = (pawn & NOT_A_FILE) >> 9;
            ulong attacksEast = (pawn & NOT_H_FILE) >> 7;
            attacks = attacksWest | attacksEast;
        }

        ulong validCaptures = attacks & (enemy | enPassantTarget);

        return singlePush | doublePush | validCaptures;
    }

    public static ulong GetPawnCapturesOnly(int square, int isBlack)
    {
        // 1. Create a bitboard for the single pawn at the given square
        ulong pawn = 1UL << square;

        ulong attacks;

        if (isBlack == 0) // White Pawn
        {
            ulong attacksWest = (pawn & NOT_A_FILE) << 7;
            ulong attacksEast = (pawn & NOT_H_FILE) << 9;
            attacks = attacksWest | attacksEast;
        }
        else // Black Pawn
        {
            ulong attacksWest = (pawn & NOT_A_FILE) >> 9;
            ulong attacksEast = (pawn & NOT_H_FILE) >> 7;
            attacks = attacksWest | attacksEast;
        }

        return attacks;
    }

    public static ulong GetRayBetween(int sq1, int sq2)
    {
        return MagicHelper.Rays[sq1, sq2];
    }
}
