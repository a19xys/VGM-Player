using UnityEngine;

public class MenuAudioDucker : MonoBehaviour
{
    [Header("Refs")]
    public AudioSource audioSource;

    [Header("Ducking")]
    [Range(0f, 1f)] public float duckTo = 0.30f;     // 30%
    public float fadeSeconds = 0.35f;

    // Estado interno
    private float? unduckedVolume; // volumen original guardado solo si > duckTo
    private int tweenId = -1;

    // Llamar desde SlidingMenuController.onMenuOpened
    public void Duck()
    {
        if (!audioSource) return;

        // Si ya está por debajo o igual al destino, no hacemos nada
        if (audioSource.volume <= duckTo + 0.0001f)
        {
            unduckedVolume = null;
            return;
        }

        unduckedVolume = audioSource.volume;
        StartTween(audioSource.volume, duckTo);
    }

    // Llamar desde SlidingMenuController.onMenuClosed
    public void Unduck()
    {
        if (!audioSource) return;

        // Solo restaurar si guardamos un volumen “original” válido (> duckTo)
        if (unduckedVolume.HasValue && unduckedVolume.Value > duckTo + 0.0001f)
        {
            StartTween(audioSource.volume, unduckedVolume.Value);
        }

        unduckedVolume = null;
    }

    private void StartTween(float from, float to)
    {
        if (tweenId != -1)
        {
            LeanTween.cancel(tweenId);
            tweenId = -1;
        }

        tweenId = LeanTween.value(gameObject, from, to, fadeSeconds)
            .setEase(LeanTweenType.easeInOutQuad)
            .setOnUpdate(v =>
            {
                // OJO: no tocamos el slider ni VolumeController, solo el volumen real.
                if (audioSource) audioSource.volume = v;
            })
            .setOnComplete(() => tweenId = -1)
            .id;
    }
}