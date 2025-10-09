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

    // Shuffle aleatorio: índice reservado para sincronizar preview (entrada) y carga (salida)
    private int? pendingShuffleIndex = null;

    /* ===================== API PÚBLICA (compatibilidad UI) ===================== */

    /// <summary>
    /// Reproduce desde un índice específico de la lista FILTRADA (sin transición).
    /// </summary>
    public void PlayFromFilteredIndex(int index)
    {
        int count = menu != null ? menu.FilteredCount() : 0;
        if (count <= 0) return;

        currentIndex = Mathf.Clamp(index, 0, count - 1);
        currentFileNumber = menu.GetFiltered()[currentIndex].FileNumber;

        // cualquier reserva de shuffle ya no aplica
        pendingShuffleIndex = null;

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
                if (currentIndex < 0) currentIndex = 0;
                PlayCurrent();
                return;

            case PlayMode.Shuffle:
                currentIndex = rng.Next(list.Count);
                break;

            case PlayMode.RepeatAll:
            case PlayMode.Normal:
            default:
                if (currentIndex < 0)
                {
                    currentIndex = 0;
                }
                else
                {
                    currentIndex++;
                    if (currentIndex >= list.Count)
                    {
                        if (playMode == PlayMode.RepeatAll) currentIndex = 0;
                        else { currentIndex = list.Count - 1; return; }
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
                currentIndex = rng.Next(list.Count);
                break;

            case PlayMode.RepeatOne:
                if (currentIndex < 0) currentIndex = Mathf.Max(0, list.Count - 1);
                PlayCurrent();
                return;

            case PlayMode.RepeatAll:
                if (currentIndex < 0)
                {
                    currentIndex = Mathf.Max(0, list.Count - 1);
                }
                else
                {
                    currentIndex = (currentIndex - 1 + list.Count) % list.Count;
                }
                break;

            case PlayMode.Normal:
            default:
                if (currentIndex < 0)
                {
                    currentIndex = Mathf.Max(0, list.Count - 1);
                }
                else
                {
                    currentIndex = Mathf.Max(0, currentIndex - 1);
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
        pendingShuffleIndex = null; // limpiar reserva pendiente al cambiar de modo

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
            currentIndex = (idx >= 0) ? idx : -1; // -1 si ya no está visible
        }
        else
        {
            currentIndex = 0; // robustez para lista vacía
        }

        // Cualquier reserva previa ya no es válida con lista nueva/orden nuevo
        pendingShuffleIndex = null;

        if (enableQueueDebug) LogState("NotifyFilteredListChanged");
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

        pendingShuffleIndex = null;

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
            pendingShuffleIndex = null; // limpiar cualquier reserva previa
        }
        else
        {
            switch (playMode)
            {
                case PlayMode.RepeatOne:
                    if (currentIndex < 0) currentIndex = 0;
                    break;

                case PlayMode.Shuffle:
                    // Usa la MISMA canción que el preview (si hay reserva). Si no, elige ahora.
                    if (pendingShuffleIndex.HasValue)
                        currentIndex = Mathf.Clamp(pendingShuffleIndex.Value, 0, list.Count - 1);
                    else
                        currentIndex = rng.Next(list.Count);

                    pendingShuffleIndex = null; // consumir la reserva
                    break;

                case PlayMode.RepeatAll:
                case PlayMode.Normal:
                default:
                    int step = Mathf.Clamp(advance, -1, 1);
                    if (currentIndex < 0)
                    {
                        currentIndex = (step >= 0) ? 0 : Mathf.Max(0, list.Count - 1);
                    }
                    else
                    {
                        int next = currentIndex + step;

                        if (next >= list.Count)
                            next = (playMode == PlayMode.RepeatAll) ? 0 : list.Count - 1;
                        else if (next < 0)
                            next = (playMode == PlayMode.RepeatAll) ? list.Count - 1 : 0;

                        currentIndex = next;
                    }
                    break;
            }
        }

        currentFileNumber = list[currentIndex].FileNumber;
        if (enableQueueDebug) LogState("ResolveTarget");
        return currentFileNumber;
    }

    // ===================== Accesores útiles ===================== //

    public bool IsFirstIndex()
    {
        var list = menu != null ? menu.GetFiltered() : null;
        if (list == null || list.Count == 0) return true;    // sin lista: tratar como primero
        return currentIndex <= 0;
    }

    // Helper público para saber si estamos en el último elemento de la lista filtrada
    public bool IsLastIndex()
    {
        var list = menu != null ? menu.GetFiltered() : null;
        if (list == null || list.Count == 0) return true;  // tratar como "último" si no hay lista
        if (currentIndex < 0) return false;                // estado virtual: no consideramos "último"
        return currentIndex >= list.Count - 1;
    }

    /* ===================== Internos ===================== */

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
                // Reserva un índice aleatorio para esta transición (colores de entrada)
                if (!pendingShuffleIndex.HasValue)
                    pendingShuffleIndex = rng.Next(list.Count);
                return Mathf.Clamp(pendingShuffleIndex.Value, 0, list.Count - 1);

            case PlayMode.RepeatAll:
            case PlayMode.Normal:
            default:
                int step = Mathf.Clamp(advance, -1, 1);
                if (currentIndex < 0)
                    return (step >= 0) ? 0 : Mathf.Max(0, list.Count - 1);

                int next = currentIndex + step;

                if (next >= list.Count)
                    next = (playMode == PlayMode.RepeatAll) ? 0 : list.Count - 1;
                else if (next < 0)
                    next = (playMode == PlayMode.RepeatAll) ? list.Count - 1 : 0;

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

        Debug.Log(
            $"[Queue::{tag}] mode={playMode} | curIndex={currentIndex} | curId={currentFileNumber} " +
            $"| list=({(list != null ? list.Count : 0)}) {seq}"
        );

        // ⬇️ Nada de “próximas” en Shuffle (evita consumir RNG y predicciones falsas)
        if (playMode != PlayMode.Shuffle)
            DebugNextUpPreview(10);
        else
            Debug.Log("[Queue::NextUp] Shuffle: Orden aleatorio en tiempo real (sin lista predeterminada).");
    }

    /// <summary>
    /// Imprime por consola el orden de las próximas N canciones según el MODO actual.
    /// </summary>
    private void DebugNextUpPreview(int n)
    {
        if (!enableQueueDebug) return;

        var list = menu != null ? menu.GetFiltered() : null;
        if (list == null || list.Count == 0) { Debug.Log("[Queue::NextUp] (lista vacía)"); return; }

        if (playMode == PlayMode.Shuffle)
        {
            // No consumir RNG ni inventar una secuencia
            Debug.Log("[Queue::NextUp] (Shuffle) Próximas: aleatorias (no precomputadas).");
            return;
        }

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
            case PlayMode.RepeatOne:
                {
                    int idx = (currentIndex < 0) ? 0 : currentIndex;
                    for (int i = 0; i < n; i++) result.Add(list[idx].FileNumber);
                    break;
                }
            case PlayMode.RepeatAll:
                {
                    int idx = (currentIndex < 0) ? -1 : currentIndex;
                    for (int i = 0; i < n; i++) { idx = (idx + 1) % list.Count; result.Add(list[idx].FileNumber); }
                    break;
                }
            case PlayMode.Normal:
            default:
                {
                    int idx = (currentIndex < 0) ? -1 : currentIndex;
                    for (int i = 0; i < n; i++) { idx = idx + 1; if (idx >= list.Count) break; result.Add(list[idx].FileNumber); }
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

        pendingShuffleIndex = null;

        if (enableQueueDebug) LogState("SetCurrentNoPlayback");
        return true;
    }

}