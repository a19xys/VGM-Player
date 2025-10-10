using UnityEngine;
using UnityEngine.UI;
using System.Globalization;

public class SongPlaybackManager : MonoBehaviour
{

    public SongLoader songLoader;
    public MusicPlayer musicPlayer;

    public AudioSource audioSource; // Fuente de audio para la reproducción
    public Slider songSlider; // Slider de la canción
    public Image highlightImage; // Imagen para el Highlight
    public Button highlightButton; // Botón para saltar al inicio del Highlight

    private float highlightStart = 0f;
    private float highlightEnd = 0f;
    private float songDuration = 0f;

    void Start()
    {
        if (highlightButton != null)
            highlightButton.onClick.AddListener(JumpToHighlight);

        if (songLoader != null)
        {
            songLoader.OnMetadataLoaded += HandleMetadataLoaded;
            songLoader.OnAudioPrepared += HandleAudioPrepared;
        }

        if (musicPlayer != null && musicPlayer.queueManager != null)
            musicPlayer.queueManager.OnPlayModeChanged += HandlePlayModeChanged;

        if (audioSource != null && audioSource.clip != null)
            songDuration = audioSource.clip.length;

        if (songLoader != null && songLoader.metadata != null)
            ParseHighlightFromMetadata(songLoader.metadata);

        UpdateHighlightVisual();
        RefreshHighlightJumpAvailabilityUI();
    }

    void Update()
    {
        // Hotkey: ir al highlight (solo si permitido)
        if (!InputLock.IsLocked && !SlidingMenuController.AnyOpen && Input.GetKeyDown(KeyCode.H))
        {
            if (IsHighlightJumpAllowed())
                JumpToHighlight();
        }
    }

    public void LoadSong()
    {
        if (audioSource.clip == null)
        {
            Debug.LogError("No hay un AudioClip asignado al AudioSource.");
            return;
        }

        songDuration = audioSource.clip.length;

        ParseHighlight();
        UpdateHighlightVisual();
    }

    private void ParseHighlight()
    {
        // Usa el nuevo formato del JSON (objeto)
        if (songLoader == null || songLoader.metadata == null || songLoader.metadata.Highlight == null)
        {
            highlightStart = 0f;
            highlightEnd = 0f;
            return;
        }

        string s = songLoader.metadata.Highlight.start;
        string e = songLoader.metadata.Highlight.end;

        if (string.IsNullOrWhiteSpace(s) || string.IsNullOrWhiteSpace(e))
        {
            highlightStart = 0f;
            highlightEnd = 0f;
            return;
        }

        highlightStart = ParseTime(s);
        highlightEnd = ParseTime(e);
    }

    private float ParseTime(string time)
    {
        // Acepta M:SS, M:SS.s, M:SS.ss, M:SS.sss (punto o coma)
        time = time.Trim().Replace(',', '.');
        string[] parts = time.Split(':');
        if (parts.Length == 2 &&
            int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int minutes) &&
            float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float seconds))
        {
            return minutes * 60f + seconds;
        }
        return 0f;
    }

    private void UpdateHighlightVisual()
    {
        if (highlightImage == null || songSlider == null)
            return;

        // Validación base: si las marcas no tienen sentido → no dibujar
        if (!TryGetValidatedHighlight(out float s, out float e))
        {
            highlightImage.enabled = false;
            return;
        }

        bool loopActive = IsLoopOneActiveWithValidLoop(out float loopStartSec, out float loopEndSec);

        // Regla nueva (CAMBIO 3): en LoopOne, si el highlight NO cabe íntegro en el segmento, no dibujar
        if (loopActive && (s < loopStartSec || e > loopEndSec))
        {
            highlightImage.enabled = false;
            return;
        }

        // Duración efectiva (en LoopOne usamos end; en otros modos, duración real)
        float effectiveDur = EffectiveHighlightDuration();
        if (effectiveDur <= 0f)
        {
            highlightImage.enabled = false;
            return;
        }

        // Normalización respecto a la duración efectiva
        float nStart = s / effectiveDur;
        float nEnd = e / effectiveDur;

        // Asegurar orden
        if (nEnd < nStart) { float tmp = nStart; nStart = nEnd; nEnd = tmp; }

        // Clamp estricto al rango visible del slider
        float cStart = Mathf.Clamp(nStart, 0f, 1f);
        float cEnd = Mathf.Clamp(nEnd, 0f, 1f);

        // Sin ancho visible → no dibujar
        if (cEnd - cStart <= 0.001f)
        {
            highlightImage.enabled = false;
            return;
        }

        // Dibujar
        highlightImage.enabled = true;

        RectTransform highlightRect = highlightImage.GetComponent<RectTransform>();
        highlightRect.anchorMin = new Vector2(cStart, highlightRect.anchorMin.y);
        highlightRect.anchorMax = new Vector2(cEnd, highlightRect.anchorMax.y);

        // Preservar altura/offsets
        highlightRect.offsetMin = new Vector2(highlightRect.offsetMin.x, highlightRect.offsetMin.y);
        highlightRect.offsetMax = new Vector2(highlightRect.offsetMax.x, highlightRect.offsetMax.y);
    }

    public void JumpToHighlight()
    {
        if (!IsHighlightJumpAllowed()) return;

        if (audioSource != null && highlightStart > 0f)
        {
            musicPlayer.JumpTime(highlightStart);
        }
    }

    private void HandleMetadataLoaded(SongLoader.SongMetadata m)
    {
        // (re)parsear desde el nuevo formato
        ParseHighlightFromMetadata(m);

        // Aún no sabemos la duración si el clip no está preparado; la fijaremos en HandleAudioPrepared
        // Refrescar disponibilidad y banda con lo que sepamos hasta ahora
        RefreshHighlightJumpAvailabilityUI();
        UpdateHighlightVisual();
    }

    private void HandleAudioPrepared(AudioClip clip)
    {
        if (clip == null) return;

        songDuration = clip.length;

        // Recoloca la banda visual con duración efectiva (LoopOne)
        UpdateHighlightVisual();

        // Habilita/deshabilita botón según validez actual
        RefreshHighlightJumpAvailabilityUI();
    }

    private void OnDestroy()
    {
        // Limpieza del botón
        if (highlightButton != null)
            highlightButton.onClick.RemoveListener(JumpToHighlight);

        // Desuscripción del loader
        if (songLoader != null)
        {
            songLoader.OnMetadataLoaded -= HandleMetadataLoaded;
            songLoader.OnAudioPrepared -= HandleAudioPrepared;
        }

        // Desuscripción del cambio de modo
        if (musicPlayer != null && musicPlayer.queueManager != null)
        {
            musicPlayer.queueManager.OnPlayModeChanged -= HandlePlayModeChanged;
        }
    }

    private void ParseHighlightFromString(string highlight)
    {
        if (string.IsNullOrEmpty(highlight) || !highlight.Contains("-"))
        {
            highlightStart = highlightEnd = 0f;
            return;
        }
        string[] times = highlight.Trim('[', ']').Split('-');
        highlightStart = ParseTime(times[0]);
        highlightEnd = ParseTime(times[1]);
    }

    // --- Loop awareness (usa SongLoader + MusicPlayer para leer estado) ---
    private bool IsLoopOneActiveWithValidLoop(out float loopStart, out float loopEnd)
    {
        loopStart = 0f; loopEnd = 0f;

        // Requiere: modo RepeatOne y metadatos con Loop completo
        if (musicPlayer == null || musicPlayer.queueManager == null) return false;
        if (musicPlayer.queueManager.playMode != PlayMode.RepeatOne) return false;
        if (songLoader == null || songLoader.metadata == null || songLoader.metadata.Loop == null) return false;

        string s = songLoader.metadata.Loop.start;
        string e = songLoader.metadata.Loop.end;
        if (string.IsNullOrWhiteSpace(s) || string.IsNullOrWhiteSpace(e)) return false;

        loopStart = ParseTime(s);
        loopEnd = ParseTime(e);

        // Normaliza frente a la duración real ya conocida en este manager
        if (songDuration > 0f)
        {
            loopStart = Mathf.Clamp(loopStart, 0f, songDuration);
            loopEnd = Mathf.Clamp(loopEnd, 0f, songDuration);
        }
        return (loopEnd - loopStart) > 0.02f;
    }

    private float EffectiveHighlightDuration()
    {
        // Base: duración real del clip conocida por este manager
        float dur = Mathf.Max(0f, songDuration);
        if (dur <= 0f) return 0f;

        if (IsLoopOneActiveWithValidLoop(out _, out float loopEnd))
            return Mathf.Clamp(loopEnd, 0f, dur);   // 0..end cuando hay Loop válido en RepeatOne

        return dur; // otros modos → duración completa
    }

    // --- Validación para habilitar/deshabilitar salto al highlight ---
    private bool IsHighlightJumpAllowed()
    {
        // 1) Validación general: dentro de [0..songDuration] y start < end
        if (!TryGetValidatedHighlight(out float s, out float e))
            return false;

        // 2) En RepeatOne + Loop válido: el highlight debe CABER ENTERO en [loopStart, loopEnd)
        if (IsLoopOneActiveWithValidLoop(out float loopStart, out float loopEnd))
        {
            if (s < loopStart || e > loopEnd)
                return false;
        }

        return true;
    }

    private void RefreshHighlightJumpAvailabilityUI()
    {
        bool allowed = IsHighlightJumpAllowed();

        if (highlightButton != null)
            highlightButton.interactable = allowed; // se desactiva el botón
    }

    private void HandlePlayModeChanged(PlayMode _)
    {
        UpdateHighlightVisual();          // ya adaptado a duración efectiva en LoopOne
        RefreshHighlightJumpAvailabilityUI(); // y disponibilidad de salto
    }

    // Valida las marcas de highlight frente a la duración real.
    // Devuelve true si (0 <= start < end <= songDuration).
    private bool TryGetValidatedHighlight(out float s, out float e)
    {
        s = highlightStart;
        e = highlightEnd;

        if (songDuration <= 0f) return false;
        if (float.IsNaN(s) || float.IsNaN(e)) return false;

        // Rango válido: [0 .. songDuration], y start < end
        if (s < 0f || e < 0f) return false;
        if (s >= songDuration) return false;
        if (e > songDuration) return false;
        if (e - s < 0.01f) return false; // mínimo ancho para considerarlo válido

        return true;
    }

    // Parseo desde el nuevo formato {start:"MM:SS(.ms)", end:"M:SS(.ms)"}
    private void ParseHighlightFromMetadata(SongLoader.SongMetadata m)
    {
        highlightStart = 0f;
        highlightEnd = 0f;

        if (m == null || m.Highlight == null) return;

        string s = m.Highlight.start;
        string e = m.Highlight.end;

        if (string.IsNullOrWhiteSpace(s) || string.IsNullOrWhiteSpace(e))
            return;

        float ss = ParseTime(s);
        float ee = ParseTime(e);

        // Guarda números; la validación completa la hace TryGetValidatedHighlight
        highlightStart = ss;
        highlightEnd = ee;
    }

}