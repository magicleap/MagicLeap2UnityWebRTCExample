using System.Collections;
using UnityEngine;
using UnityEngine.Android;
using UnityEngine.Experimental.Rendering;

/// <summary>
/// Manages obtaining a render texture from a web camera in Unity.
/// This includes initializing the camera, and converting the camera frames to a RenderTexture.
/// </summary>
public class WebCameraManager : MonoBehaviour, ICameraDeviceManager
{
    [SerializeField, Tooltip("The renderer to show the camera capture on RGB format")]
    private Renderer _screenRendererRGB = null;

    [SerializeField, Tooltip("(Can be null) When set, the script will look for a webcamera with this name and use it as the source if it exists. " +
                             "Will default to the first device if not valid or not specified.")]
    private string webCameraName = "Default";

    private bool _isCameraConfiguredAndReady = false;
    private RenderTexture _readTexture;
    private WebCamTexture _webCamTexture;

    private const int WIDTH = 1440;
    private const int HEIGHT = 1080;

    /// <summary>
    /// Gets the Render Texture that represents the web camera.
    /// </summary>
    public RenderTexture CameraTexture => _readTexture;

    public bool IsConfiguredAndReady => _isCameraConfiguredAndReady;

    /// <summary>
    /// Starts the media stream if the camera permission is granted.
    /// </summary>
    public void StartMedia()
    {
        // Only "Camera 2" is supported when using the WebCamera texture on Magic Leap 2
        if (Application.platform == RuntimePlatform.Android
            && SystemInfo.deviceModel == "Magic Leap Magic Leap 2")
        {
            webCameraName = "Camera 2";
        }

        if (Permission.HasUserAuthorizedPermission(Permission.Camera))
        {
            Debug.Log("Permission Granted, Starting WebCamTexture");
            if (!_isCameraConfiguredAndReady)
            {
                StartCoroutine(InitializeWebCamera());
            }
            else
            {
                Debug.Log("Camera is already started.");
            }
        }
        else
        {
            Debug.LogError("Camera will not work, permission has not been granted.");
        }
    }

    /// <summary>
    /// Stops the media stream.
    /// </summary>
    public void StopMedia()
    {
        StopCamera();
    }

    /// <summary>
    /// Stops the WebCamTexture and cancels the conversion coroutine if it's running.
    /// </summary>
    private void StopCamera()
    {
        if (_isCameraConfiguredAndReady)
        {
            _webCamTexture.Stop();
            _isCameraConfiguredAndReady = false;
        }
    }


    /// <summary>
    /// Initializes the camera in the editor.
    /// </summary>
    private IEnumerator InitializeWebCamera()
    {
        // Select the first available or specified web camera
        WebCamDevice userCameraDevice = WebCamTexture.devices[0];

        foreach (var webcam in WebCamTexture.devices)
        {
            Debug.Log($"Available webcam devices: {webcam.name}");
            if (webcam.name == webCameraName)
            {
                userCameraDevice = webcam;
                break;
            }
        }

        // Start the webcam texture
        _webCamTexture = new WebCamTexture(userCameraDevice.name, WIDTH, HEIGHT, 30);

        yield return new WaitForEndOfFrame();

        _webCamTexture.Play();

        yield return new WaitUntil(() => _webCamTexture.didUpdateThisFrame);

        _isCameraConfiguredAndReady = true;

        // Create a RenderTexture to store the webcam feed
        //Render Texture must be BGR for Android devices, Unity WebRTC doesn't support streaming GraphicsFormat.RGBA32 on Android
        _readTexture = new RenderTexture(WIDTH, HEIGHT, 0, GraphicsFormat.B8G8R8A8_SRGB);

        // Set the RenderTexture to the renderer if available
        if (_screenRendererRGB != null && _screenRendererRGB.gameObject.activeInHierarchy)
        {
            _screenRendererRGB.material.mainTexture = _readTexture;
        }

        // Start the frame conversion coroutine
        StartCoroutine(ConvertFrame());
    }

    /// <summary>
    /// Coroutine to convert camera frames to the RenderTexture.
    /// </summary>
    private IEnumerator ConvertFrame()
    {
        while (_webCamTexture && _webCamTexture.isPlaying)
        {
            yield return new WaitForEndOfFrame();
            Graphics.Blit(_webCamTexture, _readTexture);
        }
    }

    /// <summary>
    /// Returns true if the local camera is ready.
    /// </summary>
    public bool IsMediaReady()
    {
        return _isCameraConfiguredAndReady;
    }
}