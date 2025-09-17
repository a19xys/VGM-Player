using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

public class InteractiveElement : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler {
    private Vector3 originalScale;

    [HideInInspector]
    public Color originalColor;

    public float hoverScaleFactor = 1.1f; // Escala al pasar el ratón por encima
    public Color hoverColor = new Color(1f, 1f, 1f, 1f); // Color al pasar el ratón por encima
    public Color clickColor = new Color(0.8f, 0.8f, 0.8f, 1f); // Color al hacer clic
    public float animationDuration = 0.1f; // Duración de la animación de escala

    private bool isClicked = false;

    void Start() {
        // Guardar la escala y color originales
        originalScale = transform.localScale;
        if (TryGetComponent(out RawImage rawImage)) { originalColor = rawImage.color; }
    }

    public void OnPointerEnter(PointerEventData eventData) {

        if (isClicked) return; // No aplicar efecto hover si está en estado de clic

        // Aumentar el tamaño y cambiar el color
        LeanTween.scale(gameObject, originalScale * hoverScaleFactor, animationDuration).setEaseOutQuad();
        if (TryGetComponent(out RawImage rawImage) && rawImage.color == new Color(171 / 255f, 171 / 255f, 171 / 255f)) { rawImage.color = hoverColor; }

    }

    public void OnPointerExit(PointerEventData eventData) {

        if (isClicked) return; // No revertir si está en estado de clic

        // Restaurar tamaño y color originales
        LeanTween.scale(gameObject, originalScale, animationDuration).setEaseOutQuad();
        if (TryGetComponent(out RawImage rawImage)) { rawImage.color = originalColor; }

    }

}