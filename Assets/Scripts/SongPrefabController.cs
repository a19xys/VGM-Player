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

    // Cache de tema vigente
    private Color primary;   // Color1
    private Color secondary; // Color2
    private Color primaryDarken;     // Color1 más oscuro

    // Colores originales cacheados para restaurar
    private Color originalTitleColor;
    private Color originalGameColor;
    private Color originalIdColor;

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

        // Guardar colores originales de texto (para restaurar cuando NO sea la pista activa)
        if (titleText) originalTitleColor = titleText.color;
        if (gameText) originalGameColor = gameText.color;
        if (idText) originalIdColor = idText.color;

        titleText.text = songData.Title;
        gameText.text = songData.Game;
        idText.text = songData.FileNumber;

        // Fondo del item = Color1 vigente
        var bg = GetComponent<RawImage>();
        if (bg) bg.color = primary;

        // Corazón = Color2 si favorito, Color1 oscuro si no
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
    }

    // Activa/Desactiva estado "reproduciendo" tintando textos con Color2
    public void SetPlayingState(bool isPlaying, Color secondaryColor)
    {
        if (titleText) titleText.color = isPlaying ? secondaryColor : originalTitleColor;
        if (gameText) gameText.color = isPlaying ? secondaryColor : originalGameColor;
        if (idText) idText.color = isPlaying ? secondaryColor : originalIdColor;
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