using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class SongTransitionController : MonoBehaviour
{
    [Header("Refs")]
    public TrackQueueManager queue;
    public SongLoader loader;
    public SlidingMenuController selectionMenu;     // puede mover el player entero
    public SlidingPanelController[] panelsToOpen;   // info / remix / controles
    public EventSystem eventSystem;

    [Header("Overlay (Canvas AISLADO)")]
    public CanvasGroup overlayCanvas;
    public RectTransform blockA; // 1
    public RectTransform blockB; // 2
    public RectTransform blockC; // 3
    public RectTransform blockD; // 4
    public RectTransform blockE; // 5

    [Header("Timing")]
    public float slideDuration = 0.45f;
    public float slideStagger = 0.08f;
    public AnimationCurve inCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    public AnimationCurve outCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("Root UI (player)")]
    public RectTransform playerRoot;      // tu GO raíz “player”
    public bool snapPlayerRoot = true;    // reponer a su posición original
    private Vector2 playerInitialPos;

    private bool busy;
    private RectTransform[] blocks;

    void Awake()
    {
        if (overlayCanvas)
        {
            overlayCanvas.alpha = 0f;
            overlayCanvas.blocksRaycasts = false;
            overlayCanvas.ignoreParentGroups = true;
            overlayCanvas.transform.SetAsLastSibling();
        }
        blocks = new[] { blockA, blockB, blockC, blockD, blockE };
        PlaceBlocksOffscreen();

        if (playerRoot) playerInitialPos = playerRoot.anchoredPosition;
    }

    public bool IsBusy() => busy;

    public void GoToNext() { if (!busy) StartCoroutine(DoTransition(+1, -1)); }
    public void GoToPrevious() { if (!busy) StartCoroutine(DoTransition(-1, -1)); }
    public void PlayFromFilteredIndex(int index) { if (!busy) StartCoroutine(DoTransition(0, index)); }

    private IEnumerator DoTransition(int advance, int absoluteIndex)
    {
        busy = true;
        InputLock.Lock();
        if (eventSystem) eventSystem.sendNavigationEvents = false;

        if (snapPlayerRoot && playerRoot)
        {
            LeanTween.cancel(playerRoot.gameObject); // por si alguna animación lo está moviendo
            playerRoot.anchoredPosition = playerInitialPos;
        }

        if (overlayCanvas) { overlayCanvas.alpha = 1f; overlayCanvas.blocksRaycasts = true; }

        // Paletas
        var cur = loader.metadata;
        var nextMeta = queue.PeekMetadata(advance, absoluteIndex);

        Color cs1 = cur?.Color2 ?? Color.black;
        Color cp1 = cur?.Color1 ?? Color.black;
        Color cs2 = nextMeta.Color2;
        Color cp2 = nextMeta.Color1;

        // ENTER: cs1/cp1 y último = cp2
        PaintBlocks(new[] { cs1, cp1, cs1, cp1, cp2 });
        PlaceBlocksOffscreen();
        yield return SlideIn();

        // No mover el player si el menú lo controla
        if (selectionMenu && !selectionMenu.panelIsWholePlayerRoot)
        {
            if (!selectionMenu.IsHidden) selectionMenu.CloseInstant();
        }

        if (panelsToOpen != null)
            foreach (var p in panelsToOpen) if (p && p.IsHidden) p.OpenInstant();

        // Cargar canción bajo la tapa
        yield return StartCoroutine(SwapSongUnderCover(advance, absoluteIndex));

        // EXIT: repintar con paleta 2 (cs2/cp2), salen en reversa
        PaintBlocks(new[] { cp2, cs2, cp2, cs2, cp2 });
        if (snapPlayerRoot && playerRoot) playerRoot.anchoredPosition = playerInitialPos;
        yield return SlideOut();

        if (overlayCanvas) { overlayCanvas.alpha = 0f; overlayCanvas.blocksRaycasts = false; }
        if (eventSystem) eventSystem.sendNavigationEvents = true;
        InputLock.Unlock();
        busy = false;
    }

    /* ===== Pintado/posicionado ===== */
    private void PaintBlocks(Color[] colors)
    {
        for (int i = 0; i < blocks.Length && i < colors.Length; i++)
            SetImage(blocks[i], colors[i]);
    }
    private void SetImage(RectTransform rt, Color c)
    {
        if (!rt) return;
        var img = rt.GetComponent<Image>() ?? rt.gameObject.AddComponent<Image>();
        img.color = c; img.raycastTarget = false;
    }
    private void PlaceBlocksOffscreen()
    {
        if (!overlayCanvas) return;
        float w = ((RectTransform)overlayCanvas.transform).rect.width;
        foreach (var rt in blocks) MoveX(rt, -w);
    }
    private void MoveX(RectTransform rt, float x)
    {
        if (!rt) return;
        var p = rt.anchoredPosition; p.x = x; rt.anchoredPosition = p;
    }

    /* ===== Animación ===== */
    private IEnumerator SlideIn()
    {
        for (int i = 0; i < blocks.Length; i++)
        {
            var rt = blocks[i];
            if (rt) LeanTween.moveX(rt, 0f, slideDuration).setEase(inCurve);
            if (i < blocks.Length - 1) yield return new WaitForSeconds(slideStagger);
        }
        yield return new WaitForSeconds(slideDuration);
    }
    private IEnumerator SlideOut()
    {
        if (!overlayCanvas) yield break;
        float w = ((RectTransform)overlayCanvas.transform).rect.width;
        for (int i = blocks.Length - 1; i >= 0; i--)
        {
            var rt = blocks[i];
            if (rt) LeanTween.moveX(rt, w, slideDuration).setEase(outCurve);
            if (i > 0) yield return new WaitForSeconds(slideStagger);
        }
        yield return new WaitForSeconds(slideDuration);
    }

    /* ===== Carga ===== */
    private IEnumerator SwapSongUnderCover(int advance, int absoluteIndex)
    {
        int id = queue.ResolveTargetId(advance, absoluteIndex);
        loader.LoadSongMetadataInstant(id);
        yield return StartCoroutine(loader.LoadAudioClipRoutine(id));
        yield return StartCoroutine(loader.LoadAndPlayVideosRoutine(id));
    }
}