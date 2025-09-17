using UnityEngine;
using TMPro;

public class ScrollingText : MonoBehaviour {

    public TextMeshProUGUI textMeshPro; // Referencia al texto
    public float pauseDuration = 4f; // Tiempo de pausa al llegar al final o inicio
    public float defaultSpeed = 20f; // Velocidad del desplazamiento
    public float scrollSpeed;

    public ScrollingText otherText; // Referencia al otro texto
    public bool nowMove = true; // Indica si este texto puede moverse
    //public bool isMouseOverParent = false; // Indica si el ratón está sobre el padre

    private RectTransform textRect;
    private RectTransform parentRect;
    private float originalPositionX;
    private float textWidth;
    private float visibleWidth;
    private float pauseTimer;
    private bool movingLeft = true; // Dirección inicial
    private bool shouldScroll = false;

    void Start() {
        if (textMeshPro == null) { Debug.LogError("TextMeshProUGUI no está asignado."); return; }

        textRect = textMeshPro.GetComponent<RectTransform>();
        parentRect = textRect.parent.GetComponent<RectTransform>();

        if (parentRect == null) { Debug.LogError("El texto debe estar dentro de un contenedor con RectTransform."); return; }

        scrollSpeed = defaultSpeed;
        pauseTimer = pauseDuration;
        originalPositionX = textRect.anchoredPosition.x;
        UpdateTextMetrics();
    }

    void Update() {
        if (!shouldScroll || textRect == null) return;

        // No mover si no está permitido
        if (!nowMove) return;

        // Pausa al llegar al final o inicio
        if (pauseTimer > 0 && scrollSpeed != 0.0f) { pauseTimer -= Time.deltaTime; return; }

        // Mover el texto
        Vector2 position = textRect.anchoredPosition;
        float direction = movingLeft ? -1 : 1;

        position.x += direction * scrollSpeed * Time.deltaTime;
        textRect.anchoredPosition = position;

        // Verificar límites
        if (movingLeft && position.x <= originalPositionX - (textWidth - visibleWidth)) {
            movingLeft = false;
            HandleOtherText();
        } else if (!movingLeft && position.x >= originalPositionX) {
            movingLeft = true;
            HandleOtherText();
        }
    }

    void HandleOtherText() {
        // Activar movimiento del otro texto
        if (otherText != null) {
            if (otherText.shouldScroll) { nowMove = false; }
            else { pauseTimer = pauseDuration; }

            if (!otherText.nowMove) {
                nowMove = true;
                otherText.nowMove = true;
                pauseTimer = pauseDuration;
                otherText.pauseTimer = pauseDuration;
            }
        }
    }

    void UpdateTextMetrics() {
        // Calcular el ancho del texto y el área visible
        textWidth = textMeshPro.preferredWidth + 28.0f;
        visibleWidth = parentRect.rect.width;

        // Determinar si el texto debe desplazarse
        shouldScroll = textWidth > visibleWidth;

        // Si no es necesario desplazarse, restablecer la posición
        if (!shouldScroll) { textRect.anchoredPosition = new Vector2(originalPositionX, textRect.anchoredPosition.y); }
    }

    public void OnTextChanged() { UpdateTextMetrics(); }

    public void StopSpeed() { scrollSpeed = 0.0f; }

    public void ResumeSpeed() { scrollSpeed = defaultSpeed; }

}