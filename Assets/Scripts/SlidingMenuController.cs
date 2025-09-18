using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Controla el deslizamiento del contenedor y el velo oscuro.
/// - Si 'controlsWholePlayerRoot' es TRUE (tu caso), el panel mueve TODO el "player".
///   Entonces: Close => menú visible (velo ON), Open => menú oculto (velo OFF).
/// - Si es FALSE (el panel es el propio menú), Open => menú visible (velo ON).
/// </summary>
public class SlidingMenuController : MonoBehaviour, IPointerClickHandler
{
    [Header("Panel")]
    public RectTransform panel;               // En tu escena: el RectTransform del "player"
    public float hiddenOffset = 510.0f;
    public float animationDuration = 0.9f;
    public Vector2 hiddenDirection = Vector2.left; // Horizontal si |x|>|y|; vertical en caso contrario
    public Transform rotationTarget;

    [Header("Capa oscura")]
    public RawImage dimOverlay;
    public float fadeDuration = 0.3f;
    [Range(0f, 1f)] public float overlayAlpha = 0.785f;

    [Header("Hotkey")]
    public KeyCode toggleKey;

    [Header("Behaviour")]
    public bool startHidden = false;            // respeta estado inicial
    public bool controlsWholePlayerRoot = true; // TRUE: panel = "player" (tu wiring)
                                                // FALSE: panel = panel del menú

    // Estado público
    public bool IsHidden { get; private set; }  // TRUE => panel en posición oculta
    public bool IsOpen => !IsHidden;

    /// <summary>
    /// Menú visible según wiring:
    /// - controlsWholePlayerRoot = true  => menú visible cuando IsHidden == true (player desplazado)
    /// - controlsWholePlayerRoot = false => menú visible cuando IsHidden == false (panel del menú abierto)
    /// </summary>
    public bool IsMenuVisible => controlsWholePlayerRoot ? IsHidden : !IsHidden;

    // Atajo global opcional (por si otros scripts quieren saber si hay un menú abierto)
    public static bool AnyOpen { get; private set; }

    // Internos
    private Vector2 initialPos;
    private Vector2 hiddenPos;
    private bool canToggle = true;

    /* ================= Ciclo ================= */

    void Start()
    {
        if (!panel) return;

        initialPos = panel.anchoredPosition;
        hiddenPos = ComputeHiddenPosition();

        // Estado inicial del panel
        if (startHidden)
        {
            panel.anchoredPosition = hiddenPos;
            IsHidden = true;
        }
        else
        {
            panel.anchoredPosition = initialPos;
            IsHidden = false;
        }

        // Estado inicial del overlay: encendido sólo si el MENÚ es visible
        ApplyOverlayInstant(IsMenuVisible ? overlayAlpha : 0f, IsMenuVisible);

        AnyOpen = IsMenuVisible;
        UpdateRotation();
    }

    void Update()
    {
        if (toggleKey != KeyCode.None && Input.GetKeyDown(toggleKey))
            TryTogglePanel();
    }

    /* ================= Input ================= */

    public void OnPointerClick(PointerEventData eventData)
    {
        // Sólo si se clicó el propio botón (no sus hijos)
        if (eventData.pointerEnter == gameObject)
            TryTogglePanel();
    }

    public void TryTogglePanel()
    {
        if (InputLock.IsLocked) return;
        if (!canToggle) return;

        if (IsHidden) Open();
        else Close();
    }

    /* =============== Animadas ================= */

    /// <summary>Open coloca el panel en su posición inicial.</summary>
    public void Open()
    {
        if (!panel || !canToggle) return;
        canToggle = false;

        LeanTween.move(panel, initialPos, animationDuration)
            .setEase(LeanTweenType.easeInOutQuart)
            .setOnComplete(() => canToggle = true);

        // Cambia estado primero, así IsMenuVisible refleja la nueva realidad
        IsHidden = false;

        // Overlay visible sólo si el MENÚ está visible
        SetOverlayVisibleAnimated(IsMenuVisible);

        AnyOpen = IsMenuVisible;
        UpdateRotation();
    }

    /// <summary>Close desplaza el panel hasta su posición oculta.</summary>
    public void Close()
    {
        if (!panel || !canToggle) return;
        canToggle = false;

        LeanTween.move(panel, hiddenPos, animationDuration)
            .setEase(LeanTweenType.easeInOutQuart)
            .setOnComplete(() => canToggle = true);

        IsHidden = true;

        // Overlay visible sólo si el MENÚ está visible
        SetOverlayVisibleAnimated(IsMenuVisible);

        AnyOpen = IsMenuVisible;
        UpdateRotation();
    }

    /* =============== Instantáneas =============== */

    public void OpenInstant()
    {
        if (!panel) return;

        panel.anchoredPosition = initialPos;
        IsHidden = false;

        ApplyOverlayInstant(IsMenuVisible ? overlayAlpha : 0f, IsMenuVisible);

        AnyOpen = IsMenuVisible;
        UpdateRotation();
    }

    public void CloseInstant()
    {
        if (!panel) return;

        panel.anchoredPosition = hiddenPos;
        IsHidden = true;

        ApplyOverlayInstant(IsMenuVisible ? overlayAlpha : 0f, IsMenuVisible);

        AnyOpen = IsMenuVisible;
        UpdateRotation();
    }

    /// <summary>Coloca el panel exactamente en la pos. inicial sin tocar estados.</summary>
    public void SnapToInitial()
    {
        if (!panel) return;
        panel.anchoredPosition = initialPos;
    }

    /* ================= Helpers ================= */

    private Vector2 ComputeHiddenPosition()
    {
        // Si movimiento principal es horizontal => usa width; si no, usa height
        float distance = Mathf.Abs(hiddenDirection.x) >= Mathf.Abs(hiddenDirection.y)
            ? (panel.rect.width - hiddenOffset)
            : (panel.rect.height - hiddenOffset);

        return initialPos + hiddenDirection.normalized * distance;
    }

    private void SetOverlayVisibleAnimated(bool visible)
    {
        if (!dimOverlay) return;

        dimOverlay.gameObject.SetActive(true); // lo encendemos para animar siempre
        dimOverlay.raycastTarget = visible;    // sólo bloquea clicks cuando visible

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
                // Al final, si debía estar oculto, sí desactivamos el GO
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
        // Simple feedback visual (ajústalo a tu gusto)
        rotationTarget.localRotation = Quaternion.Euler(0f, 0f, IsHidden ? 90f : 270f);
    }
}