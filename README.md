# Magic Leap 2 Unity WebRTC Example Project

## Introduction

This project demonstrates how to implement WebRTC functionality in Unity to stream a Render Texture between a Magic Leap 2 device and a PC. This simplified example serves as a starting point for developers who are migrating from the legacy MLWebRTC APIs. Developers should build more robust and performant solutions if deploying WebRTC into production environments.

* For more complete samples and information on Unity WebRTC see the samples included with the `com.unity.webrtc` package.
* You can obtain the camera image from the Magic Leap 2 using the `WebCameraTexture` or `MLCamera` APIs. This project demonstrates both options. For a generic, non Magic Leap specific, implementation see Unity's [VideoReceive Sample](https://github.com/Unity-Technologies/com.unity.webrtc/tree/main/Samples~/VideoReceive) provided in their WebRTC package.

## Running the Example

This section will guide you through the process of setting up and running the Magic Leap 2 Unity WebRTC Example project. Follow these steps to get the project up and running on both your Magic Leap 2 device and your computer.

Note: When building the application for the Magic Leap 2, the `MediaManager` script allows you to choose to use the `WebCameraTexture` or `MLCamera` APIs to obtain the camera image.

### Prerequisites

Before you begin, make sure you have the following installed on your development machine:

- Unity 2022.3 or later
- Magic Leap 2 device
- Python 3, for running the signaling server.
- A stable internet connection, Both your PC and Magic Leap 2 must be connected to the same network.
- Basic knowledge of Unity and WebRTC concepts.

### Project Setup

#### 1. Clone or Download the Repository

1. **Clone (Recommended)**: If you have Git installed, open a terminal and run the following command to clone the repository: Clone the project repository to your local machine.

    ```sh
    git clone <repository-url>
    ```

2. **Download**: Alternatively, you can download the project as a ZIP file and extract it to your desired location.

#### 2. Start the Server

The project includes a Python script that sets up a local signaling server necessary for WebRTC connections.

1. Open a command-line interface (CLI). For example: Command Prompt on Windows or Terminal on Mac and Linux.
2. Navigate to the `Server-Scripts` directory by running the `cd` command followed by the path to the project's `/Server/` folder:

```sh
cd <PATH-TO-YOUR-PROJECT-FOLDER>/Server-Scripts/
```

3. Start the server by running the following command:

```sh
python server.py
```

4. If successful the command line will print the following. If running the script resulted in an error, make sure Python is correctly installed on your computer.

```sh
Bottle v0.12.18 server starting up (using WSGIRefServer())...
Listening on http://0.0.0.0:8080/
Hit Ctrl-C to quit.
```

#### 3. Obtain Your Local IP Address

For this example to work correctly, you need both your computer and the Magic Leap device connected to the same network. You will also need to know your computer's local IP address by entering the following command:

1. Open a command-line interface (CLI). For example: Command Prompt on Windows or Terminal on Mac and Linux.
2. Find the IPv4/Local IP Address by running the following command:
* Mac and Linux: **ifconfig**
* Windows: **ipconfig**

:::tip
Often, local IP addresses start with `192.168.1.X`. Each device on your network will have a unique number in the last position. However, your address might follow a different pattern, like `10.0.0.X`. Always check your configuration before moving forward.
:::

#### 4. Building and Running the Application

This section provides instructions on how to open the Example Unity project, build and run the application on the Magic Leap 2 and start the application in the Unity Editor.

1. **Open the Project in Unity**:
   1. Launch Unity Hub, add the cloned project, and open it.

2. **Build the application for Magic Leap 2** :
   1. From the menu open the Build Settings (**File > Build Settings**).
   2. Select **Android** as the target platform.
   3. Ensure the `Assets/Scenes/WebRTCScene` is included in the **Build Settings**.
   4. Click **Build** or **Build and Run**.
   5. Run the application on the Magic Leap and accept the Camera and Microphone permission prompts when they appear.

3. **Run Application in the Unity Editor:**
   - With the Unity Editor open, hit the **Play** button to start the scene inside the Editor.


### 5. Using the Example

1. Ensure both your computer and the Magic Leap 2 device are connected to the same network.
2. Press the "Start Media" button to initialize the camera on both devices.
3. Select the input panel with the IP address. Enter the local IP address of your server in the input field the press "Connect". This connects the Magic Leap 2 and Editor to the local server.
4. If successful, you will see both the Magic Leap 2 and computer video streams on both devices.

## Troubleshooting

- **Connection Issues**: Ensure both devices are on the same network. Check firewall settings especially if port 8080 is blocked.
- **Camera or Microphone Permissions**: Make sure the app has the necessary permissions on both the PC and Magic Leap. Re-run the permission setup if needed.
- **Python Version**: Ensure you are using a recent version of Python 3. Check your version with `python --version`.
- **See Device Logs**: See the logs to get more information on what is occurring on device or on the server.
    - Device Logs : You can view the logs on your device using [device bridge](https://developer-docs.magicleap.cloud/docs/guides/developer-tools/ml-hub/ml-hub-device-bridge).
    - Server Logs : Logs are printed directly into the CLI that is running the `server.py` script.
    - Editor Logs : When testing on your computer, logs are printed directly into the Unity Editor's Console when the application is running.

## Folder Structure

- **`Server/`**
    - Contains the Python scripts for running a local WebRTC server.
- **`Assets/Materials/`**
  - Contains the `DefaultMat`, a default URP material.
- **`Assets/Plugins/Android`**
  - Contains the default Android Manifest.
- **`Assets/Scenes/`**
  - Contains the `WebRTCScene` that showcases the demo WebRTC code.
- **`Assets/Scripts/`**
  - Contains the WebRTC scripts that work with Magic Leap 2 and the example scene:
    - `ConcurrentWebRequestManager.cs`
    - `ICameraDeviceManager.cs`
    - `MagicLeapCameraManager.cs`
    - `MediaManager.cs`
    - `MicrophoneManager.cs`
    - `PermissionManager.cs`
    - `ServerCommunication.cs`
    - `Singleton.cs`
    - `UIController.cs`
    - `WebCameraManager.cs`
    - `WebRTCController.cs`
- **`Assets/Shaders/`**
  - Contains the `StrideAdjustmentShader.shader` for stride adjustment and vertically flipping the Magic Leap 2 Camera images.
  - Contains the default Text Mesh Pro assets and scripts.
- **`Assets/URP/`**
  - Contains the URP pipeline and render settings.
- **`Assets/TextMesh Pro/`**,**`Assets/XR/`**,**`Assets/XRI/`**,
  - Contains the default resources for Unity's Text Mesh Pro, XR Plugins, and XRI packages.


## Example Scene Structure

The Example WebRTCScene includes the following root objects in the scene hierarchy.

- **Directional Light**: Unity Default Light component.
- **EventSystem** : Unity Default Event System component.
- **XR Rig**: Prefab from the Magic Leap SDK to handle XR-specific input such as head tracking and controller input.
- **UI Controller**: A world-space canvas with XRI (XR Interaction Toolkit) components managed by the UIController script for user interaction.
- **Media Manager**: Manages media capture and playback, using either WebCameraManager (for the computer) or MagicLeapCameraManager (for Magic Leap 2) based on the platform.
- **Permission Manager**: Integrates the Permission Manager component which handles requesting runtime permissions for microphone and camera access.
- **Microphone Manager**:  Integrates the Microphone Manager component which manages the microphone input via an audio source.
- **WebRTCController**: Hosts the WebRTC Controller component (for managing WebRTC connections) and Server Communication component (to handle communication with the WebRTC signaling server)
- **Keyboard Manager**: Prefab from the Magic Leap XR Keyboard package to handle text input when specifying an IP address.

## Scripts Overview

### WebRTC Logic

The following scripts handle the core WebRTC logic

#### `ConcurrentWebRequestManager.cs`

Handles concurrent web requests to ensure only one request is processed at a time. Essential for managing communication with the signaling server.

#### `ServerCommunication.cs`

Manages communication with the signaling server for WebRTC connections, including login, SDP offers/answers, and ICE candidates.


#### `WebRTCController.cs`

Handles WebRTC connections, creating peer connections, managing SDP offers/answers, and ICE candidates. Communicates with the signaling server for connection setup and tracks connection state changes.

---

### UI Logic

These scripts handle displaying the correct UI elements for App states like Media, Connecting, and Disconnecting.

#### `UIController.cs`

Manages the user interface for starting media and handling WebRTC connections.

---

### Camera / Microphone Logic

The following scripts obtain the device's camera and microphone input so it can be sent over WebRTC.

#### `MediaManager.cs`

Central manager for media components like the camera and microphone. Uses the `MicrophoneManager`, `PermissionManager` and `MagicLeapCameraManager` on Magic Leap 2 and `WebCameraManager` on standalone.

#### `ICameraDeviceManager.cs`

Interface for camera device managers so that the camera image can be obtained from various sources, providing properties and methods for managing camera streams.

#### `MagicLeapCameraManager.cs`

Manages obtaining a render texture from the Magic Leap 2 using the `MLCamera` API. This includes initialization, configuration, and video capture and outputs the camera image onto a `RenderTexture`.

#### `WebCameraManager.cs`

Manages obtaining a render texture from `WebCameraTexture`, including initialization and conversion of camera frames to a `RenderTexture`.

#### `MicrophoneManager.cs`

Manages the microphone for capturing audio, including initialization and configuration of the `AudioSource`.

#### `PermissionManager.cs`

Handles requesting and checking permissions for the camera and microphone on Android.

---

### Common Scripts

These scripts are used across multiple scripts that include common functionality.

#### `Singleton.cs`

Generic singleton pattern implementation for Unity. Which allows scripts to be referenced using a static instance.
