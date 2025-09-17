using UnityEngine;

public class SlidingPanelController : MonoBehaviour
{
    [Header("Panel")]
    public RectTransform panel;
    public float hiddenOffset;
    public float animationDuration = 0.5f;
    public Vector2 hiddenDirection;
    public Transform rotationTarget;

    [Header("Behaviour")]
    public bool startHidden = false;

    [Header("Hotkey")]
    public KeyCode toggleKey;

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

        UpdateRotation();
    }

    void Update()
    {
        // Hotkey de teclado
        if (toggleKey != KeyCode.None && Input.GetKeyDown(toggleKey))
            TryTogglePanel();
    }

    // ==== API para UI Buttons ====
    public void OnClickToggle() => TryTogglePanel();

    // ==== API general ====
    public void TryTogglePanel()
    {
        if (InputLock.IsLocked || !canToggle || !panel) return;
        if (IsHidden) Open();
        else Close();
    }

    public void Open()
    {
        if (!panel || !canToggle) return;
        canToggle = false;
        LeanTween.move(panel, initialPos, animationDuration)
                 .setEase(LeanTweenType.easeInOutQuart)
                 .setOnComplete(() => canToggle = true);
        IsHidden = false;
        UpdateRotation();
    }

    public void Close()
    {
        if (!panel || !canToggle) return;
        canToggle = false;
        LeanTween.move(panel, hiddenPos, animationDuration)
                 .setEase(LeanTweenType.easeInOutQuart)
                 .setOnComplete(() => canToggle = true);
        IsHidden = true;
        UpdateRotation();
    }

    public void OpenInstant()
    {
        if (!panel) return;
        panel.anchoredPosition = initialPos;
        IsHidden = false;
        UpdateRotation();
    }

    public void CloseInstant()
    {
        if (!panel) return;
        panel.anchoredPosition = hiddenPos;
        IsHidden = true;
        UpdateRotation();
    }

    // ==== Internos ====
    private Vector2 ComputeHiddenPosition()
    {
        float distance = Mathf.Abs(hiddenDirection.x) > Mathf.Abs(hiddenDirection.y)
            ? (panel.rect.width - hiddenOffset)     // horizontal
            : (panel.rect.height - hiddenOffset);   // vertical
        return initialPos + hiddenDirection.normalized * distance;
    }

    private void UpdateRotation()
    {
        if (!rotationTarget) return;
        rotationTarget.localRotation = Quaternion.Euler(0f, 0f, IsHidden ? 90f : 270f);
    }
}