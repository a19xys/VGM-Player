using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public enum PlayMode { Normal, RepeatOne, RepeatAll, Shuffle }

public class TrackQueueManager : MonoBehaviour {
    public event Action<PlayMode> OnPlayModeChanged;

    [Header("Refs")]
    public SongMenuManager menu;   // asignar en Inspector
    public SongLoader loader;      // asignar en Inspector

    [Header("State")]
    public PlayMode playMode = PlayMode.Normal;

    // �ndice actual dentro de la lista FILTRADA del men�
    private int currentIndex = 0;

    // Estado de shuffle
    private readonly System.Random rng = new System.Random();
    private readonly Stack<int> history = new Stack<int>(); // para Previous en shuffle
    private List<int> shuffleOrder = new List<int>();
    private int shufflePtr = 0; // puntero dentro de shuffleOrder

    /* ===================== API P�BLICA (compatibilidad) ===================== */

    /// <summary>
    /// Reproduce desde un �ndice espec�fico de la lista FILTRADA (sin transici�n).
    /// </summary>
    public void PlayFromFilteredIndex(int index) {
        EnsureListReady();
        currentIndex = Mathf.Clamp(index, 0, menu.FilteredCount() - 1);
        RecenterShuffleOn(currentIndex);
        PlayCurrent();
    }

    /// <summary>
    /// Reproduce la canci�n en currentIndex (sin transici�n).
    /// </summary>
    public void PlayCurrent() {
        var list = menu.GetFiltered();
        if (list == null || list.Count == 0) return;
        var fileNumber = list[currentIndex].FileNumber;
        loader.NextSong(int.Parse(fileNumber));
    }

    /// <summary>
    /// Siguiente pista seg�n el modo, y reproduce (sin transici�n).
    /// </summary>
    public void Next() {
        var list = menu.GetFiltered(); if (list == null || list.Count == 0) return;

        if (playMode == PlayMode.RepeatOne) { PlayCurrent(); return; }

        if (playMode == PlayMode.Shuffle) {
            history.Push(currentIndex);
            shufflePtr = (shufflePtr + 1) % list.Count;
            currentIndex = shuffleOrder[shufflePtr];
        } else {
            currentIndex++;
            if (currentIndex >= list.Count) {
                if (playMode == PlayMode.RepeatAll) currentIndex = 0;
                else { currentIndex = list.Count - 1; return; } // quedarse al final en Normal
            }
        }
        PlayCurrent();
    }

    /// <summary>
    /// Pista anterior seg�n el modo, y reproduce (sin transici�n).
    /// </summary>
    public void Previous() {
        var list = menu.GetFiltered(); if (list == null || list.Count == 0) return;

        if (playMode == PlayMode.Shuffle && history.Count > 0) {
            currentIndex = history.Pop();
        } else { currentIndex = Mathf.Max(0, currentIndex - 1); }
        PlayCurrent();
    }

    /// <summary>
    /// Cambia el modo de reproducci�n. Reconstruye shuffle si procede.
    /// </summary>
    public void SetMode(PlayMode mode) {
        if (playMode == mode) return;
        playMode = mode;

        if (playMode == PlayMode.Shuffle) {
            BuildShuffleOrder();
            RecenterShuffleOn(currentIndex);
            history.Clear();
        }

        OnPlayModeChanged?.Invoke(playMode);
    }

    /// <summary>
    /// Sincroniza la cola con un ID de canci�n (p.ej., si se cambia desde fuera).
    /// Reproduce esa canci�n y centra el shuffle en ese �ndice. (sin transici�n)
    /// </summary>
    public void SyncWithSongId(int id) {
        var list = menu.GetFiltered();
        if (list == null || list.Count == 0) return;

        string fileNumber = id.ToString("D3");
        int idx = list.FindIndex(s => s.FileNumber == fileNumber);
        if (idx < 0) return;

        currentIndex = idx;
        RecenterShuffleOn(currentIndex);
        PlayCurrent();
    }

    /// <summary>
    /// Debe llamarse cuando cambie el filtrado/orden en el men�.
    /// Recalcula el orden shuffle y mantiene el puntero en rango.
    /// </summary>
    public void NotifyFilteredListChanged() {
        int count = menu.FilteredCount();
        currentIndex = Mathf.Clamp(currentIndex, 0, Mathf.Max(0, count - 1));
        BuildShuffleOrder();
        RecenterShuffleOn(currentIndex);
        // history no se borra aqu� para poder volver en shuffle a pistas previas
    }

    /* ===================== API PARA LA TRANSICI�N ===================== */

    /// <summary>
    /// Devuelve los metadatos de la pista destino SIN tocar el estado interno (para pintar bloques).
    /// </summary>
    public SongLoader.SongMetadata PeekMetadata(int advance, int absoluteIndex) {
        int idx = ComputeTargetIndex(advance, absoluteIndex);
        var list = menu.GetFiltered();
        if (list == null || list.Count == 0 || idx < 0 || idx >= list.Count)
            return new SongLoader.SongMetadata { Title = "Unknown", Color1 = Color.black, Color2 = Color.black };

        string fileNumber = list[idx].FileNumber;
        return LoadMetadataFromJson(fileNumber);
    }

    /// <summary>
    /// Resuelve el ID (int) de la pista destino y ACTUALIZA el estado de la cola.
    /// Debe llamarse cuando la pantalla ya est� cubierta, justo antes de cargar la canci�n.
    /// </summary>
    public int ResolveTargetId(int advance, int absoluteIndex) {
        var list = menu.GetFiltered();
        if (list == null || list.Count == 0) return 0;

        if (absoluteIndex >= 0) {
            currentIndex = Mathf.Clamp(absoluteIndex, 0, list.Count - 1);
            RecenterShuffleOn(currentIndex);
            history.Clear(); // opcional: empezar historial desde este punto
        } else {
            switch (playMode) {
                case PlayMode.RepeatOne:
                    // currentIndex se queda tal cual
                    break;

                case PlayMode.Shuffle:
                    if (advance >= 0) {
                        history.Push(currentIndex);
                        shufflePtr = (shufflePtr + 1) % list.Count;
                        currentIndex = shuffleOrder[shufflePtr];
                    } else {
                        if (history.Count > 0) currentIndex = history.Pop();
                        else currentIndex = Mathf.Max(0, currentIndex - 1);
                    }
                    break;

                case PlayMode.RepeatAll:
                case PlayMode.Normal:
                default:
                    int step = Mathf.Clamp(advance, -1, 1);
                    int next = currentIndex + step;
                    if (next >= list.Count) next = (playMode == PlayMode.RepeatAll) ? 0 : list.Count - 1;
                    if (next < 0) next = 0;
                    currentIndex = next;
                    break;
            }
        }

        string fileNumber = list[currentIndex].FileNumber;
        return int.Parse(fileNumber);
    }

    /* ===================== INTERNOS ===================== */

    private void EnsureListReady() {
        if (menu == null || menu.GetFiltered() == null) return;
        if (shuffleOrder == null || shuffleOrder.Count != menu.FilteredCount())
            BuildShuffleOrder();
    }

    private void BuildShuffleOrder() {
        var n = menu != null ? menu.FilteredCount() : 0;
        shuffleOrder = new List<int>(n);
        for (int i = 0; i < n; i++) shuffleOrder.Add(i);

        // Fisher�Yates
        for (int i = n - 1; i > 0; i--) {
            int j = rng.Next(i + 1);
            (shuffleOrder[i], shuffleOrder[j]) = (shuffleOrder[j], shuffleOrder[i]);
        }
        shufflePtr = Mathf.Clamp(shuffleOrder.IndexOf(currentIndex), 0, Math.Max(0, n - 1));
    }

    private void RecenterShuffleOn(int index) {
        if (shuffleOrder == null || shuffleOrder.Count == 0) BuildShuffleOrder();
        int pos = shuffleOrder.IndexOf(index);
        shufflePtr = (pos >= 0) ? pos : 0;
    }

    /// <summary>
    /// Versi�n INOFENSIVA (sin efectos) para calcular a qu� �ndice ir�amos.
    /// No modifica ni historial, ni punteros, ni currentIndex.
    /// </summary>
    private int ComputeTargetIndex(int advance, int absoluteIndex) {
        var list = menu.GetFiltered();
        if (list == null || list.Count == 0) return 0;

        if (absoluteIndex >= 0)
            return Mathf.Clamp(absoluteIndex, 0, list.Count - 1);

        switch (playMode) {
            case PlayMode.RepeatOne:
                return currentIndex;

            case PlayMode.Shuffle:
                if (advance >= 0) {
                    if (list.Count == 0) return currentIndex;
                    int nextPtr = (shufflePtr + 1) % list.Count;
                    return shuffleOrder[nextPtr];
                } else {
                    // mirar sin consumir historial
                    if (history.Count > 0) {
                        int prev = history.Peek();
                        return Mathf.Clamp(prev, 0, list.Count - 1);
                    }
                    return Mathf.Max(0, currentIndex - 1);
                }

            case PlayMode.RepeatAll:
            case PlayMode.Normal:
            default:
                int step = Mathf.Clamp(advance, -1, 1);
                int next = currentIndex + step;
                if (next >= list.Count) next = (playMode == PlayMode.RepeatAll) ? 0 : list.Count - 1;
                if (next < 0) next = 0;
                return next;
        }
    }

    private SongLoader.SongMetadata LoadMetadataFromJson(string fileNumber) {
        string basePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "VGM Hall of Fame");
        string jsonPath = Path.Combine(basePath, $"info{fileNumber}.json");
        if (!File.Exists(jsonPath))
            return new SongLoader.SongMetadata { Title = "Unknown", Color1 = Color.black, Color2 = Color.black };

        string json = File.ReadAllText(jsonPath);
        return JsonUtility.FromJson<SongLoader.SongMetadata>(json);
    }

}
