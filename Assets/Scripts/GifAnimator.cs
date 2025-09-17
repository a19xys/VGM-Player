using UnityEngine;
using UnityEngine.UI;

public class GifAnimator : MonoBehaviour {
    public MusicPlayer musicPlayer;
    public RawImage gifImage; // RawImage donde se mostrará el GIF
    public Texture[] frames; // Array de texturas que forman el GIF
    public float frameRate = 10f; // Velocidad de fotogramas (FPS)

    private int currentFrame = 0;
    private float timer;

    void Start() {
        if (frames.Length > 0) {
            gifImage.texture = frames[0];
        }
    }

    void Update() {
        if (frames.Length == 0) return;

        if (musicPlayer.audioSource.isPlaying) {

            timer += Time.deltaTime;
            if (timer >= 1f / frameRate) {
                timer -= 1f / frameRate;
                currentFrame = (currentFrame + 1) % frames.Length;
                gifImage.texture = frames[currentFrame];
            }

        }

    }
}