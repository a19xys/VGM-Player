using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class LogoHoverOpacity : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Header("Refs")]
    public SongLoader songLoader;                 // Asigna tu SongLoader
    public Graphic target;                        // Si lo dejas vacío, usa el Graphic del propio GO (RawImage)
    public SlidingPanelController infoPanel;      // Asigna el panel de info si quieres forzar opaco cuando esté abierto

    [Header("Opacity")]
    [Range(0f, 1f)] public float idleAlpha = 0.75f;   // 75% de opacidad cuando NO hay hover
    [Range(0f, 1f)] public float hoverAlpha = 1f;     // Opaco al hacer hover
    public float fadeSeconds = 0.25f;

    private CanvasGroup cg;
    private int tweenId = -1;
    private bool pointerOver = false;
    private bool lastForceOpaque = false;

    void Awake()
    {
        if (!target) target = GetComponent<Graphic>();
        cg = GetComponent<CanvasGroup>();
        if (!cg) cg = gameObject.AddComponent<CanvasGroup>();
        cg.interactable = true;
        cg.blocksRaycasts = true; // para recibir hover
    }

    void Start()
    {
        bool forceOpaque = ShouldForceOpaque();
        lastForceOpaque = forceOpaque;
        cg.alpha = forceOpaque ? 1f : (pointerOver ? hoverAlpha : idleAlpha);
    }

    void Update()
    {
        bool forceOpaque = ShouldForceOpaque();

        // Si cambia el estado de "forzar opaco", haz tween hacia el valor nuevo
        if (forceOpaque != lastForceOpaque)
        {
            float targetAlpha = forceOpaque ? 1f : (pointerOver ? hoverAlpha : idleAlpha);
            FadeTo(targetAlpha);
            lastForceOpaque = forceOpaque;
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        pointerOver = true;
        if (ShouldForceOpaque()) return; // en vinilo o info abierta, siempre opaco
        FadeTo(hoverAlpha);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        pointerOver = false;
        if (ShouldForceOpaque()) return; // en vinilo o info abierta, siempre opaco
        FadeTo(idleAlpha);
    }

    private bool ShouldForceOpaque()
    {
        // 1) Modo vinilo (no hay vídeo activo)
        bool vinylMode = songLoader != null &&
                         songLoader.videoContainer != null &&
                         !songLoader.videoContainer.activeSelf;

        // 2) Panel de información abierto (IsHidden == false)
        bool infoOpen = infoPanel != null && !infoPanel.IsHidden;

        return vinylMode || infoOpen;
    }

    private void FadeTo(float a)
    {
        if (!cg) return;
        if (Mathf.Approximately(cg.alpha, a)) return;
        CancelTween();
        tweenId = LeanTween.value(gameObject, cg.alpha, a, fadeSeconds)
            .setOnUpdate(v => { if (cg) cg.alpha = v; })
            .setEase(LeanTweenType.easeInOutQuad)
            .setOnComplete(() => tweenId = -1)
            .id;
    }

    private void CancelTween()
    {
        if (tweenId != -1)
        {
            LeanTween.cancel(tweenId);
            tweenId = -1;
        }
    }

    void OnDisable() => CancelTween();
}