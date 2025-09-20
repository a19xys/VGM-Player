using UnityEngine;

/// <summary>
/// Fader para cambios de canción:
/// - Fadea el AudioSource a 0 (sin tocar el slider).
/// - Restaura el volumen original justo antes de reproducir la nueva canción.
/// - Si el usuario cambió el volumen durante el fade, se respeta (no se pisa).
/// </summary>
public class SongSkipFader : MonoBehaviour
{
    [Header("Refs")]
    public AudioSource audioSource;

    [Header("Fade")]
    [Tooltip("Duración por defecto del fade-out al pasar de canción.")]
    public float fadeSeconds = 0.5f;

    private float cachedPreFadeVolume = 1f;
    private int tweenId = -1;
    private bool isFading = false;

    /// <summary>Inicia un fade a 0. Si no pasas duración, usa FadeSeconds.</summary>
    public void BeginFadeOut(float seconds = -1f)
    {
        if (!audioSource) return;

        if (seconds < 0f) seconds = fadeSeconds;

        // Cancela tween anterior si lo hubiera
        if (tweenId != -1) {
            LeanTween.cancel(tweenId);
            tweenId = -1;
        }

        // Guardamos el volumen actual para restaurarlo después.
        cachedPreFadeVolume = audioSource.volume;
        isFading = true;

        float from = audioSource.volume;
        float to = 0f;

        tweenId = LeanTween.value(gameObject, from, to, seconds)
            .setEase(LeanTweenType.easeInOutQuad)
            .setOnUpdate(v =>
            {
                if (audioSource) audioSource.volume = v; // ¡No tocamos slider!
            })
            .setOnComplete(() =>
            {
                tweenId = -1;
                isFading = false;
            })
            .id;
    }

    /// <summary>
    /// Restaura el volumen si el AudioSource quedó “silencio” por el fade.
    /// Si el usuario cambió el volumen (ya > 0), no hacemos nada.
    /// Llamar justo antes de arrancar la nueva canción (StartPlayback).
    /// </summary>
    public void RestoreIfSilent() {
        if (!audioSource) return;

        // 1) SIEMPRE cancelamos el tween en curso para que no “arrastre” a la nueva canción.
        if (tweenId != -1)
        {
            LeanTween.cancel(tweenId);
            tweenId = -1;
            isFading = false;
        }

        // 2) Si el volumen está prácticamente en silencio, restauramos el volumen previo.
        //    Si ya es > 0, significa que el usuario/ducker lo ha tocado: lo respetamos.
        if (audioSource.volume <= 0.001f)
            audioSource.volume = cachedPreFadeVolume;
    }

    /// <summary>Por si necesitas abortar un fade en curso.</summary>
    public void CancelFade()
    {
        if (tweenId != -1) LeanTween.cancel(tweenId);
        tweenId = -1;
        isFading = false;
    }
}
