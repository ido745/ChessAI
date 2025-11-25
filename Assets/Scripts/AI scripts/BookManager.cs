using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

public class BookManager : MonoBehaviour
{
    private BoardLogic boardLogic;

    // Saves the opening books.
    private TextAsset[] openingFiles;

    public static BookManager Instance { get; private set; }

    private void Awake()
    {
        // If there is already an instance and it is not this one, destroy this object
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    public void Start()
    {
        if (BoardLogic.Instance != null)
        {
            boardLogic = BoardLogic.Instance;
        }
        openingFiles = Resources.LoadAll<TextAsset>("Openings");
    }

    public Move? TryBookMove()
    {
        string currentMoves = boardLogic.openingLine.Trim();

        // Store potential book moves with their details
        List<(Move move, string openingName, int lineLength)> candidateMoves = new List<(Move, string, int)>();

        foreach (TextAsset openingFile in openingFiles)
        {
            // Split by newline, handling both \r\n (Windows) and \n (Unix)
            string[] lines = openingFile.text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (string line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                string[] parts = line.Split('\t');
                if (parts.Length < 3) continue;

                string openingName = parts[1].Trim();
                string moves = parts[2].Trim();

                int index = moves.IndexOf(currentMoves, StringComparison.Ordinal);
                if (index >= 0)
                {
                    string remaining = moves.Substring(index + currentMoves.Length).Trim();
                    if (string.IsNullOrEmpty(remaining)) continue;

                    string[] tokens = remaining.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                    // Skip over move numbers like "4."
                    string nextSan = tokens.FirstOrDefault(t => !t.EndsWith("."));
                    if (nextSan == null) continue;

                    Move? bookMove = FindMoveFromSAN(nextSan);
                    if (bookMove.HasValue)
                    {
                        candidateMoves.Add((bookMove.Value, openingName, moves.Length));
                    }
                }
            }
        }

        if (candidateMoves.Count == 0)
            return null;

        // Sort by line length (most detailed first) and take top 10
        var topCandidates = candidateMoves
            .OrderByDescending(x => x.lineLength)
            .Take(10)
            .ToList();

        // Randomly select from the top candidates
        if (topCandidates.Count > 0)
        {
            int randomIndex = UnityEngine.Random.Range(0, topCandidates.Count);
            var selectedMove = topCandidates[randomIndex];

            if (InfoTextManager.Instance != null)
                InfoTextManager.Instance.openingText.text = selectedMove.openingName;

            return selectedMove.move;
        }

        return null;
    }

    private Move? FindMoveFromSAN(string san)
    {
        Move[] moves = new Move[256];
        int movesCount = boardLogic.moveCalculator.GenerateAllMoves(moves, boardLogic.turn);

        for (int i = 0; i < movesCount; i++)
        {
            Move move = moves[i];
            string moveSan = boardLogic.MoveToSAN(move);

            if (moveSan == san)
                return move;
        }

        return null;
    }
}
