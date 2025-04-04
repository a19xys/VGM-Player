using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

public class MusicPlayer : MonoBehaviour, IDragHandler, IBeginDragHandler, IEndDragHandler, IPointerDownHandler
{

    public SongLoader songLoader;
    public AudioSource audioSource; // Fuente de audio para reproducir la música
    public Slider progressBar;      // Barra de progreso de la canción
    public GameObject grip;         // Agarre de la barra
    public TextMeshProUGUI currentTimeText;    // Texto del tiempo actual
    public TextMeshProUGUI durationText;       // Texto de la duración total (o cuenta regresiva)

    public float skiplapse;

    public RawImage repeatButton;   // Botón de repetición
    public RawImage shuffleButton;  // Botón de shuffle
    public RawImage playButton;     // Botón de reproducción
    public Texture playTexture;     // Textura para el botón de "Play"
    public Texture pauseTexture;    // Textura para el botón de "Pause"

    private float dragNormalizedPosition; // Posición normalizada durante el arrastre
    private float defaultTextSpeed;

    private bool isDragging = false; // Indica si se está arrastrando el agarre
    private bool showCountdown = false; // Indica si mostrar el contador regresivo en el texto 2

    private bool isRepeatActive = false; // Indica si la repetición está activada
    private bool isShuffleActive = false; // Indica si el shuffle está activado

    private Color inactiveColor = new Color(171 / 255f, 171 / 255f, 171 / 255f);

    void Start() {

        // Configurar barra de progreso
        if (progressBar != null) { progressBar.minValue = 0; progressBar.maxValue = 1; }
        
        // Desactivar el bucle al inicio
        if (audioSource != null) { audioSource.loop = false; }

        // Mostrar la duración total de la canción
        if (audioSource.clip != null && durationText != null) { durationText.text = FormatTime(audioSource.clip.length); }

        // Establecer colores iniciales de botones
        if (repeatButton != null) { repeatButton.color = inactiveColor; }
        if (shuffleButton != null) { shuffleButton.color = inactiveColor; }

    }

    void Update() {
    
        // Pausar y reproducir con barra espaciadora
        if (Input.GetKeyDown(KeyCode.Space)) { TogglePlayPause(); }
        playButton.texture = audioSource.isPlaying ? pauseTexture : playTexture;

        // Detectar si la canción terminó
        if (audioSource.clip != null && !audioSource.isPlaying && audioSource.time >= audioSource.clip.length) {

            if (isRepeatActive) {
                // Repetir la canción
                audioSource.time = 0;
                audioSource.Play();
            } else {
                // Resetear al principio
                audioSource.time = 0;
                UpdateProgressBar();

                // Mostrar tiempos correctos al acabar la canción
                if (durationText != null && showCountdown) { durationText.text = "-" + FormatTime(audioSource.clip.length); }
                currentTimeText.text = "0:00";
            }
        
        }

        // Actualizar barra de progreso y textos si no se está arrastrando
        if (!isDragging && audioSource.isPlaying) {

            UpdateProgressBar();

            // Actualizar texto del tiempo actual
            if (currentTimeText != null) { currentTimeText.text = FormatTime(audioSource.time); }

            // Actualizar el contador regresivo si está activado
            if (showCountdown && durationText != null) {
                float remainingTime = audioSource.clip.length - audioSource.time;
                durationText.text = "-" + FormatTime(remainingTime);
            }

        }

        // Saltar 5 segundos hacia atrás o adelante con las flechas
        if (Input.GetKeyDown(KeyCode.LeftArrow)) { SkipTime(-skiplapse); }
        if (Input.GetKeyDown(KeyCode.RightArrow)) { SkipTime(skiplapse); }

        if (Input.GetKeyDown(KeyCode.S)) { ToggleShuffle(); }
        if (Input.GetKeyDown(KeyCode.L)) { ToggleRepeat(); }
        if (Input.GetKeyDown(KeyCode.P)) { }
        if (Input.GetKeyDown(KeyCode.N)) { }

    }

    //// ************************************************************ ////

    public void OnDrag(PointerEventData eventData)
    {
        if (isDragging && progressBar != null)
        {
            RectTransform progressBarRect = progressBar.GetComponent<RectTransform>();
            Vector2 localPoint;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(progressBarRect, eventData.position, eventData.pressEventCamera, out localPoint))
            {
                dragNormalizedPosition = Mathf.Clamp01((localPoint.x / progressBarRect.rect.width) + 0.5f);
                progressBar.value = dragNormalizedPosition;

                UpdateGripPosition(dragNormalizedPosition);
            }
        }
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        RectTransform gripRect = grip.GetComponent<RectTransform>();
        if (RectTransformUtility.RectangleContainsScreenPoint(gripRect, eventData.position, eventData.pressEventCamera))
        {
            isDragging = true;
        }
    }

    public void OnEndDrag(PointerEventData eventData) {
        if (isDragging) {
            isDragging = false;

            if (audioSource.clip != null) {
                float newTime = Mathf.Clamp(audioSource.clip.length * dragNormalizedPosition, 0, audioSource.clip.length - 0.01f);
                audioSource.time = newTime;
            }

            if (!audioSource.isPlaying) { audioSource.Play(); }
        }
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (progressBar != null)
        {
            RectTransform progressBarRect = progressBar.GetComponent<RectTransform>();
            Vector2 localPoint;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(progressBarRect, eventData.position, eventData.pressEventCamera, out localPoint))
            {
                float normalizedPosition = Mathf.Clamp01((localPoint.x / progressBarRect.rect.width) + 0.5f);
                progressBar.value = normalizedPosition;

                UpdateGripPosition(normalizedPosition);

                if (audioSource.clip != null)
                {
                    float newTime = audioSource.clip.length * normalizedPosition;
                    audioSource.time = Mathf.Clamp(newTime, 0, audioSource.clip.length - 0.01f);

                    if (!audioSource.isPlaying)
                    {
                        audioSource.Play();
                    }
                }
            }
        }
    }

    //// ************************************************************ ////

    public void TogglePlayPause() {

        if (audioSource.isPlaying) { audioSource.Pause(); }
        else {
            // Reiniciar al comienzo si la canción terminó
            if (audioSource.time >= audioSource.clip.length) { audioSource.time = 0; }
            audioSource.Play();
        }

    }

    public void ToggleShuffle() {
        isShuffleActive = !isShuffleActive;
        isRepeatActive = false;

        if (repeatButton != null && shuffleButton != null) {
            shuffleButton.color = isShuffleActive ? songLoader.metadata.Color2 : inactiveColor;
            repeatButton.color = isRepeatActive ? songLoader.metadata.Color2 : inactiveColor;
        }

        shuffleButton.GetComponent<InteractiveButton>().originalColor = shuffleButton.color;
        repeatButton.GetComponent<InteractiveButton>().originalColor = repeatButton.color;

        // Placeholder para funcionalidad futura
        Debug.Log("Shuffle activado: " + isShuffleActive);
        audioSource.loop = isRepeatActive;
    }

    public void ToggleRepeat() {
        isShuffleActive = false;
        isRepeatActive = !isRepeatActive;

        if (repeatButton != null && shuffleButton != null) {
            shuffleButton.color = isShuffleActive ? songLoader.metadata.Color2 : inactiveColor;
            repeatButton.color = isRepeatActive ? songLoader.metadata.Color2 : inactiveColor;
        }

        shuffleButton.GetComponent<InteractiveButton>().originalColor = shuffleButton.color;
        repeatButton.GetComponent<InteractiveButton>().originalColor = repeatButton.color;

        audioSource.loop = isRepeatActive;
    }

    //// ************************************************************ ////

    public void OnDurationTextClick() {

        showCountdown = !showCountdown;

        if (audioSource.clip != null) {
            if (showCountdown) {
                // Mostrar contador regresivo
                float remainingTime = audioSource.clip.length - audioSource.time;
                durationText.text = "-" + FormatTime(remainingTime);
            } else {
                // Mostrar duración total
                durationText.text = FormatTime(audioSource.clip.length);
            }
        }

    }

    private void UpdateProgressBar()
    {
        if (audioSource.clip != null)
        {
            progressBar.value = audioSource.time / audioSource.clip.length;

            UpdateGripPosition(progressBar.value);
        }
    }

    private void UpdateGripPosition(float normalizedPosition)
    {
        RectTransform progressBarRect = progressBar.GetComponent<RectTransform>();
        float gripX = Mathf.Lerp(progressBarRect.rect.xMin, progressBarRect.rect.xMax, normalizedPosition);

        Vector3 localPosition = grip.transform.localPosition;
        localPosition.x = gripX;

        grip.transform.localPosition = localPosition;
    }

    private void SkipTime(float seconds) {
    
        if (audioSource.clip != null) {

            float newTime = audioSource.time + seconds;
            if (newTime >= audioSource.clip.length) {
                newTime = 0;
                if (!isRepeatActive) { TogglePlayPause(); }
            } else if (newTime < 0) { newTime = 0; }

            JumpTime(newTime);

        }

    }

    public void JumpTime(float newTime) {

            audioSource.time = newTime;

            // Actualizar barra de progreso y textos después del salto
            UpdateProgressBar();
            if (currentTimeText != null) { currentTimeText.text = FormatTime(audioSource.time); }

            // Actualizar el contador regresivo si está activado
            if (showCountdown && durationText != null) {
                float remainingTime = audioSource.clip.length - audioSource.time;
                durationText.text = "-" + FormatTime(remainingTime);
            }

    }

    private string FormatTime(float time)
    {
        int minutes = Mathf.FloorToInt(time / 60f);
        int seconds = Mathf.FloorToInt(time % 60f);
        return string.Format("{0}:{1:00}", minutes, seconds);
    }

    public void PlaySelectedSong(AudioClip clip)
    {
        if (audioSource != null)
        {
            audioSource.clip = clip;
            audioSource.Play();

            if (durationText != null)
            {
                durationText.text = FormatTime(audioSource.clip.length);
            }
        }
    }
}