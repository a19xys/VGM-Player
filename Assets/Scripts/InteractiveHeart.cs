using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class InteractiveHeart : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler {

    private Vector3 originalScale;

    public float hoverScaleFactor = 1.1f; // Escala al pasar el ratón por encima
    public float pressedScaleFactor = 0.9f; // Escala al presionar el botón
    public float animationDuration = 0.1f; // Duración de las animaciones de escala

    private bool isPressed = false; // Indica si el botón está presionado

    void Start() {
        // Guardar la escala original
        originalScale = transform.localScale;
    }

    public void OnPointerEnter(PointerEventData eventData) {
        if (isPressed) return; // No aplicar efecto hover si está presionado

        // Aumentar el tamaño
        LeanTween.scale(gameObject, originalScale * hoverScaleFactor, animationDuration).setEaseOutQuad();
    }

    public void OnPointerExit(PointerEventData eventData) {
        if (isPressed) return; // No revertir si está presionado

        // Restaurar tamaño original
        LeanTween.scale(gameObject, originalScale, animationDuration).setEaseOutQuad();
    }

    public void OnPointerDown(PointerEventData eventData) {
        // Reducir la escala para simular un botón presionado
        isPressed = true;
        LeanTween.scale(gameObject, originalScale * pressedScaleFactor, animationDuration).setEaseOutQuad();
    }

    public void OnPointerUp(PointerEventData eventData) {
        isPressed = false;

        // Comprobación de si continúa el puntero sobre el botón
        if (eventData.pointerEnter == gameObject) { LeanTween.scale(gameObject, originalScale * hoverScaleFactor, animationDuration).setEaseOutQuad(); }
        else { LeanTween.scale(gameObject, originalScale, animationDuration).setEaseOutQuad(); }
    }

}