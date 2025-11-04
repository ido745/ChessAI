using System.Collections;
using UnityEngine;

public class Compete : MonoBehaviour
{
    [SerializeField] private AI OldVersion;
    [SerializeField] private AI1 NewVersion;
    [SerializeField] private BoardLogic boardLogic;
    [SerializeField] private GraphicalBoard graphicalBoard;
    [SerializeField] private int numberOfGames = 30;
    [SerializeField] private float delayBetweenMoves = 0.5f;

    private int oldWins = 0;
    private int newWins = 0;
    private int draws = 0;

    //void Start()
    //{
    //    StartCoroutine(RunCompetition());
    //}

    IEnumerator RunCompetition()
    {
        yield return new WaitForSeconds(1f);

        for (int gameNum = 0; gameNum < numberOfGames; gameNum++)
        {
            bool oldIsWhite = (gameNum % 2 == 0);

            Debug.Log($"=== Starting game {gameNum + 1}/{numberOfGames} ===");
            Debug.Log($"Old AI is {(oldIsWhite ? "White" : "Black")}");

            ResetGame();

            yield return StartCoroutine(PlayGame(oldIsWhite));

            RecordGameResult(gameNum + 1, oldIsWhite);

            yield return new WaitForSeconds(2f);
        }

        PrintFinalResults();
    }

    IEnumerator PlayGame(bool oldIsWhite)
    {
        int moveCount = 0;
        int maxMoves = 300;

        while (moveCount < maxMoves)
        {
            // Check for draw conditions BEFORE making a move
            if (boardLogic.IsDraw())
            {
                Debug.Log($"Draw detected at move {moveCount}");
                Debug.Log($"Reason - HalfMoveClock: {boardLogic.halfMoveClock}, PositionHistory count: {boardLogic.positionHistory.Count}");
                boardLogic.gameEnded = true;
                boardLogic.winner = -1;
                break;
            }

            // Generate legal moves
            //Move[] testMoves = new Move[256];
            //int legalMoves = boardLogic.moveCalculator.GenerateAllMoves(testMoves, boardLogic.turn);

            if (boardLogic.gameEnded && boardLogic.winner == 1 - boardLogic.turn)
            {
                // Game over - checkmate or stalemate
                boardLogic.gameEnded = true;
                if (boardLogic.IsInCheck())
                {
                    boardLogic.winner = 1 - boardLogic.turn;
                    Debug.Log($"Checkmate! {(boardLogic.turn == 0 ? "Black" : "White")} wins!");
                }
                else
                {
                    boardLogic.winner = -1;
                    Debug.Log("Stalemate!");
                }
                break;
            }

            bool oldsTurn = (oldIsWhite && boardLogic.turn == 0) || (!oldIsWhite && boardLogic.turn == 1);

            //Debug.Log($"Move {moveCount + 1}: {(boardLogic.turn == 0 ? "White" : "Black")} to move ({(oldsTurn ? "Old AI" : "New AI")})");

            // AI thinking
            if (oldsTurn)
            {
                OldVersion.StartThinking();
                yield return new WaitUntil(() => !OldVersion.IsThinking());
            }
            else
            {
                NewVersion.StartThinking();
                yield return new WaitUntil(() => !NewVersion.IsThinking());
            }

            moveCount++;
            yield return new WaitForSeconds(delayBetweenMoves);
        }

        if (moveCount >= maxMoves)
        {
            Debug.Log("Game exceeded max moves - declaring draw");
            boardLogic.gameEnded = true;
            boardLogic.winner = -1;
        }
    }

    void ResetGame()
    {
        boardLogic.ResetBoard();
        graphicalBoard.DrawPieces(boardLogic.board);

        OldVersion.ResetAI();
        NewVersion.ResetAI();

        Debug.Log("Board reset for new game");
    }

    void RecordGameResult(int gameNumber, bool oldIsWhite)
    {
        // Check the winner field to determine the result
        if (boardLogic.winner == -1)
        {
            // Draw
            draws++;
            Debug.Log($"Game {gameNumber}: Draw");
        }
        else
        {
            // Someone won
            if ((oldIsWhite && boardLogic.winner == 0) || (!oldIsWhite && boardLogic.winner == 1))
            {
                oldWins++;
                Debug.Log($"Game {gameNumber}: Old AI wins!");
            }
            else
            {
                newWins++;
                Debug.Log($"Game {gameNumber}: New AI wins!");
            }
        }

        Debug.Log($"Current score: Old AI {oldWins} - {newWins} New AI (Draws: {draws})");
    }

    void PrintFinalResults()
    {
        Debug.Log("=================================");
        Debug.Log("===== COMPETITION RESULTS =====");
        Debug.Log("=================================");
        Debug.Log($"Old AI wins: {oldWins}");
        Debug.Log($"New AI wins: {newWins}");
        Debug.Log($"Draws: {draws}");
        Debug.Log($"Total games: {numberOfGames}");

        float oldWinRate = (oldWins / (float)numberOfGames) * 100f;
        float newWinRate = (newWins / (float)numberOfGames) * 100f;

        Debug.Log($"Old AI win rate: {oldWinRate:F1}%");
        Debug.Log($"New AI win rate: {newWinRate:F1}%");
        Debug.Log("=================================");
    }
}