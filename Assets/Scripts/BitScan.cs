using System;
using System.Collections.Generic;
using UnityEngine;

public static class BitScan
{
    private static readonly int[] Index64 = {
       0,  1, 48,  2, 57, 49, 28,  3,
      61, 58, 50, 42, 38, 29, 17,  4,
      62, 55, 59, 36, 53, 51, 43, 22,
      45, 39, 33, 30, 24, 18, 12,  5,
      63, 47, 56, 27, 60, 41, 37, 16,
      54, 35, 52, 21, 44, 32, 23, 11,
      46, 26, 40, 15, 34, 20, 31, 10,
      25, 14, 19,  9, 13,  8,  7,  6
    };

    private const ulong DeBruijn64 = 0x03F79D71B4CB0A89UL;

    public static int TrailingZeroCount(ulong x)
    {
        return Index64[((x & (ulong)-(long)x) * DeBruijn64) >> 58];
    }

    // Enumerate all set bits in a bitboard, from LSB to MSB.
    public static IEnumerable<int> BitscanAll(ulong bb)
    {
        while (bb != 0)
        {
            ulong lsbMask = bb & (ulong)-(long)bb;
            int index = TrailingZeroCount(lsbMask);
            yield return index;
            bb &= bb - 1; // clear lowest bit
        }
    }

    public static int PopCount(ulong value)
    {
        int count = 0;
        while (value != 0)
        {
            // This clever trick removes the least significant set bit.
            value &= value - 1;
            count++;
        }
        return count;
    }

    public static void PrintBinary(ulong value)
    {
        string bits = Convert.ToString((long)value, 2).PadLeft(64, '0');
        Debug.Log(bits);
    }

    public static bool GetBit(ulong bb, int sq)
    {
        // Shift the bit at 'sq' to the LSB and mask with 1—returns true if that bit is 1
        return ((bb >> sq) & 1UL) != 0;
    }

    public static ulong SetBit(ulong bb, int sq)
    {
        // Use bitwise OR to set the bit at 'sq'
        return bb | (1UL << sq);
    }

    public static ulong ClearBit(ulong bb, int sq)
    {
        // Use AND with the inverted bit mask to clear the bit at 'sq'
        return bb & ~(1UL << sq);
    }
}
