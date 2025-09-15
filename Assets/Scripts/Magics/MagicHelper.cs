using System.Diagnostics;
using static BitScan;
using System;
using System.Collections.Generic;
using UnityEngine;

public static class MagicHelper
{
    public static readonly ulong[,] Rays = new ulong[64, 64];

    // Generate blocker mask (excludes edges)
    public static ulong CreateRookMovementMask(int square)
    {
        ulong mask = 0UL;
        int rank = square / 8;
        int file = square % 8;

        // Horizontal mask: all squares on the same rank except edges (files 0 and 7)
        ulong rankMask = 0x7E; // Binary: 01111110 (excludes files 0 and 7)
        rankMask <<= (rank * 8); // Shift to the correct rank

        // Vertical mask: all squares on the same file except edges (ranks 0 and 7)
        ulong fileMask = 0x00FFFFFFFFFFFF00UL; // Excludes ranks 0 and 7
        fileMask &= (0x0101010101010101UL << file); // Select only the current file

        // Combine masks and exclude the rook's own square
        mask = (rankMask | fileMask) & (~(1UL << square));

        return mask;
    }
    public static ulong CreateBishopMovementMask(int square)
    {
        // A simple slow calculation will also do -- it's only done once at the beginning.
        ulong mask = 0UL;
        int rank = square >> 3, file = square & 7;
        (int dy, int dx)[] dirs = new[] { (1, 1), (1, -1), (-1, 1), (-1, -1) };

        foreach (var d in dirs)
        {
            int rr = rank + d.dy, ff = file + d.dx;
            while (rr > 0 && rr < 7 && ff > 0 && ff < 7)
            {
                mask |= 1UL << (rr * 8 + ff);
                rr += d.dy; ff += d.dx;
            }
        }
        return mask;
    }

    // Enumerate all subsets of blocker mask
    public static ulong[] CreateAllBlockerBitboards(ulong mask)
    {
        int bits = PopCount(mask);
        int total = 1 << bits;
        var list = new ulong[total];
        int idx = 0;
        ulong subset = 0;
        do
        {
            list[idx++] = subset;
            subset = (subset - mask) & mask;
        } while (subset != 0);
        return list;
    }

    // Compute legal moves for given blockers (ray cast)
    public static ulong LegalMoveBitboardFromBlockers(int square, ulong blockers, bool rook)
    {
        ulong attacks = 0UL;
        int r = square >> 3, f = square & 7;
        (int dr, int df)[] dirs = rook
            ? new[] { (1, 0), (-1, 0), (0, 1), (0, -1) }
            : new[] { (1, 1), (1, -1), (-1, 1), (-1, -1) };

        foreach (var d in dirs)
        {
            int rr = r + d.dr, ff = f + d.df;
            while (rr >= 0 && rr < 8 && ff >= 0 && ff < 8)
            {
                int sq = rr * 8 + ff;
                attacks |= 1UL << sq;
                if ((blockers & (1UL << sq)) != 0) break;
                rr += d.dr; ff += d.df;
            }
        }
        return attacks;
    }

    public static void GenerateRayTable()
    {
        for (int sq1 = 0; sq1 < 64; sq1++)
        {
            for (int sq2 = 0; sq2 < 64; sq2++)
            {
                if (sq1 == sq2) continue;

                // Determine the direction of the ray from sq1 to sq2
                int rank1 = sq1 / 8;
                int file1 = sq1 % 8;
                int rank2 = sq2 / 8;
                int file2 = sq2 % 8;

                int dr = (rank2 > rank1) ? 1 : (rank2 < rank1) ? -1 : 0; // Rank direction
                int df = (file2 > file1) ? 1 : (file2 < file1) ? -1 : 0; // File direction

                // Check if the squares are aligned (on the same rank, file, or diagonal)
                bool isAligned = (dr == 0 || df == 0 || (System.Math.Abs(dr) == System.Math.Abs(df)));
                if (!isAligned)
                {
                    Rays[sq1, sq2] = 0UL;
                    continue;
                }

                ulong ray = 0UL;
                int currentRank = rank1 + dr;
                int currentFile = file1 + df;

                // "Draw" the line of bits between the two squares
                while ((currentRank != rank2 || currentFile != file2))
                {
                    int currentSq = currentRank * 8 + currentFile;
                    ray |= (1UL << currentSq);
                    currentRank += dr;
                    currentFile += df;
                }
                Rays[sq1, sq2] = ray;
            }
        }
    }
}
