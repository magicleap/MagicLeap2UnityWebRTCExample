using System.Collections;
using UnityEngine;
using MagicLeap;
using UnityEngine.UI;

/// <summary>
/// Manages media components such as camera and microphone for both device and editor platforms.
/// </summary>
public class MediaManager : Singleton<MediaManager>
{
    [Header("Remote Media")]
    [SerializeField] private RawImage _remoteVideoRenderer;
    [SerializeField]
    public AudioSource  _receiveAudio;

    [Header("Local Media")]

    [SerializeReference]
    private MagicLeapCameraManager _magicLeapCameraDeviceManager;
    [SerializeReference]
    private WebCameraManager _webCameraDeviceManager;
    [SerializeField]
    private MicrophoneManager _microphoneManager;

    [Header("Permissions")]

    [SerializeField] 
    private PermissionManager _permissionManager;

    [Header("Magic Leap Settings")]

    [SerializeField]
    [Tooltip("Will use the MLCamera APIs instead of the WebCamera Texture component.")]
    private bool _useMLCamera = true;

    private ICameraDeviceManager _targetCameraDeviceManager;

    public RawImage RemoteVideoRenderer => _remoteVideoRenderer;

    public RenderTexture CameraTexture => _targetCameraDeviceManager.CameraTexture;

    public AudioSource SourceAudio => _microphoneManager.SourceAudio;

    public AudioSource ReceiveAudio => _receiveAudio;

 

    private IEnumerator Start()
    {

        if (_useMLCamera && Application.platform == RuntimePlatform.Android
            && SystemInfo.deviceModel == "Magic Leap Magic Leap 2")
        {
            _targetCameraDeviceManager = _magicLeapCameraDeviceManager;
        }
        else
        {
            _targetCameraDeviceManager = _webCameraDeviceManager;
        }

        _permissionManager.RequestPermission();
        yield return new WaitUntil(() => _permissionManager.PermissionsGranted);
        UIController.Instance.OnStartMediaButtonPressed += StartMedia;

    }

    private void StartMedia()
    {
        _targetCameraDeviceManager.StartMedia();
        _microphoneManager.SetupAudio();
    }

    private void OnDisable()
    {
        UIController.Instance.OnStartMediaButtonPressed -= StartMedia;
        StopMedia();
    }

    /// <summary>
    /// Stop microphone and camera on Magic Leap 2
    /// TODO: Implement UI to actually call this function
    /// </summary>
    private void StopMedia()
    {
        _targetCameraDeviceManager.StopMedia();
        _microphoneManager.StopMicrophone();

    }

    /// <summary>
    /// Returns true if local microphone and camera are ready
    /// </summary>
    public bool IsMediaReady()
    {
        Debug.Log($" Camera Device Ready = {_targetCameraDeviceManager.IsConfiguredAndReady} && Microphone Ready = {_microphoneManager.IsConfiguredAndReady} ");
        return _targetCameraDeviceManager != null 
               && _targetCameraDeviceManager.IsConfiguredAndReady 
               && _microphoneManager.IsConfiguredAndReady;
    }
}
