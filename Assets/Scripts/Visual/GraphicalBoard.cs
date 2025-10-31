using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

// Responsible for drawing the UI and creating the gameObjects needed.
public class GraphicalBoard : MonoBehaviour
{
    [SerializeField] public Vector2 tileSize = new Vector2(50, 50);

    [SerializeField] private Transform piecesHolder;

    [SerializeField] public RectTransform PromotionUI_W;
    [SerializeField] public RectTransform PromotionUI_B;

    [Header("Colors")]
    [SerializeField] Color whiteColor;
    [SerializeField] Color blackColor;
    [SerializeField] Color highlightColor = Color.cyan;

    [Header("Sprites")]
    [SerializeField] Sprite squareSprite;
    [SerializeField] Sprite targetSprite;
    [SerializeField] Sprite capturingSprite;

    [Header("Timer & UI")]
    [SerializeField] Timer timer;
    [SerializeField] GameObject GameOverPanel;
    TextMeshProUGUI endgameText;

    public const float X_CENTER = 3.5f, Y_CENTER = 3.5f;

    private int turn = 0;

    private Transform cellsHolder;
    private Transform targetsHolder;
    private Transform highlightHolder;
    private GameObject[] boardPieces = new GameObject[64];
    private BoardLogic boardLogic;

    public bool playingAI = true;
    public int playingColor = 0;
    public bool gameStarted = false;
    public bool isBoardFlipped = false;

    Dictionary<int, string> dict = new Dictionary<int, string>
    {
    { 1, "king" },
    { 2, "pawn" },
    { 3, "knight" },
    { 4, "bishop" },
    { 5, "rook" },
    { 6, "queen" }
    };

    void Start()
    {
        GameOverPanel.SetActive(false);
        endgameText = GameOverPanel.GetComponentInChildren<TextMeshProUGUI>();
        DrawBoardUI();
        
        boardLogic = GetComponentInChildren<BoardLogic>();
    }

    public void GameStarted()
    {
        gameStarted = true;
        if (playingColor == 1 && playingAI)
        {
            // Flip the board, we start as black.
            flipBoard();

            CallAI();
            print($"turn: {boardLogic.turn}");
        }
    }

    public void MakeVisualMove(Move move, GameObject go = null, bool animation = true)
    {
        if (go == null)
        {
            // Default: game object of the piece that just moved.
            go = boardPieces[move.from];
        }

        boardPieces[move.from] = null;
        int color = Piece.IsBlack(move.movedPiece);

        if (move.capturedPiece != 0)
        {
            boardPieces[move.to].SetActive(false);
        }
        boardPieces[move.to] = go;

        HighlightMove(move.from, move.to);

        if (move.flag == 1)
        {
            bool isKingSide = (move.to - move.from) == 2;
            int rookFrom, rookTo;
            if (isKingSide)
            {
                rookFrom = color == 0 ? 7 : 63;  // h1 or h8
                rookTo = color == 0 ? 5 : 61;    // f1 or f8
            }
            else
            {
                rookFrom = color == 0 ? 0 : 56;  // a1 or a8
                rookTo = color == 0 ? 3 : 59;    // d1 or d8
            }

            GameObject rookGO = boardPieces[rookFrom];
            boardPieces[rookFrom] = null;
            boardPieces[rookTo] = rookGO;

            Vector2 anchoredPos = GetBoardPositionFromIndex(rookTo);
            RectTransform rect = rookGO.GetComponent<RectTransform>();

            if(animation)
                StartCoroutine(SmoothMove(rookGO, anchoredPos, 0.1f));
            else
                rect.anchoredPosition = anchoredPos;
            rookGO.GetComponent<BasePiece>().index = rookTo;
        }

        else if (move.flag == 2)
        {
            // Promotion -- begin by destroying the pawn game object, and instantiating a promoted piece.
            Destroy(go);

            Vector2 anchoredPos = GetBoardPositionFromIndex(move.to);

            int promotedColor = Piece.GetColor(move.promotionPiece);

            string pieceName = dict[Piece.GetPieceType(move.promotionPiece)];

            if ((promotedColor & 8) != 0) { pieceName += "W"; }
            else if ((promotedColor & 16) != 0) { pieceName += "B"; }

            // Instantiate the piece.
            var piecePrefab = Resources.Load<GameObject>(pieceName);

            GameObject instance = Instantiate(piecePrefab, this.transform);

            if (playingColor == 1)
                instance.GetComponent<RectTransform>().Rotate(0, 0, 180);

            var canvas = transform.GetComponentInParent<Canvas>();
            var raycaster = transform.GetComponentInParent<GraphicRaycaster>();

            var piece = SetPiece<BasePiece>(instance, canvas, raycaster, anchoredPos, move.movedPiece, move.to);

            piece.GetComponent<BasePiece>().SetType(move.promotionPiece);

            boardPieces[move.to] = instance;
        }

        else if (move.flag == 3)
        {
            // En passant
            int isBlack = Piece.IsBlack(move.movedPiece);

            if (isBlack == 1)
            {
                boardPieces[move.to + 8].SetActive(false);
                boardPieces[move.to + 8] = null;
            }
            if (isBlack == 0)
            {
                boardPieces[move.to - 8].SetActive(false);
                boardPieces[move.to - 8] = null;
            }
        }
        if (move.flag != 2)
        {
            Vector2 newPos = GetBoardPositionFromIndex(move.to);

            RectTransform rect = go.GetComponent<RectTransform>();

            if (animation)
                StartCoroutine(SmoothMove(go, newPos, 0.1f));
            else
                rect.anchoredPosition = newPos;
            go.GetComponent<BasePiece>().index = move.to;
        }

        timer.SwitchTurn();

        // Look for checkmate/stalemate
        int checkForEndGame = boardLogic.FindPinsAndChecks(turn);
        if (checkForEndGame == 1)
        {
            // 1 - turn won by checkmate

            if (turn == 1)
                EndGame("Game over - white won!");
            else
                EndGame("Game over - black won!");
            
        }
        if (checkForEndGame == 2)
        {
            // Stalemate
            EndGame("Stalemate!");
        }
    }

    public Vector2 GetBoardPositionFromIndex(int index)
    {
        int x = index % 8;
        int y = index / 8;
        Vector2 newPos = new Vector2((x - X_CENTER) * tileSize.x, (y - Y_CENTER) * tileSize.y);
        return newPos;
    }

    public void flipBoard()
    {
        RectTransform rt = GetComponent<RectTransform>();
        rt.Rotate(0f, 0f, 180f);
        isBoardFlipped = !isBoardFlipped;

        foreach (GameObject piece in boardPieces)
        {
            if (piece == null) continue;

            rt = piece.GetComponent<RectTransform>();
            rt.Rotate(0f, 0f, 180f);
        }

        // Flip the timers
        timer.flipTimers();

        // Flip promotion bars
        PromotionUI_W.Rotate(0, 0, 180);
        PromotionUI_B.Rotate(0, 0, 180);
    }

    public void EndGame(string endgameMessage)
    {
        GameOverPanel.SetActive(true);
        endgameText.text = endgameMessage;
        timer.PauseTimer();
        boardLogic.EndGame();
    }
    private IEnumerator SmoothMove(GameObject piece, Vector2 targetPos, float duration)
    {
        RectTransform rect = piece.GetComponent<RectTransform>();
        Vector2 startPos = rect.anchoredPosition;

        // Wait two frames to ensure smooth start
        yield return null;
        yield return null;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            rect.anchoredPosition = Vector2.Lerp(startPos, targetPos, t);
            yield return null;
        }

        rect.anchoredPosition = targetPos; // Snap to final position
    }

    void DrawBoardUI()
    {
        // Creates a cell holder, which is at the top = the background.
        GameObject holder = new GameObject("CellsHolder", typeof(RectTransform));
        holder.transform.SetParent(this.transform, false);
        cellsHolder = holder.transform;

        cellsHolder.SetAsFirstSibling();

        // HighLight holder
        highlightHolder = transform.Find("HighLightHolder");

        if (highlightHolder == null)
            highlightHolder = (new GameObject("HighLightHolder", typeof(RectTransform))).transform;
        highlightHolder.SetParent(this.transform, false);

        // Target holder
        targetsHolder = transform.Find("TargetHolder");

        if (targetsHolder == null)
            targetsHolder = (new GameObject("TargetHolder", typeof(RectTransform))).transform;
        targetsHolder.SetParent(this.transform, false);

        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                bool isLight = (x + y) % 2 != 0;
                Color color = isLight ? whiteColor : blackColor;

                Vector2 anchoredPos = GetBoardPositionFromIndex(y * 8 + x);
                GameObject square = DrawSquare(anchoredPos, color, y * 8 + x, squareSprite, cellsHolder);
                square.tag = "Cell";
            }
        }
    }

    GameObject DrawSquare(Vector2 anchoredPosition, Color color, int i, Sprite sprite, Transform holder, float scaleMultiplier = 1f, float capacity = 1f)
    {
        GameObject square = new GameObject("Cell" + i, typeof(RectTransform), typeof(Image));
        
        square.transform.SetParent(holder, false);

        RectTransform rt = square.GetComponent<RectTransform>();
        rt.sizeDelta = tileSize * scaleMultiplier;
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);

        rt.anchoredPosition = anchoredPosition;

        Image img = square.GetComponent<Image>();
        img.color = new Color(color.r, color.g, color.b, capacity);
        img.sprite = sprite;
        img.type = Image.Type.Simple;

        return square;
    }

    public void DrawPieces(int[] board)
    {
        for (int i = 0; i < 64; i++)
        {
            int pieceType = board[i] % 8;
            if (pieceType == 0)
                continue;

            string pieceName = dict[pieceType];

            if ((board[i] & 8) != 0) { pieceName += "W"; }
            else if ((board[i] & 16) != 0) { pieceName += "B"; }

            // Instantiate the piece.
            var piecePrefab = Resources.Load<GameObject>(pieceName);
            if (piecePrefab == null)
            {
                Debug.LogWarning($"Could not load prefab for {pieceName}");
                continue;
            }
            GameObject instance = Instantiate(piecePrefab, this.transform);

            Vector2 anchoredPos = GetBoardPositionFromIndex(i);

            var canvas = transform.GetComponentInParent<Canvas>();
            var raycaster = transform.GetComponentInParent<GraphicRaycaster>();

            var piece = SetPiece<BasePiece>(instance, canvas, raycaster, anchoredPos, board[i], i);

            boardPieces[i] = instance;
        }
    }

    public T SetPiece<T>(GameObject go, Canvas canvas, GraphicRaycaster raycaster, Vector2 pos, int pieceType, int indx)
        where T : BasePiece
    {
        var piece = go.AddComponent<T>();
        piece.canvas = canvas;
        piece.raycaster = raycaster;
        piece.SetType(pieceType);
        piece.SetIndx(indx);

        go.transform.SetParent(piecesHolder, false);

        Image img = ((GameObject)go).GetComponent<Image>();
        img.type = Image.Type.Sliced; // or Simple if you prefer

        img.raycastTarget = true;                            // ensure it is hittable

        RectTransform rt = go.GetComponent<RectTransform>();
        // Set position and size.
        rt.sizeDelta = tileSize;
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);

        rt.anchoredPosition = pos;
        return piece;
    }

    public void HighlightMove(int from, int to)
    {
        HideHighlight();

        // from square
        Vector2 anchoredPos = GetBoardPositionFromIndex(from);

        DrawSquare(anchoredPos, highlightColor, from, capturingSprite, highlightHolder, 1f, 0.5f);

        // to square
        anchoredPos = GetBoardPositionFromIndex(to);

        DrawSquare(anchoredPos, highlightColor, to, capturingSprite, highlightHolder, 1f, 0.5f);
    }

    public void HideHighlight()
    {
        if (highlightHolder == null || highlightHolder.gameObject == null)
            return;

        foreach (Transform child in highlightHolder)
        {
            Destroy(child.gameObject);
        }
    }

    public void ShowTargets(ulong moves, ulong enemyPieces)
    {
        // This loop efficiently iterates through each '1' in the moves bitboard
        while (moves != 0)
        {
            // 1. Find the index of the next available move (the least significant bit)
            int to = BitScan.TrailingZeroCount(moves);
            ulong moveBit = 1UL << to;

            // 2. Calculate the screen position
            Vector2 anchoredPos = GetBoardPositionFromIndex(to);

            // 3. Check if this move is a capture by seeing if the destination
            //    square is occupied by an enemy piece.
            if ((moveBit & enemyPieces) != 0)
            {
                // It's a capture - draw the capturing sprite
                DrawSquare(anchoredPos, Color.red, to, capturingSprite, targetsHolder, 1f, 0.5f);
            }
            else
            {
                // It's a quiet move - draw the normal target sprite
                DrawSquare(anchoredPos, Color.red, to, targetSprite, targetsHolder, 0.5f);
            }

            // 4. Clear the bit we just processed so the loop can find the next one
            moves &= moves - 1;
        }
    }

    public void HideTargets()
    {
        if (targetsHolder == null || targetsHolder.gameObject == null)
            return;

        foreach (Transform child in targetsHolder)
        {
            Destroy(child.gameObject);
        }
    }

    public IEnumerator ShowPromotionUI(int oldIndex, int promotionIndex, GameObject pieceGO)
    {
        Vector2 anchoredPos = GetBoardPositionFromIndex(promotionIndex);

        RectTransform promBar = (promotionIndex / 8 == 0) ? PromotionUI_B : PromotionUI_W;
        // Move the UI to the promotion position and display it.
        promBar.anchoredPosition = anchoredPos;

        promBar.gameObject.SetActive(true);

        // Get the new piece type from user
        int chosenPiece = -1;
        promBar.gameObject.GetComponent<PromotionUI>().Show(piece => chosenPiece = piece);

        // wait until a piece is chosen
        yield return new WaitUntil(() => chosenPiece != -1);

        promBar.gameObject.SetActive(false);

        BasePiece promotedPiece = pieceGO.GetComponent<BasePiece>();
        int previousType = promotedPiece.GetPieceType();

        Move move = new Move(oldIndex, promotionIndex, previousType, boardLogic.board[promotionIndex], 2, chosenPiece | Piece.GetColor(previousType));

        boardLogic.MakeMove(move);
        MakeVisualMove(move, pieceGO);

        // Call the ai now that we're done.
        CallAI();
    }

    public void CallAI()
    {
        if (!playingAI)
        {
            // We're playing a friend
            flipBoard();
            return;
        }

        // We need to wait one frame to let the UI clear.
        StartCoroutine(CallAINextFrame());
    }

    private IEnumerator CallAINextFrame()
    {
        yield return null; // wait one frame, so UI updates finish
        // Call the AI to make a move
        GameObject AIobject = GameObject.Find("AI_manager");
        AI ai = AIobject.GetComponent<AI>();
        ai.StartThinking();
    }
}
