using UnityEngine;
using UnityEngine.UI;

public class VolumeController : MonoBehaviour {

    public Slider volumeSlider; // Referencia al slider del volumen
    public AudioSource audioSource; // Referencia al AudioSource
    public RawImage muteButton; // Referencia al RawImage que actúa como botón de mute
    public Texture unmutedTexture; // Textura para el estado no muteado
    public Texture mutedTexture; // Textura para el estado muteado

    private bool isMuted = false; // Indica si está muteado
    private float previousVolume = 1f; // Guarda el último valor de volumen antes de mutear

    void Start() {

        if (volumeSlider != null && audioSource != null) {
            // Inicializar el slider con el volumen actual
            volumeSlider.value = audioSource.volume;

            // Agregar listener al slider
            volumeSlider.onValueChanged.AddListener(SetVolume);
        }

        // Configurar textura inicial
        UpdateMuteButtonTexture();

    }

    void Update() {
        // Aumentar o disminuir el volumen con las teclas de flecha
        if (Input.GetKeyDown(KeyCode.UpArrow)) { AdjustVolume(0.1f); }
        if (Input.GetKeyDown(KeyCode.DownArrow)) { AdjustVolume(-0.1f); }
        if (Input.GetKeyDown(KeyCode.M)) { ToggleMute(); }
    }

    public void ToggleMute() {

        isMuted = !isMuted;

        if (isMuted) {

            // Guardar el volumen actual (si no está en 0) y mutear
            if (audioSource.volume > 0) { previousVolume = audioSource.volume; }

            audioSource.volume = 0;

            if (volumeSlider != null) { volumeSlider.value = 0; }

        } else {

            // Restaurar el volumen anterior
            audioSource.volume = previousVolume;
            
            if (volumeSlider != null) { volumeSlider.value = previousVolume; }

        }

        // Actualizar la textura del botón
        UpdateMuteButtonTexture();

    }

    private void SetVolume(float value) {

        if (audioSource != null) {

            // Si el slider está en 0, marcar como muteado
            if (value == 0) {

                isMuted = true;
                UpdateMuteButtonTexture();

            } else {

                // Si el slider sube desde 0, desmutear
                if (isMuted) {
                    isMuted = false;
                    UpdateMuteButtonTexture();

                }

                // Actualizar previousVolume para reflejar el nuevo valor
                //previousVolume = value;

            }

            // Ajustar el volumen del audio
            audioSource.volume = value;

        }

    }

    private void UpdateMuteButtonTexture() {
        if (muteButton != null) { muteButton.texture = isMuted ? mutedTexture : unmutedTexture; }
    }

    private void AdjustVolume(float delta) {

        if (audioSource != null) {
            // Ajustar el volumen dentro del rango [0, 1]
            float newVolume = Mathf.Round(Mathf.Clamp(audioSource.volume + delta, 0f, 1f) * 10.0f) * 0.1f;
            audioSource.volume = newVolume;

            // Actualizar el slider si existe
            if (volumeSlider != null) { volumeSlider.value = newVolume; }

            // Si el volumen llega a 0, marcar como muteado
            if (newVolume == 0) { isMuted = true; }
            else {

                // Si el volumen sube desde 0, desmutear
                if (isMuted) { isMuted = false; }

                // Guardar el volumen actual como previousVolume
                previousVolume = newVolume;

            }

            // Actualizar la textura del botón mute
            UpdateMuteButtonTexture();
        }

    }

}