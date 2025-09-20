using UnityEngine;
using TMPro;

/// <summary>
/// Marquee dual y sincronizado para dos TMP (versión con baseline fijo).
/// - Captura la posición de inicio REAL (baseline) una sola vez por rótulo.
/// - En cada reset de canción, fuerza ambos a su baseline antes de medir.
/// - Máquina de estados robusta: PauseStart -> Left -> PauseEnd -> Right.
/// </summary>
public class DualRemixMarquee : MonoBehaviour
{
    [Header("Refs")]
    public TextMeshProUGUI textA;
    public TextMeshProUGUI textB;
    [Tooltip("Viewport visible (soft mask). Si se deja vacío, se usa el padre de textA.")]
    public RectTransform viewport;
    [Tooltip("Opcional: pausa el scroll cuando la música está en pausa.")]
    public AudioSource audioSource;

    [Header("Behaviour")]
    public float speed = 28f;          // px/s
    public float pauseSeconds = 3f;    // pausa al inicio y final
    public float edgePadding = 16f;    // holgura extra para que no “raspe” el borde

    private enum Phase { PauseStart, ScrollLeft, PauseEnd, ScrollRight }
    private Phase phase = Phase.PauseStart;
    private float phaseTimer;

    private Line a = new Line();
    private Line b = new Line();
    private bool initialized;
    private bool manualPause;   // hover u otra pausa externa
    private bool active;        // si el ciclo está en marcha

    void OnEnable()
    {
        // Al reactivar, medimos en el próximo Update.
        initialized = false;
    }

    void Update()
    {
        if (!active) return;
        if (manualPause || (audioSource && !audioSource.isPlaying)) return;

        if (!initialized)
        {
            if (!EnsureAll()) return;
            // Baseline primero (true = forzar snap duro a baseline fijo)
            a.CaptureBaselineIfNeeded(true);
            b.CaptureBaselineIfNeeded(true);
            a.SnapToBaseline();
            b.SnapToBaseline();

            RecalculateMetricsInternal();  // ahora ya medimos con startX en baseline

            phase = Phase.PauseStart;
            phaseTimer = pauseSeconds;
            initialized = true;
        }

        float dt = Time.deltaTime;
        switch (phase)
        {
            case Phase.PauseStart:
                phaseTimer -= dt;
                if (phaseTimer <= 0f)
                {
                    a.ResetPhaseForLeft();
                    b.ResetPhaseForLeft();
                    phase = Phase.ScrollLeft;
                }
                break;

            case Phase.ScrollLeft:
                {
                    bool doneA = a.ScrollTowardsEnd(speed, dt);
                    bool doneB = b.ScrollTowardsEnd(speed, dt);
                    if (doneA && doneB)
                    {
                        phase = Phase.PauseEnd;
                        phaseTimer = pauseSeconds;
                    }
                    break;
                }

            case Phase.PauseEnd:
                phaseTimer -= dt;
                if (phaseTimer <= 0f)
                {
                    a.ResetPhaseForRight();
                    b.ResetPhaseForRight();
                    phase = Phase.ScrollRight;
                }
                break;

            case Phase.ScrollRight:
                {
                    bool doneA = a.ScrollTowardsStart(speed, dt);
                    bool doneB = b.ScrollTowardsStart(speed, dt);
                    if (doneA && doneB)
                    {
                        phase = Phase.PauseStart;
                        phaseTimer = pauseSeconds;
                    }
                    break;
                }
        }
    }

    /* ====================== API pública ====================== */

    /// <summary>
    /// Llamar con la pantalla cubierta al cambiar de canción/remix.
    /// Fuerza ambos textos a su baseline inicial y reinicia ciclo.
    /// </summary>
    public void ResetAndStart()
    {
        active = true;
        initialized = false; // provocamos el “hard reset” en el próximo Update
    }

    public void StopScrolling() => active = false;

    public void OnMouseEnterArea() { manualPause = true; }
    public void OnMouseExitArea() { manualPause = false; }

    /// <summary>Si cambias texto/estilo en runtime, re-mide y reinicia.</summary>
    public void RecalculateMetrics()
    {
        if (!EnsureAll()) return;
        // Snap duro a baseline antes de medir, para no heredar desplazamientos previos
        a.CaptureBaselineIfNeeded(false); // no recapturamos; usamos baseline guardado
        b.CaptureBaselineIfNeeded(false);
        a.SnapToBaseline();
        b.SnapToBaseline();

        RecalculateMetricsInternal();
        phase = Phase.PauseStart;
        phaseTimer = pauseSeconds;
    }

    /* ====================== Internos ====================== */

    private bool EnsureAll()
    {
        if (!a.Ensure(textA)) return false;
        if (!b.Ensure(textB)) return false;

        if (!viewport)
        {
            if (a.textRect && a.textRect.parent)
                viewport = a.textRect.parent.GetComponent<RectTransform>();
        }
        return viewport;
    }

    private void RecalculateMetricsInternal()
    {
        a.Measure(viewport, edgePadding);
        b.Measure(viewport, edgePadding);

        // Colocar ambos al “inicio lógico” (startX == baseline)
        a.SnapToStart();
        b.SnapToStart();
    }

    /* ====================== Línea ====================== */

    private class Line
    {
        public TextMeshProUGUI tmp;
        public RectTransform textRect;
        public RectTransform parentRect;

        // baseline fijo (la posición de layout original, capturada 1 vez)
        private bool hasBaseline;
        private float baseStartX;

        // métricas del ciclo actual
        public float startX;     // arranque del ciclo (igual a baseline)
        public float endX;       // destino hacia la izquierda
        public bool needsScroll; // si hace falta desplazamiento
        private bool phaseDone;

        public bool Ensure(TextMeshProUGUI t)
        {
            tmp = t;
            if (!tmp) return false;
            if (!textRect) textRect = tmp.GetComponent<RectTransform>();
            if (!textRect) return false;
            if (!parentRect && textRect.parent) parentRect = textRect.parent.GetComponent<RectTransform>();
            return parentRect;
        }

        /// <summary>
        /// Captura la posición de inicio de layout (baseline) una sola vez.
        /// Si forceSnap es true y ya había baseline, NO se sobreescribe;
        /// sólo forzamos el Rect a ese baseline al llamar a SnapToBaseline().
        /// </summary>
        public void CaptureBaselineIfNeeded(bool forceSnap)
        {
            if (!hasBaseline)
            {
                baseStartX = textRect.anchoredPosition.x;
                hasBaseline = true;
            }
            // Si forceSnap es true, el “snap” real se hace fuera (SnapToBaseline()).
        }

        public void SnapToBaseline()
        {
            if (!hasBaseline) return;
            var p = textRect.anchoredPosition;
            p.x = baseStartX;
            textRect.anchoredPosition = p;
        }

        public void Measure(RectTransform viewport, float padding)
        {
            if (!tmp || !textRect || !viewport) return;

            float textW = tmp.preferredWidth + padding;
            float viewW = viewport.rect.width;

            // startX siempre desde nuestro baseline fijo
            startX = hasBaseline ? baseStartX : textRect.anchoredPosition.x;

            float delta = Mathf.Max(0f, textW - viewW);
            needsScroll = delta > 0.5f;

            endX = startX - delta;
            phaseDone = !needsScroll; // si no hay scroll, esta fase se considera hecha
        }

        public void SnapToStart()
        {
            var p = textRect.anchoredPosition;
            p.x = startX;
            textRect.anchoredPosition = p;
            phaseDone = !needsScroll;
        }

        public void ResetPhaseForLeft() { phaseDone = !needsScroll; }
        public void ResetPhaseForRight() { phaseDone = !needsScroll; }

        public bool ScrollTowardsEnd(float speed, float dt)
        {
            if (phaseDone) return true;

            float x = textRect.anchoredPosition.x;
            float target = endX;
            float step = speed * dt;

            if (x <= target + 0.5f)
            {
                SetX(target);
                phaseDone = true;
                return true;
            }
            SetX(x - step);
            return false;
        }

        public bool ScrollTowardsStart(float speed, float dt)
        {
            if (phaseDone) return true;

            float x = textRect.anchoredPosition.x;
            float target = startX;
            float step = speed * dt;

            if (x >= target - 0.5f)
            {
                SetX(target);
                phaseDone = true;
                return true;
            }
            SetX(x + step);
            return false;
        }

        private void SetX(float x)
        {
            var p = textRect.anchoredPosition;
            p.x = x;
            textRect.anchoredPosition = p;
        }
    }
}