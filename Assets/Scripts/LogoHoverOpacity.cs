using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class LogoHoverOpacity : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Header("Refs")]
    public SongLoader songLoader;        // Asigna tu SongLoader
    public Graphic target;               // Si lo dejas vacío, usa el Graphic del propio GO (RawImage)

    [Header("Opacity")]
    [Range(0f, 1f)] public float idleAlpha = 0.8f;   // 80% de transparencia cuando NO hay hover
    [Range(0f, 1f)] public float hoverAlpha = 1f;    // Opaco al hacer hover
    public float fadeSeconds = 0.2f;

    private CanvasGroup cg;
    private int tweenId = -1;
    private bool pointerOver = false;
    private bool lastForceOpaque = false;

    void Awake()
    {
        if (!target) target = GetComponent<Graphic>();
        cg = GetComponent<CanvasGroup>();
        if (!cg) cg = gameObject.AddComponent<CanvasGroup>();
        cg.interactable = true;
        cg.blocksRaycasts = true; // para que el RawImage reciba el hover
    }

    void Start()
    {
        // Estado inicial según modo vídeo/vinilo
        bool forceOpaque = IsVinylMode();
        lastForceOpaque = forceOpaque;
        cg.alpha = forceOpaque ? 1f : (pointerOver ? hoverAlpha : idleAlpha);
    }

    void Update()
    {
        // Si cambia el modo (vídeo <-> vinilo), actualiza alpha y cancela tweens.
        bool forceOpaque = IsVinylMode();
        if (forceOpaque != lastForceOpaque)
        {
            CancelTween();
            cg.alpha = forceOpaque ? 1f : (pointerOver ? hoverAlpha : idleAlpha);
            lastForceOpaque = forceOpaque;
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        pointerOver = true;
        if (IsVinylMode()) return; // en vinilo, siempre opaco; sin animación de hover
        FadeTo(hoverAlpha);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        pointerOver = false;
        if (IsVinylMode()) return; // en vinilo, siempre opaco
        FadeTo(idleAlpha);
    }

    private bool IsVinylMode()
    {
        // En tu flujo, si no hay vídeo -> videoContainer está desactivado => modo vinilo
        return songLoader != null &&
               songLoader.videoContainer != null &&
               !songLoader.videoContainer.activeSelf;
    }

    private void FadeTo(float a)
    {
        if (!cg) return;
        if (Mathf.Approximately(cg.alpha, a)) return;
        CancelTween();
        tweenId = LeanTween.value(gameObject, cg.alpha, a, fadeSeconds)
            .setOnUpdate(v => { if (cg) cg.alpha = v; })
            .setEase(LeanTweenType.easeInOutQuad)
            .setOnComplete(() => tweenId = -1)
            .id;
    }

    private void CancelTween()
    {
        if (tweenId != -1)
        {
            LeanTween.cancel(tweenId);
            tweenId = -1;
        }
    }

    void OnDisable() => CancelTween();
}