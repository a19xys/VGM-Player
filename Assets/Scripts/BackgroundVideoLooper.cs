using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

/// <summary>
/// Video de fondo ULTRA estable para UI (RawImage):
/// - Reloj externo (DSP) → inmune a hitches.
/// - Loop manual por tiempo → sin micro-parones al cruzar el final.
/// - RenderTexture persistente → sin reallocs ni glitches en Canvas.
/// - skipOnDrop activo → fluido bajo carga.
/// </summary>
[RequireComponent(typeof(VideoPlayer))]
public class BackgroundVideoLooper : MonoBehaviour
{
    [Header("Refs")]
    public VideoPlayer bgPlayer;            // Si null, se autodetecta en Awake
    public RawImage targetRawImage;         // RawImage de la UI que muestra el vídeo

    [Header("Play")]
    public bool waitForFirstFrame = true;
    public bool autoPlayOnEnable = true;

    [Header("Loop (override opcional)")]
    [Tooltip("Si > 0, fuerza la duración del loop en segundos (ignora length del contenedor).")]
    public double loopDurationOverrideSec = 0.0;

    [Header("RenderTexture")]
    [Tooltip("Si true, crea un RenderTexture persistente y lo reutiliza entre prepares.")]
    public bool usePersistentRT = true;
    [Tooltip("Formato del RT (deja Default si no sabes).")]
    public RenderTextureFormat rtFormat = RenderTextureFormat.Default;
    [Tooltip("Filtro del RT (Bilinear suele ir mejor para UI).")]
    public FilterMode rtFilter = FilterMode.Bilinear;
    [Tooltip("Si el video reporta 0x0, fuerza este tamaño provisional.")]
    public Vector2Int fallbackSize = new Vector2Int(1280, 720);

    [Header("Clock/Loop")]
    [Tooltip("FALSE = usa tiempo interno del VideoPlayer con loop nativo (recomendado para UI). TRUE = reloj externo por DSP.")]
    public bool useExternalClock = false;
    [Tooltip("Comprueba que el frame avance; si se atasca, re-empuja la reproducción.")]
    public bool enableStallWatchdog = true;
    [Tooltip("Segundos acumulados sin avanzar frame antes de intentar el 'nudge'.")]
    public float stallThresholdSeconds = 0.5f;
    [Tooltip("Intervalo del watchdog (tiempo real, no escalado).")]
    public float stallCheckIntervalSeconds = 0.25f;

    // Estado interno
    private double _clipLengthSec = 0.0;
    private double _dspStart = 0.0;
    private bool _prepared = false;
    private bool _playing = false;
    private RenderTexture _rt;              // persistente
    private int _videoW, _videoH;

    // ---- Watchdog anti-stall ----
    private Coroutine _stallCo;
    private long _lastFrameChecked = long.MinValue;
    private float _stalledAccum;

    void Reset()
    {
        bgPlayer = GetComponent<VideoPlayer>();
        if (!targetRawImage) targetRawImage = GetComponentInChildren<RawImage>(true);
    }

    void Awake()
    {
        if (!bgPlayer) bgPlayer = GetComponent<VideoPlayer>();
        if (!targetRawImage) targetRawImage = GetComponentInChildren<RawImage>(true);

        // Config de estabilidad
        bgPlayer.playOnAwake = false;
        bgPlayer.isLooping = false;                 // loop manual
        bgPlayer.skipOnDrop = true;
        bgPlayer.waitForFirstFrame = waitForFirstFrame;
        bgPlayer.timeReference = VideoTimeReference.ExternalTime;
        bgPlayer.sendFrameReadyEvents = false;

        bgPlayer.prepareCompleted -= OnPrepared;
        bgPlayer.prepareCompleted += OnPrepared;

        // Pequeña optimización UI (no interactúa)
        if (targetRawImage) targetRawImage.raycastTarget = false;
    }

    void OnEnable()
    {
        if (autoPlayOnEnable)
            StartCoroutine(PrepareAndPlayRoutine());
    }

    void OnDisable()
    {
        _playing = false;
        // NO destruimos _rt si usePersistentRT == true, para evitar reallocs al re-enable
        if (!usePersistentRT) ReleaseRT();
        StopStallWatchdog();
    }

    void OnDestroy()
    {
        bgPlayer.prepareCompleted -= OnPrepared;
        ReleaseRT(); // seguridad al salir del juego/escena
        StopStallWatchdog();
    }

    private IEnumerator PrepareAndPlayRoutine()
    {
        _prepared = false;
        _playing = false;

        if (!bgPlayer) yield break;
        if (string.IsNullOrEmpty(bgPlayer.url) && !bgPlayer.clip)
        {
            Debug.LogWarning("[BackgroundVideoLooper] Sin URL ni VideoClip asignado.");
            yield break;
        }

        // Salida a RenderTexture si pintamos en UI (RawImage)
        if (targetRawImage)
        {
            bgPlayer.renderMode = VideoRenderMode.RenderTexture;

            if (usePersistentRT && _rt == null)
            {
                var sz = (fallbackSize.x > 0 && fallbackSize.y > 0) ? fallbackSize : new Vector2Int(1280, 720);
                _rt = NewRT(sz.x, sz.y);
            }

            bgPlayer.targetTexture = _rt;
            targetRawImage.texture = _rt;
        }

        // Config base: sin audio, drop permitido
        bgPlayer.audioOutputMode = VideoAudioOutputMode.None;
        bgPlayer.skipOnDrop = true;

        // Reloj y loop
        bgPlayer.timeReference = useExternalClock ? VideoTimeReference.ExternalTime
                                                  : VideoTimeReference.InternalTime;
        bgPlayer.isLooping = !useExternalClock;   // si usamos reloj interno, que el loop sea nativo
        bgPlayer.playbackSpeed = 1f;

        // Preparar
        bgPlayer.Prepare();
        while (!bgPlayer.isPrepared) yield return null;

        // Medidas reales (width/height son uint)
        int vw = (bgPlayer.width > 0u) ? (int)bgPlayer.width : fallbackSize.x;
        int vh = (bgPlayer.height > 0u) ? (int)bgPlayer.height : fallbackSize.y;
        _videoW = Mathf.Max(2, vw);
        _videoH = Mathf.Max(2, vh);

        if (targetRawImage && usePersistentRT)
        {
            if (_rt == null || _rt.width != _videoW || _rt.height != _videoH)
            {
                ReleaseRT();
                _rt = NewRT(_videoW, _videoH);
                bgPlayer.targetTexture = _rt;
                targetRawImage.texture = _rt;
            }
        }

        if (waitForFirstFrame)
        {
            // Muestra 1er frame sin flash
            bgPlayer.Play();
            yield return null;
            bgPlayer.Pause();
        }

        _clipLengthSec = SafeClipLengthSeconds(bgPlayer);
        _prepared = true;
        _playing = true;

        // Arranque
        if (useExternalClock)
        {
            _dspStart = AudioSettings.dspTime;
            bgPlayer.externalReferenceTime = 0.0;
        }

        bgPlayer.Play();

        // Watchdog
        StopStallWatchdog();
        if (enableStallWatchdog) StartStallWatchdog();
    }

    private void OnPrepared(VideoPlayer _)
    {
        _clipLengthSec = SafeClipLengthSeconds(bgPlayer);
    }

    private double SafeClipLengthSeconds(VideoPlayer vp)
    {
        // 1) Override manual si está definido
        if (loopDurationOverrideSec > 0.0001) return loopDurationOverrideSec;

        // 2) Si hay VideoClip, su length suele ser el más fiable
        if (vp.clip != null)
        {
            double l = vp.clip.length;
            if (!double.IsNaN(l) && !double.IsInfinity(l) && l > 0.0) return l;
        }

        // 3) length del VideoPlayer (URL/file); a veces viene 0/NaN hasta después de prepare
        double len = vp.length;
        if (!double.IsNaN(len) && !double.IsInfinity(len) && len > 0.0) return len;

        // 4) Derivar por frameCount/frameRate (cuidado: frameCount es ulong)
        if (vp.frameRate > 0.1 && vp.frameCount > 0UL)
        {
            len = (double)vp.frameCount / vp.frameRate;
            if (!double.IsNaN(len) && !double.IsInfinity(len) && len > 0.0) return len;
        }

        // 5) Fallback conservador (evita quedarse en 0 => sin loop)
        return 60.0;
    }

    void Update()
    {
        if (!_prepared || !_playing || !bgPlayer) return;

        if (!useExternalClock)
        {
            // Reloj interno + loop nativo → no empujamos nada aquí.
            // El VideoPlayer gestiona el bucle por sí mismo.
            return;
        }

        // === Ruta reloj externo (opcional) ===
        double dur = (_clipLengthSec > 0.0001) ? _clipLengthSec : SafeClipLengthSeconds(bgPlayer);
        if (dur <= 0.0001) dur = 60.0;

        double tDSP = AudioSettings.dspTime;
        double elapsed = tDSP - _dspStart;

        if (elapsed >= dur)
        {
            double loops = System.Math.Floor(elapsed / dur);
            _dspStart += dur * loops;
            elapsed -= dur * loops;
        }
        else if (elapsed < 0.0)
        {
            _dspStart = tDSP;
            elapsed = 0.0;
        }

        bgPlayer.externalReferenceTime = elapsed;
    }

    /* ===================== Helpers RT ===================== */

    private RenderTexture NewRT(int w, int h)
    {
        var rt = new RenderTexture(Mathf.Max(2, w), Mathf.Max(2, h), 0, rtFormat)
        {
            useMipMap = false,
            autoGenerateMips = false,
            antiAliasing = 1,
            filterMode = rtFilter,
            wrapMode = TextureWrapMode.Clamp,
            name = $"BGVideoRT_{w}x{h}"
        };
        rt.Create();
        return rt;
    }

    private void ReleaseRT()
    {
        if (_rt != null)
        {
            if (bgPlayer) bgPlayer.targetTexture = null;
            if (targetRawImage && ReferenceEquals(targetRawImage.texture, _rt))
                targetRawImage.texture = null;
            _rt.Release();
            Destroy(_rt);
            _rt = null;
        }
    }

    /// <summary>Reinicia el reloj externo (por si quieres sincronizar con algo).</summary>
    public void RestartClock()
    {
        _dspStart = AudioSettings.dspTime;
    }

    private void StartStallWatchdog()
    {
        if (_stallCo != null) StopCoroutine(_stallCo);
        _stalledAccum = 0f;
        _lastFrameChecked = long.MinValue;
        _stallCo = StartCoroutine(WatchdogRoutine());
    }

    private void StopStallWatchdog()
    {
        if (_stallCo != null)
        {
            StopCoroutine(_stallCo);
            _stallCo = null;
        }
        _stalledAccum = 0f;
        _lastFrameChecked = long.MinValue;
    }

    private IEnumerator WatchdogRoutine()
    {
        var wait = new WaitForSecondsRealtime(Mathf.Max(0.05f, stallCheckIntervalSeconds));
        while (true)
        {
            yield return wait;

            if (!_prepared || !_playing || !bgPlayer || !bgPlayer.isPrepared)
            {
                _stalledAccum = 0f;
                continue;
            }

            long f = bgPlayer.frame; // frame es long
            if (_lastFrameChecked == long.MinValue)
            {
                _lastFrameChecked = f;
                _stalledAccum = 0f;
                continue;
            }

            if (f == _lastFrameChecked)
            {
                _stalledAccum += Mathf.Max(0.05f, stallCheckIntervalSeconds);
                if (_stalledAccum >= Mathf.Max(0.1f, stallThresholdSeconds))
                {
                    NudgePlayback();
                    _stalledAccum = 0f;
                }
            }
            else
            {
                _stalledAccum = 0f;
            }

            _lastFrameChecked = f;
        }
    }

    private void NudgePlayback()
    {
        if (!bgPlayer) return;

        if (!bgPlayer.isPlaying)
        {
            bgPlayer.Play();
            return;
        }

        // Mini-seek para romper deadlocks del decoder sin producir parpadeo visible
        double dur = (_clipLengthSec > 0.0001) ? _clipLengthSec : SafeClipLengthSeconds(bgPlayer);
        if (dur <= 0.01) dur = 60.0;

        double t = 0.0;
        try { t = bgPlayer.time; } catch { t = 0.0; }

        double newT = (t + 0.033) % dur; // ~1 frame a 30fps
        bool wasLooping = bgPlayer.isLooping;

        // No tocamos el modo de reloj; sólo empujamos el decodificador
        bgPlayer.isLooping = false;
        bgPlayer.time = newT;
        bgPlayer.Play();
        bgPlayer.isLooping = wasLooping;
    }

}