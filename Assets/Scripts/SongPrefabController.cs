using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

public class SongPrefabController : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI gameText;
    public TextMeshProUGUI idText;
    public RawImage heartIcon; // Icono del corazón
    public SongData songData;

    [Header("Width clamp")]
    public RawImage playingTail;                 // RawImage al final del título (badge/cola)
    [Tooltip("Ancho máx. del título cuando NO suena (px). 0 = sin límite.")]
    public float titleMaxWidthInactive = 50f;
    [Tooltip("Ancho máx. del título cuando SÍ suena (px). 0 = sin límite.")]
    public float titleMaxWidthActive = 45f;

    // Cache de tema vigente
    private Color primary;   // Color1
    private Color secondary; // Color2
    private Color primaryDarken;     // Color1 más oscuro

    // Colores originales cacheados para restaurar
    private Color originalTitleColor;
    private Color originalGameColor;
    private Color originalIdColor;

    private LayoutElement titleLE;
    private ContentSizeFitter titleFitter;
    private ContentSizeFitter.FitMode originalFitterMode = ContentSizeFitter.FitMode.PreferredSize;
    private bool fitterCached;
    private RectTransform titleRT;

    public void Initialize(
    SongData data,
    System.Action<SongData> onClickCallback,
    System.Action<SongData> onFavoriteCallback,
    Color primaryColor,
    Color secondaryColor)
    {
        songData = data;
        primary = primaryColor;
        secondary = secondaryColor;
        primaryDarken = Darken(primary, 0.25f);

        // Cache colores originales de textos
        if (titleText) originalTitleColor = titleText.color;
        if (gameText) originalGameColor = gameText.color;
        if (idText) originalIdColor = idText.color;

        // Textos
        titleText.text = songData.Title;
        gameText.text = songData.Game;
        idText.text = songData.FileNumber;

        // Fondo del item = Color1 vigente
        var bg = GetComponent<RawImage>();
        if (bg) bg.color = primary;

        // Corazón = Color2 si favorito, Color1 oscurecido si no
        if (heartIcon) heartIcon.color = songData.IsFavorite ? secondary : primaryDarken;

        // Botón del prefab sin navegación por teclado
        var btn = GetComponent<Button>();
        if (btn != null)
        {
            var nav = btn.navigation;
            nav.mode = Navigation.Mode.None;
            btn.navigation = nav;
        }

        // Eventos
        GetComponent<Button>().onClick.AddListener(() => onClickCallback?.Invoke(songData));
        heartIcon.GetComponent<Button>().onClick.AddListener(() =>
        {
            songData.IsFavorite = !songData.IsFavorite;
            if (heartIcon) heartIcon.color = songData.IsFavorite ? secondary : primaryDarken;
            onFavoriteCallback?.Invoke(songData);
        });

        // ---- Soporte ancho máximo + badge ----
        if (titleText)
        {
            titleLE = titleText.GetComponent<LayoutElement>();
            if (!titleLE) titleLE = titleText.gameObject.AddComponent<LayoutElement>();

            titleFitter = titleText.GetComponent<ContentSizeFitter>();
            if (titleFitter)
            {
                originalFitterMode = titleFitter.horizontalFit;
                fitterCached = true;
            }

            titleRT = titleText.rectTransform;
        }

        // Por defecto, no "suena" aún: ocultar tail y aplicar ancho inactivo
        if (playingTail) playingTail.gameObject.SetActive(false);
        ApplyTitleWidthClamp(isPlaying: false);
        ForceRelayout();
    }

    // Activa/Desactiva estado "reproduciendo" tintando textos con Color2
    public void SetPlayingState(bool isPlaying, Color secondaryColor)
    {
        // Colores de texto
        if (titleText) titleText.color = isPlaying ? secondaryColor : originalTitleColor;
        if (gameText) gameText.color = isPlaying ? secondaryColor : originalGameColor;
        if (idText) idText.color = isPlaying ? secondaryColor : originalIdColor;

        // Activar/Desactivar tail al final del título
        if (playingTail) playingTail.gameObject.SetActive(isPlaying);

        // Control del GIF del tail (no necesita referencias de escena)
        if (playingTail)
        {
            var gif = playingTail.GetComponent<GifAnimator>();
            if (gif != null)
            {
                gif.controlledExternally = true;
                gif.followMusicPlayer = true;   // <- ahora seguirá el Play/Pause global
                gif.SetAnimating(isPlaying);    // sólo anima si esta fila es la que suena
            }
        }

        ApplyTitleWidthClamp(isPlaying);
        ForceRelayout();
    }

    private void ApplyTitleWidthClamp(bool isPlaying)
    {
        if (!titleText) return;

        // Máximo según estado
        float cap = isPlaying ? titleMaxWidthActive : titleMaxWidthInactive;

        // Que el Fitter no nos pelee en horizontal: auto-gestionamos el ancho
        if (titleFitter && fitterCached)
            titleFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        // Ancho intrínseco del texto (ignora el rect actual)
        // Usa la API de TMP para obtener el "preferred width" real del string
        float intrinsic = titleText.GetPreferredValues(titleText.text, Mathf.Infinity, Mathf.Infinity).x;

        // Si cap > 0, limitamos; si cap <= 0, sin tope (pero ajustado al texto, no infinito)
        float targetWidth = (cap > 0f) ? Mathf.Min(intrinsic, cap) : intrinsic;

        // Fijamos el tamaño del RectTransform para que el tail quede pegado
        if (titleRT)
            titleRT.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, targetWidth);

        // Hint al sistema de layout (si hay LayoutGroup padre)
        if (titleLE)
        {
            titleLE.ignoreLayout = false;
            titleLE.minWidth = -1f;
            titleLE.preferredWidth = targetWidth;
            titleLE.flexibleWidth = 0f;
        }
    }

    private void ForceRelayout()
    {
        Canvas.ForceUpdateCanvases();

        // Rebuild del propio item
        var itemRT = transform as RectTransform;
        if (itemRT)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(itemRT);
            var parent = itemRT.parent as RectTransform;
            if (parent) LayoutRebuilder.ForceRebuildLayoutImmediate(parent);
        }

        // Rebuild específico del título (por si no hay LayoutGroup)
        if (titleRT)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(titleRT);
            var p = titleRT.parent as RectTransform;
            if (p) LayoutRebuilder.ForceRebuildLayoutImmediate(p);
        }

        if (titleText)
            titleText.ForceMeshUpdate();
    }

    // Llamado cuando cambia el tema (Color1/Color2)
    public void ApplyTheme(Color newPrimary, Color newSecondary)
    {
        primary = newPrimary;
        secondary = newSecondary;
        primaryDarken = Darken(newPrimary, 0.25f);

        var bg = GetComponent<RawImage>();
        if (bg) bg.color = primary;

        if (heartIcon) heartIcon.color = songData.IsFavorite ? secondary : primaryDarken;
    }

    private static Color Darken(Color c, float t)
    {
        // t en [0..1]. 0 = sin cambio; 1 = negro.
        return Color.Lerp(c, Color.black, Mathf.Clamp01(t));
    }

    // Subrayado hover sin cambios
    public void OnPointerEnter(PointerEventData eventData) { titleText.fontStyle |= FontStyles.Underline; }
    public void OnPointerExit(PointerEventData eventData) { titleText.fontStyle &= ~FontStyles.Underline; }
}