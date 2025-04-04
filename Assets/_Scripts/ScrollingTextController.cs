using UnityEngine;

public class ScrollingTextController : MonoBehaviour {

    public AudioSource audioSource; // Fuente de audio para sincronizar
    public ScrollingText text1; // Primer texto
    public ScrollingText text2; // Segundo texto

    private bool isMouseOverParent = false; // Si el ratón está sobre el contenedor

    void Update() {
        if (audioSource == null || text1 == null || text2 == null) return;

        // Si la música está pausada o detenida
        if (!audioSource.isPlaying) { StopBothTexts(); }
        else if (audioSource.isPlaying && !isMouseOverParent) { ResumeBothTexts(); }
    }

    public void OnMouseEnterParent() {
        isMouseOverParent = true;
        StopBothTexts();
    }

    public void OnMouseExitParent() {
        isMouseOverParent = false;
        if (audioSource != null && audioSource.isPlaying) {
            ResumeBothTexts();
        }
    }

    private void StopBothTexts() {
        text1.StopSpeed();
        text2.StopSpeed();
    }

    private void ResumeBothTexts() {
        text1.ResumeSpeed();
        text2.ResumeSpeed();
    }
}