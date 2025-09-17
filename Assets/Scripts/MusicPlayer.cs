using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

public class MusicPlayer : MonoBehaviour, IDragHandler, IBeginDragHandler, IEndDragHandler, IPointerDownHandler {

    [Header("Refs")]
    public SongLoader songLoader;
    public TrackQueueManager queueManager;
    public SongTransitionController transition; // Controlador de transición

    [Header("Audio / UI")]
    public AudioSource audioSource;            // Fuente de audio para reproducir la música
    public Slider progressBar;                 // Barra de progreso de la canción
    public GameObject grip;                    // Agarre de la barra
    public TextMeshProUGUI currentTimeText;    // Texto del tiempo actual
    public TextMeshProUGUI durationText;       // Texto de la duración total (o cuenta regresiva)

    [Header("Playback")]
    public float skiplapse = 5f;

    [Header("Buttons / Icons")]
    public RawImage repeatButton;   // Botón de repetición
    public RawImage shuffleButton;  // Botón de shuffle
    public RawImage playButton;     // Botón de reproducción
    public Texture playTexture;     // Textura para el botón de "Play"
    public Texture pauseTexture;    // Textura para el botón de "Pause"

    private float dragNormalizedPosition;      // Posición normalizada durante el arrastre
    private bool isDragging = false;           // Indica si se está arrastrando el agarre
    private bool showCountdown = false;        // Indica si mostrar el contador regresivo en el texto 2

    private readonly Color inactiveColor = new Color(171 / 255f, 171 / 255f, 171 / 255f);

    void Start() {
        // Configurar barra de progreso
        if (progressBar != null) { progressBar.minValue = 0; progressBar.maxValue = 1; }

        // Audio no en loop por defecto (RepeatOne controlará esto)
        if (audioSource != null) { audioSource.loop = false; }

        // Mostrar la duración si ya hay clip
        if (audioSource != null && audioSource.clip != null && durationText != null)
            durationText.text = FormatTime(audioSource.clip.length);

        // Colores iniciales de botones
        if (repeatButton != null) repeatButton.color = inactiveColor;
        if (shuffleButton != null) shuffleButton.color = inactiveColor;

        // Refrescar indicadores cuando cambie el modo
        if (queueManager != null) queueManager.OnPlayModeChanged += _ => RefreshModeIndicators();

        // Pintar estado actual
        RefreshModeIndicators();
        RefreshPlayIcon();
    }

    void Update() {
        // Actualiza icono Play/Pause siempre (aunque esté bloqueado)
        RefreshPlayIcon();

        // Si transición/overlay está bloqueando entradas, no procesar atajos ni navegación
        if (InputLock.IsLocked) return;

        // Teclas básicas
        if (Input.GetKeyDown(KeyCode.Space)) { TogglePlayPause(); }

        // Fin de pista → pasar con transición
        if (audioSource != null && audioSource.clip != null && !audioSource.isPlaying && audioSource.time >= audioSource.clip.length) {
            if (queueManager != null && queueManager.playMode == PlayMode.RepeatOne) { audioSource.time = 0f; audioSource.Play(); }
            else if (transition != null) { transition.GoToNext(); }
            return; // evitar doble lógica en este frame
        }

        // Actualizar barra y tiempos si se reproduce y no se arrastra
        if (!isDragging && audioSource != null && audioSource.isPlaying) {
            UpdateProgressBar();

            if (currentTimeText != null)
                currentTimeText.text = FormatTime(audioSource.time);

            if (showCountdown && durationText != null) {
                float remainingTime = audioSource.clip.length - audioSource.time;
                durationText.text = "-" + FormatTime(remainingTime);
            }
        }

        // Saltos rápidos
        if (Input.GetKeyDown(KeyCode.LeftArrow)) { SkipTime(-skiplapse); }
        if (Input.GetKeyDown(KeyCode.RightArrow)) { SkipTime(skiplapse); }

        // Navegación de pistas por teclado: vía transición
        if (Input.GetKeyDown(KeyCode.P)) { if (transition != null) transition.GoToPrevious(); }
        if (Input.GetKeyDown(KeyCode.N)) { if (transition != null) transition.GoToNext(); }

        // Cambios de modo
        if (Input.GetKeyDown(KeyCode.S)) { ToggleShuffle(); }
        if (Input.GetKeyDown(KeyCode.L)) { ToggleRepeat(); }
    }

    /* ============================= Drag / Seek ============================= */

    public void OnBeginDrag(PointerEventData eventData) {
        if (InputLock.IsLocked) return;
        if (grip == null) return;

        RectTransform gripRect = grip.GetComponent<RectTransform>();
        if (RectTransformUtility.RectangleContainsScreenPoint(gripRect, eventData.position, eventData.pressEventCamera))
            isDragging = true;
    }

    public void OnDrag(PointerEventData eventData) {
        if (InputLock.IsLocked) return;
        if (!isDragging || progressBar == null) return;

        RectTransform progressBarRect = progressBar.GetComponent<RectTransform>();
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(progressBarRect, eventData.position, eventData.pressEventCamera, out Vector2 localPoint)) {
            dragNormalizedPosition = Mathf.Clamp01((localPoint.x / progressBarRect.rect.width) + 0.5f);
            progressBar.value = dragNormalizedPosition;
            UpdateGripPosition(dragNormalizedPosition);
        }
    }

    public void OnEndDrag(PointerEventData eventData) {
        if (!isDragging) return;
        isDragging = false;

        if (InputLock.IsLocked) return;
        if (audioSource == null || audioSource.clip == null) return;

        float newTime = Mathf.Clamp(audioSource.clip.length * dragNormalizedPosition, 0, audioSource.clip.length - 0.01f);
        audioSource.time = newTime;
        if (!audioSource.isPlaying) audioSource.Play();
    }

    public void OnPointerDown(PointerEventData eventData) {
        if (InputLock.IsLocked) return;
        if (progressBar == null || audioSource == null || audioSource.clip == null) return;

        RectTransform progressBarRect = progressBar.GetComponent<RectTransform>();
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(progressBarRect, eventData.position, eventData.pressEventCamera, out Vector2 localPoint)) {
            float normalizedPosition = Mathf.Clamp01((localPoint.x / progressBarRect.rect.width) + 0.5f);
            progressBar.value = normalizedPosition;

            UpdateGripPosition(normalizedPosition);

            float newTime = Mathf.Clamp(audioSource.clip.length * normalizedPosition, 0, audioSource.clip.length - 0.01f);
            audioSource.time = newTime;

            if (!audioSource.isPlaying) audioSource.Play();
        }
    }

    /* ============================= Controles ============================= */

    public void TogglePlayPause() {
        if (audioSource == null) return;
        if (audioSource.isPlaying) audioSource.Pause();
        else {
            if (audioSource.clip != null && audioSource.time >= audioSource.clip.length)
                audioSource.time = 0f;
            audioSource.Play();
        }
        RefreshPlayIcon();
    }

    public void ToggleShuffle() {
        if (InputLock.IsLocked) return;
        if (queueManager == null) return;

        var newMode = (queueManager.playMode == PlayMode.Shuffle) ? PlayMode.Normal : PlayMode.Shuffle;
        queueManager.SetMode(newMode);
        audioSource.loop = (newMode == PlayMode.RepeatOne);
        RefreshModeIndicators();
    }

    public void ToggleRepeat() {
        if (InputLock.IsLocked) return;
        if (queueManager == null) return;

        PlayMode next = queueManager.playMode switch {
            PlayMode.Normal => PlayMode.RepeatOne,
            PlayMode.RepeatOne => PlayMode.RepeatAll,
            PlayMode.RepeatAll => PlayMode.Normal,
            PlayMode.Shuffle => PlayMode.RepeatOne,
            _ => PlayMode.Normal
        };
        queueManager.SetMode(next);
        audioSource.loop = (next == PlayMode.RepeatOne);
        RefreshModeIndicators();
    }

    public void OnDurationTextClick() {
        if (InputLock.IsLocked) return;
        if (audioSource == null || audioSource.clip == null || durationText == null) return;

        showCountdown = !showCountdown;
        if (showCountdown) {
            float remainingTime = audioSource.clip.length - audioSource.time;
            durationText.text = "-" + FormatTime(remainingTime);
        } else { durationText.text = FormatTime(audioSource.clip.length); }
    }

    /* ============================= Helpers UI ============================= */

    private void UpdateProgressBar() {
        if (progressBar == null || audioSource == null || audioSource.clip == null) return;
        progressBar.value = audioSource.time / audioSource.clip.length;
        UpdateGripPosition(progressBar.value);
    }

    private void UpdateGripPosition(float normalizedPosition) {
        if (progressBar == null || grip == null) return;

        RectTransform progressBarRect = progressBar.GetComponent<RectTransform>();
        float gripX = Mathf.Lerp(progressBarRect.rect.xMin, progressBarRect.rect.xMax, normalizedPosition);

        Vector3 localPosition = grip.transform.localPosition;
        localPosition.x = gripX;
        grip.transform.localPosition = localPosition;
    }

    private void SkipTime(float seconds) {
        if (InputLock.IsLocked) return;
        if (audioSource == null || audioSource.clip == null) return;

        float newTime = audioSource.time + seconds;

        // Si nos pasamos del final, invocar transición a Next
        if (newTime >= audioSource.clip.length - 0.001f) {
            if (transition != null) { transition.GoToNext(); return; }
            newTime = 0f; // fallback
        } else if (newTime < 0f) newTime = 0f;

        JumpTime(newTime);
    }

    public void JumpTime(float newTime) {
        if (audioSource == null || audioSource.clip == null) return;

        audioSource.time = newTime;

        UpdateProgressBar();

        if (currentTimeText != null) currentTimeText.text = FormatTime(audioSource.time);

        if (showCountdown && durationText != null) {
            float remainingTime = audioSource.clip.length - audioSource.time;
            durationText.text = "-" + FormatTime(remainingTime);
        }
    }

    private string FormatTime(float time) {
        int minutes = Mathf.FloorToInt(time / 60f);
        int seconds = Mathf.FloorToInt(time % 60f);
        return $"{minutes}:{seconds:00}";
    }

    private void RefreshPlayIcon() {
        if (playButton == null || audioSource == null) return;
        playButton.texture = audioSource.isPlaying ? pauseTexture : playTexture;
    }

    public void RefreshModeIndicators() {
        if (queueManager == null || songLoader == null) return;
        if (repeatButton == null || shuffleButton == null) return;

        var mode = queueManager.playMode;
        bool shuf = (mode == PlayMode.Shuffle);
        bool rpt1 = (mode == PlayMode.RepeatOne);
        bool rptAll = (mode == PlayMode.RepeatAll);

        shuffleButton.color = shuf ? songLoader.metadata.Color2 : inactiveColor;
        repeatButton.color = (rpt1 || rptAll) ? songLoader.metadata.Color2 : inactiveColor;

        var ibS = shuffleButton.GetComponent<InteractiveButton>();
        var ibR = repeatButton.GetComponent<InteractiveButton>();
        if (ibS) ibS.originalColor = shuffleButton.color;
        if (ibR) ibR.originalColor = repeatButton.color;

        if (audioSource) audioSource.loop = rpt1;
    }

    /* ====================== Botones UI (Next/Prev) ====================== */

    public void OnClickNext() {
        if (InputLock.IsLocked) return;
        if (transition != null) transition.GoToNext();
        else if (queueManager != null) queueManager.Next(); // fallback
    }

    public void OnClickPrevious() {
        if (InputLock.IsLocked) return;
        if (audioSource != null && audioSource.time > 3f) { JumpTime(0f); }
        else if (transition != null) transition.GoToPrevious();
        else if (queueManager != null) queueManager.Previous(); // fallback
    }

}