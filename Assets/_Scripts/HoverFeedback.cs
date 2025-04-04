using UnityEngine;

public class HoverFeedback : MonoBehaviour {

    public Vector3 moveDirection = new Vector3(0, 10, 0); // Dirección del movimiento (por defecto hacia arriba)
    public float moveDuration = 0.2f; // Duración del movimiento
    public float returnDuration = 0.2f; // Duración del regreso
    private Vector3 originalPosition; // Posición original del objeto

    private void Start() {
        // Guardar la posición original
        originalPosition = transform.localPosition;
    }

    public void OnMouseEnter() {
        // Mover el objeto en la dirección especificada
        LeanTween.moveLocal(gameObject, originalPosition + moveDirection, moveDuration).setEaseOutQuad();
    }

    public void OnMouseExit() {
        // Volver a la posición original
        LeanTween.moveLocal(gameObject, originalPosition, returnDuration).setEaseOutQuad();
    }

}