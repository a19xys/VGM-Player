using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using TMPro;

/// <summary>
/// Controla el render de letras sincronizadas:
/// - Se suscribe a SongLoader.OnMetadataLoaded para limpiar/cargar letras al cambiar de canción.
/// - Actualiza la línea visible en función de audioSource.time mientras reproduce.
/// - Si la nueva canción no tiene 'Lyrics', borra el texto (evita que se queden los anteriores).
/// Formato esperado: "[M:SS] texto [M:SS] texto ..." (pares [timestamp] + línea).
/// </summary>
public class LyricsController : MonoBehaviour
{
    [Header("Refs")]
    public TextMeshProUGUI lyricsText;   // Dónde se dibujan las letras
    public AudioSource audioSource;       // Debe apuntar al mismo AudioSource que usa SongLoader
    public SongLoader songLoader;         // Para escuchar OnMetadataLoaded (asignar en Inspector)

    // Estructura interna
    private readonly List<LyricLine> lyrics = new List<LyricLine>();
    private int currentLyricIndex = -1;

    /* ============================ Ciclo de vida ============================ */

    private void Start()
    {
        // Suscribirse al cambio de canción (metadatos)
        if (songLoader != null)
            songLoader.OnMetadataLoaded += HandleMetadataLoaded;

        // Si el proyecto arranca con una canción ya cargada, aplicar estado inicial
        if (songLoader != null && songLoader.metadata != null)
            HandleMetadataLoaded(songLoader.metadata);
        else
            ClearLyrics(); // garantizar estado visual limpio al arrancar
    }

    private void OnDestroy()
    {
        if (songLoader != null)
            songLoader.OnMetadataLoaded -= HandleMetadataLoaded;
    }

    /* ============================ Evento metadatos ============================ */

    /// <summary>
    /// Llega cada vez que SongLoader carga metadatos de una canción nueva.
    /// Limpia siempre; y si hay 'Lyrics' en metadatos, los parsea/carga.
    /// </summary>
    private void HandleMetadataLoaded(SongLoader.SongMetadata m)
    {
        // Limpiar SIEMPRE lo anterior para que no "sangren" letras viejas
        lyrics.Clear();
        currentLyricIndex = -1;
        if (lyricsText) lyricsText.text = string.Empty;

        if (m == null) return;

        // Si no hay letras, nos quedamos en blanco
        if (string.IsNullOrWhiteSpace(m.Lyrics))
            return;

        LoadLyrics(m.Lyrics); // parsea y prepara la lista de líneas con timestamps
        // No hace falta escribir nada ahora; la actualización en Update() mostrará la línea adecuada.
    }

    /* ============================ Runtime Update ============================ */

    private void Update()
    {
        if (audioSource == null || !audioSource.isPlaying) return;
        if (lyrics.Count == 0) return; // no hay letras para esta canción

        float t = audioSource.time;
        UpdateLyricsBasedOnTime(t);
    }

    /* ============================ Carga / Parseo ============================ */

    /// <summary>
    /// Carga un bloque raw de letras en formato [M:SS]texto[MM:SS]texto...
    /// </summary>
    public void LoadLyrics(string rawLyrics)
    {
        lyrics.Clear();
        currentLyricIndex = -1;

        if (string.IsNullOrWhiteSpace(rawLyrics))
        {
            if (lyricsText) lyricsText.text = string.Empty;
            return;
        }

        // Partimos por corchetes y tomamos pares (timestamp, texto)
        string[] parts = rawLyrics.Split(new[] { '[', ']' }, System.StringSplitOptions.RemoveEmptyEntries);

        for (int i = 0; i + 1 < parts.Length; i += 2)
        {
            if (TryParseTimestampToSeconds(parts[i], out float ts))
            {
                string line = parts[i + 1].Trim();
                lyrics.Add(new LyricLine { Timestamp = ts, Text = line });
            }
        }

        // Orden temporal por si el archivo viene desordenado
        lyrics.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));

        // Mostrar vacío hasta que el audio "entre" en el primer tramo
        if (lyricsText) lyricsText.text = string.Empty;
    }

    private bool TryParseTimestampToSeconds(string s, out float secondsOut) {
        // Acepta M:SS, MM:SS, y SS con decimales: M:SS.s, M:SS.ss, M:SS.sss
        secondsOut = 0f;

        // Normaliza coma → punto para evitar problemas de cultura
        s = s.Trim().Replace(',', '.');

        var parts = s.Split(':');
        if (parts.Length != 2) return false;

        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int minutes))
            return false;

        if (!float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float seconds))
            return false;

        secondsOut = minutes * 60f + seconds;
        return true;
    }

    /* ============================ Lógica de cambio de línea ============================ */

    private void UpdateLyricsBasedOnTime(float currentTime)
    {
        // Busca el tramo [i, i+1)
        for (int i = 0; i < lyrics.Count; i++)
        {
            float start = lyrics[i].Timestamp;
            float end = (i + 1 < lyrics.Count) ? lyrics[i + 1].Timestamp : float.PositiveInfinity;

            if (currentTime >= start && currentTime < end)
            {
                if (currentLyricIndex != i)
                {
                    currentLyricIndex = i;
                    DisplayLyric(lyrics[i].Text);
                }
                return;
            }
        }

        // Fuera de cualquier tramo -> limpiar
        if (currentLyricIndex != -1)
        {
            currentLyricIndex = -1;
            if (lyricsText) lyricsText.text = string.Empty;
        }
    }

    private void DisplayLyric(string lyric)
    {
        if (lyricsText != null)
            lyricsText.text = lyric;
    }

    /// <summary>
    /// Limpia visualmente el texto y resetea índices (sin borrar la lista).
    /// Útil si quieres ocultar letras cuando se pause, etc.
    /// </summary>
    public void ClearLyrics()
    {
        currentLyricIndex = -1;
        if (lyricsText) lyricsText.text = string.Empty;
    }

    /* ============================ DTO interno ============================ */

    private class LyricLine
    {
        public float Timestamp;
        public string Text;
    }
}