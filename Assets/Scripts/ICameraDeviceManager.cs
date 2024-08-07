using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public interface ICameraDeviceManager
{
    public RenderTexture CameraTexture { get; }
    public bool IsConfiguredAndReady { get; }

    public void StartMedia();
    public void StopMedia();


}
