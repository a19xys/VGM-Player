using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class SlidingMenuController : MonoBehaviour, IPointerClickHandler
{
    [Header("Panel")]
    public RectTransform panel;              // Este es tu “player” si así lo tienes
    public float hiddenOffset = 0f;
    public float animationDuration = 0.5f;
    public Vector2 hiddenDirection = Vector2.left; // X != 0 => horizontal, Y != 0 => vertical
    public Transform rotationTarget;

    [Header("Overlay (velo opcional)")]
    public RawImage dimOverlay;
    public float fadeDuration = 0.3f;
    [Range(0f, 1f)] public float overlayAlpha = 200f / 255f;

    [Header("Hotkey")]
    public KeyCode toggleKey = KeyCode.None;

    [Header("Behaviour")]
    public bool startHidden = false;            // ✅ respeta estado inicial
    public bool panelIsWholePlayerRoot = false; // ✅ si mueve todo el “player”

    public bool IsHidden { get; private set; }

    private Vector2 initialPos;
    private Vector2 hiddenPos;
    private bool canToggle = true;

    void Start()
    {
        if (!panel) return;
        initialPos = panel.anchoredPosition;
        hiddenPos = ComputeHiddenPosition();

        if (startHidden) { panel.anchoredPosition = hiddenPos; IsHidden = true; }
        else { panel.anchoredPosition = initialPos; IsHidden = false; }

        if (dimOverlay)
        {
            dimOverlay.gameObject.SetActive(!IsHidden);
            var c = dimOverlay.color; c.a = IsHidden ? 0f : overlayAlpha; dimOverlay.color = c;
        }
        UpdateRotation();
    }

    void Update()
    {
        if (toggleKey != KeyCode.None && Input.GetKeyDown(toggleKey)) TryTogglePanel();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.pointerEnter == gameObject) TryTogglePanel();
    }

    public void TryTogglePanel()
    {
        if (InputLock.IsLocked) return;
        if (!canToggle) return;
        if (IsHidden) Open();
        else Close();
    }

    /* ================= Animadas ================= */
    public void Open()
    {
        if (!panel || !canToggle) return;
        canToggle = false;
        LeanTween.move(panel, initialPos, animationDuration).setEase(LeanTweenType.easeInOutQuart)
            .setOnComplete(() => canToggle = true);
        ShowOverlay();
        IsHidden = false;
        UpdateRotation();
    }
    public void Close()
    {
        if (!panel || !canToggle) return;
        canToggle = false;
        LeanTween.move(panel, hiddenPos, animationDuration).setEase(LeanTweenType.easeInOutQuart)
            .setOnComplete(() => canToggle = true);
        HideOverlay();
        IsHidden = true;
        UpdateRotation();
    }

    /* =============== Instantáneas =============== */
    public void OpenInstant()
    {
        if (!panel) return;
        panel.anchoredPosition = initialPos;
        IsHidden = false;
        ForceOverlay(overlayAlpha);
        UpdateRotation();
    }
    public void CloseInstant()
    {
        if (!panel) return;
        panel.anchoredPosition = hiddenPos;
        IsHidden = true;
        ForceOverlay(0f);
        UpdateRotation();
    }

    public void SnapToInitial()
    {
        if (!panel) return;
        panel.anchoredPosition = initialPos;
    }

    /* ================= Helpers ================= */
    private Vector2 ComputeHiddenPosition()
    {
        var p = initialPos;
        // Si te mueves horizontal, usa width; si vertical, usa height
        float distance = Mathf.Abs(hiddenDirection.x) > Mathf.Abs(hiddenDirection.y)
            ? (panel.rect.width - hiddenOffset)
            : (panel.rect.height - hiddenOffset);
        return initialPos + hiddenDirection.normalized * distance;
    }

    private void ShowOverlay()
    {
        if (!dimOverlay) return;
        dimOverlay.gameObject.SetActive(true);
        var a0 = dimOverlay.color.a;
        LeanTween.value(gameObject, a0, overlayAlpha, fadeDuration)
            .setOnUpdate(a => { var c = dimOverlay.color; c.a = a; dimOverlay.color = c; });
    }
    private void HideOverlay()
    {
        if (!dimOverlay) return;
        var a0 = dimOverlay.color.a;
        LeanTween.value(gameObject, a0, 0f, fadeDuration)
            .setOnUpdate(a => { var c = dimOverlay.color; c.a = a; dimOverlay.color = c; })
            .setOnComplete(() => dimOverlay.gameObject.SetActive(false));
    }
    private void ForceOverlay(float targetA)
    {
        if (!dimOverlay) return;
        dimOverlay.gameObject.SetActive(targetA > 0f);
        var c = dimOverlay.color; c.a = targetA; dimOverlay.color = c;
    }

    private void UpdateRotation()
    {
        if (!rotationTarget) return;
        rotationTarget.localRotation = Quaternion.Euler(0f, 0f, IsHidden ? 90f : 270f);
    }
}