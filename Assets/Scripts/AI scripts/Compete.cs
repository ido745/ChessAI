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

    private float totaldepthNew = 0;
    private float totaldepthOld = 0;

    private float totalnpsNew = 0;
    private float totalnpsOld = 0;

    private float totalHitRateNew = 0;
    private float totalHitRateOld = 0;

    private int TotalMovesOld = 0;
    private int TotalMovesNew = 0;

    void Start()
    {
       StartCoroutine(RunCompetition());
    }

    public void updateInfoToNew(int depth, float nps, float hitRate)
    {
        if (depth > 128) // if a non-real number was reported, just add the average again
            totaldepthNew += totaldepthNew / TotalMovesNew;
        else
            totaldepthNew += depth;

        totalnpsNew += nps;

        totalHitRateNew += hitRate;
    }

    public void updateInfoToOld(int depth, float nps, float hitRate)
    {
        if (depth > 128) // if a non-real number was reported, just add the average again
            totaldepthOld += totaldepthOld / TotalMovesOld;
        else
            totaldepthOld += depth;

        totalnpsOld += nps;

        totalHitRateOld += hitRate;
    }

    IEnumerator RunCompetition()
    {
        yield return new WaitForSeconds(1f);
        totaldepthNew = 0;
        totaldepthOld = 0;

        totalnpsNew = 0;
        totalnpsOld = 0;

        totalHitRateNew = 0;
        totalHitRateOld = 0;

        TotalMovesNew = 0;
        TotalMovesOld = 0;

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
            bool oldsTurn = (oldIsWhite && boardLogic.turn == 0) || (!oldIsWhite && boardLogic.turn == 1);

            //Debug.Log($"Move {moveCount + 1}: {(boardLogic.turn == 0 ? "White" : "Black")} to move ({(oldsTurn ? "Old AI" : "New AI")})");

            // AI thinking
            if (oldsTurn)
            {
                OldVersion.StartThinking();
                yield return new WaitUntil(() => !OldVersion.IsThinking());
                TotalMovesOld++;
            }
            else
            {
                NewVersion.StartThinking();
                yield return new WaitUntil(() => !NewVersion.IsThinking());
                TotalMovesNew++;
            }

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

            if (boardLogic.moveCalculator.GenerateAllMoves(null, boardLogic.turn) == 0)
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

        Debug.Log(" ---- Old AI ----");
        Debug.Log($"average depth: {totaldepthOld / TotalMovesOld}\n");
        Debug.Log($"average nps: {totalnpsOld / TotalMovesOld}");
        Debug.Log($"average hit rate: {totalHitRateOld / TotalMovesOld}");

        Debug.Log(" ---- New AI ----");
        Debug.Log($"average depth: {totaldepthNew / TotalMovesNew}\n");
        Debug.Log($"average nps: {totalnpsNew / TotalMovesNew}");
        Debug.Log($"average hit rate: {totalHitRateNew / TotalMovesNew}");

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

        Debug.Log(" ---- Old AI ----");
        Debug.Log($"average depth: {totaldepthOld / TotalMovesOld}\n");
        Debug.Log($"average nps: {totalnpsOld / TotalMovesOld}");
        Debug.Log($"average hit rate: {totalHitRateOld / TotalMovesOld}");

        Debug.Log(" ---- New AI ----");
        Debug.Log($"average depth: {totaldepthNew / TotalMovesNew}\n");
        Debug.Log($"average nps: {totalnpsNew / TotalMovesNew}");
        Debug.Log($"average hit rate: {totalHitRateNew / TotalMovesNew}");

        Debug.Log($"Old AI win rate: {oldWinRate:F1}%");
        Debug.Log($"New AI win rate: {newWinRate:F1}%");
        Debug.Log("=================================");
    }
}