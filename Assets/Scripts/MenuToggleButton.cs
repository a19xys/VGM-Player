using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class MenuToggleButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Header("Refs")]
    public SlidingMenuController slidingMenu; // Asignar en Inspector
    public Graphic icon;                      // (opcional) sólo para tintado, no para alfa
    public Transform rotationTarget;          // Rotación 0° / +180° según estado del menú

    [Header("Opacity via CanvasGroup")]
    [Range(0f, 1f)] public float hiddenAlpha = 0.35f; // menú oculto (idle)
    [Range(0f, 1f)] public float visibleAlpha = 1f;  // menú visible o hover
    public float fadeSeconds = 0.15f;

    [Header("Hover movement (tipo HoverFeedback)")]
    public Vector3 moveDirection = new Vector3(10, 0, 0);
    public float moveDuration = 0.2f;
    public float returnDuration = 0.2f;

    private Button btn;
    private int fadeTweenId = -1;
    private int moveTweenId = -1;
    private Vector3 originalLocalPos;
    private CanvasGroup cg;

    // Rotación base (respetar la inicial del icono)
    private Quaternion baseLocalRotation;
    private static readonly Quaternion ROT_180_Z = Quaternion.Euler(0f, 0f, 180f);

    void Awake()
    {
        btn = GetComponent<Button>();
        if (!btn) btn = gameObject.AddComponent<Button>();

        // Desactivar navegación por teclado (evitar Space accidental)
        var nav = btn.navigation; nav.mode = Navigation.Mode.None; btn.navigation = nav;
        btn.onClick.AddListener(OnClick);

        // CanvasGroup para controlar opacidad (si no hay, se crea)
        cg = GetComponent<CanvasGroup>();
        if (!cg) cg = gameObject.AddComponent<CanvasGroup>();
        cg.interactable = true;   // el alpha no afecta interacción
        cg.blocksRaycasts = true; // que siga clicable en alpha bajo

        // Default rotation target
        if (!rotationTarget)
        {
            if (icon) rotationTarget = icon.transform;
            else rotationTarget = this.transform;
        }
        baseLocalRotation = rotationTarget.localRotation;
    }

    void Start()
    {
        originalLocalPos = transform.localPosition;

        if (slidingMenu != null)
        {
            slidingMenu.onMenuOpened.AddListener(OnMenuOpened);
            slidingMenu.onMenuClosed.AddListener(OnMenuClosed);
        }

        bool menuVisible = (slidingMenu && slidingMenu.IsMenuVisible);
        ApplyVisual(menuVisible, instant: true); // alpha por CanvasGroup + rotación
    }

    void OnDestroy()
    {
        if (slidingMenu != null)
        {
            slidingMenu.onMenuOpened.RemoveListener(OnMenuOpened);
            slidingMenu.onMenuClosed.RemoveListener(OnMenuClosed);
        }
        if (fadeTweenId != -1) LeanTween.cancel(fadeTweenId);
        if (moveTweenId != -1) LeanTween.cancel(moveTweenId);
    }

    /* ======================= Click ======================= */
    private void OnClick()
    {
        if (InputLock.IsLocked || slidingMenu == null) return;

        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);

        if (slidingMenu.IsMenuVisible) slidingMenu.CloseMenu();
        else slidingMenu.OpenMenu();
        // La rotación/alpha se actualizan por eventos
    }

    /* ======================= Hover ======================= */
    public void OnPointerEnter(PointerEventData eventData)
    {
        MoveTo(originalLocalPos + moveDirection, moveDuration);
        if (!slidingMenu || slidingMenu.IsMenuVisible) return; // hover sólo con menú oculto
        FadeTo(visibleAlpha);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        MoveTo(originalLocalPos, returnDuration);
        if (!slidingMenu || slidingMenu.IsMenuVisible) return;
        FadeTo(hiddenAlpha);
    }

    /* =================== Eventos del menú =================== */
    private void OnMenuOpened() => ApplyVisual(true, instant: false);
    private void OnMenuClosed() => ApplyVisual(false, instant: false);

    /* =================== Visual + Rotación =================== */
    private void ApplyVisual(bool menuVisible, bool instant)
    {
        // Opacidad por CanvasGroup (no por icon.color.a)
        float targetA = menuVisible ? visibleAlpha : hiddenAlpha;
        if (instant) SetAlpha(targetA);
        else FadeTo(targetA);

        // Posición: reset a base al cambiar estado del menú
        MoveTo(originalLocalPos, instant ? 0f : returnDuration);

        // Rotación instantánea relativa a la rotación capturada
        rotationTarget.localRotation = menuVisible ? baseLocalRotation * ROT_180_Z : baseLocalRotation;
    }

    /* =================== Tweens helpers =================== */
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

    private void MoveTo(Vector3 localTarget, float duration)
    {
        if (moveTweenId != -1) { LeanTween.cancel(moveTweenId); moveTweenId = -1; }
        if (duration <= 0f) { transform.localPosition = localTarget; return; }

        moveTweenId = LeanTween.moveLocal(gameObject, localTarget, duration)
            .setEaseOutQuad()
            .setOnComplete(() => moveTweenId = -1)
            .id;
    }
}