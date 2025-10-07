using UnityEngine;
using UnityEngine.Video;

public class BackgroundVideoController : MonoBehaviour
{

    private VideoPlayer videoPlayer;

    void Start()
    {
        videoPlayer = GetComponent<VideoPlayer>();
        videoPlayer.isLooping = true; // Asegura que esté en bucle
        videoPlayer.Play();
    }

}