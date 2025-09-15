using UnityEngine;
using UnityEngine.UI;
using System;

public class PromotionUI : MonoBehaviour
{
    public Button queenButton;
    public Button knightButton;
    public Button bishopButton;
    public Button rookButton;
    public bool listening = false;

    private Action<int> onChosen;

    public void Show(Action<int> callback)
    {
        listening = true;
        gameObject.SetActive(true);
        onChosen = callback;

        knightButton.onClick.AddListener(() => Select(Piece.Knight));
        bishopButton.onClick.AddListener(() => Select(Piece.Bishop));
        rookButton.onClick.AddListener(() => Select(Piece.Rook));
        queenButton.onClick.AddListener(() => Select(Piece.Queen));
    }

    private void Select(int pieceCode)
    {
        onChosen?.Invoke(pieceCode);  // return piece code
        Hide();
        listening = false;
    }

    public void StartListening()
    {
        listening = true;
    }

    private void Update()
    {
        if (!listening) return;

        if (Input.GetKeyDown(KeyCode.Q))
            Select(Piece.Queen);
        else if (Input.GetKeyDown(KeyCode.W))
            Select(Piece.Knight);
        else if (Input.GetKeyDown(KeyCode.E))
            Select(Piece.Bishop);
        else if (Input.GetKeyDown(KeyCode.R))
            Select(Piece.Rook);
    }

    private void Hide()
    {
        gameObject.SetActive(false);

        // cleanup listeners
        knightButton.onClick.RemoveAllListeners();
        bishopButton.onClick.RemoveAllListeners();
        rookButton.onClick.RemoveAllListeners();
        queenButton.onClick.RemoveAllListeners();
    }
}
