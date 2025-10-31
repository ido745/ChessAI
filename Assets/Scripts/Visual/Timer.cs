using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class Timer : MonoBehaviour
{
    TextMeshProUGUI currentClock;
    int turn = 0;

    TextMeshProUGUI whiteClock;
    TextMeshProUGUI blackClock;

    [SerializeField] GraphicalBoard graphicalBoard;

    [SerializeField] GameObject whiteClockObj;
    [SerializeField] GameObject blackClockObj;

    [SerializeField] float startingTimeInMinutes = 10f;

    private float whiteTimeRemaining;
    private float blackTimeRemaining;
    private bool isRunning = false;
    private bool noTime = false;

    private void Start()
    {
        Initialize();
    }

    void Initialize()
    {
        // Initialize both clocks to starting time
        whiteTimeRemaining = startingTimeInMinutes * 60f;
        blackTimeRemaining = startingTimeInMinutes * 60f;

        whiteClock = whiteClockObj.GetComponent<TextMeshProUGUI>();
        blackClock = blackClockObj.GetComponent<TextMeshProUGUI>();

        UpdateClockDisplay(whiteClock, whiteTimeRemaining);
        UpdateClockDisplay(blackClock, blackTimeRemaining);
    }

    public void BeginGame(int GetTurn)
    {
        turn = GetTurn;
        if (turn == 0)
            currentClock = whiteClock;
        else
            currentClock = blackClock;

        if (noTime)
        {
            gameObject.SetActive(false);
            return;
        }

        isRunning = true;
    }

    public void SwitchTurn()
    {
        turn = 1 - turn;
        if (turn == 0)
            currentClock = whiteClock;
        else
            currentClock = blackClock;
    }

    public void changeTime(int i)
    {
        noTime = false;
        switch (i)
        {
            case 0:
                startingTimeInMinutes = 10;
                break;
            case 1:
                startingTimeInMinutes = 3;
                break;
            case 2:
                noTime = true;
                gameObject.SetActive(false);
                break;
            default:
                break;
        }
        Initialize();
    }

    public void flipTimers()
    {
        Vector2 tempPos = whiteClockObj.transform.position;
        whiteClockObj.transform.position = blackClockObj.transform.position;
        blackClockObj.transform.position = tempPos;
    }

    void Update()
    {
        if (!isRunning)
            return;

        // Decrease the current player's clock
        if (turn == 0)
        {
            whiteTimeRemaining -= Time.deltaTime;
            if (whiteTimeRemaining <= 0)
            {
                whiteTimeRemaining = 0;
                graphicalBoard.EndGame("Timeout - black won!");
            }
            UpdateClockDisplay(whiteClock, whiteTimeRemaining);
        }
        else
        {
            blackTimeRemaining -= Time.deltaTime;
            if (blackTimeRemaining <= 0)
            {
                blackTimeRemaining = 0;
                graphicalBoard.EndGame("Timeout - white won!");
            }
            UpdateClockDisplay(blackClock, blackTimeRemaining);
        }
    }

    void UpdateClockDisplay(TextMeshProUGUI clock, float timeInSeconds)
    {
        // Convert seconds to minutes:seconds format
        int minutes = Mathf.FloorToInt(timeInSeconds / 60f);
        int seconds = Mathf.FloorToInt(timeInSeconds % 60f);

        // Format as MM:SS
        clock.text = string.Format("{0:00}:{1:00}", minutes, seconds);
    }

    public void PauseTimer()
    {
        isRunning = false;
    }

    public void ResumeTimer()
    {
        isRunning = true;
    }
}