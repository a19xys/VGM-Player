using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;

public class SlidingPanelController : MonoBehaviour
{

    [System.Serializable] public class PanelEvent : UnityEvent { }

    [Header("Panel")]
    public RectTransform panel;
    public float hiddenOffset;                // cu�nto queda �asomado�
    public float animationDuration = 0.5f;
    public Vector2 hiddenDirection;           // hacia d�nde se oculta
    public Transform rotationTarget;

    [Header("Behaviour")]
    public bool startHidden = false;
    [Header("Hotkey")]
    public KeyCode toggleKey;

    [Header("Eventos")]
    public PanelEvent onPanelOpened;
    public PanelEvent onPanelClosed;

    // --- Scale on open/close (optional) ---
    [Header("Scale target (optional)")]
    public Transform scaleTarget;
    [Range(0.2f, 2f)] public float openScaleFactor = 0.81f;
    [Range(0.2f, 2f)] public float closedScaleFactor = 1.00f;

    public bool IsHidden { get; private set; }

    private Vector2 initialPos;
    private Vector2 hiddenPos;
    private bool canToggle = true;
    private bool initialized;                 // para evitar doble init

    private Vector3 _baseScale = Vector3.one;
    private bool _baseScaleCaptured = false;
    private int _scaleTweenId = -1;

    /* ================= Ciclo ================= */
    void Awake()
    {
        // No calculamos nada a�n; lo haremos en OnEnable/Start dependiendo de si el GO
        // est� activo o se activa m�s tarde.
    }

    void OnEnable()
    {
        // Cuando el panel se activa por primera vez (o reaparece),
        // su Rect puede cambiar: recalculamos geometr�a de forma robusta.
        RecalculateGeometry(keepHiddenState: false, applySnap: false);
        // Snap al estado inicial deseado s�lo la primera vez:
        if (!initialized)
        {
            if (startHidden) { panel.anchoredPosition = hiddenPos; IsHidden = true; }
            else { panel.anchoredPosition = initialPos; IsHidden = false; }
            UpdateRotation();
            initialized = true;
        }
        else
        {
            // Si ya estaba inicializado, mantenemos el estado actual y lo �snapemos�
            // a sus nuevas coordenadas (por si tama�o cambi�).
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
        // Llamar cuando activas el GO o cambias su contenido/tama�o (p.ej., al pasar de no-remix a remix).
        // 1) Forzar layout para que rect.width/height sean correctos
        ForceRebuildLayout();
        // 2) Recalcular geometr�a manteniendo estado visible/oculto actual y snapear
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

        // Animación de apertura del panel
        LeanTween.move(panel, initialPos, animationDuration)
            .setEase(LeanTweenType.easeInOutQuart)
            .setOnComplete(() => canToggle = true);

        IsHidden = false;
        UpdateRotation();
        onPanelOpened?.Invoke();

        // Escalado sincronizado con la misma duración
        StartScaleTweenFactor(openScaleFactor);
    }

    public void Close()
    {
        if (!panel || !canToggle) return;
        canToggle = false;

        RecalculateGeometry(keepHiddenState: false, applySnap: false); // por si el tamaño cambió

        // Animación de cierre del panel
        LeanTween.move(panel, hiddenPos, animationDuration)
            .setEase(LeanTweenType.easeInOutQuart)
            .setOnComplete(() => canToggle = true);

        IsHidden = true;
        UpdateRotation();
        onPanelClosed?.Invoke();

        // Escalado sincronizado con la misma duración
        StartScaleTweenFactor(closedScaleFactor);
    }

    public void OpenInstant()
    {
        if (!panel) return;
        RecalculateGeometry(keepHiddenState: false, applySnap: false);
        panel.anchoredPosition = initialPos;
        IsHidden = false;
        UpdateRotation();
        onPanelOpened?.Invoke();

        // Aplicar escala instantánea acorde a estado "abierto"
        SetScaleInstantFactor(openScaleFactor);
    }

    public void CloseInstant()
    {
        if (!panel) return;
        RecalculateGeometry(keepHiddenState: false, applySnap: false);
        panel.anchoredPosition = hiddenPos;
        IsHidden = true;
        UpdateRotation();
        onPanelClosed?.Invoke();

        // Aplicar escala instantánea acorde a estado "cerrado"
        SetScaleInstantFactor(closedScaleFactor);
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

        // Antes de leer tama�os, nos aseguramos de tener initialPos definido:
        if (initialPos == default)
            initialPos = panel.anchoredPosition;

        // Calcular hiddenPos con el tama�o actual
        hiddenPos = ComputeHiddenPosition();

        // Restaurar estado si as� se pide
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
        // Por si hay LayoutGroups/ContentSizeFitter en jerarqu�a
        LayoutRebuilder.ForceRebuildLayoutImmediate(panel);
        var p = panel.parent as RectTransform;
        if (p) LayoutRebuilder.ForceRebuildLayoutImmediate(p);
    }

    private void EnsureBaseScaleCaptured()
    {
        if (_baseScaleCaptured || scaleTarget == null) return;
        _baseScale = scaleTarget.localScale; // respeta la escala original del objeto
        _baseScaleCaptured = true;
    }

    /* ================= Helpers logo ================= */

    private void StartScaleTweenFactor(float factor)
    {
        if (scaleTarget == null) return;
        EnsureBaseScaleCaptured();

        if (_scaleTweenId != -1)
        {
            LeanTween.cancel(_scaleTweenId);
            _scaleTweenId = -1;
        }

        Vector3 target = _baseScale * factor;
        _scaleTweenId = LeanTween.scale(scaleTarget.gameObject, target, animationDuration)
            .setEase(LeanTweenType.easeInOutQuart)
            .id;
    }

    private void SetScaleInstantFactor(float factor)
    {
        if (scaleTarget == null) return;
        EnsureBaseScaleCaptured();
        scaleTarget.localScale = _baseScale * factor;
    }

}