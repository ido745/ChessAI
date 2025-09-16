using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

// Responsible for drawing the UI and creating the gameObjects needed.
public class GraphicalBoard : MonoBehaviour
{
    [SerializeField] Color whiteColor;
    [SerializeField] Color blackColor;
    [SerializeField] public Vector2 tileSize = new Vector2(50, 50);
    [SerializeField] Sprite squareSprite;
    [SerializeField] Sprite targetSprite;
    [SerializeField] Sprite capturingSprite;

    private Transform cellsHolder;
    private Transform targetsHolder;
    private Transform debugHolder;
    private GameObject[] boardPieces = new GameObject[64];

    [SerializeField] private Transform piecesHolder;

    [SerializeField] public RectTransform PromotionUI_W;
    [SerializeField] public RectTransform PromotionUI_B;

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
        DrawBoardUI();
    }

    public void MakeVisualMove(Move move, GameObject go = null)
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

            int x = rookTo % 8;
            int y = rookTo / 8;
            Vector2 anchoredPos = new Vector2((x - 4) * tileSize.x, (y - 3.5f) * tileSize.y);
            RectTransform rect = rookGO.GetComponent<RectTransform>();
            rect.anchoredPosition = anchoredPos;
            rookGO.GetComponent<BasePiece>().index = rookTo;
        }

        else if (move.flag == 2)
        {
            // Promotion -- begin by destroying the pawn game object, and instantiating a promoted piece.
            Destroy(go);

            int x = move.to % 8;
            int y = move.to / 8;
            Vector2 anchoredPos = new Vector2((x - 4) * tileSize.x, (y - 3.5f) * tileSize.y);

            int promotedColor = Piece.GetColor(move.promotionPiece);

            string pieceName = dict[Piece.GetPieceType(move.promotionPiece)];

            if ((promotedColor & 8) != 0) { pieceName += "W"; }
            else if ((promotedColor & 16) != 0) { pieceName += "B"; }

            // Instantiate the piece.
            var piecePrefab = Resources.Load<GameObject>(pieceName);

            GameObject instance = Instantiate(piecePrefab, this.transform);

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
                boardPieces[move.to + 8].SetActive(false);
                boardPieces[move.to + 8] = null;
            if (isBlack == 0)
                boardPieces[move.to - 8].SetActive(false);
                boardPieces[move.to - 8] = null;
        }
        if (move.flag == 0 || move.flag == 1 || move.flag == 4)
        {
            Vector2 newPos = new Vector2((move.to % 8 - 4) * tileSize.x, (move.to / 8 - 3.5f) * tileSize.y);

            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchoredPosition = newPos;
            go.GetComponent<BasePiece>().index = move.to;
        }
    }

    void DrawBoardUI()
    {
        CreateCellsHolder();
        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                bool isLight = (x + y) % 2 != 0;
                Color color = isLight ? whiteColor : blackColor;

                Vector2 anchoredPos = new Vector2((x-4) * tileSize.x, (y-3.5f) * tileSize.y);
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

            int x = i % 8;
            int y = i / 8;
            Vector2 anchoredPos = new Vector2((x - 4) * tileSize.x, (y - 3.5f) * tileSize.y);

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

    public void ShowTargets(List<Move> moves)
    {
        // Create holder object
        GameObject holder = new GameObject("TargetHolder", typeof(RectTransform));
        holder.transform.SetParent(this.transform, false);
        targetsHolder = holder.transform;

        targetsHolder.SetSiblingIndex(1);

        foreach (var move in moves)
        {
            // Position is calculated by the same way we calculated the Cells' position
            int x = move.to % 8;
            int y = move.to / 8;
            Vector2 anchoredPos = new Vector2((x - 4) * tileSize.x, (y - 3.5f) * tileSize.y);

            if (move.capturedPiece == 0)
                DrawSquare(anchoredPos, Color.red, move.to, targetSprite, targetsHolder, 0.5f);
            else
                DrawSquare(anchoredPos, Color.red, move.to, capturingSprite, targetsHolder, 1f, 0.5f);
        }
    }

    public void ShowTargets(ulong moves, ulong enemyPieces)
    {
        GameObject holder = new GameObject("TargetHolder", typeof(RectTransform));
        holder.transform.SetParent(this.transform, false);
        targetsHolder = holder.transform;

        targetsHolder.SetSiblingIndex(1);

        // This loop efficiently iterates through each '1' in the moves bitboard
        while (moves != 0)
        {
            // 1. Find the index of the next available move (the least significant bit)
            int to = BitScan.TrailingZeroCount(moves);
            ulong moveBit = 1UL << to;

            // 2. Calculate the screen position
            int x = to % 8;
            int y = to / 8;
            Vector2 anchoredPos = new Vector2((x - 4) * tileSize.x, (y - 3.5f) * tileSize.y);

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

    public void DebugShowSquares(ulong squares, Color squareColor)
    {
        DebugHideSquares();
        GameObject holder = new GameObject("DebugVisualizer", typeof(RectTransform));
        holder.transform.SetParent(this.transform, false);
        debugHolder = holder.transform;

        debugHolder.SetSiblingIndex(1);

        // This loop efficiently iterates through each '1' in the moves bitboard
        while (squares != 0)
        {
            // 1. Find the index of the next available move (the least significant bit)
            int to = BitScan.TrailingZeroCount(squares);
            ulong moveBit = 1UL << to;

            // 2. Calculate the screen position
            int x = to % 8;
            int y = to / 8;
            Vector2 anchoredPos = new Vector2((x - 4) * tileSize.x, (y - 3.5f) * tileSize.y);

            
            DrawSquare(anchoredPos, squareColor, to, capturingSprite, debugHolder, 1f, 0.5f);
            squares &= squares - 1;
        }
    }

    public IEnumerator ShowPromotionUI(int oldIndex, int promotionIndex, GameObject pieceGO)
    {
        int x = promotionIndex % 8;
        int y = promotionIndex / 8;
        Vector2 anchoredPos = new Vector2((x - 4) * tileSize.x, (y - 3.5f) * tileSize.y);

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

        BoardLogic boardManager = GetComponentInChildren<BoardLogic>();
        Move move = new Move(oldIndex, promotionIndex, previousType, boardManager.board[promotionIndex], 2, chosenPiece | Piece.GetColor(previousType));

        boardManager.MakeMove(move);
        MakeVisualMove(move, pieceGO);

        // Call the ai now that we're done.
        CallAI();
    }

    public void CallAI()
    {
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

    public void DebugHideSquares()
    {
        if (debugHolder != null && debugHolder.gameObject != null)
            Destroy(debugHolder.gameObject);
    }

    public void HideTargets()
    {
        if (targetsHolder != null && targetsHolder.gameObject != null)
            Destroy(targetsHolder.gameObject);
    }

    void CreateCellsHolder()
    {
        // Creates a cell holder, which is at the top = the background.
        GameObject holder = new GameObject("CellsHolder", typeof(RectTransform));
        holder.transform.SetParent(this.transform, false);
        cellsHolder = holder.transform;

        // Make sure it's rendered behind the pieces
        cellsHolder.SetAsFirstSibling();
    }
}
