using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine;
using TMPro;

public class SongPrefabController : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler {

    public TextMeshProUGUI titleText;
    public TextMeshProUGUI gameText;
    public TextMeshProUGUI idText;
    public RawImage heartIcon; // Icono del corazón
    public SongData songData;

    private Color defaultHeartColor; // Color inicial del corazón
    private Color favoriteColor = Color.white; // Color cuando está en favoritos

    public void Initialize(SongData data, System.Action<SongData> onClickCallback, System.Action<SongData> onFavoriteCallback, Color defaultHeartColor) {
        songData = data;

        titleText.text = songData.Title;
        gameText.text = songData.Game;
        idText.text = songData.FileNumber;

        GetComponent<RawImage>().color = defaultHeartColor;

        heartIcon.color = songData.IsFavorite ? favoriteColor : defaultHeartColor;

        // Agregar eventos
        GetComponent<Button>().onClick.AddListener(() => onClickCallback.Invoke(songData));
        heartIcon.GetComponent<Button>().onClick.AddListener(() => {
            songData.IsFavorite = !songData.IsFavorite;
            heartIcon.color = songData.IsFavorite ? favoriteColor : defaultHeartColor;
            onFavoriteCallback.Invoke(songData);
        });
    }

    // Subrayar titleText al pasar el ratón
    public void OnPointerEnter(PointerEventData eventData) { titleText.fontStyle |= FontStyles.Underline; }

    public void OnPointerExit(PointerEventData eventData) { titleText.fontStyle &= ~FontStyles.Underline; }

}