using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Control de volumen con slider + botón mute.
/// - Respeta InputLock: ignora atajos de teclado y callbacks mientras hay transición.
/// - Expone SetVolumeExternal para actualizar desde código si hiciera falta.
/// </summary>
public class VolumeController : MonoBehaviour {

    [Header("Refs")]
    public SlidingMenuController selectionMenu;
    public Slider volumeSlider;
    public AudioSource audioSource;
    public RawImage muteButton;
    public Texture unmutedTexture;
    public Texture mutedTexture;

    private bool isMuted = false;
    private float previousVolume = 1f;

    void Start() {
        if (volumeSlider != null && audioSource != null) {
            volumeSlider.value = audioSource.volume;
            volumeSlider.onValueChanged.AddListener(SetVolume);
        }
        UpdateMuteButtonTexture();
    }

    void Update() {
        // Bloqueo por transición
        if (InputLock.IsLocked) return;
        // Bloque por menú abierto
        if (SlidingMenuController.AnyOpen) return;

        if (Input.GetKeyDown(KeyCode.UpArrow)) AdjustVolume(0.1f);
        if (Input.GetKeyDown(KeyCode.DownArrow)) AdjustVolume(-0.1f);
        if (Input.GetKeyDown(KeyCode.M)) ToggleMute();
    }

    /* ================== API pública ================== */

    public void ToggleMute() {
        if (InputLock.IsLocked) return;
        if (audioSource == null) return;

        isMuted = !isMuted;

        if (isMuted) {
            if (audioSource.volume > 0f) previousVolume = audioSource.volume;
            audioSource.volume = 0f;
            if (volumeSlider) volumeSlider.SetValueWithoutNotify(0f);
        } else {
            audioSource.volume = previousVolume;
            if (volumeSlider) volumeSlider.SetValueWithoutNotify(previousVolume);
        }
        UpdateMuteButtonTexture();
    }

    public void SetVolume(float value) {
        if (InputLock.IsLocked) return;
        if (audioSource == null) return;

        // 0 -> marcar muteado; >0 -> desmutear
        if (Mathf.Approximately(value, 0f)) {
            isMuted = true;
        } else {
            if (isMuted) isMuted = false;
            previousVolume = value; // recuerda el último valor “bueno”
        }

        audioSource.volume = value;
        UpdateMuteButtonTexture();
    }

    /// <summary>
    /// Usar si quieres fijar volumen desde código sin disparar onValueChanged del slider.
    /// </summary>
    public void SetVolumeExternal(float value) {
        value = Mathf.Clamp01(value);
        if (audioSource) audioSource.volume = value;
        if (volumeSlider) volumeSlider.SetValueWithoutNotify(value);

        isMuted = Mathf.Approximately(value, 0f);
        if (!isMuted) previousVolume = value;

        UpdateMuteButtonTexture();
    }

    /* ================== Internos ================== */

    private void AdjustVolume(float delta) {
        if (audioSource == null) return;

        float newVolume = Mathf.Round(Mathf.Clamp(audioSource.volume + delta, 0f, 1f) * 10f) * 0.1f;
        if (audioSource) audioSource.volume = newVolume;
        if (volumeSlider) volumeSlider.SetValueWithoutNotify(newVolume);

        if (Mathf.Approximately(newVolume, 0f)) { isMuted = true; }
        else {
            if (isMuted) isMuted = false;
            previousVolume = newVolume;
        }

        UpdateMuteButtonTexture();
    }

    private void UpdateMuteButtonTexture() {
        if (!muteButton) return;
        muteButton.texture = isMuted ? mutedTexture : unmutedTexture;
    }

}
