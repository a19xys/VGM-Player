using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class InteractiveButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler {

    private Vector3 originalScale;

    [HideInInspector]
    public Color originalColor;

    public float hoverScaleFactor = 1.1f; // Escala al pasar el ratón por encima
    public float pressedScaleFactor = 0.9f; // Escala al presionar el botón
    public Color hoverColor = new Color(1f, 1f, 1f, 1f); // Color al pasar el ratón por encima
    public Color clickColor = new Color(0.8f, 0.8f, 0.8f, 1f); // Color al hacer clic
    public float animationDuration = 0.1f; // Duración de las animaciones de escala

    private bool isPressed = false; // Indica si el botón está presionado

    void Start() {
        // Guardar la escala y color originales
        originalScale = transform.localScale;
        if (TryGetComponent(out RawImage rawImage)) {
            originalColor = rawImage.color;
        }
    }

    public void OnPointerEnter(PointerEventData eventData) {
        if (isPressed) return; // No aplicar efecto hover si está presionado

        // Aumentar el tamaño y cambiar el color
        LeanTween.scale(gameObject, originalScale * hoverScaleFactor, animationDuration).setEaseOutQuad();
        if (TryGetComponent(out RawImage rawImage)) {
            if (rawImage.color == new Color(171 / 255f, 171 / 255f, 171 / 255f)) {
                rawImage.color = hoverColor;
            }
        }
    }

    public void OnPointerExit(PointerEventData eventData) {
        if (isPressed) return; // No revertir si está presionado

        // Restaurar tamaño y color originales
        LeanTween.scale(gameObject, originalScale, animationDuration).setEaseOutQuad();
        if (TryGetComponent(out RawImage rawImage)) {
            rawImage.color = originalColor;
        }
    }

    public void OnPointerDown(PointerEventData eventData) {
        // Cambiar color mientras esté presionado el botón
        if (TryGetComponent(out RawImage rawImage)) {
            rawImage.color = clickColor;
        }

        // Reducir la escala para simular un botón presionado
        isPressed = true;
        LeanTween.scale(gameObject, originalScale * pressedScaleFactor, animationDuration).setEaseOutQuad();
    }

    public void OnPointerUp(PointerEventData eventData) {
        isPressed = false;

        if (TryGetComponent(out RawImage rawImage)) {
            rawImage.color = originalColor;
        }

        // Comprobación de si continúa el puntero sobre el botón
        if (eventData.pointerEnter == gameObject) {
            LeanTween.scale(gameObject, originalScale * hoverScaleFactor, animationDuration).setEaseOutQuad();
        } else {
            LeanTween.scale(gameObject, originalScale, animationDuration).setEaseOutQuad();
        }
    }
}