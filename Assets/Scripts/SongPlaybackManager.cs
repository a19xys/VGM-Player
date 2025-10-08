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
        if (highlightButton != null) { highlightButton.onClick.AddListener(JumpToHighlight); }

        // Suscripción a eventos del loader
        if (songLoader != null)
        {
            songLoader.OnMetadataLoaded += HandleMetadataLoaded;
            songLoader.OnAudioPrepared += HandleAudioPrepared;
        }

        // Si ya hay algo cargado al arrancar (firstSongId), inicializa
        if (audioSource != null && audioSource.clip != null)
        {
            songDuration = audioSource.clip.length;
        }
        if (songLoader != null && songLoader.metadata != null)
        {
            ParseHighlightFromString(songLoader.metadata.Highlight);
        }
        UpdateHighlightVisual();
    }

    void Update() {
        // Hotkey: ir al highlight
        if (!InputLock.IsLocked && !SlidingMenuController.AnyOpen && Input.GetKeyDown(KeyCode.H))
        {
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
        string highlight = songLoader.metadata.Highlight; // Obtener el campo Highlight

        if (string.IsNullOrEmpty(highlight) || !highlight.Contains("-"))
        {
            highlightStart = highlightEnd = 0f; // Sin Highlight
            return;
        }

        string[] times = highlight.Trim('[', ']').Split('-');
        highlightStart = ParseTime(times[0]);
        highlightEnd = ParseTime(times[1]);
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
        if (highlightImage == null || songSlider == null || songDuration <= 0) return;

        float highlightStartNormalized = highlightStart / songDuration;
        float highlightEndNormalized = highlightEnd / songDuration;

        RectTransform highlightRect = highlightImage.GetComponent<RectTransform>();

        // Solo ajustar el ancho basado en el tiempo normalizado
        highlightRect.anchorMin = new Vector2(highlightStartNormalized, highlightRect.anchorMin.y);
        highlightRect.anchorMax = new Vector2(highlightEndNormalized, highlightRect.anchorMax.y);

        // Mantener los offsets actuales para que la altura no se modifique
        highlightRect.offsetMin = new Vector2(highlightRect.offsetMin.x, highlightRect.offsetMin.y);
        highlightRect.offsetMax = new Vector2(highlightRect.offsetMax.x, highlightRect.offsetMax.y);
    }


    public void JumpToHighlight()
    {
        if (audioSource != null && highlightStart > 0f) { musicPlayer.JumpTime(highlightStart); }
    }

    private void HandleMetadataLoaded(SongLoader.SongMetadata m)
    {
        // (re)parsear el highlight de los metadatos nuevos
        ParseHighlightFromString(m != null ? m.Highlight : null);
        // Aún no sabemos la duración si el clip no está preparado; la fijaremos en HandleAudioPrepared
    }

    private void HandleAudioPrepared(AudioClip clip)
    {
        if (clip == null) return;
        songDuration = clip.length;
        UpdateHighlightVisual();   // recoloca la banda visual en el slider
    }

    private void OnDestroy()
    {
        if (songLoader != null)
        {
            songLoader.OnMetadataLoaded -= HandleMetadataLoaded;
            songLoader.OnAudioPrepared -= HandleAudioPrepared;
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

}