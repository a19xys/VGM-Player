using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Vinilo UI que:
/// - Rota con aceleración/frenado suave (ramp).
/// - Muestra la carátula recortada al centro (uvRect).
/// - Se recorta visualmente con una máscara circular (Image + Mask).
///
/// Jerarquía recomendada:
///   VinylRoot (RectTransform)  [este script en el root]
///     - CircleMask (Image con sprite circular) + Mask (ShowMaskGraphic: a gusto)
///         - discArt (RawImage)   <-- aquí se pinta la carátula
///
/// No tocamos pivots/tamaños en runtime (adiós “jitter”).
/// </summary>
public class VinylDiscController : MonoBehaviour
{
    [Header("Refs")]
    [Tooltip("Nodo que rota (normalmente el mismo GameObject del vinilo)")]
    public RectTransform rotor;
    [Tooltip("Image con sprite circular + componente Mask (padre del discArt)")]
    public RawImage circleMask;
    [Tooltip("RawImage hijo (dentro de la máscara) donde se pinta la carátula")]
    public RawImage discArt;

    [Header("Spin")]
    [Tooltip("RPM objetivo cuando está reproduciendo")]
    public float rpm = 20f;
    [Tooltip("Segundos que tarda en pasar de 0->rpm o de rpm->0")]
    public float spinRampSeconds = 0.6f;

    // Estado
    private bool desiredSpin;        // objetivo lógico (true=reproduciendo)
    private float currentRPM;        // estado actual suavizado
    private float angleZ;            // rotación acumulada

    void Reset()
    {
        rotor = GetComponent<RectTransform>();
        if (!circleMask) circleMask = GetComponentInChildren<RawImage>();
        if (!discArt) discArt = GetComponentInChildren<RawImage>(true);
    }

    void Awake()
    {
        EnsureMask();
    }

    void Update()
    {
        // Suavizado de rpm (subida/bajada)
        float targetRPM = desiredSpin ? Mathf.Max(0f, rpm) : 0f;
        if (spinRampSeconds <= 0.0001f) currentRPM = targetRPM;
        else
        {
            float maxDelta = (Mathf.Max(rpm, 0.0001f) / spinRampSeconds) * Time.deltaTime;
            currentRPM = Mathf.MoveTowards(currentRPM, targetRPM, maxDelta);
        }

        if (rotor && currentRPM > 0f)
        {
            float degPerSec = currentRPM * 360f / 60f;
            angleZ += degPerSec * Time.deltaTime;
            var e = rotor.localEulerAngles;
            e.z = angleZ;
            rotor.localEulerAngles = e;
        }
    }

    /// <summary>Activa/desactiva la rotación objetivo (con rampa suave).</summary>
    public void SetSpinDesired(bool play)
    {
        desiredSpin = play;
    }

    /// <summary>Muestra el vinilo y, si desiredSpin=true, seguirá acelerando.</summary>
    public void Show()
    {
        if (!gameObject.activeSelf) gameObject.SetActive(true);
    }

    /// <summary>Oculta el vinilo y detiene giro (sin animación y sin perder ángulo).</summary>
    public void Hide()
    {
        desiredSpin = false;
        currentRPM = 0f;
        if (gameObject.activeSelf) gameObject.SetActive(false);
    }

    /// <summary>
    /// Establece la carátula. Centra y recorta a cuadrado con UVs (no deforma).
    /// </summary>
    public void SetArtwork(Texture tex)
    {
        if (!discArt) return;

        discArt.texture = tex;
        discArt.enabled = (tex != null);

        if (tex == null || tex.width == 0 || tex.height == 0)
        {
            discArt.uvRect = new Rect(0, 0, 1, 1);
            return;
        }

        int w = tex.width;
        int h = tex.height;

        if (w > h)
        {
            float offset = (w - h) * 0.5f;
            float x = offset / w;
            float size = (float)h / w;
            discArt.uvRect = new Rect(x, 0f, size, 1f);
        }
        else if (h > w)
        {
            float offset = (h - w) * 0.5f;
            float y = offset / h;
            float size = (float)w / h;
            discArt.uvRect = new Rect(0f, y, 1f, size);
        }
        else
        {
            discArt.uvRect = new Rect(0, 0, 1, 1);
        }
    }

    /// <summary>
    /// Si el Image de la máscara no tiene Mask, lo añade y lo configura.
    /// (Necesario para que el arte NO se salga del círculo.)
    /// </summary>
    private void EnsureMask()
    {
        if (!circleMask) return;

        var mask = circleMask.GetComponent<Mask>();
        if (!mask) mask = circleMask.gameObject.AddComponent<Mask>();

        // Tu decides si quieres ver el gráfico de la máscara (círculo) o sólo usarla:
        // true = se ve el sprite circular además del arte recortado
        // false = el sprite no se dibuja, pero recorta igual
        mask.showMaskGraphic = true; // o false, a tu gusto
    }

    /// <summary>Opcional: fija el ángulo manualmente.</summary>
    public void SetAngle(float zDegrees)
    {
        angleZ = zDegrees;
        if (rotor != null)
        {
            var e = rotor.localEulerAngles;
            e.z = angleZ;
            rotor.localEulerAngles = e;
        }
    }
}