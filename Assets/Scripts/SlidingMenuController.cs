using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.Events;

/// <summary>
/// Control del menú deslizante con SEMÁNTICA NORMAL y configuración fija:
/// - El 'panel' es SIEMPRE el "player" completo.
/// - El MENÚ arranca OCULTO (player en su posición original).
///
/// API normal:
///   OpenMenu()  => MENÚ VISIBLE  (overlay ON; player desplazado a hiddenPos)
///   CloseMenu() => MENÚ OCULTO   (overlay OFF; player en initialPos)
///   TryToggleMenu()
///
/// Retrocompatibilidad (wrappers):
///   TryTogglePanel(), Open(), Close(), OpenInstant(), CloseInstant()
/// </summary>
public class SlidingMenuController : MonoBehaviour, IPointerClickHandler
{
    [Header("Panel (player root)")]
    public RectTransform panel;               // RectTransform del "player" completo
    public float hiddenOffset = 510f;         // cuánto queda asomado cuando está desplazado
    public float animationDuration = 0.9f;    // duración del deslizamiento
    public Vector2 hiddenDirection = Vector2.left; // hacia dónde se desplaza el player para VER el menú
    public Transform rotationTarget;          // icono/chevron opcional que rota

    [Header("Capa oscura (overlay)")]
    public RawImage dimOverlay;
    public float fadeDuration = 0.3f;
    [Range(0f, 1f)] public float overlayAlpha = 0.8f;

    [Header("Hotkey")]
    public KeyCode toggleKey = KeyCode.Escape;

    [System.Serializable] public class MenuEvent : UnityEvent { }

    [Header("Eventos")]
    public MenuEvent onMenuOpened;
    public MenuEvent onMenuClosed;

    // Estado interno
    private Vector2 initialPos; // posición “normal” del player
    private Vector2 hiddenPos;  // posición desplazada (menú visible)
    private bool canToggle = true;

    /// <summary>
    /// TRUE cuando el panel está en hiddenPos (player desplazado).
    /// Como el panel es el player, esto significa MENÚ VISIBLE.
    /// </summary>
    public bool IsHidden { get; private set; }

    /// <summary>
    /// Visibilidad del MENÚ (igual a IsHidden en este modelo fijo).
    /// </summary>
    public bool IsMenuVisible => IsHidden;

    /// <summary>
    /// Flag global para que otros scripts bloqueen hotkeys cuando el menú esté abierto.
    /// </summary>
    public static bool AnyOpen { get; private set; }

    /* ================= Ciclo ================= */

    void Start()
    {
        if (!panel) return;

        initialPos = panel.anchoredPosition;
        hiddenPos = ComputeHiddenPosition();

        // Estado inicial FIJO: MENÚ oculto (player en initialPos)
        panel.anchoredPosition = initialPos;
        IsHidden = false;          // panel NO está en hiddenPos
        AnyOpen = false;

        // Overlay apagado
        ApplyOverlayInstant(0f, false);

        UpdateRotation();
    }

    void Update()
    {
        if (toggleKey != KeyCode.None && Input.GetKeyDown(toggleKey))
            TryToggleMenu();
    }

    /* ================= Input ================= */

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.pointerEnter == gameObject)
            TryToggleMenu();
    }

    /* ============== API NORMAL ============== */

    public void TryToggleMenu()
    {
        if (InputLock.IsLocked) return;
        if (!canToggle) return;

        if (IsMenuVisible) CloseMenu();
        else OpenMenu();
    }

    /// <summary>Abre el MENÚ (player a hiddenPos, overlay ON) con animación.</summary>
    public void OpenMenu()
    {
        if (!panel || !canToggle) return;
        canToggle = false;

        LeanTween.move(panel, hiddenPos, animationDuration)
            .setEase(LeanTweenType.easeInOutQuart)
            .setOnComplete(() => canToggle = true);

        IsHidden = true;     // ahora el panel está (o irá) a hiddenPos
        SetOverlayVisibleAnimated(true);
        AnyOpen = true;
        onMenuOpened?.Invoke();
        UpdateRotation();
    }

    /// <summary>Cierra el MENÚ (player a initialPos, overlay OFF) con animación.</summary>
    public void CloseMenu()
    {
        if (!panel || !canToggle) return;
        canToggle = false;

        LeanTween.move(panel, initialPos, animationDuration)
            .setEase(LeanTweenType.easeInOutQuart)
            .setOnComplete(() => canToggle = true);

        IsHidden = false;    // vuelve a initialPos
        SetOverlayVisibleAnimated(false);
        AnyOpen = false;
        onMenuClosed?.Invoke();
        UpdateRotation();
    }

    public void OpenMenuInstant()
    {
        if (!panel) return;

        panel.anchoredPosition = hiddenPos;
        IsHidden = true;

        ApplyOverlayInstant(overlayAlpha, true);
        AnyOpen = true;
        onMenuOpened?.Invoke();
        UpdateRotation();
    }

    public void CloseMenuInstant()
    {
        if (!panel) return;

        panel.anchoredPosition = initialPos;
        IsHidden = false;

        ApplyOverlayInstant(0f, false);
        AnyOpen = false;
        onMenuClosed?.Invoke();
        UpdateRotation();
    }

    /* ================= Helpers ================= */

    private Vector2 ComputeHiddenPosition()
    {
        // Distancia según eje dominante (horizontal si |x|>=|y|, en otro caso vertical)
        float distance = Mathf.Abs(hiddenDirection.x) >= Mathf.Abs(hiddenDirection.y)
            ? (panel.rect.width - hiddenOffset)
            : (panel.rect.height - hiddenOffset);

        return initialPos + hiddenDirection.normalized * distance;
    }

    private void SetOverlayVisibleAnimated(bool visible)
    {
        if (!dimOverlay) return;

        dimOverlay.gameObject.SetActive(true);
        dimOverlay.raycastTarget = visible;

        float fromA = dimOverlay.color.a;
        float toA = visible ? overlayAlpha : 0f;

        LeanTween.value(gameObject, fromA, toA, fadeDuration)
            .setOnUpdate(a =>
            {
                var c = dimOverlay.color;
                c.a = a;
                dimOverlay.color = c;
            })
            .setOnComplete(() =>
            {
                if (!visible)
                    dimOverlay.gameObject.SetActive(false);
            });
    }

    private void ApplyOverlayInstant(float alpha, bool visible)
    {
        if (!dimOverlay) return;

        dimOverlay.gameObject.SetActive(visible);
        dimOverlay.raycastTarget = visible;

        var c = dimOverlay.color;
        c.a = alpha;
        dimOverlay.color = c;
    }

    private void UpdateRotation()
    {
        if (!rotationTarget) return;
        // 270° cuando el menú está visible, 90° cuando está oculto (ajústalo si tu icono lo necesita)
        rotationTarget.localRotation = Quaternion.Euler(0f, 0f, IsMenuVisible ? 270f : 90f);
    }
}
