using System.Text;
using UnityEngine;
using TMPro;

public class ChessDebugDisplay : MonoBehaviour
{
    [SerializeField] private BoardLogic boardLogic;
    [SerializeField] private TextMeshProUGUI debugText;
    [SerializeField] private bool autoUpdate = true;
    [SerializeField] private float updateInterval = 0.5f;

    private float lastUpdateTime;

    public void UpdateText()
    {
        if (autoUpdate && Time.time - lastUpdateTime > updateInterval)
        {
            UpdateDebugDisplay();
            lastUpdateTime = Time.time;
        }
    }

    // Call this manually if you want to update on-demand
    public void UpdateDebugDisplay()
    {
        if (boardLogic == null || debugText == null) return;

        StringBuilder sb = new StringBuilder();

        // === POSITION INFO ===
        sb.AppendLine("=== POSITION INFO ===");
        sb.AppendLine($"Turn: {(boardLogic.turn == 0 ? "White" : "Black")}");
        sb.AppendLine($"Half-move clock: {boardLogic.halfMoveClock}/100");
        sb.AppendLine($"In Check: {boardLogic.IsInCheck()}");
        sb.AppendLine($"Game Ended: {boardLogic.gameEnded}");
        if (boardLogic.gameEnded)
        {
            sb.AppendLine($"Winner: {(boardLogic.winner == -1 ? "Draw" : boardLogic.winner == 0 ? "White" : "Black")}");
        }
        sb.AppendLine();

        // === PIECE COUNT ===
        sb.AppendLine("=== PIECE COUNT ===");
        if (boardLogic.numOfPieces != null && boardLogic.numOfPieces.GetLength(0) >= 2)
        {
            sb.AppendLine("White Pieces:");
            sb.AppendLine($"  Pawns:   {GetPieceCount(0, Piece.Pawn)}");
            sb.AppendLine($"  Knights: {GetPieceCount(0, Piece.Knight)}");
            sb.AppendLine($"  Bishops: {GetPieceCount(0, Piece.Bishop)}");
            sb.AppendLine($"  Rooks:   {GetPieceCount(0, Piece.Rook)}");
            sb.AppendLine($"  Queens:  {GetPieceCount(0, Piece.Queen)}");
            sb.AppendLine($"  King:    {GetPieceCount(0, Piece.King)}");

            sb.AppendLine("Black Pieces:");
            sb.AppendLine($"  Pawns:   {GetPieceCount(1, Piece.Pawn)}");
            sb.AppendLine($"  Knights: {GetPieceCount(1, Piece.Knight)}");
            sb.AppendLine($"  Bishops: {GetPieceCount(1, Piece.Bishop)}");
            sb.AppendLine($"  Rooks:   {GetPieceCount(1, Piece.Rook)}");
            sb.AppendLine($"  Queens:  {GetPieceCount(1, Piece.Queen)}");
            sb.AppendLine($"  King:    {GetPieceCount(1, Piece.King)}");
        }
        sb.AppendLine();

        // === MATERIAL BALANCE ===
        sb.AppendLine("=== MATERIAL BALANCE ===");
        int whiteMaterial = CalculateMaterial(0);
        int blackMaterial = CalculateMaterial(1);
        int balance = whiteMaterial - blackMaterial;
        sb.AppendLine($"White: {whiteMaterial} centipawns");
        sb.AppendLine($"Black: {blackMaterial} centipawns");
        sb.AppendLine($"Balance: {(balance > 0 ? "+" : "")}{balance} (White perspective)");
        sb.AppendLine();

        // === CASTLING RIGHTS ===
        sb.AppendLine("=== CASTLING RIGHTS ===");
        sb.AppendLine($"White Kingside:  {HasCastlingRight(boardLogic.castlingRights, 0)}");
        sb.AppendLine($"White Queenside: {HasCastlingRight(boardLogic.castlingRights, 1)}");
        sb.AppendLine($"Black Kingside:  {HasCastlingRight(boardLogic.castlingRights, 2)}");
        sb.AppendLine($"Black Queenside: {HasCastlingRight(boardLogic.castlingRights, 3)}");
        sb.AppendLine($"White castled: {boardLogic.castled[0]}");
        sb.AppendLine($"Black castled: {boardLogic.castled[1]}");
        sb.AppendLine();

        // === EN PASSANT ===
        sb.AppendLine("=== EN PASSANT ===");
        if (boardLogic.enPassantSquare != 0)
        {
            int epSquare = BitScan.TrailingZeroCount(boardLogic.enPassantSquare);
            sb.AppendLine($"En Passant Square: {SquareToString(epSquare)}");
        }
        else
        {
            sb.AppendLine("En Passant: None");
        }
        sb.AppendLine();

        // === ZOBRIST KEY ===
        sb.AppendLine("=== ZOBRIST KEY ===");
        sb.AppendLine($"Key: 0x{boardLogic.zobristKey:X16}");
        sb.AppendLine();

        // === POSITION HISTORY ===
        sb.AppendLine("=== POSITION HISTORY ===");
        if (boardLogic.positionHistory != null)
        {
            sb.AppendLine($"History size: {boardLogic.positionHistory.Count}");

            // Check for repetitions
            var currentKey = boardLogic.zobristKey;
            int repetitions = 0;
            foreach (var key in boardLogic.positionHistory)
            {
                if (key == currentKey) repetitions++;
            }
            sb.AppendLine($"Current position seen: {repetitions + 1} time(s)");

            if (repetitions >= 2)
            {
                sb.AppendLine("⚠️ THREEFOLD REPETITION!");
            }
        }
        sb.AppendLine();

        // === BITBOARDS ===
        sb.AppendLine("=== BITBOARDS ===");
        sb.AppendLine($"White pieces: {CountBits(boardLogic.Wbitboard)}");
        sb.AppendLine($"Black pieces: {CountBits(boardLogic.Bbitboard)}");
        sb.AppendLine();

        // === LEGAL MOVES ===
        sb.AppendLine("=== LEGAL MOVES ===");
        Move[] moves = new Move[256];
        int moveCount = boardLogic.moveCalculator.GenerateAllMoves(moves, boardLogic.turn);
        sb.AppendLine($"Legal moves: {moveCount}");
        sb.AppendLine();

        // === OPENING LINE ===
        if (!string.IsNullOrEmpty(boardLogic.openingLine))
        {
            sb.AppendLine("=== OPENING LINE ===");
            sb.AppendLine(boardLogic.openingLine);
        }

        debugText.text = sb.ToString();
    }

    private int GetPieceCount(int color, int pieceType)
    {
        if (boardLogic.numOfPieces == null) return 0;
        return boardLogic.numOfPieces[color, pieceType - 1];
    }

    private int CalculateMaterial(int color)
    {
        int material = 0;
        material += GetPieceCount(color, Piece.Pawn) * 100;
        material += GetPieceCount(color, Piece.Knight) * 320;
        material += GetPieceCount(color, Piece.Bishop) * 330;
        material += GetPieceCount(color, Piece.Rook) * 500;
        material += GetPieceCount(color, Piece.Queen) * 900;
        return material;
    }

    private bool HasCastlingRight(int rights, int bit)
    {
        return (rights & (1 << bit)) != 0;
    }

    private string SquareToString(int square)
    {
        int file = square % 8;
        int rank = square / 8;
        char fileChar = (char)('a' + file);
        char rankChar = (char)('1' + rank);
        return $"{fileChar}{rankChar}";
    }

    private int CountBits(ulong bitboard)
    {
        int count = 0;
        while (bitboard != 0)
        {
            bitboard &= bitboard - 1;
            count++;
        }
        return count;
    }

    // Manual update button (call from inspector or another script)
    [ContextMenu("Force Update")]
    public void ForceUpdate()
    {
        UpdateDebugDisplay();
    }

    // Print detailed bitboard visualization
    [ContextMenu("Print Bitboard Visualization")]
    public void PrintBitboardVisualization()
    {
        if (boardLogic == null) return;

        Debug.Log("=== WHITE BITBOARD ===");
        Debug.Log(VisualizeBitboard(boardLogic.Wbitboard));

        Debug.Log("=== BLACK BITBOARD ===");
        Debug.Log(VisualizeBitboard(boardLogic.Bbitboard));
    }

    private string VisualizeBitboard(ulong bitboard)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine();

        for (int rank = 7; rank >= 0; rank--)
        {
            sb.Append($"{rank + 1} ");
            for (int file = 0; file < 8; file++)
            {
                int square = rank * 8 + file;
                bool occupied = ((bitboard >> square) & 1) == 1;
                sb.Append(occupied ? "■ " : "□ ");
            }
            sb.AppendLine();
        }
        sb.AppendLine("  a b c d e f g h");

        return sb.ToString();
    }

    // Log all legal moves for debugging
    [ContextMenu("Log Legal Moves")]
    public void LogLegalMoves()
    {
        if (boardLogic == null) return;

        Move[] moves = new Move[256];
        int moveCount = boardLogic.moveCalculator.GenerateAllMoves(moves, boardLogic.turn);

        StringBuilder sb = new StringBuilder();
        sb.AppendLine($"=== Legal Moves for {(boardLogic.turn == 0 ? "White" : "Black")} ({moveCount} moves) ===");

        for (int i = 0; i < moveCount; i++)
        {
            string san = boardLogic.MoveToSAN(moves[i]);
            string from = SquareToString(moves[i].from);
            string to = SquareToString(moves[i].to);
            sb.AppendLine($"{i + 1}. {san} ({from} -> {to})");
        }

        Debug.Log(sb.ToString());
    }
}