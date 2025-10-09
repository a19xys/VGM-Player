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

    [Tooltip("Activa los logs detallados de la cola en consola.")]
    public bool enableQueueDebug = false;

    // Índice actual dentro de la lista FILTRADA del menú.
    // -1 significa "estado virtual antes del primer elemento" (p.ej. al aplicar un filtro que oculta la canción actual).
    private int currentIndex = 0;

    // Recordatorio de la canción actual por FileNumber (para reubicar tras cambios de filtro/orden)
    [SerializeField] private string currentFileNumber = null;

    // Estado de shuffle
    private readonly System.Random rng = new System.Random();
    private readonly Stack<int> history = new Stack<int>();  // para "Previous" en Shuffle
    private List<int> shuffleOrder = new List<int>();

    // Puntero dentro del shuffleOrder. -1 significa "antes del primer elemento" (virtual).
    private int shufflePtr = 0;

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
        currentFileNumber = menu.GetFiltered()[currentIndex].FileNumber;

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

        // Si estamos en estado virtual (-1) no hay "actual" que reproducir: saltamos a primera de la lista.
        if (currentIndex < 0) currentIndex = 0;

        string fileNumber = list[currentIndex].FileNumber; // STRING sin padding
        currentFileNumber = fileNumber;                    // memoriza “qué suena”

        if (enableQueueDebug) LogState("PlayCurrent");

        if (loader != null)
        {
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

        switch (playMode)
        {
            case PlayMode.RepeatOne:
                // Si estamos "fuera" (-1), no tiene sentido repetir. Ir a primera y reproducir.
                if (currentIndex < 0) currentIndex = 0;
                PlayCurrent();
                return;

            case PlayMode.Shuffle:
                if (currentIndex < 0)
                {
                    // Venimos de estado virtual (p.ej., filtrado ocultó la actual): empezar shuffle por el principio.
                    history.Clear();
                    shufflePtr = 0;
                    currentIndex = shuffleOrder[shufflePtr];
                }
                else
                {
                    history.Push(currentIndex);
                    shufflePtr = WrapShufflePtr(shufflePtr + 1, list.Count);
                    currentIndex = shuffleOrder[shufflePtr];
                }
                break;

            case PlayMode.RepeatAll:
            case PlayMode.Normal:
            default:
                if (currentIndex < 0)
                {
                    // Estado virtual: el primer "Next" va a la PRIMERA canción visible.
                    currentIndex = 0;
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
                break;
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

        switch (playMode)
        {
            case PlayMode.Shuffle:
                if (currentIndex < 0)
                {
                    // Estado virtual: el primer "Previous" va al ÚLTIMO del shuffle.
                    history.Clear();
                    shufflePtr = list.Count - 1;
                    currentIndex = shuffleOrder[shufflePtr];
                }
                else
                {
                    if (history.Count > 0) currentIndex = history.Pop();
                    else currentIndex = Mathf.Max(0, currentIndex - 1); // sin wrap
                }
                break;

            case PlayMode.RepeatOne:
                // Si estamos "fuera" (-1), caer al final lógico.
                if (currentIndex < 0) currentIndex = Mathf.Max(0, list.Count - 1);
                PlayCurrent();
                return;

            case PlayMode.RepeatAll:
                if (currentIndex < 0)
                {
                    // Estado virtual: ir a la ÚLTIMA canción visible.
                    currentIndex = Mathf.Max(0, list.Count - 1);
                }
                else
                {
                    // Envolver hacia atrás.
                    int count = list.Count;
                    currentIndex = (currentIndex - 1 + count) % count;
                }
                break;

            case PlayMode.Normal:
            default:
                if (currentIndex < 0)
                {
                    // Estado virtual: el primer "Previous" va a la ÚLTIMA canción visible.
                    currentIndex = Mathf.Max(0, list.Count - 1);
                }
                else
                {
                    currentIndex = Mathf.Max(0, currentIndex - 1); // sin wrap
                }
                break;
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

            // Si estamos en estado virtual (-1), queremos que el primer Next vaya al primero del shuffle.
            shufflePtr = (currentIndex < 0) ? -1 : IndexOfInShuffle(currentIndex);
            history.Clear();
        }

        if (enableQueueDebug) LogState($"SetMode -> {playMode}");
        OnPlayModeChanged?.Invoke(playMode);
    }

    /// <summary>
    /// Debe llamarse cuando cambie el filtrado/orden en el menú.
    /// Recalcula el orden shuffle y mantiene la canción actual si sigue visible.
    /// Si NO sigue visible, entra en estado virtual (currentIndex = -1).
    /// </summary>
    public void NotifyFilteredListChanged()
    {
        var list = menu != null ? menu.GetFiltered() : null;

        if (list != null && list.Count > 0 && !string.IsNullOrEmpty(currentFileNumber))
        {
            int idx = list.FindIndex(s => s.FileNumber == currentFileNumber);
            if (idx >= 0)
            {
                // La misma canción sigue visible: mantener puntero.
                currentIndex = idx;
            }
            else
            {
                // La actual ya no está visible tras el filtro: entrar en estado virtual.
                currentIndex = -1;
            }
        }
        else
        {
            // Lista vacía → forzar 0 (sin usar estado virtual) por robustez.
            currentIndex = 0;
        }

        BuildShuffleOrder();

        if (currentIndex >= 0) RecenterShuffleOn(currentIndex);
        else shufflePtr = -1; // virtual: primer Next → primer elemento; Previous → último

        if (enableQueueDebug) LogState("NotifyFilteredListChanged");
        // No vaciamos history aquí: para Shuffle ya lo hacemos al entrar en modo, o cuando toque.
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
        currentFileNumber = idToken;

        RecenterShuffleOn(currentIndex);

        if (enableQueueDebug) LogState("SyncWithSongId");
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
                    // Si venimos de estado virtual, caer a 0; si no, nos quedamos tal cual.
                    if (currentIndex < 0) currentIndex = 0;
                    break;

                case PlayMode.Shuffle:
                    if (advance >= 0)
                    {
                        if (currentIndex < 0)
                        {
                            history.Clear();
                            shufflePtr = 0;
                            currentIndex = shuffleOrder[shufflePtr];
                        }
                        else
                        {
                            history.Push(currentIndex);
                            shufflePtr = WrapShufflePtr(shufflePtr + 1, list.Count);
                            currentIndex = shuffleOrder[shufflePtr];
                        }
                    }
                    else
                    {
                        if (currentIndex < 0)
                        {
                            history.Clear();
                            shufflePtr = list.Count - 1;
                            currentIndex = shuffleOrder[shufflePtr];
                        }
                        else
                        {
                            if (history.Count > 0) currentIndex = history.Pop();
                            else currentIndex = Mathf.Max(0, currentIndex - 1);
                        }
                    }
                    break;

                case PlayMode.RepeatAll:
                case PlayMode.Normal:
                default:
                    int step = Mathf.Clamp(advance, -1, 1);

                    if (currentIndex < 0)
                    {
                        // Estado virtual: avance+ → primera; avance- → última
                        currentIndex = (step >= 0) ? 0 : Mathf.Max(0, list.Count - 1);
                    }
                    else
                    {
                        int next = currentIndex + step;
                        if (next >= list.Count) next = (playMode == PlayMode.RepeatAll) ? 0 : list.Count - 1;
                        if (next < 0) next = 0;
                        currentIndex = next;
                    }
                    break;
            }
        }

        currentFileNumber = list[currentIndex].FileNumber; // STRING exacto (sin padding)
        if (enableQueueDebug) LogState("ResolveTarget");
        return currentFileNumber;
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

        // Reajustar shufflePtr:
        //  - Si currentIndex >= 0 → centrar en esa posición
        //  - Si currentIndex < 0 → estado virtual → shufflePtr = -1 (antes del primero)
        shufflePtr = (currentIndex >= 0)
            ? Mathf.Clamp(IndexOfInShuffle(currentIndex), 0, Mathf.Max(0, n - 1))
            : -1;

        if (enableQueueDebug)
        {
            var list = menu != null ? menu.GetFiltered() : null;
            string order = "";
            if (list != null)
            {
                for (int k = 0; k < shuffleOrder.Count; k++)
                {
                    int idx = shuffleOrder[k];
                    if (idx >= 0 && idx < list.Count)
                        order += list[idx].FileNumber + (k < shuffleOrder.Count - 1 ? " -> " : "");
                }
            }
            Debug.Log($"[Queue] BuildShuffleOrder: {order} | ptr={shufflePtr} (curIdx={currentIndex})");
            DebugNextUpPreview(10);
        }
    }

    private void RecenterShuffleOn(int index)
    {
        if (shuffleOrder == null || shuffleOrder.Count == 0) BuildShuffleOrder();
        if (index < 0) { shufflePtr = -1; return; }
        int pos = shuffleOrder.IndexOf(index);
        shufflePtr = (pos >= 0) ? pos : -1;
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
                return (currentIndex < 0) ? 0 : currentIndex;

            case PlayMode.Shuffle:
                if (advance >= 0)
                {
                    if (list.Count == 0) return (currentIndex < 0) ? 0 : currentIndex;
                    int nextPtr = (shufflePtr < 0) ? 0 : (shufflePtr + 1) % list.Count;
                    return shuffleOrder[nextPtr];
                }
                else
                {
                    if (shufflePtr < 0) return shuffleOrder[Mathf.Max(0, list.Count - 1)];
                    if (history.Count > 0) return Mathf.Clamp(history.Peek(), 0, list.Count - 1);
                    return Mathf.Max(0, currentIndex - 1);
                }

            case PlayMode.RepeatAll:
            case PlayMode.Normal:
            default:
                int step = Mathf.Clamp(advance, -1, 1);
                if (currentIndex < 0)
                    return (step >= 0) ? 0 : Mathf.Max(0, list.Count - 1);

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

    /* ===================== Utils ===================== */

    private int WrapShufflePtr(int value, int count)
    {
        if (count <= 0) return -1;
        if (value < 0) return count - 1;
        if (value >= count) return 0;
        return value;
    }

    private int IndexOfInShuffle(int indexInList)
    {
        return shuffleOrder != null ? shuffleOrder.IndexOf(indexInList) : -1;
    }

    /* ===================== Debug helpers ===================== */

    private void LogState(string tag)
    {
        if (!enableQueueDebug) return;

        var list = menu != null ? menu.GetFiltered() : null;

        string seq = "";
        if (list != null)
        {
            for (int i = 0; i < list.Count; i++)
            {
                string mark = (i == currentIndex) ? "*" : "";
                seq += $"{mark}{list[i].FileNumber}{mark}";
                if (i < list.Count - 1) seq += " | ";
            }
        }

        string shuf = "";
        if (list != null && shuffleOrder != null && shuffleOrder.Count == list.Count)
        {
            for (int k = 0; k < shuffleOrder.Count; k++)
            {
                int idx = shuffleOrder[k];
                if (idx >= 0 && idx < list.Count)
                    shuf += list[idx].FileNumber + (k < shuffleOrder.Count - 1 ? " -> " : "");
            }
        }

        string hist = "";
        if (history != null && history.Count > 0)
        {
            hist = string.Join(",", history.ToArray());
        }

        Debug.Log(
            $"[Queue::{tag}] mode={playMode} | curIndex={currentIndex} | curId={currentFileNumber} " +
            $"| list=({(list != null ? list.Count : 0)}) {seq} " +
            $"| shufflePtr={shufflePtr} | shuffle=[{shuf}] | history=[{hist}]"
        );

        DebugNextUpPreview(10);
    }

    /// <summary>
    /// Imprime por consola el orden de las próximas N canciones según el MODO actual.
    /// - Shuffle: sigue shuffleOrder a partir de shufflePtr+1 (o 0 si ptr=-1), con wrap.
    /// - RepeatOne: repite la actual N veces (si currentIndex<0, usa la primera).
    /// - RepeatAll: lista lineal desde currentIndex+1 con wrap.
    /// - Normal: lista lineal hasta el final (sin wrap).
    /// </summary>
    private void DebugNextUpPreview(int n)
    {
        if (!enableQueueDebug) return;

        var list = menu != null ? menu.GetFiltered() : null;
        if (list == null || list.Count == 0) { Debug.Log("[Queue::NextUp] (lista vacía)"); return; }

        List<string> nextUp = GetNextUpSequence(n);
        Debug.Log($"[Queue::NextUp] Próximas {nextUp.Count} → {string.Join(" | ", nextUp)}");
    }

    /// <summary>
    /// Devuelve la secuencia de las próximas N canciones, sin consumir estado.
    /// </summary>
    private List<string> GetNextUpSequence(int n)
    {
        var list = menu != null ? menu.GetFiltered() : null;
        var result = new List<string>(Mathf.Max(0, n));
        if (list == null || list.Count == 0 || n <= 0) return result;

        switch (playMode)
        {
            case PlayMode.Shuffle:
                {
                    int ptr = (shufflePtr < 0) ? -1 : shufflePtr;
                    for (int i = 0; i < n; i++)
                    {
                        ptr = WrapShufflePtr(ptr + 1, list.Count);
                        int idx = shuffleOrder[ptr];
                        result.Add(list[idx].FileNumber);
                    }
                    break;
                }

            case PlayMode.RepeatOne:
                {
                    int idx = (currentIndex < 0) ? 0 : currentIndex;
                    for (int i = 0; i < n; i++)
                        result.Add(list[idx].FileNumber);
                    break;
                }

            case PlayMode.RepeatAll:
                {
                    int idx = (currentIndex < 0) ? -1 : currentIndex;
                    for (int i = 0; i < n; i++)
                    {
                        idx = (idx + 1) % list.Count;
                        result.Add(list[idx].FileNumber);
                    }
                    break;
                }

            case PlayMode.Normal:
            default:
                {
                    int idx = (currentIndex < 0) ? -1 : currentIndex;
                    for (int i = 0; i < n; i++)
                    {
                        idx = idx + 1;
                        if (idx >= list.Count) break; // en Normal no hay wrap
                        result.Add(list[idx].FileNumber);
                    }
                    break;
                }
        }

        return result;
    }

    // Fija el índice actual por FileNumber SIN disparar carga ni reproducción.
    // Devuelve true si encontró el id en la lista filtrada.
    public bool SetCurrentByFileNumberNoPlayback(string idToken)
    {
        var list = menu != null ? menu.GetFiltered() : null;
        if (list == null || list.Count == 0) return false;

        int idx = list.FindIndex(s => s.FileNumber == idToken);
        if (idx < 0) return false;

        currentIndex = idx;
        currentFileNumber = idToken;
        RecenterShuffleOn(currentIndex);

        if (enableQueueDebug) LogState("SetCurrentNoPlayback");
        return true;
    }

}