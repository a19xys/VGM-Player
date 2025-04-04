using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class LyricsController : MonoBehaviour {
    [Header("UI Element")]
    public TextMeshProUGUI lyricsText;

    [Header("Audio Source")]
    public AudioSource audioSource;

    private List<LyricLine> lyrics = new List<LyricLine>();
    private int currentLyricIndex = -1;

    void Update() {
        if (audioSource == null || !audioSource.isPlaying || lyrics.Count == 0) return;

        float currentTime = audioSource.time;
        UpdateLyricsBasedOnTime(currentTime);
    }

    public void LoadLyrics(string rawLyrics) {
        // Limpiar datos previos
        lyrics.Clear();
        currentLyricIndex = -1;

        // Parsear las letras con sus marcas de tiempo
        string[] lines = rawLyrics.Split(new[] { '[', ']' }, System.StringSplitOptions.RemoveEmptyEntries);

        for (int i = 0; i < lines.Length; i += 2) {
            if (float.TryParse(ParseTimestamp(lines[i]), out float timestamp)) {
                lyrics.Add(new LyricLine { Timestamp = timestamp, Text = lines[i + 1].Trim() });
            }
        }

        // Ordenar las letras por marca de tiempo
        lyrics.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
    }

    private string ParseTimestamp(string timestamp) {
        string[] parts = timestamp.Split(':');
        if (parts.Length == 2 &&
            int.TryParse(parts[0], out int minutes) &&
            float.TryParse(parts[1], out float seconds)) {
            return (minutes * 60 + seconds).ToString();
        }
        return "0";
    }

    private void UpdateLyricsBasedOnTime(float currentTime) {
        for (int i = 0; i < lyrics.Count; i++) {
            float startTime = lyrics[i].Timestamp;
            float endTime = (i + 1 < lyrics.Count) ? lyrics[i + 1].Timestamp : Mathf.Infinity;

            if (currentTime >= startTime && currentTime < endTime) {
                if (currentLyricIndex != i) {
                    currentLyricIndex = i;
                    DisplayLyric(lyrics[i].Text);
                }
                return;
            }
        }

        // Si no hay letras activas, limpiar
        if (currentLyricIndex != -1) {
            currentLyricIndex = -1;
            ClearLyrics();
        }
    }

    private void DisplayLyric(string lyric) {
        if (lyricsText != null) {
            lyricsText.text = lyric;
        }
    }

    public void ClearLyrics() {
        if (lyricsText != null) {
            lyricsText.text = "";
        }
    }

    private class LyricLine {
        public float Timestamp;
        public string Text;
    }
}