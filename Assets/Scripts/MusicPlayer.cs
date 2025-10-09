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

    // Estado
    private float dragNormalizedPosition;      // Posición normalizada durante drag
    private bool isDragging = false;           // ¿Se está arrastrando el agarre?
    private bool showCountdown = false;        // ¿Mostrar cuenta atrás en durationText?
    private readonly Color inactiveColor = new Color(171 / 255f, 171 / 255f, 171 / 255f);
    private bool lastPlaying;                  // Para sincronizar vinilo

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

        // Si arrancamos con clip ya cargado (firstSongId), mostrar su duración
        if (audioSource != null && audioSource.clip != null && durationText != null)
        {
            durationText.text = showCountdown
                ? "-" + FormatTime(audioSource.clip.length)
                : FormatTime(audioSource.clip.length);
        }

        // Inicializar estado de vinilo
        lastPlaying = (audioSource != null && audioSource.isPlaying);
        UpdateVinylSpin();
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
        if (!isDragging && audioSource != null && audioSource.clip != null && audioSource.isPlaying) {
            UpdateProgressBar();
            if (currentTimeText != null)
                currentTimeText.text = FormatTime(SafeAudioTime());

            if (showCountdown && durationText != null && audioSource.clip != null)
            {
                float remainingTime = audioSource.clip.length - audioSource.time;
                durationText.text = "-" + FormatTime(remainingTime);
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
                // NORMAL + última: parar y volver a 0 sin transición
                audioSource.Stop();
                JumpTime(0f);
                RefreshPlayIcon();
                UpdateVinylSpin();
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

        // Calcula el tiempo objetivo (permitimos llegar hasta len para detectar "fin")
        float candidate = Mathf.Clamp(audioSource.clip.length * dragNormalizedPosition, 0f, audioSource.clip.length);
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

            float candidate = Mathf.Clamp(audioSource.clip.length * normalizedPosition, 0f, audioSource.clip.length);
            HandleSeekReleaseTo(candidate);
        }
    }

    private void UpdateTimePreviewUI(float normalized)
    {
        if (audioSource == null || audioSource.clip == null) return;

        float clipLen = audioSource.clip.length;
        float previewSec = Mathf.Clamp01(normalized) * clipLen;

        // Texto de tiempo actual (vista previa)
        if (currentTimeText != null)
            currentTimeText.text = FormatTime(previewSec);

        // Si está activada la cuenta atrás, también previsualizamos el restante
        if (durationText != null && showCountdown)
        {
            float remaining = Mathf.Max(0f, clipLen - previewSec);
            durationText.text = "-" + FormatTime(remaining);
        }
        // Si NO hay cuenta atrás, se deja la duración total tal y como está,
        // igual que hace Update() cuando no está en modo countdown.
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
            // Audio ON + vídeo ON -> Space pausa SOLO audio (vídeo sigue)
            audioSource.Pause();
        }
        else
        {
            // Audio OFF
            // Si audio y vídeo están ambos pausados -> arrancar ambos sincronizados
            if (videoReady && !videoPlaying)
            {
                if (audioSource.clip != null && audioSource.time >= audioSource.clip.length - 0.0001f)
                    audioSource.time = 0f;

                // Reproduce audio + vídeo (o vinilo) en el MISMO frame
                songLoader.StartPlayback();
            }
            else
            {
                // No hay vídeo, o el vídeo ya está ON -> arranca solo el audio
                if (audioSource.clip != null && audioSource.time >= audioSource.clip.length - 0.0001f)
                    audioSource.time = 0f;

                audioSource.Play();
            }
        }

        RefreshPlayIcon();
        lastPlaying = audioSource.isPlaying;
        UpdateVinylSpin();
    }

    public void TogglePlayPauseAudioAndVideo()
    {
        if (audioSource == null) return;

        bool videoActive = songLoader != null && songLoader.videoContainer != null && songLoader.videoContainer.activeSelf;
        var vp = (songLoader != null) ? songLoader.videoPlayer : null;
        bool videoPlaying = videoActive && vp != null && vp.isPrepared && vp.isPlaying;
        bool audioPlaying = audioSource.isPlaying;

        // Si cualquiera está reproduciendo -> pausar ambos
        if (audioPlaying || videoPlaying)
        {
            audioSource.Pause();
            if (videoActive && vp != null) vp.Pause();

            lastPlaying = false;
            RefreshPlayIcon();
            UpdateVinylSpin();
            return;
        }

        // Si ambos están en pausa -> reanudar sincronizados
        if (songLoader != null)
        {
            // Reproduce audio y vídeo (o vinilo) en el MISMO frame.
            songLoader.StartPlayback();
        }
        else
        {
            audioSource.Play();
        }

        lastPlaying = (audioSource != null && audioSource.isPlaying);
        RefreshPlayIcon();
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

        if (showCountdown)
        {
            float remainingTime = audioSource.clip.length - audioSource.time;
            durationText.text = "-" + FormatTime(remainingTime);
        }
        else
        {
            durationText.text = FormatTime(audioSource.clip.length);
        }
    }

    /* ============================= Helpers UI ============================= */
    
    private void UpdateProgressBar()
    {
        if (progressBar == null || audioSource == null || audioSource.clip == null) return;

        progressBar.value = audioSource.time / audioSource.clip.length;
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

        float newTime = audioSource.time + seconds;

        // Si nos pasamos del final, invocar transición a Next
        if (newTime >= audioSource.clip.length - 0.001f)
        {
            if (transition != null) { transition.GoToNext(); return; }
            newTime = 0f; // Fallback si no hay transición
        }
        else if (newTime < 0f) newTime = 0f;

        JumpTime(newTime);
    }

    public void JumpTime(float newTime)
    {
        if (audioSource == null || audioSource.clip == null) return;

        audioSource.time = newTime;
        UpdateProgressBar();

        if (currentTimeText != null) currentTimeText.text = FormatTime(audioSource.time);

        if (showCountdown && durationText != null)
        {
            float remainingTime = audioSource.clip.length - audioSource.time;
            durationText.text = "-" + FormatTime(remainingTime);
        }

        // Asegura vinilo coherente
        UpdateVinylSpin();

        // Avisar al pulso para realinear con el tiempo actual
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

        // Loop real del AudioSource solo en RepeatOne
        if (audioSource) audioSource.loop = isRepeatOne;
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
        }
        else
        {
            audioSource.Play();
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
                float remaining = audioSource.clip.length - audioSource.time; // ~clip.length
                durationText.text = "-" + FormatTime(remaining);
            }
            else
            {
                durationText.text = FormatTime(audioSource.clip.length);
            }
        }

        // Realinear el pulso, si existe
        if (songLoader != null && songLoader.beatPulseUI != null)
            songLoader.beatPulseUI.RealignToSongTime();
    }

    private void HandleSeekReleaseTo(float newTime)
    {
        if (audioSource == null || audioSource.clip == null) return;

        float len = audioSource.clip.length;
        const float endEpsilon = 0.02f; // ~20 ms
        var mode = (queueManager != null) ? queueManager.playMode : PlayMode.Normal;
        bool atEnd = newTime >= len - endEpsilon;

        if (atEnd)
        {
            // LOOP ONE: “ir a la siguiente” = reiniciar la MISMA sin transición
            if (mode == PlayMode.RepeatOne)
            {
                RestartCurrentNoTransition();
                return;
            }

            // NORMAL + última pista: parar y volver a 0 sin transición
            if (mode == PlayMode.Normal && queueManager != null && queueManager.IsLastIndex())
            {
                audioSource.Stop();
                JumpTime(0f); // también actualiza la UI
                RefreshPlayIcon();
                UpdateVinylSpin();
                return;
            }

            // Otros modos: dejar justo antes del final para que gobierne la lógica normal
            newTime = Mathf.Max(0f, len - 0.01f);
        }

        // Seek estándar
        audioSource.time = newTime;
        if (!audioSource.isPlaying) audioSource.Play();

        // Refrescos UI
        UpdateProgressBar();
        if (currentTimeText != null) currentTimeText.text = FormatTime(audioSource.time);
        if (showCountdown && durationText != null)
        {
            float remainingTime = len - audioSource.time;
            durationText.text = "-" + FormatTime(remainingTime);
        }

        UpdateVinylSpin();

        // Re-alinear el pulso si existe
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

        // Reset progreso y tiempo actual
        if (progressBar) { progressBar.value = 0f; UpdateGripPosition(0f); }
        if (currentTimeText) currentTimeText.text = "0:00";

        // Fijar duración de la nueva pista (contando modo cuenta atrás)
        if (durationText)
        {
            durationText.text = showCountdown
                ? "-" + FormatTime(clip.length)
                : FormatTime(clip.length);
        }
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

}