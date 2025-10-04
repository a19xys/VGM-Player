using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// Refleja el estado de un SlidingPanelController:
/// - Panel CERRADO: traslúcido en reposo, opaco al hover.
/// - Panel ABIERTO: siempre opaco (sin hover).
/// El fade se hace con CanvasGroup (no se pisa con tintes del tema).
/// </summary>
public class PanelButtonAlpha : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Header("Refs")]
    public SlidingPanelController panel; // panel info al que refleja

    [Header("Opacity via CanvasGroup")]
    [Range(0f, 1f)] public float hiddenAlpha = 0.35f; // panel cerrado → reposo traslúcido
    [Range(0f, 1f)] public float visibleAlpha = 1f;  // panel abierto u hover (cuando cerrado)
    public float fadeSeconds = 0.15f;

    [Tooltip("Si está activo, este botón hará Toggle del panel al hacer clic.")]
    public bool bindClickToToggle = true;

    private Button btn;
    private CanvasGroup cg;
    private int fadeTweenId = -1;

    void Awake()
    {
        // CanvasGroup para controlar opacidad (si no hay, se crea)
        cg = GetComponent<CanvasGroup>();
        if (!cg) cg = gameObject.AddComponent<CanvasGroup>();
        cg.interactable = true;
        cg.blocksRaycasts = true;

        btn = GetComponent<Button>();
        if (!btn) btn = gameObject.AddComponent<Button>();

        var nav = btn.navigation; nav.mode = Navigation.Mode.None; btn.navigation = nav;

        if (bindClickToToggle)
            btn.onClick.AddListener(OnClickToggle);
    }

    void Start()
    {
        if (!panel)
        {
            Debug.LogWarning("PanelButtonAlpha: falta referencia a SlidingPanelController.");
            return;
        }

        panel.onPanelOpened.AddListener(() => ApplyVisual(menuOpen: true, instant: false));
        panel.onPanelClosed.AddListener(() => ApplyVisual(menuOpen: false, instant: false));

        // Estado inicial
        ApplyVisual(menuOpen: !panel.IsHidden, instant: true);
    }

    void OnDestroy()
    {
        if (panel != null)
        {
            panel.onPanelOpened.RemoveAllListeners();
            panel.onPanelClosed.RemoveAllListeners();
        }
        if (fadeTweenId != -1) LeanTween.cancel(fadeTweenId);
    }

    private void OnClickToggle()
    {
        if (InputLock.IsLocked || panel == null) return;

        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);

        panel.TryTogglePanel();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (panel == null || !panel.IsHidden) return; // hover sólo cuando está CERRADO
        FadeTo(visibleAlpha);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (panel == null || !panel.IsHidden) return;
        FadeTo(hiddenAlpha);
    }

    private void ApplyVisual(bool menuOpen, bool instant)
    {
        float targetA = menuOpen ? visibleAlpha : hiddenAlpha;
        if (instant) SetAlpha(targetA);
        else FadeTo(targetA);
    }

    private void FadeTo(float a)
    {
        if (!cg) return;
        if (Mathf.Approximately(cg.alpha, a)) return;

        if (fadeTweenId != -1) { LeanTween.cancel(fadeTweenId); fadeTweenId = -1; }
        float from = cg.alpha;
        fadeTweenId = LeanTween.value(gameObject, from, a, fadeSeconds)
            .setOnUpdate(v => { if (cg) cg.alpha = v; })
            .setOnComplete(() => fadeTweenId = -1)
            .id;
    }

    private void SetAlpha(float a)
    {
        if (cg) cg.alpha = a;
    }
}