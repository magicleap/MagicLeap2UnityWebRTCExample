using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Android;
using UnityEngine.Experimental.Rendering;
using UnityEngine.XR.MagicLeap;

/// <summary>
/// MagicLeapCameraManager
/// This script manages the Magic Leap Camera, enabling it asynchronously. It is based on the simple camera example 
/// from the Magic Leap developer portal.
/// 
/// This script performs the following functions:
/// 1. Initializes and configures the camera asynchronously.
/// 2. Starts and stops video capture.
/// 3. Processes video frames to Render Texture
/// 
/// Reference:
/// Magic Leap Developer Portal - Simple Camera Example
/// https://developer-docs.magicleap.cloud/docs/guides/unity/camera/ml-camera-example/#async-example
/// </summary>
public class MagicLeapCameraManager : MonoBehaviour, ICameraDeviceManager
{
    [SerializeField, Tooltip("The renderer to show the camera capture on RGB format")]
    private Renderer _screenRendererRGB = null; // Renderer to display the RGB format camera capture

    [SerializeField, Tooltip("Shader used for stride adjustment and vertical flip corrections")]
    private Shader _strideAdjustmentShader; // Shader for stride adjustment and vertical flip corrections

    private RenderTexture _readTexture; // RenderTexture to hold the adjusted video frame
    private Texture2D _writeTexture; // Texture2D to hold the raw camera frame data
    private bool _updatingTexture = false; // Flag to indicate if the texture update is in progress
    private Material _strideAdjustmentMaterial; // Material to apply the stride adjustment shader

    #region Capture Config
    private int _targetImageWidth = 1920; // Target width for the captured image
    private int _targetImageHeight = 1080; // Target height for the captured image
    private MLCameraBase.Identifier _cameraIdentifier = MLCameraBase.Identifier.Main; // Identifier for the camera to use
    private MLCameraBase.CaptureFrameRate _targetFrameRate = MLCameraBase.CaptureFrameRate._30FPS; // Target frame rate for the camera capture
    private MLCameraBase.OutputFormat _outputFormat = MLCameraBase.OutputFormat.RGBA_8888; // Output format for the captured video
    private MLCamera.ConnectFlag _connectFlags = MLCamera.ConnectFlag.MR; // Flags to specify the connection type and capabilities
    #endregion

    #region Magic Leap Camera Info
    private MLCamera _captureCamera; // The connected MLCamera instance
    private bool _isCapturingVideo = false; // True if video capture is currently active
    #endregion

    private bool _isCameraInitializationInProgress; // Flag to indicate if camera initialization is in progress
    private bool _isCameraConfiguredAndReady = false; // True if the camera is configured and ready for capture

    private static readonly int EffectiveWidthNormalizedProperty = Shader.PropertyToID("_EffectiveWidthNormalized"); // Shader property ID for effective width normalization

    public RenderTexture CameraTexture => _readTexture; // Property to get the current camera texture

    public bool IsConfiguredAndReady => _isCameraConfiguredAndReady; // Property to check if the camera is configured and ready

    private void Awake()
    {
        // Initialize the material with the shader for stride and vertical flip corrections.
        _strideAdjustmentMaterial = new Material(_strideAdjustmentShader);
    }

    public void StartMedia()
    {
        if (Permission.HasUserAuthorizedPermission(Permission.Camera))
        {
            Debug.Log("Permission Granted, Starting Magic Leap Camera.");
            if (!_isCameraConfiguredAndReady)
            {
                TryEnableMLCamera((success) => { _isCameraConfiguredAndReady = success; });
            }
            else
            {
                Debug.Log("Camera is already started.");
            }
        }
        else
        {
            Debug.LogError("Camera permission has not been granted.");
        }
    }

    public void StopMedia()
    {
        DisconnectCamera();
        _isCameraConfiguredAndReady = false;
    }

    private async void TryEnableMLCamera(Action<bool> onCameraCaptureStarted = null)
    {
        if (_isCameraInitializationInProgress)
        {
            return;
        }

        _isCameraInitializationInProgress = true;

        Debug.Log("Initializing camera.");
        bool isCameraAvailable = await WaitForCameraAvailabilityAsync();

        if (isCameraAvailable)
        {
            await ConnectAndConfigureCameraAsync();
        }

        _isCameraInitializationInProgress = false;
        onCameraCaptureStarted?.Invoke(_isCapturingVideo);
    }

    /// <summary>
    /// Waits for the camera to become available, retrying up to a maximum number of attempts.
    /// </summary>
    /// <returns>True if the camera becomes available; otherwise, false.</returns>
    private async Task<bool> WaitForCameraAvailabilityAsync()
    {
        const int maxAttempts = 10;
        int attempts = 0;

        while (attempts < maxAttempts)
        {
            bool cameraDeviceAvailable = false;
            MLResult result = MLCameraBase.GetDeviceAvailabilityStatus(_cameraIdentifier, out cameraDeviceAvailable);

            if (result.IsOk && cameraDeviceAvailable)
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromSeconds(1.0f));
            attempts++;
        }

        return false;
    }

    /// <summary>
    /// Connects and configures the camera asynchronously.
    /// </summary>
    /// <returns>True if the camera is successfully connected and configured; otherwise, false.</returns>
    private async Task<bool> ConnectAndConfigureCameraAsync()
    {
        Debug.Log("Starting Camera Capture.");

        var context = CreateCameraContext();
        _captureCamera = await MLCamera.CreateAndConnectAsync(context);

        if (_captureCamera == null)
        {
            Debug.LogError("Could not create or connect to a valid camera. Stopping Capture.");
            return false;
        }

        Debug.Log("Camera Connected.");

        bool hasImageStreamCapabilities = GetStreamCapabilityWBestFit(out var streamCapability);
        if (!hasImageStreamCapabilities)
        {
            Debug.LogError("Could not start capture. No valid Image Streams available. Disconnecting Camera.");
            await DisconnectCameraAsync();
            return false;
        }

        Debug.Log("Preparing camera configuration.");

        var captureConfig = CreateCaptureConfig(streamCapability);
        var prepareResult = _captureCamera.PrepareCapture(captureConfig, out _);

        if (!MLResult.DidNativeCallSucceed(prepareResult.Result, nameof(_captureCamera.PrepareCapture)))
        {
            Debug.LogError($"Could not prepare capture. Result: {prepareResult.Result}. Disconnecting Camera.");
            await DisconnectCameraAsync();
            return false;
        }

        Debug.Log("Starting Video Capture.");

        bool captureStarted = await StartVideoCaptureAsync();
        if (!captureStarted)
        {
            Debug.LogError("Could not start capture. Disconnecting Camera.");
            await DisconnectCameraAsync();
            return false;
        }

        return _isCapturingVideo;
    }

    /// <summary>
    /// Creates the camera connection context.
    /// </summary>
    /// <returns>The camera connection context.</returns>
    private MLCameraBase.ConnectContext CreateCameraContext()
    {
        var context = MLCameraBase.ConnectContext.Create();
        context.CamId = _cameraIdentifier;
        context.Flags = _connectFlags;
        return context;
    }

    /// <summary>
    /// Creates the capture configuration.
    /// </summary>
    /// <param name="streamCapability">The stream capability.</param>
    /// <returns>The capture configuration.</returns>
    private MLCameraBase.CaptureConfig CreateCaptureConfig(MLCameraBase.StreamCapability streamCapability)
    {
        var captureConfig = new MLCameraBase.CaptureConfig
        {
            CaptureFrameRate = _targetFrameRate,
            StreamConfigs = new MLCameraBase.CaptureStreamConfig[]
            {
                MLCameraBase.CaptureStreamConfig.Create(streamCapability, _outputFormat)
            }
        };
        return captureConfig;
    }

    /// <summary>
    /// Starts video capture asynchronously.
    /// </summary>
    /// <returns>True if the video capture starts successfully; otherwise, false.</returns>
    private async Task<bool> StartVideoCaptureAsync()
    {
        await _captureCamera.PreCaptureAEAWBAsync();

        var startCapture = await _captureCamera.CaptureVideoStartAsync();
        _isCapturingVideo = MLResult.DidNativeCallSucceed(startCapture.Result, nameof(_captureCamera.CaptureVideoStart));

        if (!_isCapturingVideo)
        {
            Debug.LogError($"Could not start camera capture. Result: {startCapture.Result}");
            return false;
        }
        _captureCamera.OnRawVideoFrameAvailable += OnCaptureRawVideoFrameAvailable;
        return true;
    }

    /// <summary>
    /// Disconnects the camera asynchronously.
    /// </summary>
    private void DisconnectCamera()
    {
        if (_captureCamera != null)
        {
            if (_isCapturingVideo)
            {
                _captureCamera.CaptureVideoStop();
                _captureCamera.OnRawVideoFrameAvailable -= OnCaptureRawVideoFrameAvailable;
            }

            _captureCamera.Disconnect();
            _captureCamera = null;
        }

        _isCapturingVideo = false;
    }

    /// <summary>
    /// Disconnects the camera asynchronously.
    /// </summary>
    private async Task DisconnectCameraAsync()
    {
        if (_captureCamera != null)
        {
            if (_isCapturingVideo)
            {
                await _captureCamera.CaptureVideoStopAsync();
                _captureCamera.OnRawVideoFrameAvailable -= OnCaptureRawVideoFrameAvailable;
            }

            await _captureCamera.DisconnectAsync();
            _captureCamera = null;
        }

        _isCapturingVideo = false;
    }

    /// <summary>
    /// Gets the best-fit stream capability.
    /// </summary>
    /// <param name="streamCapability">The best-fit stream capability.</param>
    /// <returns>True if a best-fit stream capability is found; otherwise, false.</returns>
    private bool GetStreamCapabilityWBestFit(out MLCameraBase.StreamCapability streamCapability)
    {
        streamCapability = default;

        if (_captureCamera == null)
        {
            Debug.Log("No Camera Connected. Cannot get stream capabilities.");
            return false;
        }

        var streamCapabilities = MLCameraBase.GetImageStreamCapabilitiesForCamera(_captureCamera, MLCameraBase.CaptureType.Video);

        if (streamCapabilities.Length <= 0)
        {
            return false;
        }

        if (MLCameraBase.TryGetBestFitStreamCapabilityFromCollection(streamCapabilities, _targetImageWidth, _targetImageHeight, MLCameraBase.CaptureType.Video, out streamCapability))
        {
            Debug.Log($"Stream: {streamCapability} selected with best fit.");
            return true;
        }

        Debug.Log($"No best fit found. Stream: {streamCapabilities[0]} selected by default.");
        streamCapability = streamCapabilities[0];
        return true;
    }

    /// <summary>
    /// Handles each video frame received from ML Camera.
    /// </summary>
    private void OnCaptureRawVideoFrameAvailable(MLCameraBase.CameraOutput frameInfo, MLCameraBase.ResultExtras resultExtras, MLCameraBase.Metadata metadataHandle)
    {
        if (!_updatingTexture)
        {
            _updatingTexture = true;
            UpdateRGBTexture(frameInfo);
        }
    }

    /// <summary>
    /// Updates the RGB texture with the received camera frame data.
    /// </summary>
    private void UpdateRGBTexture(MLCamera.CameraOutput output)
    {
        var imagePlane = output.Planes[0];
        int strideInPixels = (int)(imagePlane.Stride / imagePlane.BytesPerPixel);
        int height = (int)imagePlane.Height;
        int width = (int)imagePlane.Width;

        if (_writeTexture == null)
        {
            Debug.Log("Creating new Texture2D");
            _writeTexture = new Texture2D(strideInPixels, height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear
            };
        }
        if (_readTexture == null)
        {
            Debug.Log("Creating new RenderTexture");
            // Render Texture must be BGR for Android devices, Unity WebRTC doesn't support streaming GraphicsFormat.RGBA32 on Android
            _readTexture = new RenderTexture(width, height, 0, GraphicsFormat.B8G8R8A8_SRGB); 
            if (_screenRendererRGB != null && _screenRendererRGB.gameObject.activeInHierarchy)
            {
                _screenRendererRGB.material.mainTexture = _readTexture;
            }

            float normalizedWidthForShader = ((float)width / strideInPixels);
            _strideAdjustmentMaterial.SetFloat(EffectiveWidthNormalizedProperty, normalizedWidthForShader);
            Debug.Log($"Stride Adjustment value: {_strideAdjustmentMaterial.GetFloat(EffectiveWidthNormalizedProperty)}");
        }

        _writeTexture.LoadRawTextureData(imagePlane.Data);
        _writeTexture.Apply(false);

        Graphics.Blit(_writeTexture, _readTexture, _strideAdjustmentMaterial);
        _updatingTexture = false;
    }
}
