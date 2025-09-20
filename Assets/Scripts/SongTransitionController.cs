using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class SongTransitionController : MonoBehaviour
{
    [Header("Refs")]
    public TrackQueueManager queue;                 // Debe exponer ResolveTargetFileNumber(...) y PeekMetadata(...)
    public SongLoader loader;                       // Debe exponer LoadSongMetadataInstant(string), PrepareAudioClipRoutine(string,bool), PrepareVideosRoutine(string,bool), StartPlayback()
    public SlidingMenuController selectionMenu;     // Lo cerraremos al cubrir la pantalla
    public SlidingPanelController[] panelsToOpen;   // Paneles info/remix/controles para abrir bajo cobertura
    public EventSystem eventSystem;                 // Para desactivar navegación
    public SongSkipFader skipFader;                 // Fundido del volumen de la canción al pasar a otra
    public DualRemixMarquee dualRemixMarquee;

    [Header("Overlay container")]
    [Tooltip("CanvasGroup del overlay que contiene los 4 bloques. Debe estar a pantalla completa encima de la UI.")]
    public CanvasGroup overlayCanvas;

    [Header("4 blocks (left -> right)")]
    public RectTransform block1;
    public RectTransform block2;
    public RectTransform block3;
    public RectTransform block4;

    [Header("Anim")]
    public float slideDuration = 0.3f;
    public float slideStagger = 0.12f;
    public AnimationCurve inCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    public AnimationCurve outCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    private bool busy;

    void Awake()
    {
        if (overlayCanvas)
        {
            overlayCanvas.alpha = 0f;
            overlayCanvas.blocksRaycasts = false;
        }
        PlaceBlocksOffscreen();
    }

    public bool IsBusy() => busy;

    /* =================== API pública =================== */
    public void GoToNext() { if (!busy) StartCoroutine(DoTransition(+1, -1)); }
    public void GoToPrevious() { if (!busy) StartCoroutine(DoTransition(-1, -1)); }
    public void PlayFromFilteredIndex(int index) { if (!busy) StartCoroutine(DoTransition(0, index)); }
    public void GoToAbsoluteIndex(int index) => PlayFromFilteredIndex(index); // compat

    /* =================== Núcleo =================== */
    private IEnumerator DoTransition(int advance, int absoluteIndex) {

        busy = true;

        InputLock.Lock();

        if (eventSystem) eventSystem.sendNavigationEvents = false;
        if (overlayCanvas) {
            overlayCanvas.alpha = 1f;
            overlayCanvas.blocksRaycasts = true;
        }

        if (skipFader != null) skipFader.BeginFadeOut();

        // 1) Colores actual / siguiente para ENTRADA y último bloque
        var cur = loader != null ? loader.metadata : null;
        var nextMet = queue != null ? queue.PeekMetadata(advance, absoluteIndex) : null;

        Color cp1 = cur != null ? cur.Color1 : Color.black;
        Color cs1 = cur != null ? cur.Color2 : Color.black;
        Color cp2 = nextMet != null ? nextMet.Color1 : Color.black; // lo usaremos en salida
        Color cs2 = nextMet != null ? nextMet.Color2 : Color.black;

        // 2) Pintar ENTRADA: cp1, cs1, cp1, cs2
        PaintBlocks(
            b1: cp1,
            b2: cs1,
            b3: cp1,
            b4: cs2
        );
        PlaceBlocksOffscreen();

        // 3) ENTRADA (cubrir)
        yield return SlideIn();

        // 4) Con pantalla cubierta: cerrar menú y abrir paneles (sin animación, para evitar flicker)
        ForceCloseSelectionMenu();
        ForceOpenPanels();
        dualRemixMarquee.ResetAndStart();

        // 5) Preparar TODO (sin reproducir todavía)
        //    - Resolver id destino
        string id = queue.ResolveTargetFileNumber(advance, absoluteIndex);
        if (!string.IsNullOrEmpty(id))
        {
            // Metadatos y colores INSTANT
            loader.LoadSongMetadataInstant(id);
            // Preparar audio (clip asignado, sin Play)
            yield return StartCoroutine(loader.PrepareAudioClipRoutine(id, autoPlay: false));
            // Preparar vídeo (VideoPlayer.Prepare o fallback vinilo listo, sin Play/Spin)
            yield return StartCoroutine(loader.PrepareVideosRoutine(id, autoPlay: false));

            // (Opcional) recalcular cp2/cs2 por si el Peek y el JSON difieren
            cp2 = loader.metadata.Color1;
            cs2 = loader.metadata.Color2;
        }

        // 6) Arranque sincronizado (audio y vídeo/vinilo en el MISMO frame)
        if (skipFader != null) skipFader.RestoreIfSilent();
        loader.StartPlayback();

        // 7) Pintar SALIDA en orden inverso 4-3-2-1:
        //    cs2, cp2, cs2, cp2
        PaintBlocks(
            b1: cp2, // este color irá en el bloque1 pero saldrá al final; no importa el orden de pintado
            b2: cs2,
            b3: cp2,
            b4: cs2
        );

        // 8) SALIDA (descubrir) — orden 4,3,2,1 hacia la derecha
        yield return SlideOut();

        // 9) Desbloqueo
        if (overlayCanvas)
        {
            overlayCanvas.alpha = 0f;
            overlayCanvas.blocksRaycasts = false;
        }
        if (eventSystem) eventSystem.sendNavigationEvents = true;
        InputLock.Unlock();
        busy = false;
    }

    /* =================== Helpers UI =================== */
    private void ForceCloseSelectionMenu()
    {
        if (selectionMenu == null) return;
        selectionMenu.CloseMenuInstant(); // asegura que NO quede abierto al terminar
    }

    private void ForceOpenPanels()
    {
        if (panelsToOpen == null) return;
        foreach (var p in panelsToOpen)
        {
            if (p == null) continue;
            p.OpenInstant(); // abiertos bajo cobertura siempre
        }
    }

    private void PaintBlocks(Color b1, Color b2, Color b3, Color b4)
    {
        SetImageColor(block1, b1);
        SetImageColor(block2, b2);
        SetImageColor(block3, b3);
        SetImageColor(block4, b4);
    }

    private void SetImageColor(RectTransform rt, Color c)
    {
        if (!rt) return;
        var img = rt.GetComponent<Image>();
        if (!img) img = rt.gameObject.AddComponent<Image>();
        img.color = c;
    }

    private RectTransform RootRect()
    {
        return overlayCanvas ? overlayCanvas.GetComponent<RectTransform>() : null;
    }

    private void PlaceBlocksOffscreen()
    {
        var root = RootRect();
        if (!root) return;
        float w = root.rect.width;
        MoveX(block1, -w);
        MoveX(block2, -w);
        MoveX(block3, -w);
        MoveX(block4, -w);
    }

    private void MoveX(RectTransform rt, float x)
    {
        if (!rt) return;
        var p = rt.anchoredPosition;
        p.x = x;
        rt.anchoredPosition = p;
    }

    private IEnumerator SlideIn()
    {
        int id1 = LeanTween.moveX(block1, 0f, slideDuration).setEase(inCurve).id;
        yield return new WaitForSeconds(slideStagger);
        int id2 = LeanTween.moveX(block2, 0f, slideDuration).setEase(inCurve).id;
        yield return new WaitForSeconds(slideStagger);
        int id3 = LeanTween.moveX(block3, 0f, slideDuration).setEase(inCurve).id;
        yield return new WaitForSeconds(slideStagger);
        int id4 = LeanTween.moveX(block4, 0f, slideDuration).setEase(inCurve).id;
        yield return WaitForTween(id4);
    }

    
    private IEnumerator SlideOut()
    {
        var root = RootRect();
        if (!root) yield break;
        float w = root.rect.width;

        yield return new WaitForSeconds(slideStagger);
        int id4 = LeanTween.moveX(block4, w, slideDuration).setEase(outCurve).id;
        yield return new WaitForSeconds(slideStagger);
        int id3 = LeanTween.moveX(block3, w, slideDuration).setEase(outCurve).id;
        yield return new WaitForSeconds(slideStagger);
        int id2 = LeanTween.moveX(block2, w, slideDuration).setEase(outCurve).id;
        yield return new WaitForSeconds(slideStagger);
        int id1 = LeanTween.moveX(block1, w, slideDuration).setEase(outCurve).id;
        yield return WaitForTween(id1);
    }
    

    private IEnumerator WaitForTween(int tweenId)
    {
        while (LeanTween.isTweening(tweenId))
            yield return null;
    }
}