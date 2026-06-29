using UnityEngine;
using System.Collections;
using System.Threading.Tasks;

public class AI : MonoBehaviour
{
    [SerializeField] private int TIME_LIMIT = 1000;
    [SerializeField] private GraphicalBoard graphicalBoard;
    [SerializeField] private Compete versionTester;

    private SearchEngine engine;
    private BoardLogic boardLogic;
    private BookManager bookManager;

    private void Awake()
    {
        new BoardLogic();
    }

    private void Start()
    {
        boardLogic = BoardLogic.Instance;
        bookManager = BookManager.Instance;
        engine = new SearchEngine(TIME_LIMIT, new Evaluate());

        engine.ResetAI();

        boardLogic.positionCounter.Clear();
        StartCoroutine(UpdateDepthTextCoroutine());
    }

    public static bool isThinking { get; private set; }

    private ulong ponderZobrist = 0; // expected board key after opponent plays ponder move

    public void ResetAI()
    {
        engine.ResetAI();
        boardLogic.positionCounter.Clear();
    }

    public void StartThinking()
    {
        if (isThinking) return;
        StartCoroutine(ThinkCoroutine());
    }

    private IEnumerator ThinkCoroutine()
    {
        isThinking = true;

        // Book lookup must happen on the main thread (TextAsset is a Unity API)
        Move? bookMove = bookManager.TryBookMove();
        if (bookMove != null && boardLogic.normalStarting)
        {
            engine.StopSearch(); // stop any ponder search
            ponderZobrist = 0;
            boardLogic.moveExecuter.MakeMove((Move)bookMove);
            graphicalBoard.MakeVisualMove((Move)bookMove);
            isThinking = false;
            yield break;
        }

        Move bestMove;

        bool isPonderHit = engine.IsSearchRunning && ponderZobrist != 0
                           && boardLogic.zobristKey == ponderZobrist;

        if (isPonderHit)
        {
            engine.PonderHit(TIME_LIMIT);
            float ponderWaitStart = Time.realtimeSinceStartup;
            while (engine.IsSearchRunning)
            {
                if (Time.realtimeSinceStartup - ponderWaitStart > TIME_LIMIT / 1000f + 3f)
                    break;
                yield return null;
            }
            bestMove = engine.StopSearch();
        }
        else
        {
            // Do NOT call StopSearch() here on the main thread.
            // GetBestMove does an infinite Join on the task thread instead,
            // eliminating the zombie-thread / concurrent Dictionary corruption.
            var searchTask = Task.Run(() => engine.GetBestMove(boardLogic));
            while (!searchTask.IsCompleted)
                yield return null;
            if (searchTask.IsFaulted)
            {
                Debug.LogError($"[AI] Search task faulted: {searchTask.Exception?.Flatten()}");
                isThinking = false;
                yield break;
            }
            bestMove = searchTask.Result;
        }

        if (bestMove.movedPiece != 0 && bestMove.from != bestMove.to)
        {
            boardLogic.moveExecuter.MakeMove(bestMove);
            graphicalBoard.MakeVisualMove(bestMove);
        }

        if (versionTester != null)
            versionTester.updateInfoToNew(engine.LastDepthReached, engine.LastNps, engine.LastTtHitRate);

        // Start pondering the expected opponent response on a board clone
        Move pm = engine.PonderMove;
        if (pm.from != pm.to)
        {
            try
            {
                BoardLogic ponderBoard = boardLogic.Clone();
                ponderBoard.moveExecuter.MakeMove(pm);
                ponderZobrist = ponderBoard.zobristKey;
                engine.StartPondering(ponderBoard);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[AI] Failed to start ponder: {e}");
                ponderZobrist = 0;
            }
        }
        else
        {
            ponderZobrist = 0;
        }

        isThinking = false;
    }


    private IEnumerator UpdateDepthTextCoroutine()
    {
        int lastDepth = 0;
        while (true)
        {
            if (lastDepth != engine.CurrentDepth)
            {
                if (InfoTextManager.Instance != null)
                    InfoTextManager.Instance.depthText.text =
                        $"Depth: {engine.CurrentDepth}\nSelDepth: {engine.SelDepth}";
                lastDepth = engine.CurrentDepth;
            }
            yield return new WaitForSeconds(0.1f);
        }
    }

}
