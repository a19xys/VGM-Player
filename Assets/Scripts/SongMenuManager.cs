using NUnit.Framework.Internal;
using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class SongMenuManager : MonoBehaviour {

    [Header("Refs")]
    public SongLoader songLoader;
    public MusicPlayer musicPlayer;
    public SlidingMenuController slidingMenuController;
    public TrackQueueManager queueManager;
    public SongTransitionController transition;

    [Header("UI - Lista")]
    public GameObject songPrefab;
    public Transform contentParent;

    [Header("UI - Filtros")]
    public RawImage sortByIdImage;
    public RawImage sortByTitleImage;
    public RawImage sortByGameImage;
    public RawImage showFavoritesImage;
    public TextMeshProUGUI sortByIdTMP;
    public TextMeshProUGUI sortByTitleTMP;
    public TextMeshProUGUI sortByGameTMP;
    public Texture texture1;
    public Texture texture2;

    // Estado
    private readonly List<SongData> songDataList = new List<SongData>();
    private List<SongData> filteredSongList = new List<SongData>();
    private string jsonDirectory;
    private string lastSortCriterion = "id"; // id | title | game
    private bool isAscending = true;
    private bool showingFavorites = false;

    void Start() {
        LoadSongs();
        ApplyFilterAndSorting();
        CreatePrefabs();
        BindClickEvents();
        UpdateVisualFeedback();
    }

    /* ===================== Carga & filtro ===================== */

    private void LoadSongs() {
        jsonDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "VGM Hall of Fame");
        string[] jsonFiles = Directory.GetFiles(jsonDirectory, "info*.json");

        songDataList.Clear();

        foreach (string jsonFile in jsonFiles) {
            string jsonContent = File.ReadAllText(jsonFile);
            SongLoader.SongMetadata metadata = JsonUtility.FromJson<SongLoader.SongMetadata>(jsonContent);
            string fileNumber = Path.GetFileNameWithoutExtension(jsonFile).Substring(4); // "infoXYZ" -> "XYZ"

            var songData = new SongData {
                FileNumber = fileNumber,
                Title = metadata.Title,
                Game = metadata.Game,
                IsFavorite = metadata.Favorite,
                Metadata = metadata
            };

            songDataList.Add(songData);
        }

        // Orden base por ID ascendente
        songDataList.Sort((a, b) => int.Parse(a.FileNumber).CompareTo(int.Parse(b.FileNumber)));
    }

    private void ApplyFilterAndSorting() {
        // Filtro favoritos
        filteredSongList = showingFavorites ? songDataList.FindAll(s => s.IsFavorite)
                                            : new List<SongData>(songDataList);

        // Orden actual
        Comparison<SongData> comparison;
        switch (lastSortCriterion) {
            case "title":
                comparison = (a, b) => string.Compare(a.Title, b.Title, StringComparison.Ordinal);
                break;
            case "game":
                comparison = (a, b) => string.Compare(a.Game, b.Game, StringComparison.Ordinal);
                break;
            default: // "id"
                comparison = (a, b) => int.Parse(a.FileNumber).CompareTo(int.Parse(b.FileNumber));
                break;
        }

        filteredSongList.Sort(isAscending ? comparison : (a, b) => comparison(b, a));
    }

    /* ===================== Prefabs de lista ===================== */

    private void CreatePrefabs() {
        if (contentParent == null || songPrefab == null) return;

        int i = 1;
        foreach (var song in filteredSongList) {
            GameObject prefab = Instantiate(songPrefab, contentParent);
            var ctrl = prefab.GetComponent<SongPrefabController>();
            if (ctrl != null) {
                // Color base del item con el COLOR1 de la canción actual (instantáneo)
                Color baseColor = songLoader != null ? songLoader.metadata.Color1 : Color.white;
                ctrl.Initialize(song, OnSongSelected, FavoriteSong, baseColor);
            }
            prefab.name = "song_element_" + i++;
        }
    }

    private void RecreatePrefabs() {
        ClearPrefabs();
        CreatePrefabs();

        // Mantener coherencia con la cola (shuffle, punteros, etc.)
        if (queueManager != null) queueManager.NotifyFilteredListChanged();

        // Refrescar indicadores de modo (colores de botones) porque cambia Color2 activo
        if (musicPlayer != null) musicPlayer.RefreshModeIndicators();
    }

    private void ClearPrefabs() {
        if (contentParent == null) return;
        for (int i = contentParent.childCount - 1; i >= 0; i--)
            Destroy(contentParent.GetChild(i).gameObject);
    }

    /* ===================== Eventos UI ===================== */

    private void BindClickEvents() {
        if (sortByIdTMP != null) sortByIdTMP.GetComponent<Button>().onClick.AddListener(() => SortByCriterion("id"));
        if (sortByTitleTMP != null) sortByTitleTMP.GetComponent<Button>().onClick.AddListener(() => SortByCriterion("title"));
        if (sortByGameTMP != null) sortByGameTMP.GetComponent<Button>().onClick.AddListener(() => SortByCriterion("game"));
        if (showFavoritesImage != null) showFavoritesImage.GetComponent<Button>().onClick.AddListener(ToggleFavorites);
    }

    private void SortByCriterion(string criterion) {
        if (InputLock.IsLocked) return;

        if (lastSortCriterion == criterion) isAscending = !isAscending;
        else { lastSortCriterion = criterion; isAscending = true; }

        ApplyFilterAndSorting();
        UpdateVisualFeedback();
        RecreatePrefabs();
    }

    private void ToggleFavorites() {
        if (InputLock.IsLocked) return;

        showingFavorites = !showingFavorites;
        ApplyFilterAndSorting();
        UpdateVisualFeedback();
        RecreatePrefabs();
    }

    private void FavoriteSong(SongData songData) {
        if (InputLock.IsLocked) return;

        UpdateJsonFavorite(songData);
        Debug.Log($"Favorito cambiado: {songData.Title}, Estado: {(songData.IsFavorite ? "Favorito" : "No favorito")}");
    }

    private void UpdateJsonFavorite(SongData songData) {
        string jsonFile = Path.Combine(jsonDirectory, $"info{songData.FileNumber}.json");

        if (File.Exists(jsonFile)) {
            string jsonContent = File.ReadAllText(jsonFile);
            var metadata = JsonUtility.FromJson<SongLoader.SongMetadata>(jsonContent);
            metadata.Favorite = songData.IsFavorite;

            string updated = JsonUtility.ToJson(metadata, true);
            File.WriteAllText(jsonFile, updated);
        } else {
            Debug.LogError($"No se encontró el archivo JSON para {songData.Title}");
        }
    }

    /* ===================== Feedback visual ===================== */

    public void UpdateVisualFeedback() {
        Color defaultColor = Color.white;
        Color activeColor = (songLoader != null) ? songLoader.metadata.Color2 : Color.white;

        UpdateButtonVisual(sortByIdImage, sortByIdTMP, lastSortCriterion == "id", isAscending, activeColor, defaultColor);
        UpdateButtonVisual(sortByTitleImage, sortByTitleTMP, lastSortCriterion == "title", isAscending, activeColor, defaultColor);
        UpdateButtonVisual(sortByGameImage, sortByGameTMP, lastSortCriterion == "game", isAscending, activeColor, defaultColor);
        UpdateFavoritesVisual();
    }

    private void UpdateButtonVisual(RawImage image, TextMeshProUGUI tmp, bool isActive, bool ascending, Color activeColor, Color defaultColor) {
        if (image != null) {
            image.texture = isActive ? texture2 : texture1;
            image.color = isActive ? activeColor : defaultColor;

            // Flecha arriba/abajo girando el icono cuando el criterio está activo
            RectTransform rt = image.GetComponent<RectTransform>();
            if (rt != null) rt.localEulerAngles = new Vector3(0, 0, isActive && !ascending ? 180 : 0);
        }
        if (tmp != null) tmp.color = isActive ? activeColor : defaultColor;
    }

    private void UpdateFavoritesVisual() {
        Color defaultColor = Color.white;
        Color activeColor = (songLoader != null) ? songLoader.metadata.Color2 : Color.white;

        if (showFavoritesImage != null)
            showFavoritesImage.color = showingFavorites ? activeColor : defaultColor;
    }

    /* ===================== Selección de canción ===================== */

    private void OnSongSelected(SongData songData) {
        if (InputLock.IsLocked) return;
        if (songData == null) return;

        int idx = filteredSongList.FindIndex(s => s.FileNumber == songData.FileNumber);
        if (idx < 0) return;

        // NO cerramos el menú aquí: lo cerrará la transición cuando cubra la pantalla.
        if (transition != null) { transition.PlayFromFilteredIndex(idx); }
        else {
            //if (slidingMenuController != null) slidingMenuController.TryTogglePanel();
            if (queueManager != null) queueManager.PlayFromFilteredIndex(idx);
            else if (songLoader != null) {
                songLoader.LoadSongMetadataInstant(songData.FileNumber);
                StartCoroutine(songLoader.PrepareAudioClipRoutine(songData.FileNumber, true));
                StartCoroutine(songLoader.PrepareVideosRoutine(songData.FileNumber, true));
            }
        }
    }

    /* ===================== Exposición a otras clases ===================== */

    public List<SongData> GetFiltered() => filteredSongList;
    public int FilteredCount() => filteredSongList?.Count ?? 0;

    public int IndexInFilteredByFileNumber(string fileNumber) { return filteredSongList.FindIndex(s => s.FileNumber == fileNumber); }

}

/* ===================== DTO ===================== */

[System.Serializable]
public class SongData {
    public string FileNumber;
    public string Title;
    public string Game;
    public bool IsFavorite;
    public SongLoader.SongMetadata Metadata;
}