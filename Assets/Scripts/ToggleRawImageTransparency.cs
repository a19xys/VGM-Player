using UnityEngine;
using UnityEngine.UI;

public class ToggleRawImageTransparency : MonoBehaviour {

    public RawImage targetImage; // El RawImage objetivo
    public float animationDuration = 0.3f; // Duración de la animación
    private bool isActive = false; // Estado actual del RawImage

    // Método para activar y hacer FadeIn
    public void ActivateAndFadeIn() {
        if (targetImage == null || isActive) return;

        targetImage.gameObject.SetActive(true);
        Color currentColor = targetImage.color;
        Color targetColor = new Color(currentColor.r, currentColor.g, currentColor.b, 200f / 255f);

        LeanTween.value(gameObject, currentColor.a, targetColor.a, animationDuration)
            .setOnUpdate((float alpha) => { targetImage.color = new Color(currentColor.r, currentColor.g, currentColor.b, alpha); })
            .setOnComplete(() => { isActive = true; });
    }

    // Método para hacer FadeOut y desactivar
    public void FadeOutAndDeactivate() {
        if (targetImage == null || !isActive) return;

        Color currentColor = targetImage.color;
        Color targetColor = new Color(currentColor.r, currentColor.g, currentColor.b, 0f);

        LeanTween.value(gameObject, currentColor.a, targetColor.a, animationDuration)
            .setOnUpdate((float alpha) => { targetImage.color = new Color(currentColor.r, currentColor.g, currentColor.b, alpha); })
            .setOnComplete(() => {
                targetImage.gameObject.SetActive(false);
                isActive = false;
            });
    }
}