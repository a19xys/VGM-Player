using System.Collections;
using System.Collections.Generic;
using System.IO;
using System;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using UnityEngine.Video;
using TMPro;

public class SongLoader : MonoBehaviour {

    [Header("Song Metadata UI")]
    public TextMeshProUGUI title1Text;
    public TextMeshProUGUI game1Text;
    public TextMeshProUGUI title2Text;
    public TextMeshProUGUI game2Text;

    [Header("Lyrics Controller")]
    public LyricsController lyricsController;

    [Header("Game Logo")]
    public RawImage gameLogo;

    [Header("Video Player")]
    public VideoPlayer videoPlayer;

    [Header("Audio Source")]
    public AudioSource audioSource;

    [Header("UI Elements (Colors)")]
    public List<RawImage> color1RawImages;
    public List<Image> color1Images;

    public List<RawImage> color2RawImages;
    public List<Image> color2Images;

    public Transform contentParent; // Contenedor de prefabs coloreables (por ejemplo, elementos de lista)

    [Header("Remix panel")]
    public GameObject remixObject;

    [HideInInspector]
    public SongMetadata metadata;

    // --- Estado interno ---
    private string basePath;
    private readonly List<string> videoPaths = new List<string>();
    private int currentVideoIndex = -1;

    // Opcional: canción inicial para arrancar el reproductor sin transición
    [SerializeField] private int firstSong = 1008;

    void Awake() {
        // Si quieres que al abrir la app ya suene algo, puedes mantener esto.
        // Usa ya las APIs "instant" + corutinas para ser coherentes con la transición.
        if (firstSong > 0) {
            LoadSongMetadataInstant(firstSong);
            StartCoroutine(LoadAudioClipRoutine(firstSong));
            StartCoroutine(LoadAndPlayVideosRoutine(firstSong));
        }
    }

    /// <summary>
    /// Método de compatibilidad para cambiar de canción "de golpe" (sin pantalla de transición).
    /// La transición debería usar: LoadSongMetadataInstant + LoadAudioClipRoutine + LoadAndPlayVideosRoutine.
    /// </summary>
    public void NextSong(int id) {
        LoadSongMetadataInstant(id);
        StartCoroutine(LoadAudioClipRoutine(id));
        StartCoroutine(LoadAndPlayVideosRoutine(id));
    }

    // ============================================================
    // ================   METADATOS + COLORES   ===================
    // ============================================================

    /// <summary>
    /// Carga metadatos, textos y logo de forma SINCRÓNICA e INSTANTÁNEA.
    /// Aplica colores con AssignColorsInstant (sin lerp).
    /// </summary>
    public void LoadSongMetadataInstant(int id) {
        basePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "VGM Hall of Fame");

        string jsonPath = Path.Combine(basePath, $"info{id:D3}.json");
        if (!File.Exists(jsonPath)) {
            Debug.LogError($"El archivo JSON {jsonPath} no existe.");
            return;
        }

        string jsonContent = File.ReadAllText(jsonPath);
        metadata = JsonUtility.FromJson<SongMetadata>(jsonContent);

        // Textos
        if (title1Text) title1Text.text = metadata.Title;
        if (game1Text) game1Text.text = $"{metadata.Composer} · {metadata.Game} ({metadata.ReleaseYear})";
        if (title2Text) title2Text.text = metadata.RemixTitle;
        if (game2Text) game2Text.text = $"{metadata.RemixComposer} · {metadata.RemixGame} ({metadata.RemixReleaseYear})";

        // Panel Remix visible sólo si hay texto
        if (remixObject != null) remixObject.SetActive(!string.IsNullOrEmpty(title2Text.text.Trim()));

        // Letras
        if (lyricsController != null) {
            lyricsController.ClearLyrics();
            if (!string.IsNullOrEmpty(metadata.Lyrics))
                lyricsController.LoadLyrics(metadata.Lyrics);
        }

        // Logo
        string logoPath = Path.Combine(basePath, $"logo{id:D3}.png");
        if (File.Exists(logoPath)) {
            byte[] imageData = File.ReadAllBytes(logoPath);
            Texture2D logoTexture = new Texture2D(2, 2);
            logoTexture.LoadImage(imageData);

            if (gameLogo) {
                gameLogo.texture = logoTexture;
                AdjustRawImageProportions(gameLogo, logoTexture);
            }
        } else {
            Debug.LogWarning($"Logo no encontrado: {logoPath}");
            if (gameLogo) gameLogo.texture = null;
        }

        // Colores instantáneos (sin transiciones)
        AssignColorsInstant(metadata.Color1, metadata.Color2);
    }

    /// <summary>
    /// Aplica Color1/Color2 inmediatamente a todos los elementos configurados.
    /// </summary>
    public void AssignColorsInstant(Color color1, Color color2) {
        // Color a grupos definidos
        if (color1RawImages != null)
            foreach (var raw in color1RawImages) if (raw) raw.color = color1;

        if (color1Images != null)
            foreach (var img in color1Images) if (img) img.color = color1;

        if (color2RawImages != null)
            foreach (var raw in color2RawImages) if (raw) raw.color = color2;

        if (color2Images != null)
            foreach (var img in color2Images) if (img) img.color = color2;

        // Color a prefabs del contenedor (si aplica)
        if (contentParent != null) {
            foreach (Transform child in contentParent) {
                RawImage rawImage = child.GetComponentInChildren<RawImage>();
                if (rawImage) rawImage.color = color1;
            }
        }
    }

    private void AdjustRawImageProportions(RawImage rawImage, Texture2D texture) {
        RectTransform rectTransform = rawImage.rectTransform;

        float textureWidth = texture.width;
        float textureHeight = texture.height;
        float aspectRatio = textureWidth / textureHeight;

        float targetWidth = rectTransform.sizeDelta.x;
        float targetHeight = (aspectRatio != 0f) ? targetWidth / aspectRatio : rectTransform.sizeDelta.y;

        rectTransform.sizeDelta = new Vector2(targetWidth, targetHeight);
    }

    // ============================================================
    // =====================      AUDIO      ======================
    // ============================================================

    /// <summary>
    /// Carga el MP3 por id y cuando está listo asigna clip, resetea time y reproduce.
    /// Usado por la transición mientras la pantalla está cubierta.
    /// </summary>
    public IEnumerator LoadAudioClipRoutine(int id) {
        if (string.IsNullOrEmpty(basePath)) { basePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "VGM Hall of Fame"); }

        string audioPath = Path.Combine(basePath, $"song{id:D3}.mp3");
        if (!File.Exists(audioPath)) {
            Debug.LogError($"El archivo de audio {audioPath} no existe.");
            yield break;
        }

        using (UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip("file://" + audioPath, AudioType.MPEG)) {
            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success) { Debug.LogError($"Error al cargar el audio: {www.error}"); }
            else {
                AudioClip clip = DownloadHandlerAudioClip.GetContent(www);
                if (audioSource) {
                    audioSource.clip = clip;
                    audioSource.time = 0f;
                    audioSource.Play();
                }
            }
        }
    }

    // ============================================================
    // ======================      VÍDEO      =====================
    // ============================================================

    /// <summary>
    /// Limpia lista, detecta vídeos para el id, prepara y reproduce el primero.
    /// </summary>
    public IEnumerator LoadAndPlayVideosRoutine(int id) {
        if (videoPlayer == null) yield break;

        if (string.IsNullOrEmpty(basePath))
            basePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "VGM Hall of Fame");

        videoPaths.Clear();
        string[] files = Directory.GetFiles(basePath, $"video{id:D3}_*.mp4");
        if (files.Length == 0) {
            // No es error fatal: simplemente no habrá vídeo
            Debug.LogWarning($"No se encontraron vídeos para el ID {id}.");
            videoPlayer.loopPointReached -= OnVideoEnded; // por si estaba suscrito
            yield break;
        }

        videoPaths.AddRange(files);
        currentVideoIndex = -1;

        // Suscribir handler (evitar duplicados)
        videoPlayer.loopPointReached -= OnVideoEnded;
        videoPlayer.loopPointReached += OnVideoEnded;

        // Reproducir el primero preparado
        yield return PlayNextVideoPrepared();
    }

    private IEnumerator PlayNextVideoPrepared() {
        if (videoPlayer == null || videoPaths.Count == 0) yield break;

        currentVideoIndex = (currentVideoIndex + 1) % videoPaths.Count;
        videoPlayer.url = videoPaths[currentVideoIndex];

        videoPlayer.Prepare();
        while (!videoPlayer.isPrepared) yield return null;

        videoPlayer.Play();
    }

    private void OnVideoEnded(VideoPlayer vp) {
        // Avanzar al siguiente preparado
        StartCoroutine(PlayNextVideoPrepared());
    }

    // ============================================================
    // ===================   Estructura JSON   ====================
    // ============================================================

    [Serializable]
    public class SongMetadata {
        public string Title;
        public string Composer;
        public string Game;
        public int ReleaseYear;
        public string RemixTitle;
        public string RemixComposer;
        public string RemixGame;
        public string RemixReleaseYear;
        public string Highlight;
        public bool Favorite;
        public Color Color1;
        public Color Color2;
        public string Lyrics;
    }

}