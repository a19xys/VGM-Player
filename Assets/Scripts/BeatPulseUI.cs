using System;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(RectTransform))]
public class BeatPulseUI : MonoBehaviour
{
    [Header("Audio")]
    [Tooltip("Pista musical de referencia.")]
    public AudioSource music;

    [Header("Target")]
    [Tooltip("UI a escalar (por defecto, este RectTransform).")]
    public RectTransform target;

    [Header("Pulso")]
    [Tooltip("Escala máxima durante el pulso.")]
    [Min(1f)] public float pulseScale = 1.1f;
    [Tooltip("Duración de la subida del pulso (segundos).")]
    public float pulseUpDuration = 0.08f;
    [Tooltip("Duración de la bajada del pulso (segundos).")]
    public float pulseDownDuration = 0.12f;

    [HideInInspector]
    public bool pulseAroundVisualCenter = true;

    // --- Internos ---
    private readonly List<TempoSegment> segments = new List<TempoSegment>();
    private int _segIndex = 0;
    private float _nextBeatTime = 0f;
    private bool _initialized = false;
    private Vector3 _baseScale;
    private bool _isPulsing = false;
    private float _lastSongTimeChecked = -1f;

    private Vector2 _baseAnchored;      // anchoredPosition del target al arrancar
    private static readonly Vector2 kCenterPivot = new Vector2(0.5f, 0.5f);

    [Serializable]
    public class TempoSegment
    {
        public float time; // segundos desde el inicio de la canción
        public float bpm;  // beats por minuto
    }

    void Reset()
    {
        target = GetComponent<RectTransform>();
    }

    void Awake()
    {
        if (target == null) target = GetComponent<RectTransform>();
        _baseScale = target != null ? target.localScale : Vector3.one;
        _baseAnchored = target != null ? target.anchoredPosition : Vector2.zero;
    }

    void Start()
    {
        // Ya no cargamos nada desde JSON. La configuración llega vía ConfigureFromMetadata(...)
        // Si alguien configuró 'segments' antes de Start, alineamos.
        if (segments.Count > 0)
        {
            SnapToNextBeatFromTime(GetSongTime());
            _initialized = true;
        }
    }

    void Update()
    {
        if (!_initialized || music == null) return;
        // Solo palpita con la canción sonando
        if (!music.isPlaying) return;

        float t = GetSongTime();

        // Detectar seeks / arranques bruscos y realinear al siguiente beat >= t
        const float forwardJumpEpsilon = 0.25f;
        if (_lastSongTimeChecked >= 0f)
        {
            float dt = t - _lastSongTimeChecked;
            if (dt < -0.02f || dt > forwardJumpEpsilon)
            {
                SnapToNextBeatFromTime(t);
            }
        }
        else
        {
            // Primer frame “vivo” tras empezar a reproducir
            SnapToNextBeatFromTime(t);
        }
        _lastSongTimeChecked = t;

        // Si estamos pulsando y hemos rebasado el siguiente beat, re-alinea
        if (_isPulsing)
        {
            if (t >= _nextBeatTime)
                SnapToNextBeatFromTime(t);
            return;
        }

        // Disparar pulsos cruzados este frame (normalmente 0 o 1)
        int safety = 128;
        while (t >= _nextBeatTime && safety-- > 0 && !_isPulsing)
        {
            TriggerPulse();
            AdvanceToNextBeat();
        }
    }

    // ----------------------------------------
    // Core
    // ----------------------------------------

    float GetSongTime()
    {
        if (music == null || music.clip == null) return 0f;
        // Preciso y robusto al inicio: samples / frequency
        return music.timeSamples / (float)music.clip.frequency;
    }

    float CurrentBeatDurationSec()
    {
        float bpm = Mathf.Max(1f, segments[_segIndex].bpm);
        return 60f / bpm;
    }

    float NextSegmentStartOrInfinity()
    {
        if (_segIndex + 1 < segments.Count) return segments[_segIndex + 1].time;
        return float.PositiveInfinity;
    }

    void SnapToNextBeatFromTime(float t)
    {
        if (segments == null || segments.Count == 0) return;

        _segIndex = SegmentIndexForTime(t);

        float segStart = segments[_segIndex].time;
        float beatDur = CurrentBeatDurationSec();
        float segEnd = NextSegmentStartOrInfinity();

        if (t <= segStart)
        {
            _nextBeatTime = segStart;
            return;
        }

        int n = Mathf.CeilToInt((t - segStart) / beatDur);
        _nextBeatTime = segStart + n * beatDur;

        if (_nextBeatTime >= segEnd)
        {
            _segIndex = Mathf.Min(_segIndex + 1, segments.Count - 1);
            segStart = segments[_segIndex].time;
            beatDur = CurrentBeatDurationSec();
            int n2 = Mathf.CeilToInt((t - segStart) / beatDur);
            _nextBeatTime = Mathf.Max(segStart, segStart + Mathf.Max(0, n2) * beatDur);
        }
    }

    int SegmentIndexForTime(float t)
    {
        if (segments == null || segments.Count == 0) return 0;

        // Casos extremos
        if (t < segments[0].time) return 0;
        if (t >= segments[segments.Count - 1].time) return segments.Count - 1;

        // Búsqueda lineal (listas pequeñas). Para muchas entradas, usa binaria.
        for (int s = 0; s < segments.Count; s++)
        {
            float start = segments[s].time;
            float next = (s + 1 < segments.Count) ? segments[s + 1].time : float.PositiveInfinity;
            if (t >= start && t < next) return s;
        }
        return segments.Count - 1;
    }

    void AdvanceToNextBeat()
    {
        float beatDur = CurrentBeatDurationSec();
        _nextBeatTime += beatDur;

        // Si cruzamos el inicio del siguiente segmento, re-alineamos
        float segEnd = NextSegmentStartOrInfinity();
        if (_nextBeatTime >= segEnd)
        {
            float t = GetSongTime();
            SnapToNextBeatFromTime(t);
        }
    }

    void TriggerPulse()
    {
        if (target == null) return;
        if (segments == null || segments.Count == 0) return; // guard

        _isPulsing = true;

        // Subida: 1 -> pulseScale
        LeanTween.value(gameObject, 1f, pulseScale, pulseUpDuration)
            .setEaseOutCubic()
            .setOnUpdate((float s) =>
            {
                ApplyScaleWithCompensation(s);
            })
            .setOnComplete(() =>
            {
                // Bajada: pulseScale -> 1
                LeanTween.value(gameObject, pulseScale, 1f, pulseDownDuration)
                    .setEaseOutCubic()
                    .setOnUpdate((float s) =>
                    {
                        ApplyScaleWithCompensation(s);
                    })
                    .setOnComplete(() =>
                    {
                        _isPulsing = false;
                        if (segments != null && segments.Count > 0)
                            SnapToNextBeatFromTime(GetSongTime());
                    });
            });
    }

    private void ApplyScaleWithCompensation(float scaleFactor)
    {
        if (target == null) return;

        // 1) Escala el logo
        target.localScale = _baseScale * scaleFactor;

        // 2) Compensación para simular pivot en el CENTRO del logo
        if (pulseAroundVisualCenter)
        {
            // target.rect.size está en espacio local (no incluye la scale)
            Vector2 size = target.rect.size;
            Vector2 pivot = target.pivot; // p.ej. (0,1) para esquina sup-izq del RawImage

            // Offset que "ancla" el centro visual a su sitio aunque escales
            // Fórmula: offset = (pivot - (0.5,0.5)) * size * (scale - 1)
            Vector2 offset = (pivot - kCenterPivot) * size * (scaleFactor - 1f);

            // Mantén el anclaje base + offset
            target.anchoredPosition = _baseAnchored + offset;
        }
        else
        {
            // Sin compensación (útil para comparar)
            target.anchoredPosition = _baseAnchored;
        }
    }

    // ----------------------------------------
    // API pública (config / seeks)
    // ----------------------------------------

    /// <summary>
    /// Configura el pulso desde los metadatos de la canción.
    /// </summary>
    public void ConfigureFromMetadata(AudioSource musicSource, List<SongLoader.SongMetadata.BeatItem> beats, RectTransform targetOverride = null)
    {
        music = musicSource;
        if (targetOverride != null) target = targetOverride;

        // Arranca SIEMPRE en (1,1,1) para no heredar escalas de pulsos anteriores
        ResetScaleToOne();

        if (target == null) target = GetComponent<RectTransform>();
        if (target != null)
        {
            _baseScale = target.localScale;
            _baseAnchored = target.anchoredPosition; // IMPORTANTE: la anclada puede cambiar con cada logo
        }

        // Limpia estado previo
        CancelPulseTweens();
        segments.Clear();
        _segIndex = 0;
        _nextBeatTime = 0f;
        _lastSongTimeChecked = -1f;

        // Carga beats
        if (beats != null && beats.Count > 0)
        {
            foreach (var b in beats)
                segments.Add(new TempoSegment { time = Mathf.Max(0f, b.time), bpm = Mathf.Max(1f, b.bpm) });

            segments.Sort((a, b) => a.time.CompareTo(b.time));
            SnapToNextBeatFromTime(GetSongTime());
            _initialized = true;
            enabled = true;
        }
        else
        {
            // Sin beats: desactiva y mantén escala normalizada
            _initialized = false;
            enabled = false;
            // (ResetScaleToOne ya dejó la escala en 1,1,1)
        }
    }

    /// <summary>
    /// Realinea el siguiente beat con el tiempo actual de la canción (llamar tras un seek).
    /// Dispara un pulso inmediato si estamos muy cerca del beat.
    /// </summary>
    public void RealignToSongTime()
    {
        if (!_initialized) return;

        float t = GetSongTime();
        SnapToNextBeatFromTime(t);

        // Gracia perceptiva cerca del beat
        if (!_isPulsing && Mathf.Abs(_nextBeatTime - t) <= 0.06f)
        {
            TriggerPulse();
            AdvanceToNextBeat();
        }
    }

    // ----------------------------------------
    // Lifecycle
    // ----------------------------------------

    private void CancelPulseTweens()
    {
        if (target != null) LeanTween.cancel(target.gameObject);
        _isPulsing = false;
        // Asegura estado consistente de escala y posición base
        ResetScaleToOne();
    }

    private void ResetScaleToOne()
    {
        if (target == null) return;

        // Corta cualquier tween en vuelo
        LeanTween.cancel(target.gameObject);

        // Si estábamos compensando el pivot, elimina el offset actual ANTES de normalizar
        if (pulseAroundVisualCenter)
        {
            Vector2 size = target.rect.size;
            Vector2 pivot = target.pivot;

            // Factor de escala actual relativo a la base
            float baseX = (_baseScale.x == 0f) ? 1f : _baseScale.x;
            float s = target.localScale.x / baseX; // usamos X pues escalas son uniformes

            // offset aplicado en ApplyScaleWithCompensation(s)
            Vector2 appliedOffset = (pivot - kCenterPivot) * size * (s - 1f);

            // Revertirlo
            target.anchoredPosition -= appliedOffset;
        }

        // Normaliza escala y fija nuevas bases coherentes
        target.localScale = Vector3.one;
        _baseScale = Vector3.one;
        _baseAnchored = target.anchoredPosition;

        _isPulsing = false;
    }

    void OnDisable()
    {
        ResetScaleToOne();
    }

    void OnEnable()
    {
        ResetScaleToOne();
    }

}