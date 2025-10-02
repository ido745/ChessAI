using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System;
using UnityEditor.Rendering.LookDev;
using System.Collections;

public class BasePiece : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerDownHandler, IPointerUpHandler
{
    public Canvas canvas; // Assign via inspector
    public GraphicRaycaster raycaster; // Assign via inspector
    public RectTransform rectTransform;
    public CanvasGroup canvasGroup;

    public int index;
    private int pieceType;
    private BoardLogic boardManager;
    private GraphicalBoard boardDrawer;
    private Vector2 originalPosition;
    private bool isMoving = false;

    ulong moves = 0;

    void Start()
    {
        boardManager = transform.GetComponentInParent<BoardLogic>();
        boardDrawer = transform.parent.GetComponentInParent<GraphicalBoard>();
        rectTransform = GetComponent<RectTransform>();

        if (boardManager == null) Debug.LogError("BoardLogic not found!");
        if (boardDrawer == null) Debug.LogError("GraphicalBoard not found!");
        if (rectTransform == null) Debug.LogError("RectTransform not found!");
    }

    private int preSelectedPromotionPiece = -1; // -1 means no pre-selection

    public void OnPointerDown(PointerEventData eventData){}

    public void OnPointerUp(PointerEventData eventData)
    {
        PieceSelected();
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        PieceSelected();
    }

    public void OnDrag(PointerEventData eventData)
    {
        // Move the piece with the mouse
        if (isMoving)
        {
            rectTransform.anchoredPosition += eventData.delta / canvas.scaleFactor;
        }
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        MovePiece(true);
        isMoving = false;
    }

    private void PieceSelected()
    {
        // Clear anything that has already been displayed
        boardDrawer.HideTargets();

        // Show the legal moves and prepare for move
        PointerEventData pointerData = new PointerEventData(EventSystem.current);
        pointerData.position = Input.mousePosition;

        int type = pieceType;
        // Only move if this is our turn
        isMoving = (type >> 4) == boardManager.turn;
        print($"hello! moving? {isMoving}");
        if (!isMoving)
            return;

        moves = boardManager.GenerateMoves(index, pieceType);

        ulong enemies = (Piece.IsBlack(pieceType) == 1) ? boardManager.Wbitboard : boardManager.Bbitboard;
        boardDrawer.ShowTargets(moves, enemies);
        print("showing targets");
        originalPosition = rectTransform.anchoredPosition;

        preSelectedPromotionPiece = -1;
    }

    private void MovePiece(bool isDragged)
    {
        if (!isMoving)
        {
            return;
        }
        print("started to move");
        List<RaycastResult> results = new List<RaycastResult>();

        // Raycast from the piece's position if we're dragged, the mouse's position if it's a click
        PointerEventData data = new PointerEventData(EventSystem.current);
        data.position = isDragged ? transform.position : Input.mousePosition;

        raycaster.Raycast(data, results);

        foreach (RaycastResult result in results)
        {
            if (result.gameObject.CompareTag("Cell"))
            {

                // Snap the piece to the square
                RectTransform squareRect = result.gameObject.GetComponent<RectTransform>();
                rectTransform.anchoredPosition = squareRect.anchoredPosition;

                // Calculate the new index of the piece
                string name = result.gameObject.name;
                string numberPart = Regex.Match(name, @"\d+$", RegexOptions.RightToLeft).Value;

                if (string.IsNullOrEmpty(numberPart))
                {
                    break;
                }

                boardDrawer.HideTargets();

                int newIndex = int.Parse(numberPart);
                if (index == newIndex)
                    break;

                if (((1UL << newIndex) & moves) == 0)
                {
                    // Invalid move.
                    break;
                }

                int flag = boardManager.FindFlag(pieceType, index, newIndex);

                // If the move is promotion, we would also like to update the new piece type
                if (flag == 2)
                {
                    // Check if we have a pre-selected promotion piece
                    if (preSelectedPromotionPiece != -1)
                    {
                        // Use the pre-selected piece
                        int promotionPieceType = preSelectedPromotionPiece | Piece.GetColor(pieceType);
                        Move preSelectedMove = new Move(index, newIndex, pieceType, boardManager.board[newIndex], flag, promotionPieceType);

                        boardManager.MakeMove(preSelectedMove);
                        boardDrawer.MakeVisualMove(preSelectedMove, gameObject);

                        index = newIndex;
                        CallAI();
                        return;
                    }
                    else
                    {
                        // No pre-selection, show promotion UI as before
                        StartCoroutine(boardDrawer.ShowPromotionUI(index, newIndex, gameObject));
                        return;
                    }
                }

                Move move = new Move(index, newIndex, pieceType, boardManager.board[newIndex], flag);


                boardManager.MakeMove(move);
                boardDrawer.MakeVisualMove(move, gameObject, false);

                index = newIndex;

                CallAI();
                return;
            }
        }

        // No valid drop target found, reset position
        rectTransform.anchoredPosition = originalPosition;

        // Always cleanup
        isMoving = false;
        preSelectedPromotionPiece = -1; // Reset pre-selection
        boardDrawer?.HideTargets();
    }

    void Update()
    {
        // Look for move selection via click
        if (Input.GetMouseButtonDown(0) && isMoving)
        {
            print("came from update:");
            MovePiece(false);
            isMoving = false;
            originalPosition = rectTransform.anchoredPosition;
        }

        // Only check for input when dragging and it's a pawn
        if (isMoving && Piece.GetPieceType(pieceType) == Piece.Pawn)
        {
            if (Input.GetKeyDown(KeyCode.Q))
            {
                preSelectedPromotionPiece = Piece.Queen;
            }
            else if (Input.GetKeyDown(KeyCode.W))
            {
                preSelectedPromotionPiece = Piece.Knight;
            }
            else if (Input.GetKeyDown(KeyCode.E))
            {
                preSelectedPromotionPiece = Piece.Bishop;
            }
            else if (Input.GetKeyDown(KeyCode.R))
            {
                preSelectedPromotionPiece = Piece.Rook;
            }
        }
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

    public void MakePromotion(int oldIndex, int promotionIndex, int oldType, int newType)
    {
        Move move = new Move(oldIndex, promotionIndex, oldType, boardManager.board[promotionIndex], 2, newType);

        boardManager.MakeMove(move);
        boardDrawer.MakeVisualMove(move, gameObject);

        // this for visual move:  pieceType = newType;
    }

    public void SetType(int type)
    {
        pieceType = type;
    }
    public int GetPieceType()
    {
        return pieceType;
    }
    public void SetIndx(int indx)
    {
        index = indx;
    }
}