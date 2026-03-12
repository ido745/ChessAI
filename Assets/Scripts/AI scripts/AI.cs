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
        if (BoardLogic.Instance == null) new BoardLogic();
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

    private bool isThinking = false;
    public bool IsThinking() => isThinking;

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
            boardLogic.moveExecuter.MakeMove((Move)bookMove);
            graphicalBoard.MakeVisualMove((Move)bookMove);
            isThinking = false;
            yield break;
        }

        var searchTask = Task.Run(() => engine.GetBestMove(boardLogic));

        while (!searchTask.IsCompleted)
            yield return null;

        Move bestMove = searchTask.Result;

        if (bestMove.movedPiece != 0 && bestMove.from != bestMove.to)
        {
            boardLogic.moveExecuter.MakeMove(bestMove);
            graphicalBoard.MakeVisualMove(bestMove);
        }

        versionTester.updateInfoToNew(engine.LastDepthReached, engine.LastNps, engine.LastTtHitRate);

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