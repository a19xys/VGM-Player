using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

public class SongPrefabController : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler {
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI gameText;
    public TextMeshProUGUI idText;
    public RawImage heartIcon; // Icono del corazón
    public SongData songData;

    // Cache de tema vigente
    private Color primary;   // Color1
    private Color secondary; // Color2

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

        titleText.text = songData.Title;
        gameText.text = songData.Game;
        idText.text = songData.FileNumber;

        // Fondo del item = Color1 vigente
        var bg = GetComponent<RawImage>();
        if (bg) bg.color = primary;

        // Corazón = Color2 si favorito, Color1 si no
        if (heartIcon) heartIcon.color = songData.IsFavorite ? secondary : primary;

        // Componente botón del prefab
        var btn = GetComponent<Button>();
        if (btn != null) {
            var nav = btn.navigation;
            nav.mode = Navigation.Mode.None; // Evita focus/navegación por teclado
            btn.navigation = nav;
        }

        // Eventos
        GetComponent<Button>().onClick.AddListener(() => onClickCallback?.Invoke(songData));
        heartIcon.GetComponent<Button>().onClick.AddListener(() => {
            songData.IsFavorite = !songData.IsFavorite;
            if (heartIcon) heartIcon.color = songData.IsFavorite ? secondary : primary;
            onFavoriteCallback?.Invoke(songData);
        });
    }

    // Llamado cuando cambia el tema (Color1/Color2)
    public void ApplyTheme(Color newPrimary, Color newSecondary)
    {
        primary = newPrimary;
        secondary = newSecondary;

        var bg = GetComponent<RawImage>();
        if (bg) bg.color = primary;

        if (heartIcon) heartIcon.color = songData.IsFavorite ? secondary : primary;
    }

    // Subrayado hover sin cambios
    public void OnPointerEnter(PointerEventData eventData) { titleText.fontStyle |= FontStyles.Underline; }
    public void OnPointerExit(PointerEventData eventData) { titleText.fontStyle &= ~FontStyles.Underline; }
}