using UnityEngine;
using UnityEngine.UI;

public class SongPlaybackManager : MonoBehaviour {

    public SongLoader songLoader;
    public MusicPlayer musicPlayer;

    public AudioSource audioSource; // Fuente de audio para la reproducción
    public Slider songSlider; // Slider de la canción
    public Image highlightImage; // Imagen para el Highlight
    public Button highlightButton; // Botón para saltar al inicio del Highlight

    private float highlightStart = 0f;
    private float highlightEnd = 0f;
    private float songDuration = 0f;

    void Start() {
        if (highlightButton != null) { highlightButton.onClick.AddListener(JumpToHighlight); }
        LoadSong();
    }

    public void LoadSong() {
        if (audioSource.clip == null) {
            Debug.LogError("No hay un AudioClip asignado al AudioSource.");
            return;
        }

        songDuration = audioSource.clip.length;

        ParseHighlight();
        UpdateHighlightVisual();
    }

    private void ParseHighlight() {
        string highlight = songLoader.metadata.Highlight; // Obtener el campo Highlight

        if (string.IsNullOrEmpty(highlight) || !highlight.Contains("-")) {
            highlightStart = highlightEnd = 0f; // Sin Highlight
            return;
        }

        string[] times = highlight.Trim('[', ']').Split('-');
        highlightStart = ParseTime(times[0]);
        highlightEnd = ParseTime(times[1]);
    }

    private float ParseTime(string time) {
        string[] parts = time.Split(':');
        if (parts.Length == 2 && int.TryParse(parts[0], out int minutes) && float.TryParse(parts[1], out float seconds)) { 
            return minutes * 60 + seconds; 
        }
        return 0f;
    }

    private void UpdateHighlightVisual() {
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


    public void JumpToHighlight() {
        if (audioSource != null && highlightStart > 0f) { musicPlayer.JumpTime(highlightStart); }
    }

}