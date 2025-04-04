using System.Collections.Generic;
using System.IO;
using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class SongMenuManager : MonoBehaviour {

    public SongLoader songLoader;
    public SlidingMenuController slidingMenuController;

    public GameObject songPrefab;
    public Transform contentParent;

    public RawImage sortByIdImage;
    public RawImage sortByTitleImage;
    public RawImage sortByGameImage;
    public RawImage showFavoritesImage;
    public TextMeshProUGUI sortByIdTMP;
    public TextMeshProUGUI sortByTitleTMP;
    public TextMeshProUGUI sortByGameTMP;
    public Texture texture1;
    public Texture texture2;

    private List<SongData> songDataList = new List<SongData>();
    private List<SongData> filteredSongList = new List<SongData>();
    private string jsonDirectory;
    private string lastSortCriterion = "id"; // Orden inicial por ID
    private bool isAscending = true; // Orden inicial ascendente
    private bool showingFavorites = false; // Filtro de favoritos

    void Start() {
        LoadSongs();
        ApplyFilterAndSorting();
        CreatePrefabs();
        BindClickEvents();
        UpdateVisualFeedback();
    }

    private void LoadSongs() {
        jsonDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "VGM Hall of Fame");
        string[] jsonFiles = Directory.GetFiles(jsonDirectory, "info*.json");

        foreach (string jsonFile in jsonFiles) {
            string jsonContent = File.ReadAllText(jsonFile);
            SongLoader.SongMetadata metadata = JsonUtility.FromJson<SongLoader.SongMetadata>(jsonContent);
            string fileNumber = Path.GetFileNameWithoutExtension(jsonFile).Substring(4);

            SongData songData = new SongData {
                FileNumber = fileNumber,
                Title = metadata.Title,
                Game = metadata.Game,
                IsFavorite = metadata.Favorite,
                Metadata = metadata
            };

            songDataList.Add(songData);
        }

        songDataList.Sort((a, b) => int.Parse(a.FileNumber).CompareTo(int.Parse(b.FileNumber)));
    }

    private void ApplyFilterAndSorting() {
        filteredSongList = showingFavorites ? songDataList.FindAll(song => song.IsFavorite) : new List<SongData>(songDataList);

        Comparison<SongData> comparison;
        switch (lastSortCriterion) {
            case "title":
                comparison = (a, b) => string.Compare(a.Title, b.Title, StringComparison.Ordinal);
                break;
            case "game":
                comparison = (a, b) => string.Compare(a.Game, b.Game, StringComparison.Ordinal);
                break;
            default:
                comparison = (a, b) => int.Parse(a.FileNumber).CompareTo(int.Parse(b.FileNumber));
                break;
        }

        filteredSongList.Sort(isAscending ? comparison : (a, b) => comparison(b, a));
    }

    private void CreatePrefabs() {
        int i = 1;
        foreach (SongData song in filteredSongList) {
            GameObject prefab = Instantiate(songPrefab, contentParent);
            prefab.GetComponent<SongPrefabController>().Initialize(song, OnSongSelected, FavoriteSong, songLoader.metadata.Color1);
            prefab.name = "song_element_" + i++;
        }
    }

    private void FavoriteSong(SongData songData) {
        UpdateJsonFavorite(songData);
        Debug.Log($"Favorito cambiado: {songData.Title}, Estado: {(songData.IsFavorite ? "Favorito" : "No favorito")}");
    }

    private void UpdateJsonFavorite(SongData songData) {
        string jsonFile = Path.Combine(jsonDirectory, $"info{songData.FileNumber}.json");

        if (File.Exists(jsonFile)) {
            string jsonContent = File.ReadAllText(jsonFile);
            SongLoader.SongMetadata metadata = JsonUtility.FromJson<SongLoader.SongMetadata>(jsonContent);
            metadata.Favorite = songData.IsFavorite;

            string updatedJsonContent = JsonUtility.ToJson(metadata, true);
            File.WriteAllText(jsonFile, updatedJsonContent);
        } else {
            Debug.LogError($"No se encontró el archivo JSON para {songData.Title}");
        }
    }

    private void BindClickEvents() {
        sortByIdTMP.GetComponent<Button>().onClick.AddListener(() => SortByCriterion("id"));
        sortByTitleTMP.GetComponent<Button>().onClick.AddListener(() => SortByCriterion("title"));
        sortByGameTMP.GetComponent<Button>().onClick.AddListener(() => SortByCriterion("game"));
        showFavoritesImage.GetComponent<Button>().onClick.AddListener(ToggleFavorites);
    }

    private void SortByCriterion(string criterion) {
        if (lastSortCriterion == criterion) {
            isAscending = !isAscending; // Alternar entre ascendente y descendente
        } else {
            lastSortCriterion = criterion;
            isAscending = true; // Resetear a ascendente al cambiar de criterio
        }

        ApplyFilterAndSorting();
        UpdateVisualFeedback();
        RecreatePrefabs();
    }

    private void ToggleFavorites() {
        showingFavorites = !showingFavorites; // Alternar filtro de favoritos
        ApplyFilterAndSorting();
        UpdateVisualFeedback();
        RecreatePrefabs();
    }

    private void RecreatePrefabs() {
        ClearPrefabs();
        CreatePrefabs();
    }

    private void ClearPrefabs() {
        foreach (Transform child in contentParent) {
            Destroy(child.gameObject);
        }
    }

    public void UpdateVisualFeedback() {
        Color defaultColor = Color.white;
        Color activeColor = songLoader.metadata.Color2;

        UpdateButtonVisual(sortByIdImage, sortByIdTMP, lastSortCriterion == "id", isAscending, activeColor, defaultColor);
        UpdateButtonVisual(sortByTitleImage, sortByTitleTMP, lastSortCriterion == "title", isAscending, activeColor, defaultColor);
        UpdateButtonVisual(sortByGameImage, sortByGameTMP, lastSortCriterion == "game", isAscending, activeColor, defaultColor);
        UpdateFavoritesVisual();
    }

    private void UpdateButtonVisual(RawImage image, TextMeshProUGUI tmp, bool isActive, bool ascending, Color activeColor, Color defaultColor) {
        image.texture = isActive ? texture2 : texture1;
        image.color = isActive ? activeColor : defaultColor;

        // Aplicar rotación al RawImage
        RectTransform rectTransform = image.GetComponent<RectTransform>();
        rectTransform.localEulerAngles = new Vector3(0, 0, isActive && !ascending ? 180 : 0);

        if (tmp != null) tmp.color = isActive ? activeColor : defaultColor;
    }

    private void UpdateFavoritesVisual() {
        Color defaultColor = Color.white;
        Color activeColor = songLoader.metadata.Color2;

        showFavoritesImage.color = showingFavorites ? activeColor : defaultColor;
    }

    private void OnSongSelected(SongData songData) {
        Debug.Log($"Canción seleccionada: {songData.Title} del juego {songData.Game}");
        slidingMenuController.TryTogglePanel();
        songLoader.NextSong(int.Parse(songData.FileNumber));
    }
}

[System.Serializable]
public class SongData {
    public string FileNumber;
    public string Title;
    public string Game;
    public bool IsFavorite;
    public SongLoader.SongMetadata Metadata;
}