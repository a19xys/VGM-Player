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

    [Header("Video formats")]
    [Tooltip("Extensiones admitidas, en orden de preferencia.")]
    public string[] allowedVideoExtensions = new[] { ".mp4", ".webm", ".mkv" };

    [Header("UI Elements (Colors)")]
    public List<RawImage> color1RawImages;
    public List<Image> color1Images;
    public List<RawImage> color2RawImages;
    public List<Image> color2Images;
    public Transform contentParent; // elementos de lista coloreables (si procede)

    [Header("Remix panel")]
    public GameObject remixObject;

    [Header("First song")]
    public bool randomizeFirstSong = true;
    [SerializeField] private string firstSongId = "";

    [Header("Queue")]
    public TrackQueueManager queueManager; // para sincronizar el primer tema con la cola

    [Header("Transition")]
    public SongTransitionController transition;

    [Header("Vinilo")]
    public VinylDiscController vinyl;  // raíz del vinilo (RawImage circular que rota)

    public event System.Action<Color, Color> OnThemeChanged;
    public event System.Action<SongMetadata> OnMetadataLoaded;
    public event System.Action<AudioClip> OnAudioPrepared;
    public event System.Action OnVideoReallyReady;

    [HideInInspector] public SongMetadata metadata;

    // --- Estado interno ---
    private string basePath;
    private readonly List<string> videoPaths = new List<string>();
    private int currentVideoIndex = -1;
    private System.Random _rng = new System.Random();
    private bool videoFirstFrameReady;

    private Texture2D currentLogoTex;  // por si lo muestras en UI
    private Texture2D currentDiscTex;  // disc{id}.png

    /* =========================================================
     *                     CICLO DE VIDA
     * ========================================================= */
    void Awake()
    {
        basePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "VGM Hall of Fame");

        // Arranque diferido: esperamos a que el menú (vía queue) tenga lista filtrada disponible.
        if (randomizeFirstSong) { StartCoroutine(StartRandomFirstSongWhenReady()); }
        else if (!string.IsNullOrEmpty(firstSongId)) { StartCoroutine(StartSpecificFirstSongWhenReady(firstSongId)); }
    }

    /* =========================================================
    *                   COLA DE CANCIONES
    * ========================================================= */
    private IEnumerator WaitUntilMenuReady()
    {
        // Si tenemos queue y menú, esperamos a que haya lista filtrada.
        if (queueManager != null && queueManager.menu != null)
        {
            while (queueManager.menu.GetFiltered() == null || queueManager.menu.FilteredCount() == 0)
                yield return null;
        }
        // Si no hay queue/menu, seguimos sin esperar.
    }

    private IEnumerator StartRandomFirstSongWhenReady()
    {
        yield return WaitUntilMenuReady();

        string id = null;
        if (queueManager != null && queueManager.menu != null && queueManager.menu.FilteredCount() > 0)
        {
            var list = queueManager.menu.GetFiltered();
            int idx = _rng.Next(list.Count);
            id = list[idx].FileNumber;
        }
        else
        {
            var jsonFiles = Directory.GetFiles(basePath, "info*.json");
            if (jsonFiles.Length > 0)
            {
                int pick = _rng.Next(jsonFiles.Length);
                string filename = Path.GetFileNameWithoutExtension(jsonFiles[pick]); // "infoXYZ"
                id = filename.Substring(4); // "XYZ"
            }
        }

        if (string.IsNullOrEmpty(id)) yield break;

        // 🔸 Nuevo: delega en la transición para que haga SOLO la salida
        if (transition != null)
        {
            transition.StartInitialRevealWithId(id);
        }
        else if (queueManager != null)
        {
            // Fallback clásico si no has asignado la transición
            queueManager.SyncWithSongId(id);
        }
        else
        {
            LoadAndPlayDirect(id);
        }
    }

    private IEnumerator StartSpecificFirstSongWhenReady(string id)
    {
        yield return WaitUntilMenuReady();
        if (string.IsNullOrEmpty(id)) yield break;

        if (transition != null)
        {
            transition.StartInitialRevealWithId(id);
        }
        else if (queueManager != null)
        {
            queueManager.SyncWithSongId(id);
        }
        else
        {
            LoadAndPlayDirect(id);
        }
    }

    private void LoadAndPlayDirect(string id)
    {
        // Comportamiento original: cargar y reproducir directamente
        LoadSongMetadataInstant(id);
        StartCoroutine(PrepareAudioClipRoutine(id, autoPlay: true));
        StartCoroutine(PrepareVideosRoutine(id, autoPlay: true));
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
        if (remixObject && remixObject.activeInHierarchy)
        {
            var sp = remixObject.GetComponent<SlidingPanelController>();
            // Forzar que el layout se estabilice y que el panel use el tamaño correcto
            if (sp != null) { sp.OnExternalContentPossiblyChangedAndBecameActive(); }
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

        // SongLoader.cs, dentro de LoadSongMetadataInstant(string id) al final del bloque donde ya tienes los colores aplicados
        AssignColorsInstant(metadata.Color1, metadata.Color2);

        // Notificar a la UI (Color1/Color2 actualizados)
        OnThemeChanged?.Invoke(metadata.Color1, metadata.Color2);

        OnMetadataLoaded?.Invoke(metadata);

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
                OnAudioPrepared?.Invoke(clip);

                if (autoPlay) audioSource.Play();
                else audioSource.Stop(); // aseguramos que NO suene aún durante la cobertura
            }
        }
    }

    /* =========================================================
     *                         VÍDEO
     * ========================================================= */
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

        // 1) Construir lista de candidatos: video{id}_*.EXT para cada extensión permitida
        List<string> candidates = new List<string>(32);
        if (allowedVideoExtensions == null || allowedVideoExtensions.Length == 0)
        {
            // fallback defensivo
            allowedVideoExtensions = new[] { ".mp4", ".webm" };
        }
        foreach (var ext in allowedVideoExtensions)
        {
            // Buscar archivos video{id}_*.ext
            // Importante: Directory.GetFiles no acepta OR, así que iteramos extensiones.
            string pattern = $"video{id}_*{ext}";
            string[] found = Directory.GetFiles(basePath, pattern);
            if (found != null && found.Length > 0)
                candidates.AddRange(found);
        }

        if (candidates.Count == 0)
        {
            // Sin vídeos → modo “no vídeo”
            ApplyNoVideoModePreparedOnly();
            yield break;
        }

        // 2) Guardar candidatos y preparar el primero
        videoPaths.AddRange(candidates);
        currentVideoIndex = 0;

        // Limpiar/añadir callback de fin
        videoPlayer.loopPointReached -= OnVideoEnded;
        videoPlayer.loopPointReached += OnVideoEnded;

        // Activar contenedor de vídeo y ocultar vinilo
        if (videoContainer != null) videoContainer.SetActive(true);
        if (vinyl != null) vinyl.Hide();

        // Color de fondo con Color2 (robustez)
        if (gm_background != null && metadata != null)
        {
            var c = gm_background.color;
            gm_background.color = new Color(metadata.Color2.r, metadata.Color2.g, metadata.Color2.b, c.a);
        }

        // 3) Asignar URL del primer candidato y preparar
        videoPlayer.url = videoPaths[currentVideoIndex];

        // (Opcional, pero recomendado): permitir saltar frames si el decodificador va justo
        videoPlayer.skipOnDrop = true;

        videoPlayer.Prepare();
        while (!videoPlayer.isPrepared)
            yield return null;

        if (autoPlay) videoPlayer.Play();
        else videoPlayer.Pause(); // preparado, listo para StartPlayback()
    }

    private void OnVideoFrameReady(VideoPlayer source, long frameIdx)
    {
        // En cuanto recibamos al menos un frame válido, marcamos listo.
        // Nota: muchos vídeos empiezan en frame 0; otros reportan 1. Nos da igual, con >=0 vale.
        if (frameIdx >= 0) videoFirstFrameReady = true;
    }

    private IEnumerator PrimeFirstFrame(bool autoPlay)
    {
        // Arrancamos para forzar la decodificación del primer frame.
        // Si autoPlay=false, luego pausaremos en 0 para el arranque sincronizado con audio.
        videoPlayer.Play();

        const float hardTimeout = 3.0f; // seguridad contra drivers raros
        float t0 = Time.realtimeSinceStartup;

        // Espera preferente por evento frameReady
        while (!videoFirstFrameReady && (Time.realtimeSinceStartup - t0) < hardTimeout)
            yield return null;

        if (!videoFirstFrameReady)
        {
            // Respaldo por polling: algunos dispositivos no emiten frameReady de forma fiable.
            // Considera "listo" cuando tengamos frame > 0 o time > 0 con texture válida durante un par de frames.
            int consecutive = 0;
            while ((Time.realtimeSinceStartup - t0) < hardTimeout && consecutive < 2)
            {
                bool ok = (videoPlayer.texture != null) &&
                          (videoPlayer.frame > 0 || videoPlayer.time > 0.01f);
                consecutive = ok ? (consecutive + 1) : 0;
                yield return null;
            }
            videoFirstFrameReady = (consecutive >= 2);
        }

        // Si no queremos autoPlay todavía, “armamos” para sync:
        // — Pausamos, reseteamos a 0 para que StartPlayback arranque todo en el mismo frame.
        if (!autoPlay)
        {
            videoPlayer.Pause();
            // Algunos backends necesitan fijar ambos:
            videoPlayer.time = 0.0;
            videoPlayer.frame = 0;
            // Forzamos un pequeño “poke” de canvas/material para evitar negros en el frame 0.
            Canvas.ForceUpdateCanvases();
            yield return null; // un frame para estabilizar
        }
        // Si autoPlay=true, lo dejamos corriendo y devolvemos control.
    }

    private void OnDestroy()
    {
        if (videoPlayer != null)
        {
            videoPlayer.loopPointReached -= OnVideoEnded;
            videoPlayer.frameReady -= OnVideoFrameReady;
        }
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