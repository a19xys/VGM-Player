using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using UnityEngine.Video;
using TMPro;

public class SongLoader : MonoBehaviour
{
    [Header("Song Metadata UI")]
    public TextMeshProUGUI title1Text;
    public TextMeshProUGUI game1Text;
    public TextMeshProUGUI title2Text;
    public TextMeshProUGUI game2Text;

    [Header("Lyrics Controller")]
    public LyricsController lyricsController;

    [Header("Game Logo (opcional en UI)")]
    public RawImage gameLogo;

    [Header("Background / Video")]
    [Tooltip("Objeto que contiene el VideoPlayer (lo activamos/desactivamos entero).")]
    public GameObject videoContainer;   // tu 'gm'
    public VideoPlayer videoPlayer;
    [Tooltip("Fondo de la zona de vídeo (RawImage/Image) que colorearemos con Color2.")]
    public Graphic gm_background;       // tu 'gm_background'

    [Header("Audio Source")]
    public AudioSource audioSource;

    [Header("UI Elements (Colors)")]
    public List<RawImage> color1RawImages;
    public List<Image> color1Images;
    public List<RawImage> color2RawImages;
    public List<Image> color2Images;
    public Transform contentParent; // elementos de lista coloreables (si procede)

    [Header("Remix panel")]
    public GameObject remixObject;

    [Header("Vinilo")]
    public VinylDiscController vinyl;  // raíz del vinilo (RawImage circular que rota)

    [HideInInspector] public SongMetadata metadata;

    // --- Estado interno ---
    private string basePath;
    private readonly List<string> videoPaths = new List<string>();
    private int currentVideoIndex = -1;

    private Texture2D currentLogoTex;  // por si lo muestras en UI
    private Texture2D currentDiscTex;  // disc{id}.png

    [SerializeField] private string firstSongId = ""; // opcional, vacío si no quieres reproducir nada al arrancar

    /* =========================================================
     *                     CICLO DE VIDA
     * ========================================================= */
    void Awake()
    {
        basePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "VGM Hall of Fame");

        if (!string.IsNullOrEmpty(firstSongId))
        {
            // Carga directa estilo "single player" (sin transición)
            LoadSongMetadataInstant(firstSongId);
            StartCoroutine(PrepareAudioClipRoutine(firstSongId, autoPlay: true));
            StartCoroutine(PrepareVideosRoutine(firstSongId, autoPlay: true));
        }
    }

    /* =========================================================
     *                METADATOS + COLORES (INSTANT)
     * ========================================================= */
    /// <summary>
    /// Carga JSON, textos, logo y colores. No toca audio ni vídeo.
    /// </summary>
    public void LoadSongMetadataInstant(string id)
    {
        if (string.IsNullOrEmpty(basePath))
            basePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "VGM Hall of Fame");

        string jsonPath = Path.Combine(basePath, $"info{id}.json");
        if (!File.Exists(jsonPath))
        {
            Debug.LogError($"El archivo JSON {jsonPath} no existe.");
            return;
        }

        string jsonContent = File.ReadAllText(jsonPath);
        metadata = JsonUtility.FromJson<SongMetadata>(jsonContent);

        // Textos principales
        if (title1Text) title1Text.text = metadata.Title;
        if (game1Text) game1Text.text = $"{metadata.Composer} · {metadata.Game} ({metadata.ReleaseYear})";
        if (title2Text) title2Text.text = metadata.RemixTitle;
        if (game2Text) game2Text.text = $"{metadata.RemixComposer} · {metadata.RemixGame} ({metadata.RemixReleaseYear})";

        // Remix visible sólo si hay texto
        if (remixObject) remixObject.SetActive(!string.IsNullOrWhiteSpace(title2Text.text));

        // Reajustar geometría del panel de remix si existe y está activo ahora
        if (remixObject && remixObject.activeInHierarchy) {
            var sp = remixObject.GetComponent<SlidingPanelController>();
            // Forzar que el layout se estabilice y que el panel use el tamaño correcto
            if (sp != null) { sp.OnExternalContentPossiblyChangedAndBecameActive(); }
        }

        // Letras
        if (lyricsController != null)
        {
            lyricsController.ClearLyrics();
            if (!string.IsNullOrEmpty(metadata.Lyrics))
                lyricsController.LoadLyrics(metadata.Lyrics);
        }

        // Logo opcional de UI
        string logoPath = Path.Combine(basePath, $"logo{id}.png");
        currentLogoTex = null;
        if (File.Exists(logoPath))
        {
            var tex = new Texture2D(2, 2);
            tex.LoadImage(File.ReadAllBytes(logoPath));
            currentLogoTex = tex;
            if (gameLogo)
            {
                gameLogo.texture = tex;
                AdjustRawImageProportions(gameLogo, tex);
            }
        }
        else
        {
            if (gameLogo) gameLogo.texture = null;
        }

        // Arte del vinilo: disc{id}.png
        string discPath = Path.Combine(basePath, $"disc{id}.png");
        currentDiscTex = null;
        if (File.Exists(discPath))
        {
            var tex = new Texture2D(2, 2);
            tex.LoadImage(File.ReadAllBytes(discPath));
            currentDiscTex = tex;
        }
        else
        {
            // Fallback suave: si no hay 'disc', probar 'cover' y si no, usar logo.
            string coverPath = Path.Combine(basePath, $"cover{id}.png");
            if (File.Exists(coverPath))
            {
                var tex = new Texture2D(2, 2);
                tex.LoadImage(File.ReadAllBytes(coverPath));
                currentDiscTex = tex;
            }
            else
            {
                currentDiscTex = currentLogoTex;
            }
        }

        // Colores instantáneos (sin lerp)
        AssignColorsInstant(metadata.Color1, metadata.Color2);

        // Color de fondo del área de vídeo por robustez (también lo haremos en Apply*Mode)
        if (gm_background != null)
        {
            var c = gm_background.color;
            gm_background.color = new Color(metadata.Color2.r, metadata.Color2.g, metadata.Color2.b, c.a);
        }
    }

    public void AssignColorsInstant(Color color1, Color color2)
    {
        if (color1RawImages != null)
            foreach (var raw in color1RawImages) if (raw) raw.color = color1;
        if (color1Images != null)
            foreach (var img in color1Images) if (img) img.color = color1;

        if (color2RawImages != null)
            foreach (var raw in color2RawImages) if (raw) raw.color = color2;
        if (color2Images != null)
            foreach (var img in color2Images) if (img) img.color = color2;

        if (contentParent != null)
        {
            foreach (Transform child in contentParent)
            {
                var raw = child.GetComponentInChildren<RawImage>();
                if (raw) raw.color = color1;
            }
        }
    }

    private void AdjustRawImageProportions(RawImage rawImage, Texture2D texture)
    {
        // Sólo para ajustar el LOGO en UI (no tocar el vinilo aquí)
        RectTransform rt = rawImage.rectTransform;
        float aspect = (texture != null && texture.height != 0) ? (float)texture.width / texture.height : 1f;
        float w = rt.sizeDelta.x;
        float h = (aspect != 0f) ? w / aspect : rt.sizeDelta.y;
        rt.sizeDelta = new Vector2(w, h);
    }

    /* =========================================================
     *                         AUDIO
     * ========================================================= */
    /// <summary>
    /// Descarga/lee el MP3, asigna clip y lo deja listo. Si autoPlay=false NO reproduce.
    /// </summary>
    public IEnumerator PrepareAudioClipRoutine(string id, bool autoPlay = false)
    {
        if (string.IsNullOrEmpty(basePath))
            basePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "VGM Hall of Fame");

        string audioPath = Path.Combine(basePath, $"song{id}.mp3");
        if (!File.Exists(audioPath))
        {
            Debug.LogError($"El archivo de audio {audioPath} no existe.");
            yield break;
        }

        using (UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip("file://" + audioPath, AudioType.MPEG))
        {
            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"Error al cargar el audio: {www.error}");
                yield break;
            }

            AudioClip clip = DownloadHandlerAudioClip.GetContent(www);
            if (audioSource)
            {
                audioSource.clip = clip;
                audioSource.time = 0f;

                if (autoPlay) audioSource.Play();
                else audioSource.Stop(); // aseguramos que NO suene aún durante la cobertura
            }
        }
    }

    /* =========================================================
     *                         VÍDEO
     * ========================================================= */
    /// <summary>
    /// Busca vídeos 'video{id}_*.mp4'. Si hay, prepara VideoPlayer (sin Play si autoPlay=false).
    /// Si no hay, configura modo sin vídeo (vinilo visible pero sin girar si autoPlay=false).
    /// </summary>
    public IEnumerator PrepareVideosRoutine(string id, bool autoPlay = false)
    {
        if (videoPlayer == null)
        {
            ApplyNoVideoModePreparedOnly();
            yield break;
        }

        if (string.IsNullOrEmpty(basePath))
            basePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "VGM Hall of Fame");

        videoPaths.Clear();
        string[] files = Directory.GetFiles(basePath, $"video{id}_*.mp4");

        if (files.Length == 0)
        {
            ApplyNoVideoModePreparedOnly();
            yield break;
        }

        videoPaths.AddRange(files);
        currentVideoIndex = 0;

        videoPlayer.loopPointReached -= OnVideoEnded;
        videoPlayer.loopPointReached += OnVideoEnded;

        // Activa contenedor vídeo, oculta vinilo
        if (videoContainer != null) videoContainer.SetActive(true);
        if (vinyl != null) vinyl.Hide();

        // Color de fondo por robustez
        if (gm_background != null)
        {
            var c = gm_background.color;
            gm_background.color = new Color(metadata.Color2.r, metadata.Color2.g, metadata.Color2.b, c.a);
        }

        // Preparar primer vídeo
        videoPlayer.url = videoPaths[currentVideoIndex];
        videoPlayer.Prepare();
        while (!videoPlayer.isPrepared) yield return null;

        if (autoPlay) videoPlayer.Play();
        else videoPlayer.Pause(); // preparado, listo para StartPlayback()
    }

    private void OnVideoEnded(VideoPlayer vp)
    {
        StartCoroutine(PlayNextVideoPrepared());
    }

    private IEnumerator PlayNextVideoPrepared()
    {
        if (videoPlayer == null || videoPaths.Count == 0) yield break;
        currentVideoIndex = (currentVideoIndex + 1) % videoPaths.Count;
        videoPlayer.url = videoPaths[currentVideoIndex];
        videoPlayer.Prepare();
        while (!videoPlayer.isPrepared) yield return null;
        videoPlayer.Play();
    }

    /// <summary>
    /// Configuración de "sin vídeo" DEJANDO TODO LISTO pero SIN reproducir aún.
    /// </summary>
    private void ApplyNoVideoModePreparedOnly()
    {
        // Vídeo off
        if (videoContainer != null) videoContainer.SetActive(false);

        // Fondo al color secundario
        if (gm_background != null)
        {
            var c = gm_background.color;
            gm_background.color = new Color(metadata.Color2.r, metadata.Color2.g, metadata.Color2.b, c.a);
        }

        // Vinilo visible con arte, pero sin girar (hasta StartPlayback)
        if (vinyl != null)
        {
            vinyl.Show();
            vinyl.SetArtwork(currentDiscTex);
            vinyl.SetSpinDesired(false);
        }
    }

    /* =========================================================
     *                 ARRANQUE SINCRONIZADO
     * ========================================================= */
    /// <summary>
    /// Reproduce audio y vídeo (o arranca giro del vinilo) en el MISMO frame.
    /// Llamar justo antes de que empiece la salida de bloques de la transición.
    /// </summary>
    public void StartPlayback()
    {
        // Audio primero
        if (audioSource && audioSource.clip)
            audioSource.Play();

        // Vídeo, si hay
        if (videoContainer != null && videoContainer.activeSelf && videoPlayer != null && videoPlayer.isPrepared)
        {
            videoPlayer.Play();
            if (vinyl != null) vinyl.SetSpinDesired(false);
        }
        else
        {
            // Fallback vinilo: si hay audio sonando, girar
            if (vinyl != null)
            {
                vinyl.Show();
                bool playing = (audioSource != null && audioSource.isPlaying);
                vinyl.SetSpinDesired(playing);
            }
        }
    }

    /* =========================================================
     *                 COMPATIBILIDAD (int)
     * ========================================================= */
    public void LoadSongMetadataInstant(int id) => LoadSongMetadataInstant(id.ToString());
    public IEnumerator PrepareAudioClipRoutine(int id, bool autoPlay = false) => PrepareAudioClipRoutine(id.ToString(), autoPlay);
    public IEnumerator PrepareVideosRoutine(int id, bool autoPlay = false) => PrepareVideosRoutine(id.ToString(), autoPlay);

    // Compatibilidad “todo en uno” (sin transición)
    public void NextSong(int id)
    {
        string sid = id.ToString();
        LoadSongMetadataInstant(sid);
        StartCoroutine(PrepareAudioClipRoutine(sid, true));
        StartCoroutine(PrepareVideosRoutine(sid, true));
    }

    // Si todavía usas el viejo "LoadAndPlayVideosRoutine":
    public IEnumerator LoadAndPlayVideosRoutine(string id) => PrepareVideosRoutine(id, autoPlay: true);

    /* =========================================================
     *                   ESTRUCTURA JSON
     * ========================================================= */
    [Serializable]
    public class SongMetadata
    {
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

    /* =========================================================
     *                   ACCESORES ÚTILES
     * ========================================================= */
    public Texture CurrentLogoTexture => currentLogoTex;
    public Texture CurrentDiscTexture => currentDiscTex;
}