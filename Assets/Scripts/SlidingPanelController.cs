using UnityEngine;
using UnityEngine.UI;

public class SlidingPanelController : MonoBehaviour
{
    [Header("Panel")]
    public RectTransform panel;
    public float hiddenOffset;                // cuánto queda “asomado”
    public float animationDuration = 0.5f;
    public Vector2 hiddenDirection;           // hacia dónde se oculta
    public Transform rotationTarget;

    [Header("Behaviour")]
    public bool startHidden = false;
    [Header("Hotkey")]
    public KeyCode toggleKey;

    public bool IsHidden { get; private set; }

    private Vector2 initialPos;
    private Vector2 hiddenPos;
    private bool canToggle = true;
    private bool initialized;                 // para evitar doble init

    /* ================= Ciclo ================= */
    void Awake()
    {
        // No calculamos nada aún; lo haremos en OnEnable/Start dependiendo de si el GO
        // está activo o se activa más tarde.
    }

    void OnEnable()
    {
        // Cuando el panel se activa por primera vez (o reaparece),
        // su Rect puede cambiar: recalculamos geometría de forma robusta.
        RecalculateGeometry(keepHiddenState: false, applySnap: false);
        // Snap al estado inicial deseado sólo la primera vez:
        if (!initialized)
        {
            if (startHidden) { panel.anchoredPosition = hiddenPos; IsHidden = true; }
            else { panel.anchoredPosition = initialPos; IsHidden = false; }
            UpdateRotation();
            initialized = true;
        }
        else
        {
            // Si ya estaba inicializado, mantenemos el estado actual y lo “snapemos”
            // a sus nuevas coordenadas (por si tamaño cambió).
            SnapToCurrentState();
        }
    }

    void Start()
    {
        // Si el GO estaba activo desde el principio, aseguramos init
        if (!initialized)
        {
            RecalculateGeometry(keepHiddenState: false, applySnap: false);
            if (startHidden) { panel.anchoredPosition = hiddenPos; IsHidden = true; }
            else { panel.anchoredPosition = initialPos; IsHidden = false; }
            UpdateRotation();
            initialized = true;
        }
    }

    void Update()
    {
        if (toggleKey != KeyCode.None && Input.GetKeyDown(toggleKey) && !SlidingMenuController.AnyOpen)
            TryTogglePanel();
    }

    /* ================= API pública ================= */
    public void OnExternalContentPossiblyChangedAndBecameActive()
    {
        // Llamar cuando activas el GO o cambias su contenido/tamaño (p.ej., al pasar de no-remix a remix).
        // 1) Forzar layout para que rect.width/height sean correctos
        ForceRebuildLayout();
        // 2) Recalcular geometría manteniendo estado visible/oculto actual y snapear
        RecalculateGeometry(keepHiddenState: true, applySnap: true);
    }

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
        RecalculateGeometry(keepHiddenState: false, applySnap: false); // por si el tamaño cambió
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
        RecalculateGeometry(keepHiddenState: false, applySnap: false); // por si el tamaño cambió
        LeanTween.move(panel, hiddenPos, animationDuration)
                 .setEase(LeanTweenType.easeInOutQuart)
                 .setOnComplete(() => canToggle = true);
        IsHidden = true;
        UpdateRotation();
    }

    public void OpenInstant()
    {
        if (!panel) return;
        RecalculateGeometry(keepHiddenState: false, applySnap: false);
        panel.anchoredPosition = initialPos;
        IsHidden = false;
        UpdateRotation();
    }

    public void CloseInstant()
    {
        if (!panel) return;
        RecalculateGeometry(keepHiddenState: false, applySnap: false);
        panel.anchoredPosition = hiddenPos;
        IsHidden = true;
        UpdateRotation();
    }

    /* ================= Internos ================= */
    private void SnapToCurrentState()
    {
        if (!panel) return;
        RecalculateGeometry(keepHiddenState: true, applySnap: false);
        panel.anchoredPosition = IsHidden ? hiddenPos : initialPos;
        UpdateRotation();
    }

    private void RecalculateGeometry(bool keepHiddenState, bool applySnap)
    {
        if (!panel) return;

        // Guardamos el estado
        bool prevHidden = IsHidden;

        // Antes de leer tamaños, nos aseguramos de tener initialPos definido:
        if (initialPos == default)
            initialPos = panel.anchoredPosition;

        // Calcular hiddenPos con el tamaño actual
        hiddenPos = ComputeHiddenPosition();

        // Restaurar estado si así se pide
        if (keepHiddenState)
            IsHidden = prevHidden;

        if (applySnap)
            panel.anchoredPosition = IsHidden ? hiddenPos : initialPos;
    }

    private Vector2 ComputeHiddenPosition()
    {
        float distance = Mathf.Abs(hiddenDirection.x) > Mathf.Abs(hiddenDirection.y)
            ? (panel.rect.width - hiddenOffset)      // horizontal
            : (panel.rect.height - hiddenOffset);    // vertical
        return initialPos + hiddenDirection.normalized * distance;
    }

    private void UpdateRotation()
    {
        if (!rotationTarget) return;
        rotationTarget.localRotation = Quaternion.Euler(0f, 0f, IsHidden ? 270f : 90f);
    }

    private void ForceRebuildLayout()
    {
        if (!panel) return;
        // Por si hay LayoutGroups/ContentSizeFitter en jerarquía
        LayoutRebuilder.ForceRebuildLayoutImmediate(panel);
        var p = panel.parent as RectTransform;
        if (p) LayoutRebuilder.ForceRebuildLayoutImmediate(p);
    }
}