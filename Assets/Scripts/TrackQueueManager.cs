using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public enum PlayMode { Normal, RepeatOne, RepeatAll, Shuffle }

public class TrackQueueManager : MonoBehaviour
{
    public event Action<PlayMode> OnPlayModeChanged;

    [Header("Refs")]
    public SongMenuManager menu;   // Asignar en Inspector
    public SongLoader loader;      // Asignar en Inspector

    [Header("State")]
    public PlayMode playMode = PlayMode.Normal;

    // Índice actual dentro de la lista FILTRADA del menú
    private int currentIndex = 0;

    // Estado de shuffle
    private readonly System.Random rng = new System.Random();
    private readonly Stack<int> history = new Stack<int>(); // para "Previous" en Shuffle
    private List<int> shuffleOrder = new List<int>();
    private int shufflePtr = 0; // puntero dentro de shuffleOrder

    /* ===================== API PÚBLICA (compatibilidad UI) ===================== */

    /// <summary>
    /// Reproduce desde un índice específico de la lista FILTRADA (sin transición).
    /// </summary>
    public void PlayFromFilteredIndex(int index)
    {
        EnsureListReady();
        int count = menu != null ? menu.FilteredCount() : 0;
        if (count <= 0) return;

        currentIndex = Mathf.Clamp(index, 0, count - 1);
        RecenterShuffleOn(currentIndex);
        PlayCurrent();
    }

    /// <summary>
    /// Reproduce la canción en currentIndex (sin transición).
    /// </summary>
    public void PlayCurrent()
    {
        var list = menu != null ? menu.GetFiltered() : null;
        if (list == null || list.Count == 0) return;

        string fileNumber = list[currentIndex].FileNumber; // STRING sin padding
        if (loader != null) {
            loader.LoadSongMetadataInstant(fileNumber);
            StartCoroutine(loader.PrepareAudioClipRoutine(fileNumber, true));
            StartCoroutine(loader.PrepareVideosRoutine(fileNumber, true));
        }
    }

    /// <summary>
    /// Siguiente pista según el modo, y reproduce (sin transición).
    /// </summary>
    public void Next()
    {
        var list = menu != null ? menu.GetFiltered() : null;
        if (list == null || list.Count == 0) return;

        if (playMode == PlayMode.RepeatOne)
        {
            PlayCurrent();
            return;
        }

        if (playMode == PlayMode.Shuffle)
        {
            history.Push(currentIndex);
            shufflePtr = (shufflePtr + 1) % list.Count;
            currentIndex = shuffleOrder[shufflePtr];
        }
        else
        {
            currentIndex++;
            if (currentIndex >= list.Count)
            {
                if (playMode == PlayMode.RepeatAll) currentIndex = 0;
                else { currentIndex = list.Count - 1; return; } // quedarse al final en Normal
            }
        }

        PlayCurrent();
    }

    /// <summary>
    /// Pista anterior según el modo, y reproduce (sin transición).
    /// </summary>
    public void Previous()
    {
        var list = menu != null ? menu.GetFiltered() : null;
        if (list == null || list.Count == 0) return;

        if (playMode == PlayMode.Shuffle && history.Count > 0)
        {
            currentIndex = history.Pop();
        }
        else
        {
            currentIndex = Mathf.Max(0, currentIndex - 1);
        }

        PlayCurrent();
    }

    /// <summary>
    /// Cambia el modo de reproducción. Reconstruye shuffle si procede.
    /// </summary>
    public void SetMode(PlayMode mode)
    {
        if (playMode == mode) return;
        playMode = mode;

        if (playMode == PlayMode.Shuffle)
        {
            BuildShuffleOrder();
            RecenterShuffleOn(currentIndex);
            history.Clear();
        }

        OnPlayModeChanged?.Invoke(playMode);
    }

    /// <summary>
    /// Debe llamarse cuando cambie el filtrado/orden en el menú.
    /// Recalcula el orden shuffle y mantiene el puntero en rango.
    /// </summary>
    public void NotifyFilteredListChanged()
    {
        int count = menu != null ? menu.FilteredCount() : 0;
        currentIndex = Mathf.Clamp(currentIndex, 0, Mathf.Max(0, count - 1));
        BuildShuffleOrder();
        RecenterShuffleOn(currentIndex);
        // No vaciamos history aquí para poder volver en Shuffle a pistas previas si procede
    }

    /// <summary>
    /// Sincroniza la cola con un fileNumber STRING exacto (sin padding).
    /// Reproduce esa canción y centra el shuffle en ese índice. (sin transición)
    /// </summary>
    public void SyncWithSongId(string idToken)
    {
        var list = menu != null ? menu.GetFiltered() : null;
        if (list == null || list.Count == 0) return;

        int idx = list.FindIndex(s => s.FileNumber == idToken);
        if (idx < 0) return;

        currentIndex = idx;
        RecenterShuffleOn(currentIndex);
        PlayCurrent();
    }

    /* =========== API PARA TRANSICIÓN (string-first) =========== */

    /// <summary>
    /// Devuelve los metadatos de la pista destino SIN tocar el estado interno (para pintar bloques).
    /// </summary>
    public SongLoader.SongMetadata PeekMetadata(int advance, int absoluteIndex)
    {
        int idx = ComputeTargetIndex(advance, absoluteIndex);
        var list = menu != null ? menu.GetFiltered() : null;
        if (list == null || list.Count == 0 || idx < 0 || idx >= list.Count)
            return new SongLoader.SongMetadata { Title = "Unknown", Color1 = Color.black, Color2 = Color.black };

        string fileNumber = list[idx].FileNumber; // STRING
        return LoadMetadataFromJson(fileNumber);
    }

    /// <summary>
    /// Resuelve el fileNumber (string) de la pista destino y ACTUALIZA el estado de la cola.
    /// Debe llamarse cuando la pantalla ya está cubierta, justo antes de cargar la canción.
    /// </summary>
    public string ResolveTargetFileNumber(int advance, int absoluteIndex)
    {
        var list = menu != null ? menu.GetFiltered() : null;
        if (list == null || list.Count == 0) return null;

        if (absoluteIndex >= 0)
        {
            currentIndex = Mathf.Clamp(absoluteIndex, 0, list.Count - 1);
            RecenterShuffleOn(currentIndex);
            history.Clear(); // opcional: empezar historial desde este punto
        }
        else
        {
            switch (playMode)
            {
                case PlayMode.RepeatOne:
                    // currentIndex se queda tal cual
                    break;

                case PlayMode.Shuffle:
                    if (advance >= 0)
                    {
                        history.Push(currentIndex);
                        shufflePtr = (shufflePtr + 1) % list.Count;
                        currentIndex = shuffleOrder[shufflePtr];
                    }
                    else
                    {
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

        return list[currentIndex].FileNumber; // STRING exacto (sin padding)
    }

    /* ===================== Internos ===================== */

    private void EnsureListReady()
    {
        if (menu == null || menu.GetFiltered() == null) return;
        if (shuffleOrder == null || shuffleOrder.Count != menu.FilteredCount())
            BuildShuffleOrder();
    }

    private void BuildShuffleOrder()
    {
        int n = menu != null ? menu.FilteredCount() : 0;
        shuffleOrder = new List<int>(n);
        for (int i = 0; i < n; i++) shuffleOrder.Add(i);

        // Fisher–Yates
        for (int i = n - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (shuffleOrder[i], shuffleOrder[j]) = (shuffleOrder[j], shuffleOrder[i]);
        }

        shufflePtr = Mathf.Clamp(shuffleOrder.IndexOf(currentIndex), 0, Mathf.Max(0, n - 1));
    }

    private void RecenterShuffleOn(int index)
    {
        if (shuffleOrder == null || shuffleOrder.Count == 0) BuildShuffleOrder();
        int pos = shuffleOrder.IndexOf(index);
        shufflePtr = (pos >= 0) ? pos : 0;
    }

    /// <summary>
    /// Versión INOFENSIVA (sin efectos) para calcular a qué índice iríamos.
    /// No modifica ni historial, ni punteros, ni currentIndex.
    /// </summary>
    private int ComputeTargetIndex(int advance, int absoluteIndex)
    {
        var list = menu != null ? menu.GetFiltered() : null;
        if (list == null || list.Count == 0) return 0;

        if (absoluteIndex >= 0)
            return Mathf.Clamp(absoluteIndex, 0, list.Count - 1);

        switch (playMode)
        {
            case PlayMode.RepeatOne:
                return currentIndex;

            case PlayMode.Shuffle:
                if (advance >= 0)
                {
                    if (list.Count == 0) return currentIndex;
                    int nextPtr = (shufflePtr + 1) % list.Count;
                    return shuffleOrder[nextPtr];
                }
                else
                {
                    // mirar sin consumir historial
                    if (history.Count > 0) return Mathf.Clamp(history.Peek(), 0, list.Count - 1);
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

    private SongLoader.SongMetadata LoadMetadataFromJson(string fileNumber)
    {
        string basePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "VGM Hall of Fame");
        string jsonPath = Path.Combine(basePath, $"info{fileNumber}.json"); // SIN padding

        if (!File.Exists(jsonPath))
            return new SongLoader.SongMetadata { Title = "Unknown", Color1 = Color.black, Color2 = Color.black };

        string json = File.ReadAllText(jsonPath);
        return JsonUtility.FromJson<SongLoader.SongMetadata>(json);
    }
}