using UnityEngine.EventSystems;
using UnityEngine;

public class SlidingPanelController : MonoBehaviour {

    public RectTransform panel; // El panel que se desplazará
    public KeyCode toggleKey;
    public float hiddenOffset = 50f; // Cuánto se asomará el panel al ocultarse
    public float animationDuration = 0.5f; // Duración de la animación
    public Vector2 hiddenDirection; // Dirección en la que el panel se esconderá
    public Transform rotationTarget; // GameObject cuya rotación cambiará

    private Vector2 initialPosition; // Posición inicial del panel
    private Vector2 hiddenPosition; // Posición del panel cuando está oculto
    private bool isHidden = false; // Estado del panel (visible/oculto)
    private bool canToggle = true; // Cooldown para evitar múltiples toggles rápidos

    void Start() {
        if (panel != null) {
            // Guardar la posición inicial
            initialPosition = panel.anchoredPosition;

            // Calcular la posición oculta
            hiddenPosition = initialPosition + hiddenDirection.normalized * (panel.rect.width - hiddenOffset);
        }
    }

    void Update() {
        if (Input.GetKeyDown(toggleKey)) { TryTogglePanel(); }
    }

    public void TryTogglePanel() {
        if (canToggle) { TogglePanel(); }
    }

    public void TogglePanel() {
        if (panel != null) {
            // Activar cooldown
            canToggle = false;
            Invoke(nameof(ResetToggle), animationDuration);

            // Mostrar el panel volviendo a su posición inicial
            if (isHidden) { LeanTween.move(panel, initialPosition, animationDuration).setEase(LeanTweenType.easeInOutQuart); }
            // Ocultar el panel hacia la posición oculta
            else { LeanTween.move(panel, hiddenPosition, animationDuration).setEase(LeanTweenType.easeInOutQuart); }

            // Cambiar el estado del panel
            isHidden = !isHidden;

            // Actualizar la rotación del GameObject
            UpdateRotation();
        }
    }

    private void ResetToggle() {
        canToggle = true; // Permitir toggles nuevamente
    }

    private void UpdateRotation() {
        if (rotationTarget != null) {
            float rotationAngle = isHidden ? 270f : 90f;
            rotationTarget.localRotation = Quaternion.Euler(0f, 0f, rotationAngle);
        }
    }
}