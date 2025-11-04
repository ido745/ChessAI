using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static AI;

// Transposition table entry structure
public struct TTEntry
{
    public ulong zobristKey;    // Full 64-bit key for verification
    public short score;         // Evaluation score
    public byte depth;          // Search depth
    public TTEntryType type;    // Type of bound
    public Move bestMove;       // Best move found
    public byte age;            // For replacement strategy

    public TTEntry(ulong key, short score, byte depth, TTEntryType type, Move bestMove, byte age)
    {
        this.zobristKey = key;
        this.score = score;
        this.depth = depth;
        this.type = type;
        this.bestMove = bestMove;
        this.age = age;
    }

    public bool IsEmpty => zobristKey == 0;
}

public enum TTEntryType : byte
{
    Exact = 0,    // PV-node - exact score
    LowerBound = 1, // Beta cutoff - score is at least this value (fail-high)
    UpperBound = 2  // Alpha cutoff - score is at most this value (fail-low)
}

public class TranspositionTable
{
    private TTEntry[] table;
    private readonly int sizeMask;  // For fast modulo operation (size must be power of 2)
    private byte currentAge = 0;

    public int Size { get; private set; }
    public int Hits { get; private set; }
    public int Collisions { get; private set; }

    public TranspositionTable(int sizeMB = 64)
    {
        // Calculate table size (power of 2)
        int entrySize = System.Runtime.InteropServices.Marshal.SizeOf<TTEntry>();
        int maxEntries = (sizeMB * 1024 * 1024) / entrySize;

        // Round down to nearest power of 2
        Size = 1;
        while (Size <= maxEntries / 2) Size *= 2;

        table = new TTEntry[Size];
        sizeMask = Size - 1;

        Debug.Log($"Transposition Table initialized: {Size} entries ({Size * entrySize / (1024 * 1024)} MB)");
    }

    // Store position in transposition table
    public void Store(ulong zobristKey, short score, byte depth, TTEntryType type, Move bestMove)
    {
        int index = GetIndex(zobristKey);
        ref TTEntry entry = ref table[index];

        // Replacement strategy: always replace if empty, or replace if:
        // 1. Same position (zobrist key match)
        // 2. Higher depth
        // 3. Newer age and similar depth
        bool shouldReplace = entry.IsEmpty ||
                           entry.zobristKey == zobristKey ||
                           depth >= entry.depth ||
                           (currentAge != entry.age && depth >= entry.depth - 2);

        if (shouldReplace)
        {
            if (!entry.IsEmpty && entry.zobristKey != zobristKey)
                Collisions++;

            entry = new TTEntry(zobristKey, score, depth, type, bestMove, currentAge);
        }
    }

    // Probe transposition table
    public bool Probe(ulong zobristKey, byte depth, short alpha, short beta, int ply, out short score, out Move bestMove)
    {
        score = 0;
        bestMove = new Move();

        int index = GetIndex(zobristKey);
        ref TTEntry entry = ref table[index];

        if (entry.IsEmpty || entry.zobristKey != zobristKey)
            return false;

        bestMove = entry.bestMove;

        // Only use score if search depth is sufficient
        if (entry.depth >= depth)
        {
            Hits++;

            // Pass the CURRENT ply to the adjustment function
            short adjustedScore = AdjustMateScore(entry.score, ply);

            switch (entry.type)
            {
                case TTEntryType.Exact:
                    score = adjustedScore;
                    return true;

                case TTEntryType.LowerBound:
                    if (adjustedScore >= beta)
                    {
                        score = adjustedScore;
                        return true;
                    }
                    break;

                case TTEntryType.UpperBound:
                    if (adjustedScore <= alpha)
                    {
                        score = adjustedScore;
                        return true;
                    }
                    break;
            }
        }

        return false; // Hash hit but no cutoff
    }

    // Get best move from transposition table (for move ordering)
    public Move GetBestMove(ulong zobristKey)
    {
        int index = GetIndex(zobristKey);
        ref TTEntry entry = ref table[index];

        if (!entry.IsEmpty && entry.zobristKey == zobristKey)
            return entry.bestMove;

        return new Move(); // Empty move
    }

    // Clear transposition table
    public void Clear()
    {
        Array.Clear(table, 0, table.Length);
        currentAge = 0;
        Hits = 0;
        Collisions = 0;
    }

    // Age entries (call this each search or when starting new game)
    public void NextAge()
    {
        currentAge++;
        if (currentAge == 0) currentAge = 1; // Avoid 0 (empty marker)
    }

    // Get table index from zobrist key
    private int GetIndex(ulong zobristKey)
    {
        return (int)(zobristKey & (ulong)sizeMask);
    }

    // Adjust mate scores to be relative to current position
    private short AdjustMateScore(short score, int ply)
    {
        const short MATE_SCORE = 30000;

        if (score > MATE_SCORE - 1000)
        {
            return (short)(score - ply);  // Mate in fewer moves
        }
        else if (score < -MATE_SCORE + 1000)
        {
            return (short)(score + ply);  // Getting mated in fewer moves
        }

        return score;
    }

    // Get statistics
    public float GetHitRate()
    {
        int totalProbes = Hits + Collisions;
        return totalProbes > 0 ? (float)Hits / totalProbes : 0f;
    }

    public int GetUsedEntries()
    {
        int used = 0;
        for (int i = 0; i < Size; i++)
        {
            if (!table[i].IsEmpty) used++;
        }
        return used;
    }
}
