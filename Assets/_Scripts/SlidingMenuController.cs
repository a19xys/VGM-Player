using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine;

public class SlidingMenuController : MonoBehaviour, IPointerClickHandler {

    public RectTransform panel; // El panel que se desplazará
    public KeyCode toggleKey;
    public float hiddenOffset; // Cuánto se asomará el panel al ocultarse
    public float animationDuration = 0.5f; // Duración de la animación
    public Vector2 hiddenDirection; // Dirección en la que el panel se esconderá
    public Transform rotationTarget; // GameObject cuya rotación cambiará
    public RawImage targetImage; // El RawImage objetivo
    public float fadeDuration = 0.3f; // Duración de la animación

    private Vector2 initialPosition; // Posición inicial del panel
    private Vector2 hiddenPosition; // Posición del panel cuando está oculto
    private bool isHidden = false; // Estado del panel (visible/oculto)
    private bool isActive = false; // Estado actual del RawImage
    private bool canToggle = true; // Cooldown para evitar múltiples toggles rápidos
    private float cooldownDuration; // Duración del cooldown

    void Start() {
        if (panel != null) {
            // Guardar la posición inicial
            initialPosition = panel.anchoredPosition;

            // Calcular la posición oculta
            hiddenPosition = initialPosition + hiddenDirection.normalized * (panel.rect.width - hiddenOffset);

            // Cooldown entre animaciones
            cooldownDuration = animationDuration;
        }
    }

    void Update() { if (Input.GetKeyDown(toggleKey)) { TryTogglePanel(); } }

    public void OnPointerClick(PointerEventData eventData) {
        // Verificar si el clic ocurrió en el botón principal (ignorar hijos)
        if (eventData.pointerEnter == gameObject) { TryTogglePanel(); }
    }

    public void TryTogglePanel() { if (canToggle) { TogglePanel(); } }

    private void TogglePanel() {
        if (panel != null) {
            // Activar cooldown
            canToggle = false;
            Invoke(nameof(ResetToggle), cooldownDuration);

            if (isHidden) {
                // Mostrar el panel volviendo a su posición inicial
                LeanTween.move(panel, initialPosition, animationDuration).setEase(LeanTweenType.easeInOutQuart);
                FadeOutAndDeactivate();
            } else {
                // Ocultar el panel hacia la posición oculta
                LeanTween.move(panel, hiddenPosition, animationDuration).setEase(LeanTweenType.easeInOutQuart);
                ActivateAndFadeIn();
            }

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

    // Activar y hacer FadeIn a la cubierta sombreada
    public void ActivateAndFadeIn() {
        if (targetImage == null || isActive) return;

        targetImage.gameObject.SetActive(true);
        Color currentColor = targetImage.color;
        Color targetColor = new Color(currentColor.r, currentColor.g, currentColor.b, 200f / 255f);

        LeanTween.value(gameObject, currentColor.a, targetColor.a, fadeDuration)
            .setOnUpdate((float alpha) => { targetImage.color = new Color(currentColor.r, currentColor.g, currentColor.b, alpha); })
            .setOnComplete(() => { isActive = true; });
    }

    // Hacer FadeOut y desactivar cubierta sombreada
    public void FadeOutAndDeactivate() {
        if (targetImage == null || !isActive) return;

        Color currentColor = targetImage.color;
        Color targetColor = new Color(currentColor.r, currentColor.g, currentColor.b, 0f);

        LeanTween.value(gameObject, currentColor.a, targetColor.a, fadeDuration)
            .setOnUpdate((float alpha) => { targetImage.color = new Color(currentColor.r, currentColor.g, currentColor.b, alpha); })
            .setOnComplete(() => {
                targetImage.gameObject.SetActive(false);
                isActive = false;
            });
    }
}