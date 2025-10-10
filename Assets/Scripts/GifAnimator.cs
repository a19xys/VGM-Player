using UnityEngine;
using UnityEngine.UI;

public class GifAnimator : MonoBehaviour
{
    [Header("Target")]
    public RawImage gifImage;           // Si no lo asignas, usa el RawImage del mismo GO

    [Header("Frames")]
    public Texture[] frames;
    public float frameRate = 10f;       // FPS (si <=0, usa 10 FPS por defecto)

    [Header("Control")]
    [Tooltip("Si true, el prefab decide (SetAnimating).")]
    public bool controlledExternally = true;
    [Tooltip("Si true, además se pausa/reanuda siguiendo al MusicPlayer global (sin asignarlo).")]
    public bool followMusicPlayer = true;

    [Header("Tint")]
    [Tooltip("Si true, tiñe el gif con el Color1 global de la canción actual.")]
    public bool tintWithColor = true;
    [Tooltip("True = usa Color1 (primario). False = usa Color2 (secundario).")]
    public bool usePrimaryColor = true;

    // --- Estado interno ---
    private bool isAnimating = false;   // control externo
    private bool globalIsPlaying = true;
    private int currentFrame = 0;
    private float timer = 0f;
    private float originalAlpha = 1f;

    void Awake()
    {
        if (!gifImage) gifImage = GetComponent<RawImage>();
        if (gifImage) originalAlpha = gifImage.color.a;
    }

    void OnEnable()
    {
        timer = 0f;
        currentFrame = 0;
        if (frames != null && frames.Length > 0 && gifImage)
            gifImage.texture = frames[0];

        if (followMusicPlayer)
            MusicPlayer.OnGlobalPlaybackStateChanged += HandleGlobalPlaybackChanged;

        if (tintWithColor)
        {
            SongLoader.OnGlobalThemeChanged += HandleGlobalThemeChanged;
            // Aplicar color actual si ya hubo un tema emitido
            ApplyThemeTint(SongLoader.LastColor1, SongLoader.LastColor2);
        }
    }

    void OnDisable()
    {
        if (followMusicPlayer)
            MusicPlayer.OnGlobalPlaybackStateChanged -= HandleGlobalPlaybackChanged;
        if (tintWithColor)
            SongLoader.OnGlobalThemeChanged -= HandleGlobalThemeChanged;
    }

    void Update()
    {
        if (frames == null || frames.Length == 0 || gifImage == null) return;

        // ¿Debemos animar este frame?
        bool anim = controlledExternally ? isAnimating : true;
        if (followMusicPlayer) anim = anim && globalIsPlaying;
        if (!anim) return;

        float interval = (frameRate <= 0f) ? 0.1f : (1f / frameRate);
        timer += Time.deltaTime;
        if (timer >= interval)
        {
            timer -= interval;
            currentFrame = (currentFrame + 1) % frames.Length;
            gifImage.texture = frames[currentFrame];
        }
    }

    private void HandleGlobalPlaybackChanged(bool isPlaying)
    {
        globalIsPlaying = isPlaying;
        if (!globalIsPlaying && frames != null && frames.Length > 0 && gifImage != null)
        {
            // Congelar en el frame actual (o vuelve al 0 si prefieres):
            // currentFrame = 0;
            gifImage.texture = frames[currentFrame];
        }
    }

    private void HandleGlobalThemeChanged(Color c1, Color c2)
    {
        if (!tintWithColor) return;
        ApplyThemeTint(c1, c2);
    }

    private void ApplyThemeTint(Color c1, Color c2)
    {
        if (!gifImage) return;
        // Elegimos Color1 o Color2 y preservamos la alpha original del RawImage
        Color src = usePrimaryColor ? c1 : c2;
        gifImage.color = new Color(src.r, src.g, src.b, originalAlpha);
    }

    // === API pública para el prefab ===
    public void SetAnimating(bool on)
    {
        isAnimating = on;
        if (!on && frames != null && frames.Length > 0 && gifImage != null)
        {
            currentFrame = 0;
            gifImage.texture = frames[0]; // frame estático cuando no anima por estado del item
        }
    }

    public void Play() => SetAnimating(true);
    public void Pause() => SetAnimating(false);
}