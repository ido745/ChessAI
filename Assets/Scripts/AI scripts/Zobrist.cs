using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class Zobrist
{
    public static readonly ulong[,,] pieceKeys = new ulong[2, 6, 64]; // Create a random key for each piece and for each square.
    public static readonly ulong blackToMoveKey;
    public static readonly ulong[] castlingKeys = new ulong[16];
    public static readonly ulong[] enPassantFileKey = new ulong[8];

    static Zobrist()
    {
        // Use a fixed seed for reproducibility
        System.Random rng = new System.Random(18022003);

        for (int color = 0; color < 2; color++)
        {
            for (int piece = 0; piece < 6; piece++)
            {
                for (int square = 0; square < 64; square++)
                {
                    pieceKeys[color, piece, square] = NextULong(rng);
                }
            }
        }

        blackToMoveKey = NextULong(rng);

        for (int i = 0; i < 16; i++) 
        {
            castlingKeys[i] = NextULong(rng);
        }

        for (int i = 0; i < 8; i++)
        {
            enPassantFileKey[i] = NextULong(rng);
        }
    }

    private static ulong NextULong(System.Random rng)
    {
        byte[] buffer = new byte[8];
        rng.NextBytes(buffer);
        return BitConverter.ToUInt64(buffer, 0);
    }
}
