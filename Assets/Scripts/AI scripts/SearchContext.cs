using System.Diagnostics;

public class SearchContext
{
    public int MaxPly { get; set; } = 128;
    public int TimeLimit { get; set; } = 1000;
    public bool Aborted { get; set; }
    public int NodesSearched { get; set; }
    public int TTProbes { get; set; }
    public int SelDepth { get; set; }
    public int CurrentDepth { get; set; }
    public Stopwatch SearchStopwatch { get; set; }

    // PV tables
    public Move[,] PVTable { get; set; }
    public int[] PVLength { get; set; }

    public void Reset() { /* ... */ }
}