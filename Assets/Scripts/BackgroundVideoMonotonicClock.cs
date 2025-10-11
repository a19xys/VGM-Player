using System;
using UnityEngine;
using UnityEngine.Video;

/// <summary>
/// Reloj externo MONOTÓNICO para un VideoPlayer de fondo:
/// - Nunca retrocede frames (elimina el “temblor”).
/// - Limita el avance por frame (cap) para suavizar los spikes.
/// - Hace wrap a 0 de forma limpia cuando alcanza el final.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(VideoPlayer))]
public class BackgroundVideoMonotonicClock : MonoBehaviour
{
    [Header("Refs")]
    public VideoPlayer player; // opcional; si null, se autoasigna

    [Header("Ajustes")]
    [Tooltip("Avance máximo por frame (seg). 0.03≈1 frame a 30fps, 0.016≈1 frame a 60fps.")]
    public double maxAdvancePerFrame = 0.033; // ~1 frame a 30fps

    [Tooltip("Si true, aplica opciones seguras para un fondo en bucle.")]
    public bool enforceSafePlayerOptions = true;

    private double _startUnscaled;   // unscaled time de referencia
    private double _smoothedTime;    // tiempo que enviamos (monotónico, [0..duration))
    private double _duration;        // duración del clip
    private bool _ready;

    private void Awake()
    {
        if (!player) player = GetComponent<VideoPlayer>();
    }

    private void OnEnable()
    {
        if (!player) return;

        if (enforceSafePlayerOptions)
        {
            player.audioOutputMode = VideoAudioOutputMode.None;
            player.isLooping = true;
            player.skipOnDrop = true;
            player.waitForFirstFrame = true;
        }

        // Conducido por reloj externo
        player.timeReference = VideoTimeReference.ExternalTime;
        player.prepareCompleted += OnPrepared;

        if (player.isPrepared) Bootstrap();
        else player.Prepare();
    }

    private void OnDisable()
    {
        if (player) player.prepareCompleted -= OnPrepared;
        _ready = false;
    }

    private void OnPrepared(VideoPlayer _)
    {
        Bootstrap();
    }

    private void Bootstrap()
    {
        if (!player || !player.isPrepared) return;

        _duration = Math.Max(0.0001, player.length);
        _startUnscaled = Time.unscaledTimeAsDouble;
        _smoothedTime = 0.0;

        if (!player.isPlaying) player.Play();
        _ready = true;
    }

    private void Update()
    {
        if (!_ready || !player || !player.isPrepared) return;

        // Tiempo objetivo desde que se preparó (mod duración)
        double tTarget = Time.unscaledTimeAsDouble - _startUnscaled;
        if (_duration > 0.0001)
        {
            tTarget %= _duration;
            if (tTarget < 0) tTarget += _duration;
        }

        // Delta hacia adelante únicamente (0..duration)
        double deltaForward = tTarget - _smoothedTime;
        if (_duration > 0.0001)
        {
            // reduce a rango [0, duration)
            deltaForward %= _duration;
            if (deltaForward < 0) deltaForward += _duration;
        }
        // IMPORTANTE: jamás retrocedemos (deltaForward nunca negativo).

        // Limita avance por frame para evitar salto brusco tras un spike
        double stepCap = Math.Max(0.001, maxAdvancePerFrame); // seguridad
        double step = (deltaForward > stepCap) ? stepCap : deltaForward;

        _smoothedTime += step;

        // Wrap limpio
        if (_duration > 0.0001)
        {
            if (_smoothedTime >= _duration) _smoothedTime -= _duration;
        }

        // Aplica al VideoPlayer
        player.externalReferenceTime = _smoothedTime;
    }
}
