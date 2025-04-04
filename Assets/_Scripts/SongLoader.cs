using System.Collections.Generic;
using System.Collections;
using System.IO;
using System;
using UnityEngine.Networking;
using UnityEngine.Video;
using UnityEngine.UI;
using UnityEngine;
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

    [Header("UI Elements")]
    public List<RawImage> color1RawImages;
    public List<Image> color1Images;
    
    public List<RawImage> color2RawImages;
    public List<Image> color2Images;

    public Transform contentParent; // Contenedor de prefabs

    [Header("Remix panel")]
    public GameObject remixObject;

    [HideInInspector]
    public SongMetadata metadata;

    private string basePath;
    private List<string> videoPaths = new List<string>();
    private int currentVideoIndex = 0;
    private int firstSong = 20;

    void Awake() { NextSong(firstSong); }

    public void NextSong(int id) {

        if (lyricsController != null) { lyricsController.ClearLyrics(); }

        basePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "VGM Hall of Fame");

        if (!Directory.Exists(basePath)) {
            Directory.CreateDirectory(basePath);
            Debug.Log($"Directorio creado en: {basePath}");
        }

        LoadSongMetadata(id);
        LoadAndPlayAudio(id);
        LoadGameLogo(id);
        LoadAndPlayVideos(id);
    }

    /**************************************************************/

    void LoadSongMetadata(int id) {
        string jsonPath = Path.Combine(basePath, $"info{id:D3}.json");

        if (!File.Exists(jsonPath)) { Debug.LogError($"El archivo JSON {jsonPath} no existe."); return; }

        string jsonContent = File.ReadAllText(jsonPath);
        metadata = JsonUtility.FromJson<SongMetadata>(jsonContent);

        if (title1Text) title1Text.text = metadata.Title;
        if (game1Text) game1Text.text = metadata.Composer + " · " + metadata.Game + " (" + metadata.ReleaseYear + ")";
        if (title2Text) title2Text.text = metadata.RemixTitle;
        if (game2Text) game2Text.text = metadata.RemixComposer + " · " + metadata.RemixGame + " (" + metadata.RemixReleaseYear + ")";

        AssignColors(metadata.Color1, metadata.Color2);

        // Desactivar bloque de remix cuando no haya datos de remix que mostrar
        if (remixObject != null) { remixObject.SetActive(!string.IsNullOrEmpty(title2Text.text.Trim())); }

        // Pasar las letras al LyricsController
        if (lyricsController != null && !string.IsNullOrEmpty(metadata.Lyrics)) { lyricsController.LoadLyrics(metadata.Lyrics); }

    }

    /**************************************************************/

    public void AssignColors(Color color1, Color color2) {
        StopAllCoroutines(); // Detener transiciones en curso

        // Cambiar colores en RawImages e Images definidos
        foreach (var rawImage in color1RawImages) { if (rawImage) StartCoroutine(TransitionColor(rawImage, rawImage.color, color1, 1.6f)); }
        foreach (var image in color1Images) { if (image) StartCoroutine(TransitionColor(image, image.color, color1, 1.6f)); }

        foreach (var rawImage in color2RawImages) { if (rawImage) StartCoroutine(TransitionColor(rawImage, rawImage.color, color2, 1.6f)); }
        foreach (var image in color2Images) { if (image) StartCoroutine(TransitionColor(image, image.color, color2, 1.6f)); }

        // Cambiar colores en todos los prefabs dentro del contenedor
        foreach (Transform child in contentParent) {
            RawImage rawImage = child.GetComponentInChildren<RawImage>();
            if (rawImage) { StartCoroutine(TransitionColor(rawImage, rawImage.color, color1, 1.6f)); }
        }
    }

    private IEnumerator TransitionColor(Graphic graphic, Color startColor, Color targetColor, float duration) {
        float elapsed = 0f;
        while (elapsed < duration) {
            if (graphic == null) yield break; // Salir si el objeto se destruyó
            elapsed += Time.deltaTime;
            graphic.color = Color.Lerp(startColor, targetColor, elapsed / duration);
            yield return null;
        }
        if (graphic != null) graphic.color = targetColor; // Asegurarse del color final
    }

    private IEnumerator TransitionColor(RawImage rawImage, Color startColor, Color targetColor, float duration) {
        float elapsed = 0f;
        while (elapsed < duration) {
            if (rawImage == null) yield break; // Salir si el objeto se destruyó
            elapsed += Time.deltaTime;
            rawImage.color = Color.Lerp(startColor, targetColor, elapsed / duration);
            yield return null;
        }
        if (rawImage != null) rawImage.color = targetColor; // Asegurarse del color final
    }

    /**************************************************************/

    void LoadGameLogo(int id) {
        string logoPath = Path.Combine(basePath, $"logo{id:D3}.png");
        if (!File.Exists(logoPath)) {
            Debug.LogError($"El archivo de logo {logoPath} no existe.");
            return;
        }

        byte[] imageData = File.ReadAllBytes(logoPath);
        Texture2D logoTexture = new Texture2D(2, 2);
        logoTexture.LoadImage(imageData);

        if (gameLogo) {
            gameLogo.texture = logoTexture;
            AdjustRawImageProportions(gameLogo, logoTexture);
        }
    }

    void AdjustRawImageProportions(RawImage rawImage, Texture2D texture) {
        RectTransform rectTransform = rawImage.rectTransform;

        float textureWidth = texture.width;
        float textureHeight = texture.height;

        float aspectRatio = textureWidth / textureHeight;

        float targetWidth = rectTransform.sizeDelta.x;
        float targetHeight = targetWidth / aspectRatio;

        rectTransform.sizeDelta = new Vector2(targetWidth, targetHeight);
    }

    /**************************************************************/

    void LoadAndPlayVideos(int id) {
        videoPaths.Clear();
        string[] files = Directory.GetFiles(basePath, $"video{id:D3}_*.mp4");

        if (files.Length == 0) {
            Debug.LogError($"No se encontraron videos para el ID {id}.");
            return;
        }

        videoPaths.AddRange(files);
        PlayNextVideo();
    }

    void PlayNextVideo() {
        if (videoPaths.Count == 0 || videoPlayer == null) {
            Debug.LogError("No hay videos disponibles para reproducir.");
            return;
        }

        currentVideoIndex = (currentVideoIndex + 1) % videoPaths.Count;
        videoPlayer.url = videoPaths[currentVideoIndex];
        videoPlayer.Play();

        videoPlayer.loopPointReached += (vp) => PlayNextVideo();
    }

    /**************************************************************/

    void LoadAndPlayAudio(int id) {
        string audioPath = Path.Combine(basePath, $"song{id:D3}.mp3");
        if (!File.Exists(audioPath)) {
            Debug.LogError($"El archivo de audio {audioPath} no existe.");
            return;
        }

        StartCoroutine(LoadAudioClip(audioPath));
    }

    IEnumerator LoadAudioClip(string audioPath) {
        using (UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip("file://" + audioPath, AudioType.MPEG)) {
            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success) {
                Debug.LogError($"Error al cargar el audio: {www.error}");
            } else {
                AudioClip clip = DownloadHandlerAudioClip.GetContent(www);
                if (audioSource) {
                    audioSource.clip = clip;
                    audioSource.Play();
                }
            }
        }
    }

    /**************************************************************/

    [System.Serializable]
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