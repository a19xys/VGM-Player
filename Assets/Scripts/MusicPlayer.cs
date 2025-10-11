using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Reproductor: gestiona Play/Pause, barra, tiempos, modos y navegación.
/// </summary>
public class MusicPlayer : MonoBehaviour, IDragHandler, IBeginDragHandler, IEndDragHandler, IPointerDownHandler {
    
    [Header("Refs")]
    public SongLoader songLoader;
    public TrackQueueManager queueManager;
    public SongTransitionController transition; // Controlador de transición (siguiente/anterior con animación)
    public SlidingMenuController selectionMenu;

    [Header("Audio / UI")]
    public AudioSource audioSource;            // Fuente de audio
    public Slider progressBar;                 // Barra de progreso
    public GameObject grip;                    // Agarre de la barra
    public TextMeshProUGUI currentTimeText;    // Tiempo actual
    public TextMeshProUGUI durationText;       // Duración total (o cuenta atrás)

    [Header("Playback")]
    public float skiplapse = 5f;

    [Header("Buttons / Icons")]
    public RawImage repeatButton;              // Botón Repeat
    public RawImage shuffleButton;             // Botón Shuffle
    public RawImage playButton;                // Botón Play/Pause
    public Texture playTexture;                // Icono Play
    public Texture pauseTexture;               // Icono Pause
    public Texture repeatAllTexture;           // Icono Repeat All
    public Texture repeatOneTexture;           // Icono Repeat One

    public static event System.Action<bool> OnGlobalPlaybackStateChanged;

    // Estado
    private float dragNormalizedPosition;      // Posición normalizada durante drag
    private bool isDragging = false;           // ¿Se está arrastrando el agarre?
    private bool showCountdown = false;        // ¿Mostrar cuenta atrás en durationText?
    private readonly Color inactiveColor = new Color(171 / 255f, 171 / 255f, 171 / 255f);
    private bool lastPlaying;                  // Para sincronizar vinilo
    private const float LoopEdgeEpsilon = 0.005f; // ~5 ms para disparar el salto con suavidad

    /* ============================= Ciclo de vida ============================= */

    void Start()
    {
        // Configuración inicial de la barra
        if (progressBar != null) { progressBar.minValue = 0; progressBar.maxValue = 1; }

        // No hacemos loop salvo RepeatOne (lo ajustamos en RefreshModeIndicators)
        if (audioSource != null) { audioSource.loop = false; }

        // Suscripciones a eventos
        if (queueManager != null) queueManager.OnPlayModeChanged += HandlePlayModeChanged;
        if (songLoader != null)
        {
            songLoader.OnThemeChanged += HandleThemeChanged;   // Colores botones según tema
            songLoader.OnAudioPrepared += HandleAudioPrepared;  // ⬅️ Duración/progreso de la nueva pista
        }

        // Estado visual inicial
        RefreshModeIndicators();
        RefreshPlayIcon();

        // Si arrancamos con clip ya cargado (firstSongId), mostrar duración EFECTIVA
        if (audioSource != null && audioSource.clip != null && durationText != null)
        {
            float dur = EffectiveDurationSec();
            durationText.text = showCountdown ? ("-" + FormatTime(dur)) : FormatTime(dur);
        }

        // Inicializar estado de vinilo
        lastPlaying = (audioSource != null && audioSource.isPlaying);
        UpdateVinylSpin();
        OnGlobalPlaybackStateChanged?.Invoke(lastPlaying);
    }

    private void OnDestroy()
    {
        if (queueManager != null) queueManager.OnPlayModeChanged -= HandlePlayModeChanged;
        if (songLoader != null)
        {
            songLoader.OnThemeChanged -= HandleThemeChanged;
            songLoader.OnAudioPrepared -= HandleAudioPrepared;
        }
    }

    /* ============================= Update ============================= */

    void Update()
    {
        // Icono Play/Pause siempre actualizado
        RefreshPlayIcon();

        // Actualizar barra y tiempos mientras suena y no se arrastra
        if (!isDragging && audioSource != null && audioSource.clip != null && audioSource.isPlaying)
        {
            UpdateProgressBar();
            if (currentTimeText != null)
                currentTimeText.text = FormatTime(SafeAudioTime());
            if (showCountdown && durationText != null && audioSource.clip != null)
            {
                float dur = EffectiveDurationSec();
                float remainingTime = Mathf.Max(0f, dur - audioSource.time);
                durationText.text = "-" + FormatTime(remainingTime);
            }
        }

        // ===== LoopOne con segmento Loop {start,end} desde metadatos =====
        if (queueManager != null && queueManager.playMode == PlayMode.RepeatOne &&
            audioSource != null && audioSource.clip != null && audioSource.isPlaying)
        {
            if (TryGetLoopRangeSeconds(out float loopStart, out float loopEnd))
            {
                if (audioSource.time >= loopEnd - LoopEdgeEpsilon)
                {
                    // Salto suave al inicio del segmento (sin transición)
                    JumpTime(loopStart);
                    return;
                }
            }
        }

        // Fin de pista
        if (audioSource != null && audioSource.clip != null &&
            !audioSource.isPlaying && audioSource.time >= audioSource.clip.length - 0.001f)
        {
            var mode = queueManager != null ? queueManager.playMode : PlayMode.Normal;
            if (mode == PlayMode.RepeatOne)
            {
                // LOOP ONE: reanudar la misma sin transición (audio+vídeo sincronizados)
                RestartCurrentNoTransition();
            }
            else if (mode == PlayMode.Normal && queueManager != null && queueManager.IsLastIndex())
            {
                // NORMAL + última: PAUSAR y volver a 0 (no Stop) para permitir seeks correctos
                PauseAndResetToStart();
            }
            else if (transition != null)
            {
                // Otros modos -> transición a la siguiente
                transition.GoToNext();
            }
            return;
        }

        // Bloquear hotkeys si el menú de canciones está abierto
        if (selectionMenu != null && selectionMenu.IsHidden) return;

        // Hotkeys básicas
        if (Input.GetKeyDown(KeyCode.Space)) { TogglePlayPause(); }

        // ====== Saltos 5 / 10 / 30 según modificadores ======
        bool ctrl = Input.GetKey(KeyCode.LeftControl);
        bool altL = Input.GetKey(KeyCode.LeftAlt); // Alt Izq específicamente
        float skipSeconds = 10f;                    // flechas solas
        if (ctrl) skipSeconds = 30f;          // Ctrl + flechas
        else if (altL) skipSeconds = 5f;           // Alt Izq + flechas
        if (Input.GetKeyDown(KeyCode.LeftArrow)) { SkipTime(-skipSeconds); }
        if (Input.GetKeyDown(KeyCode.RightArrow)) { SkipTime(skipSeconds); }
        // ============================================================

        // K -> pausa/reanuda audio + vídeo a la vez
        if (Input.GetKeyDown(KeyCode.K)) { TogglePlayPauseAudioAndVideo(); }

        // Navegación de pistas vía transición
        if (Input.GetKeyDown(KeyCode.P)) { OnClickPrevious(); }
        if (Input.GetKeyDown(KeyCode.N)) { OnClickNext(); }

        // Modos
        if (Input.GetKeyDown(KeyCode.S)) { ToggleShuffle(); }
        if (Input.GetKeyDown(KeyCode.L)) { ToggleRepeat(); }

        // Si Play/Pause cambia desde fuera, sincroniza vinilo
        if (audioSource != null && audioSource.isPlaying != lastPlaying)
        {
            lastPlaying = audioSource.isPlaying;
            UpdateVinylSpin();
            OnGlobalPlaybackStateChanged?.Invoke(lastPlaying);
        }
    }

    /* ============================= Drag / Seek ============================= */

    public void OnBeginDrag(PointerEventData eventData) {
        if (InputLock.IsLocked || grip == null) return;

        RectTransform gripRect = grip.GetComponent<RectTransform>();
        if (RectTransformUtility.RectangleContainsScreenPoint(gripRect, eventData.position, eventData.pressEventCamera)) {
            isDragging = true;

            // Previsualiza inmediatamente con la posición actual del slider
            if (progressBar != null) {
                dragNormalizedPosition = progressBar.value;
                UpdateTimePreviewUI(dragNormalizedPosition);
            }
        }
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (InputLock.IsLocked || !isDragging || progressBar == null) return;

        RectTransform progressBarRect = progressBar.GetComponent<RectTransform>();
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(progressBarRect, eventData.position, eventData.pressEventCamera, out Vector2 localPoint))
        {
            dragNormalizedPosition = Mathf.Clamp01((localPoint.x / progressBarRect.rect.width) + 0.5f);
            progressBar.value = dragNormalizedPosition;
            UpdateGripPosition(dragNormalizedPosition);

            // Vista previa mientras se arrastra
            UpdateTimePreviewUI(dragNormalizedPosition);
        }
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (!isDragging) return;
        isDragging = false;
        if (InputLock.IsLocked || audioSource == null || audioSource.clip == null) return;

        // Calcula el tiempo objetivo dentro de [0 .. duración efectiva]
        float candidate = NormalizedToTime(dragNormalizedPosition);
        HandleSeekReleaseTo(candidate);
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (InputLock.IsLocked || progressBar == null || audioSource == null || audioSource.clip == null) return;

        RectTransform progressBarRect = progressBar.GetComponent<RectTransform>();
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(progressBarRect, eventData.position, eventData.pressEventCamera, out Vector2 localPoint))
        {
            float normalizedPosition = Mathf.Clamp01((localPoint.x / progressBarRect.rect.width) + 0.5f);
            progressBar.value = normalizedPosition;
            UpdateGripPosition(normalizedPosition);

            // Tiempo candidato dentro de [0 .. duración efectiva]
            float candidate = NormalizedToTime(normalizedPosition);
            HandleSeekReleaseTo(candidate);
        }
    }

    private void UpdateTimePreviewUI(float normalized)
    {
        if (audioSource == null || audioSource.clip == null) return;

        float clipLen = EffectiveDurationSec();
        float previewSec = Mathf.Clamp01(normalized) * clipLen;

        if (currentTimeText != null)
            currentTimeText.text = FormatTime(previewSec);

        if (durationText != null && showCountdown)
        {
            float remaining = Mathf.Max(0f, clipLen - previewSec);
            durationText.text = "-" + FormatTime(remaining);
        }
    }

    /* ============================= Controles ============================= */

    public void TogglePlayPause()
    {
        if (audioSource == null) return;

        bool videoActive = songLoader != null && songLoader.videoContainer != null && songLoader.videoContainer.activeSelf;
        var vp = (songLoader != null) ? songLoader.videoPlayer : null;
        bool videoReady = videoActive && vp != null && vp.isPrepared;
        bool videoPlaying = videoReady && vp.isPlaying;

        if (audioSource.isPlaying)
        {
            audioSource.Pause();
            pendingPausedTime = audioSource.time; // guarda el tiempo real al pausar
        }
        else
        {
            // Aplica el tiempo pendiente antes de reproducir
            if (pendingPausedTime >= 0f) audioSource.time = pendingPausedTime;

            if (videoReady && !videoPlaying)
            {
                songLoader.StartPlayback(); // reanuda audio+vídeo en el mismo frame
            }
            else
            {
                if (audioSource.clip != null && audioSource.time >= audioSource.clip.length - 0.0001f)
                    audioSource.time = 0f;
                audioSource.Play();
            }
            ClearPendingTime();
        }

        RefreshPlayIcon();
        UpdateLastPlayingAndBroadcast();
        UpdateVinylSpin();
    }

    public void TogglePlayPauseAudioAndVideo()
    {
        if (audioSource == null) return;

        bool videoActive = songLoader != null && songLoader.videoContainer != null && songLoader.videoContainer.activeSelf;
        var vp = (songLoader != null) ? songLoader.videoPlayer : null;
        bool videoPlaying = videoActive && vp != null && vp.isPrepared && vp.isPlaying;
        bool audioPlaying = audioSource.isPlaying;

        if (audioPlaying || videoPlaying)
        {
            audioSource.Pause();
            pendingPausedTime = audioSource.time; // guarda donde nos quedamos
            if (videoActive && vp != null) vp.Pause();
            RefreshPlayIcon();
            UpdateLastPlayingAndBroadcast();
            UpdateVinylSpin();
            return;
        }

        // Ambos estaban en pausa -> reanudar sincronizados
        if (pendingPausedTime >= 0f) audioSource.time = pendingPausedTime;
        if (songLoader != null) songLoader.StartPlayback();
        else audioSource.Play();
        ClearPendingTime();

        RefreshPlayIcon();
        UpdateLastPlayingAndBroadcast();
        UpdateVinylSpin();
    }

    public void ToggleShuffle()
    {
        if (InputLock.IsLocked || queueManager == null) return;

        var newMode = (queueManager.playMode == PlayMode.Shuffle) ? PlayMode.Normal : PlayMode.Shuffle;
        queueManager.SetMode(newMode);

        // NO tocamos audioSource.loop aquí: sólo depende de RepeatOne (se gestiona en RefreshModeIndicators)
        RefreshModeIndicators();
    }

    public void ToggleRepeat()
    {
        if (InputLock.IsLocked || queueManager == null) return;

        PlayMode next = queueManager.playMode switch
        {
            PlayMode.Normal => PlayMode.RepeatAll,
            PlayMode.Shuffle => PlayMode.RepeatAll,
            PlayMode.RepeatAll => PlayMode.RepeatOne,
            PlayMode.RepeatOne => PlayMode.Normal,
            _ => PlayMode.Normal
        };

        queueManager.SetMode(next);
        RefreshModeIndicators(); // Ajusta color de botones y loop (RepeatOne)
    }

    public void OnDurationTextClick()
    {
        if (InputLock.IsLocked || audioSource == null || audioSource.clip == null || durationText == null) return;

        showCountdown = !showCountdown;
        float dur = EffectiveDurationSec();

        if (showCountdown)
        {
            float remainingTime = Mathf.Max(0f, dur - audioSource.time);
            durationText.text = "-" + FormatTime(remainingTime);
        }
        else
        {
            durationText.text = FormatTime(dur);
        }
    }

    /* ============================= Helpers UI ============================= */

    private void UpdateProgressBar()
    {
        if (progressBar == null || audioSource == null || audioSource.clip == null) return;
        float dur = Mathf.Max(0.0001f, EffectiveDurationSec());
        float t = GetDisplayedTime();
        float norm = Mathf.Clamp01(t / dur);
        progressBar.value = norm;
        UpdateGripPosition(progressBar.value);
    }

    private void UpdateGripPosition(float normalizedPosition)
    {
        if (progressBar == null || grip == null) return;

        RectTransform progressBarRect = progressBar.GetComponent<RectTransform>();
        float gripX = Mathf.Lerp(progressBarRect.rect.xMin, progressBarRect.rect.xMax, normalizedPosition);
        Vector3 localPosition = grip.transform.localPosition;
        localPosition.x = gripX;
        grip.transform.localPosition = localPosition;
    }

    // Tiempo seguro y más preciso al inicio
    private float SafeAudioTime()
    {
        if (audioSource == null || audioSource.clip == null) return 0f;
        return audioSource.timeSamples / (float)audioSource.clip.frequency;
    }

    private void SkipTime(float seconds)
    {
        if (InputLock.IsLocked || audioSource == null || audioSource.clip == null) return;

        float baseTime = GetDisplayedTime();   // usa el tiempo mostrado si estamos en pausa
        float target = baseTime + seconds;

        // Si no es modo Normal+última pista, puedes permitir transición; si no, clamp
        if (target >= audioSource.clip.length - 0.001f)
        {
            if (queueManager != null && queueManager.playMode != PlayMode.Normal)
            {
                if (transition != null) { transition.GoToNext(); return; }
                target = 0f;
            }
        }
        else if (target < 0f) target = 0f;

        JumpTime(target);
        // Mantiene el estado (si estaba en pausa, sigue en pausa; si estaba reproduciendo, sigue reproduciendo)
    }

    public void JumpTime(float newTime)
    {
        if (audioSource == null || audioSource.clip == null) return;

        SetPendingTime(newTime, true);

        UpdateProgressBar();

        if (currentTimeText != null) currentTimeText.text = FormatTime(GetDisplayedTime());

        if (showCountdown && durationText != null)
        {
            float dur = EffectiveDurationSec();
            float remainingTime = Mathf.Max(0f, dur - GetDisplayedTime());
            durationText.text = "-" + FormatTime(remainingTime);
        }

        UpdateVinylSpin();

        if (songLoader != null && songLoader.beatPulseUI != null)
            songLoader.beatPulseUI.RealignToSongTime();
    }

    private string FormatTime(float time)
    {
        int minutes = Mathf.FloorToInt(time / 60f);
        int seconds = Mathf.FloorToInt(time % 60f);
        return $"{minutes}:{seconds:00}";
    }

    private void RefreshPlayIcon()
    {
        if (playButton == null || audioSource == null) return;
        playButton.texture = audioSource.isPlaying ? pauseTexture : playTexture;
    }

    public void RefreshModeIndicators()
    {
        if (queueManager == null || songLoader == null) return;
        if (repeatButton == null || shuffleButton == null) return;

        var mode = queueManager.playMode;
        bool isShuffle = (mode == PlayMode.Shuffle);
        bool isRepeatOne = (mode == PlayMode.RepeatOne);
        bool isRepeatAll = (mode == PlayMode.RepeatAll);

        // Colores (secundario cuando está activo; gris cuando inactivo)
        Color c2 = songLoader.metadata != null ? songLoader.metadata.Color2 : Color.white;
        shuffleButton.color = isShuffle ? c2 : inactiveColor;
        repeatButton.color = (isRepeatOne || isRepeatAll) ? c2 : inactiveColor;

        // Icono de repeat: RepeatOne vs RepeatAll
        if (isRepeatOne && repeatOneTexture != null)
            repeatButton.texture = repeatOneTexture;
        else if ((isRepeatAll || mode == PlayMode.Normal || mode == PlayMode.Shuffle) && repeatAllTexture != null)
            repeatButton.texture = repeatAllTexture; // icono base de "loop"

        // Sincroniza InteractiveButton (si lo usas)
        var ibS = shuffleButton.GetComponent<InteractiveButton>();
        var ibR = repeatButton.GetComponent<InteractiveButton>();
        if (ibS) ibS.originalColor = shuffleButton.color;
        if (ibR) ibR.originalColor = repeatButton.color;

        // Importante: desactivar SIEMPRE el loop nativo del AudioSource
        // (gestionar el relanzamiento al final de pista en Update → "Fin de pista")
        if (audioSource) audioSource.loop = false;

        // Refrescar duración/barra en caso de que cambie el modo (LoopOne con Loop acorta ‘end’)
        RefreshLoopAwareUI();
    }

    private void RestartCurrentNoTransition()
    {
        if (audioSource == null) return;

        // Reiniciar tiempo
        audioSource.time = 0f;

        // Arranque sincronizado (audio + vídeo o vinilo) en el MISMO frame
        if (songLoader != null)
        {
            songLoader.StartPlayback();

            // 🔧 Asegurar estado visual coherente EN ESTE MISMO FRAME
            lastPlaying = (audioSource != null && audioSource.isPlaying);
            RefreshPlayIcon();
            UpdateVinylSpin();
        }
        else
        {
            audioSource.Play();
            lastPlaying = true;
            RefreshPlayIcon();
            UpdateVinylSpin();
        }

        // Refrescar UI de tiempos/progreso
        UpdateProgressBar();
        if (currentTimeText != null) currentTimeText.text = "0:00";
        if (durationText != null && audioSource.clip != null)
        {
            if (showCountdown)
            {
                float remaining = EffectiveDurationSec() - audioSource.time; // usa duración efectiva si la tienes
                durationText.text = "-" + FormatTime(Mathf.Max(0f, remaining));
            }
            else
            {
                // Mostrar duración efectiva si procede (RepeatOne+Loop), si no, la real
                durationText.text = FormatTime(EffectiveDurationSec());
            }
        }

        // Realinear el pulso, si existe
        if (songLoader != null && songLoader.beatPulseUI != null)
            songLoader.beatPulseUI.RealignToSongTime();
    }

    private void HandleSeekReleaseTo(float newTime)
    {
        if (audioSource == null || audioSource.clip == null) return;

        bool wasPlaying = audioSource.isPlaying;

        SetPendingTime(newTime, true);

        // Mantener el estado previo
        if (wasPlaying)
        {
            if (!audioSource.isPlaying) audioSource.Play();
            ClearPendingTime(); // a partir de ahora el motor avanza el tiempo
        }

        // Refresco UI inmediato
        UpdateProgressBar();
        if (currentTimeText != null) currentTimeText.text = FormatTime(GetDisplayedTime());
        if (showCountdown && durationText != null)
        {
            float dur = EffectiveDurationSec();
            float remaining = Mathf.Max(0f, dur - GetDisplayedTime());
            durationText.text = "-" + FormatTime(remaining);
        }

        lastPlaying = audioSource.isPlaying;
        RefreshPlayIcon();
        UpdateVinylSpin();

        if (songLoader != null && songLoader.beatPulseUI != null)
            songLoader.beatPulseUI.RealignToSongTime();
    }

    /* ====================== Botones UI (Next/Prev) ====================== */

    public void OnClickNext()
    {
        if (InputLock.IsLocked) return;

        // LOOP ONE: Next → reiniciar la MISMA pista SIN transición
        if (queueManager != null && queueManager.playMode == PlayMode.RepeatOne)
        {
            RestartCurrentNoTransition();
            return;
        }

        // Caso especial que ya tenías para Normal+última lo mantienes aparte
        if (queueManager != null && queueManager.playMode == PlayMode.Normal && queueManager.IsLastIndex())
        {
            // Reproduce la misma desde 0 sin transición (tu regla de "no hay siguiente")
            RestartCurrentNoTransition();
            return;
        }

        // Resto de modos → transición normal
        if (transition != null) transition.GoToNext();
        else if (queueManager != null) queueManager.Next();
    }

    public void OnClickPrevious()
    {
        if (InputLock.IsLocked) return;

        // LOOP ONE: Previous → reiniciar la MISMA pista SIN transición
        if (queueManager != null && queueManager.playMode == PlayMode.RepeatOne)
        {
            RestartCurrentNoTransition();
            return;
        }

        // NORMAL + PRIMERA CANCIÓN: siempre reiniciar sin transición (da igual el tiempo transcurrido)
        if (queueManager != null &&
            queueManager.playMode == PlayMode.Normal &&
            queueManager.IsFirstIndex())
        {
            RestartCurrentNoTransition();
            return;
        }

        // Resto de casos en Normal: si >3s, vuelve a 0 sin transición
        if (audioSource != null && audioSource.time > 3f)
        {
            JumpTime(0f);
            if (songLoader != null && audioSource.isPlaying) songLoader.StartPlayback();
            return;
        }

        // Otros modos / situaciones: transición a la anterior
        if (transition != null) transition.GoToPrevious();
        else if (queueManager != null) queueManager.Previous();
    }

    /* ====================== Event Handlers ====================== */

    private void HandlePlayModeChanged(PlayMode _)
    {
        RefreshModeIndicators();

        if (queueManager == null || songLoader == null || audioSource == null || audioSource.clip == null)
            return;

        // Sólo actuamos al entrar/estar en RepeatOne con loop válido
        if (queueManager.playMode != PlayMode.RepeatOne)
            return;

        if (!TryGetValidLoopSegment_MusicPlayer(out float loopStart, out float loopEnd))
            return;

        // Regla: si estamos DESPUÉS de end y cambiamos a RepeatOne → reinicio a 0 sin transición.
        // Si estamos ANTES de start -> NO tocar.
        const float eps = 0.01f;
        float t = audioSource.time;
        if (t > loopEnd - eps)
        {
            // JumpTime preserva el estado (si estaba pausado, sigue pausado; si estaba reproduciendo, continúa)
            JumpTime(0f);
        }
        // Caso "entre start y end": no se toca nada.
    }

    private void HandleThemeChanged(Color c1, Color c2)
    {
        // Los botones activos deben usar el nuevo Color2
        RefreshModeIndicators();
    }

    /// <summary>
    /// Llega cuando SongLoader ha preparado el NUEVO AudioClip.
    /// Resetea progreso/tiempos y actualiza la duración mostrada (soluciona PROBLEMA 6).
    /// </summary>
    private void HandleAudioPrepared(AudioClip clip)
    {
        if (clip == null) return;

        pendingPausedTime = 0f; // nueva pista: el tiempo mostrado/seek parte de 0

        if (progressBar) { progressBar.value = 0f; UpdateGripPosition(0f); }
        if (currentTimeText) currentTimeText.text = "0:00";

        if (durationText)
        {
            float dur = EffectiveDurationSec();
            durationText.text = showCountdown ? ("-" + FormatTime(dur)) : FormatTime(dur);
        }
    }

    // Lee y valida el Loop del metadata actual. Devuelve segundos absolutos.
    private bool TryGetValidLoop(out float startSec, out float endSec)
    {
        startSec = 0f; endSec = 0f;
        if (songLoader == null || songLoader.metadata == null || songLoader.metadata.Loop == null) return false;
        if (audioSource == null || audioSource.clip == null) return false;

        string s = songLoader.metadata.Loop.start;
        string e = songLoader.metadata.Loop.end;
        if (string.IsNullOrWhiteSpace(s) || string.IsNullOrWhiteSpace(e)) return false;

        if (!TryParseTimestamp(s, out startSec)) return false;
        if (!TryParseTimestamp(e, out endSec)) return false;

        // Validación básica contra el clip
        float len = audioSource.clip.length;
        if (startSec < 0f || endSec <= 0f) return false;
        if (startSec >= endSec) return false;
        if (startSec >= len) return false;
        if (endSec > len + 0.0001f) endSec = len; // clamp suave por seguridad

        return true;
    }

    // "MM:SS(.fff)" o "M:SS(.ff)" → segundos
    private bool TryParseTimestamp(string txt, out float seconds)
    {
        seconds = 0f;
        if (string.IsNullOrWhiteSpace(txt)) return false;
        txt = txt.Trim().Replace(',', '.');

        // Permite "SS(.fff)" sin minutos
        int colon = txt.IndexOf(':');
        if (colon < 0)
        {
            if (float.TryParse(txt, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float sOnly))
            {
                seconds = Mathf.Max(0f, sOnly);
                return true;
            }
            return false;
        }

        string mStr = txt.Substring(0, colon);
        string sStr = txt.Substring(colon + 1);

        if (!int.TryParse(mStr, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int m)) return false;
        if (!float.TryParse(sStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float s)) return false;

        seconds = Mathf.Max(0f, m * 60f + s);
        return true;
    }

    private void UpdateLastPlayingAndBroadcast()
    {
        bool cur = (audioSource != null && audioSource.isPlaying);
        lastPlaying = cur;
        OnGlobalPlaybackStateChanged?.Invoke(cur); // ← notifica a los GIFs
    }

    private void PauseAndResetToStart()
    {
        if (audioSource == null || audioSource.clip == null) return;

        audioSource.Pause();
        pendingPausedTime = 0f;          // la UI y los siguientes seeks parten de 0
        audioSource.time = 0f;           // intentamos reflejarlo también en el engine

        // UI coherente
        UpdateProgressBar();
        if (currentTimeText != null) currentTimeText.text = "0:00";

        if (durationText != null)
        {
            float dur = EffectiveDurationSec();
            if (showCountdown)
                durationText.text = "-" + FormatTime(Mathf.Max(0f, dur - GetDisplayedTime()));
            else
                durationText.text = FormatTime(dur);
        }

        RefreshPlayIcon();
        UpdateVinylSpin();
        UpdateLastPlayingAndBroadcast();
        if (songLoader != null && songLoader.beatPulseUI != null)
            songLoader.beatPulseUI.RealignToSongTime();
    }

    /* ====================== Vinilo: sync con Play/Pause y vídeo ====================== */

    private void UpdateVinylSpin()
    {
        if (songLoader == null || songLoader.vinyl == null) return;

        bool isPlaying = (audioSource != null && audioSource.isPlaying);
        bool videoActive = (songLoader.videoContainer != null && songLoader.videoContainer.activeSelf);

        // Sólo gira si NO hay vídeo y el audio está reproduciendo
        songLoader.vinyl.SetSpinDesired(!videoActive && isPlaying);
    }

    /* ====================== Segmentos de bucle ====================== */

    private bool HasValidCustomLoop()
    {
        return TryGetLoopRangeSeconds(out _, out _);
    }

    private bool TryGetLoopRangeSeconds(out float startSec, out float endSec)
    {
        startSec = 0f; endSec = 0f;
        if (songLoader == null || songLoader.metadata == null || songLoader.metadata.Loop == null) return false;
        var lp = songLoader.metadata.Loop;
        if (string.IsNullOrWhiteSpace(lp.start) || string.IsNullOrWhiteSpace(lp.end)) return false;

        if (!TryParseFlexibleTimestamp(lp.start, out startSec)) return false;
        if (!TryParseFlexibleTimestamp(lp.end, out endSec)) return false;

        // Normalizar contra la duración del clip si la tenemos
        if (audioSource != null && audioSource.clip != null)
        {
            float len = audioSource.clip.length;
            startSec = Mathf.Clamp(startSec, 0f, len);
            endSec = Mathf.Clamp(endSec, 0f, len);
        }

        return (endSec - startSec) > 0.02f; // al menos 20 ms
    }

    private static bool TryParseFlexibleTimestamp(string s, out float secondsOut)
    {
        secondsOut = 0f;
        if (string.IsNullOrWhiteSpace(s)) return false;
        s = s.Trim().Replace(',', '.');

        var inv = System.Globalization.CultureInfo.InvariantCulture;
        string[] parts = s.Split(':');

        if (parts.Length == 3)
        {
            if (!int.TryParse(parts[0], out int hh)) return false;
            if (!int.TryParse(parts[1], out int mm)) return false;
            if (!float.TryParse(parts[2], System.Globalization.NumberStyles.Float, inv, out float ss)) return false;
            secondsOut = hh * 3600f + mm * 60f + ss;
            return true;
        }
        else if (parts.Length == 2)
        {
            if (!int.TryParse(parts[0], out int mm)) return false;
            if (!float.TryParse(parts[1], System.Globalization.NumberStyles.Float, inv, out float ss)) return false;
            secondsOut = mm * 60f + ss;
            return true;
        }
        else if (parts.Length == 1)
        {
            if (!float.TryParse(parts[0], System.Globalization.NumberStyles.Float, inv, out float ss)) return false;
            secondsOut = ss;
            return true;
        }
        return false;
    }

    // --- Loop-aware duration helpers ---
    private float EffectiveDurationSec()
    {
        if (queueManager != null && queueManager.playMode == PlayMode.RepeatOne &&
            TryGetLoopRangeSeconds(out _, out float loopEnd) &&
            audioSource != null && audioSource.clip != null)
        {
            // Duración efectiva = 0 .. loopEnd
            return Mathf.Clamp(loopEnd, 0f, audioSource.clip.length);
        }
        // Sin LoopOne válido -> longitud real
        return (audioSource != null && audioSource.clip != null) ? audioSource.clip.length : 0f;
    }

    private float NormalizedToTime(float normalized)
    {
        float dur = EffectiveDurationSec();
        return Mathf.Clamp01(normalized) * dur;
    }

    /// <summary>Refresca duración mostrada (total/restante) y barra usando la duración efectiva.</summary>
    private void RefreshLoopAwareUI()
    {
        if (audioSource == null || audioSource.clip == null) return;

        // Actualizar barra
        UpdateProgressBar();

        // Actualizar textos
        if (currentTimeText != null)
            currentTimeText.text = FormatTime(SafeAudioTime());

        if (durationText != null)
        {
            float dur = EffectiveDurationSec();
            float remaining = Mathf.Max(0f, dur - audioSource.time);
            durationText.text = showCountdown ? ("-" + FormatTime(remaining)) : FormatTime(dur);
        }
    }

    // Devuelve true si hay Loop start/end válidos en los metadatos y dentro de la duración del clip
    private bool TryGetValidLoopSegment_MusicPlayer(out float loopStart, out float loopEnd)
    {
        loopStart = 0f; loopEnd = 0f;
        if (songLoader == null || songLoader.metadata == null) return false;

        // Esperamos un objeto metadata.Loop con strings start/end
        // Estructura asumida: public class LoopInfo { public string start; public string end; }
        // en SongLoader.SongMetadata: public LoopInfo Loop;
        var meta = songLoader.metadata;
        var loopObj = meta.Loop; // ya existente en tus metadatos
        if (loopObj == null) return false;

        if (string.IsNullOrWhiteSpace(loopObj.start) || string.IsNullOrWhiteSpace(loopObj.end))
            return false;

        if (!ParseTimestampFlexible(loopObj.start, out loopStart)) return false;
        if (!ParseTimestampFlexible(loopObj.end, out loopEnd)) return false;

        if (audioSource != null && audioSource.clip != null)
        {
            float len = audioSource.clip.length;
            loopStart = Mathf.Clamp(loopStart, 0f, len);
            loopEnd = Mathf.Clamp(loopEnd, 0f, len);
            if (loopEnd <= loopStart + 0.0001f) return false;
        }
        else
        {
            if (loopEnd <= loopStart + 0.0001f) return false;
        }
        return true;
    }

    // Acepta "SS", "M:SS", "MM:SS", "HH:MM:SS" con opcionales decimales y coma/punto
    private static bool ParseTimestampFlexible(string s, out float secondsOut)
    {
        secondsOut = 0f;
        if (string.IsNullOrWhiteSpace(s)) return false;

        s = s.Trim().Replace(',', '.');
        string[] parts = s.Split(':');
        float sec = 0f;

        // Sólo segundos
        if (parts.Length == 1)
        {
            return float.TryParse(parts[0], System.Globalization.NumberStyles.Float,
                                  System.Globalization.CultureInfo.InvariantCulture, out secondsOut);
        }
        // M:SS(.ms)  /  H:MM:SS(.ms)
        else if (parts.Length == 2 || parts.Length == 3)
        {
            int h = 0, m = 0;
            float ssec = 0f;

            if (parts.Length == 3)
            {
                if (!int.TryParse(parts[0], out h)) return false;
                if (!int.TryParse(parts[1], out m)) return false;
                if (!float.TryParse(parts[2], System.Globalization.NumberStyles.Float,
                                    System.Globalization.CultureInfo.InvariantCulture, out ssec)) return false;
            }
            else // 2 partes
            {
                if (!int.TryParse(parts[0], out m)) return false;
                if (!float.TryParse(parts[1], System.Globalization.NumberStyles.Float,
                                    System.Globalization.CultureInfo.InvariantCulture, out ssec)) return false;
            }

            sec = h * 3600f + m * 60f + ssec;
            secondsOut = Mathf.Max(0f, sec);
            return true;
        }

        return false;
    }

    // TEST

    // === Gestión de tiempo en pausa ===
    // -1f == sin tiempo pendiente (usar engine)
    private float pendingPausedTime = -1f;

    private float GetDisplayedTime()
    {
        if (audioSource == null || audioSource.clip == null) return 0f;
        if (audioSource.isPlaying) return SafeAudioTime();
        if (pendingPausedTime >= 0f) return Mathf.Clamp(pendingPausedTime, 0f, EffectiveDurationSec());
        return Mathf.Clamp(audioSource.time, 0f, EffectiveDurationSec());
    }

    // Evita caer exactamente en el final para no reactivar lógica de fin de pista
    private float ClampPlayableTime(float t)
    {
        if (audioSource == null || audioSource.clip == null) return 0f;
        float dur = Mathf.Max(0f, EffectiveDurationSec());
        if (dur <= 0.02f) return 0f;
        return Mathf.Clamp(t, 0f, dur - 0.01f);
    }

    private void SetPendingTime(float t, bool applyToAudio)
    {
        float tt = ClampPlayableTime(t);
        pendingPausedTime = tt;
        if (applyToAudio && audioSource != null && audioSource.clip != null)
            audioSource.time = tt; // algunos drivers lo ignorarán en pausa; mantenemos el 'pending' como verdad
    }

    private void ClearPendingTime()
    {
        pendingPausedTime = -1f;
    }

}