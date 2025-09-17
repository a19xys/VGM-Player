using UnityEngine;
using TMPro;

public class NowPlayingText : MonoBehaviour
{
    public MusicPlayer musicPlayer;
    public TextMeshProUGUI textMeshPro; // Referencia al componente TextMeshPro
    public float updateInterval; // Intervalo de tiempo entre actualizaciones

    private string baseText = "NOW PLAYING"; // Texto base
    private int dotCount = 0; // Contador de puntos
    private float timer = 0f; // Temporizador

    void Update() {

        if (textMeshPro == null) { Debug.LogError("TextMeshProUGUI no está asignado."); return; }

        if (musicPlayer.audioSource.isPlaying) {

            // Incrementar el temporizador
            timer += Time.deltaTime;

            // Actualizar el texto cuando se alcance el intervalo
            if (timer >= updateInterval) {
                timer = 0f; // Reiniciar temporizador
                UpdateText();
            }

        } else { textMeshPro.text = "PAUSE"; dotCount = 3; timer = updateInterval; }

    }

    private void UpdateText() {

        // Incrementar o reiniciar el contador de puntos
        dotCount = (dotCount + 1) % 4;

        // Generar el texto con el número de puntos correspondiente
        string newText = baseText + new string('.', dotCount);

        // Actualizar el texto en el componente TextMeshPro
        textMeshPro.text = newText;

    }

}