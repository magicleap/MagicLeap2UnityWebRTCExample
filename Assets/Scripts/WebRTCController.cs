using MagicLeap;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.WebRTC;
using UnityEngine;

/// <summary>
/// Manages WebRTC connections, including creating peer connections, 
/// handling SDP offers/answers, and managing ICE candidates.
/// Communicates with a signaling server for connection setup.
/// Tracks connection state changes and informs the UI.
/// </summary>
public class WebRTCController : Singleton<WebRTCController>
{
    private RTCIceServer[] _iceServers = null;
    private RTCPeerConnection _peerConnection;
    private bool _waitingForAnswer = false;
    private bool _waitingForAnswerGetRequest = false;
    private bool _waitingforICEs = false;

    private Coroutine _coroutinePollServerForSDP;
    private Coroutine _coroutinePollServerForICE;

    private const float PERIODICITY_CHECK_ANSWERS = 1f;
    [SerializeField] private ServerCommunication _serverCommunication;

    public Action<WebRTCConnectionState> OnWebRTCConnectionStateChange;

    private List<RTCRtpSender> _rtcRtpSenders;
    private AudioStreamTrack _audioStreamTrack;
    private VideoStreamTrack _videoStreamTrack;
    private MediaStream _sendStream = null;
    private WebRTCConnectionState controllerState = WebRTCConnectionState.NotConnected;

    /// <summary>
    /// Enum representing the various states of the WebRTC connection.
    /// </summary>
    public enum WebRTCConnectionState
    {
        NotConnected,
        Connecting,
        Connected,
    }


    private void Start()
    {
        // Subscribe to UI button events for WebRTC connection changes
        UIController.Instance.OnWebRTCConnectionChangeButtonPressed += WebRtcConnectionChange;

        // Subscribe to server communication events for handling WebRTC signaling
        _serverCommunication.OnLoginAnswer += OnLoggedOnServer;
        _serverCommunication.OnRemoteSDPOffer += OnRemoteSDPOfferReceivedFromServer;
        _serverCommunication.OnSDPOfferSentInServer += OnSDPOfferSent;
        _serverCommunication.OnAnswerChecked += OnAnswerCheckedOnServer;
        _serverCommunication.OnNewRemoteICECandidate += ConsumeRemoteIce;

        _rtcRtpSenders = new List<RTCRtpSender>();
    }

    private void SetConnectionState(WebRTCConnectionState state)
    {
        Debug.Log($"WebRTC connection state changed to: {state}");
        controllerState = state;
        OnWebRTCConnectionStateChange?.Invoke(state);
    }

    /// <summary>
    /// Consumes a remote ICE candidate received from the signaling server.
    /// ICE (Interactive Connectivity Establishment) candidates are used to establish peer-to-peer connections.
    /// </summary>
    /// <param name="candidate">The ICE candidate.</param>
    /// <param name="sdpMid">The media stream identification.</param>
    /// <param name="sdpMLineIndex">The index of the media stream line.</param>
    private void ConsumeRemoteIce(string candidate, string sdpMid, int sdpMLineIndex)
    {
        if (_peerConnection == null)
        {
            Debug.LogError("ConsumeRemoteIce: No active peer connection.");
            return;
        }

        Debug.Log("Consuming remote ICE candidate.");
        RTCIceCandidateInit candidateInit = new RTCIceCandidateInit
        {
            sdpMid = sdpMid,
            sdpMLineIndex = sdpMLineIndex,
            candidate = candidate
        };

        RTCIceCandidate iceCandidate = new RTCIceCandidate(candidateInit);
        Debug.Log($"Adding remote ICE Candidate: {iceCandidate.Candidate}");
        bool result = _peerConnection.AddIceCandidate(iceCandidate);
        if (result)
        {
            Debug.Log("Successfully added remote ICE candidate.");
        }
        else
        {
            Debug.LogError("Failed to add remote ICE candidate.");
        }
    }

    /// <summary>
    /// Handles the event when an answer is checked on the server.
    /// If a remote answer is received, it sets the remote description and starts checking for ICE candidates.
    /// </summary>
    /// <param name="remoteAnswer">The remote SDP answer.</param>
    private void OnAnswerCheckedOnServer(string remoteAnswer)
    {
        _waitingForAnswerGetRequest = false;

        if (!string.IsNullOrEmpty(remoteAnswer))
        {
            Debug.Log("Received an SDP answer from the server.");
            _waitingForAnswer = false;

            RTCSessionDescription remoteAnswerDesc = new RTCSessionDescription
            {
                sdp = remoteAnswer,
                type = RTCSdpType.Answer
            };

            if (_peerConnection == null)
            {
                Debug.LogError("No active peer connection.");
            }
            else
            {
                Debug.Log($"Setting remote description: {remoteAnswerDesc.sdp}");
                RTCSetSessionDescriptionAsyncOperation op = _peerConnection.SetRemoteDescription(ref remoteAnswerDesc);
                if (!op.IsError)
                {
                    Debug.Log("Remote description set successfully. Polling server for ICE candidates.");
                    _waitingforICEs = true;
                    if (_coroutinePollServerForICE == null)
                    {
                        _coroutinePollServerForICE = StartCoroutine(RemoteICECheckCoroutine());
                    }
                }
                else
                {
                    Debug.LogError("Failed to set remote description.");
                    SetConnectionState(WebRTCConnectionState.NotConnected);
                }
            }
        }
    }

    /// <summary>
    /// Coroutine that periodically polls the server for ICE candidates.
    /// </summary>
    private IEnumerator RemoteICECheckCoroutine()
    {
        while (_waitingforICEs)
        {
            Debug.Log("Polling server for ICE candidates...");
            _serverCommunication.CheckRemoteIce();
            yield return new WaitForSeconds(PERIODICITY_CHECK_ANSWERS);
        }
    }

    /// <summary>
    /// Handles the event when an SDP offer has been successfully sent to the server.
    /// Starts polling the server for an answer.
    /// </summary>
    private void OnSDPOfferSent()
    {
        Debug.Log("SDP offer sent to server.");
        _waitingForAnswer = true;
        if (_coroutinePollServerForSDP == null)
        {
            _coroutinePollServerForSDP = StartCoroutine(PollServerWaitingForSDPAnswer());
        }
    }

    /// <summary>
    /// In this Coroutine we will poll the server periodically waiting for an Answer
    /// </summary>
    private IEnumerator PollServerWaitingForSDPAnswer()
    {
        while (_waitingForAnswer && !_waitingForAnswerGetRequest)
        {
            Debug.Log("Polling server for SDP answer...");
            _waitingForAnswerGetRequest = true;
            _serverCommunication.CheckAnswers();
            yield return new WaitForSeconds(PERIODICITY_CHECK_ANSWERS);
        }
    }

    /// <summary>
    /// If we detect an awaiting offer in the signaling server after querying it, we convert it into an RTCSessionDescription object,
    /// and include it in our peerConnection class.
    /// </summary>
    /// <param name="offer">The SDP offer as a string.</param>
    private void OnRemoteSDPOfferReceivedFromServer(string offer)
    {
        if (!string.IsNullOrEmpty(offer))
        {
            Debug.Log("Received an SDP offer from the server.");
            RTCSessionDescription incomingOffer = new RTCSessionDescription
            {
                sdp = offer,
                type = RTCSdpType.Offer
            };

            if (_peerConnection == null)
            {
                Debug.LogError("No active peer connection.");
            }
            else
            {
                StartCoroutine(SetOfferDescription(incomingOffer));
            }
        }
        else
        {
            // This means we are the first ones arriving to the server, let's create an offer and leave it there so next
            // user can take it when connecting. In the meantime from here we will stay waiting until remote participant
            // connects. Polling the server from time to time to check if any user connected and sent an answer.
            StartCoroutine(CreateOffer());
        }
    }

    /// <summary>
    /// Initiates the process of creating an SDP (Session Description Protocol) offer.
    /// This offer includes details about the media capabilities and requirements of the peer initiating the call,
    /// including terms of the connection, codecs, formats, and other media settings.
    /// </summary>
    public IEnumerator CreateOffer()
    {
        Debug.Log("Creating SDP offer...");
        RTCSessionDescriptionAsyncOperation op = _peerConnection.CreateOffer();
        yield return op;

        if (!op.IsError)
        {
            Debug.Log("SDP offer creation complete.");
            yield return OnCreateOfferSuccess(op.Desc);
        }
        else
        {
            Debug.LogError($"Failed to create SDP offer: {op.Error}");
            SetConnectionState(WebRTCConnectionState.NotConnected);
            yield break;
        }
    }

    /// <summary>
    /// When Offer has been created successfully, it sets the local description with the SDP offer but still doesn’t transmit it to the peer.
    /// It’s more about preparing your local setup to be ready to send and receive media based on the offer.
    /// </summary>
    /// <param name="desc">The SDP description.</param>
    private IEnumerator OnCreateOfferSuccess(RTCSessionDescription desc)
    {
        RTCSetSessionDescriptionAsyncOperation op = _peerConnection.SetLocalDescription(ref desc);
        yield return op;
        if (!op.IsError)
        {
            Debug.Log("Local description set successfully.");
            _serverCommunication.SendOfferToSignalServer(desc.sdp);
        }
        else
        {
            Debug.LogError($"Failed to set local description: {op.Error}");
            SetConnectionState(WebRTCConnectionState.NotConnected);
            yield break;
        }
    }

    /// <summary>
    /// Sets the remote SDP offer description.
    /// </summary>
    /// <param name="offer">The remote SDP offer.</param>
    private IEnumerator SetOfferDescription(RTCSessionDescription offer)
    {
        Debug.Log($"Setting remote description: {offer.sdp}");
        RTCSetSessionDescriptionAsyncOperation op = _peerConnection.SetRemoteDescription(ref offer);
        yield return op;

        if (!op.IsError)
        {
            Debug.Log("Remote description set successfully.");
            // After consuming the SDP offer, generate and send an SDP answer to the server.
            StartCoroutine(CreateAnswer());
        }
        else
        {
            Debug.LogError($"Failed to set remote description: {op.Error}");
            SetConnectionState(WebRTCConnectionState.NotConnected);
        }
    }

    /// <summary>
    /// Creates an SDP answer in response to an SDP offer.
    /// </summary>
    private IEnumerator CreateAnswer()
    {
        Debug.Log("Creating SDP answer...");
        RTCSessionDescriptionAsyncOperation op = _peerConnection.CreateAnswer();
        yield return op;

        if (!op.IsError)
        {
            if (_peerConnection.SignalingState != RTCSignalingState.HaveRemoteOffer)
            {
                Debug.LogError("Signaling state does not have offer.");
                SetConnectionState(WebRTCConnectionState.NotConnected);
                yield break;
            }

            Debug.Log("SDP answer creation complete.");
            yield return StartCoroutine(OnCreateAnswerSuccess(op.Desc));
        }
        else
        {
            Debug.LogError($"Failed to create SDP answer: {op.Error.message}");
            SetConnectionState(WebRTCConnectionState.NotConnected);
        }
    }

    /// <summary>
    /// When an SDP answer has been created successfully, sets the local description with the SDP answer,
    /// and starts checking for ICE candidates.
    /// </summary>
    /// <param name="desc">The SDP description.</param>
    private IEnumerator OnCreateAnswerSuccess(RTCSessionDescription desc)
    {
        Debug.Log($"Setting local description with SDP: {desc.sdp}");

        RTCSetSessionDescriptionAsyncOperation op = _peerConnection.SetLocalDescription(ref desc);
        yield return op;

        if (!op.IsError)
        {
            Debug.Log("Local description set successfully.");
            _serverCommunication.SendAnswerToSignalServer(desc.sdp);
            _waitingforICEs = true;

            if (_coroutinePollServerForICE == null)
            {
                _coroutinePollServerForICE = StartCoroutine(RemoteICECheckCoroutine());
            }
        }
        else
        {
            Debug.LogError($"Failed to set local description: {op.Error}");
            SetConnectionState(WebRTCConnectionState.NotConnected);
        }
    }

    /// <summary>
    /// Handles the WebRTC connection change event from the UI button.
    /// </summary>
    /// <param name="connect">Whether to connect or disconnect.</param>
    /// <param name="IPaddressFromTextField">The server IP address.</param>
    private void WebRtcConnectionChange(bool connect, string IPaddressFromTextField)
    {
        if (connect)
        {
            if (controllerState != WebRTCConnectionState.NotConnected)
            {
                Debug.Log($"Already {controllerState}");
                return;
            }

            SetConnectionState(WebRTCConnectionState.Connecting);

            // Handle connection
            if (!MediaManager.Instance.IsMediaReady())
            {
                Debug.LogError("Media not ready.");
                UIController.Instance.ChangeUIForMediaInput();
                SetConnectionState(WebRTCConnectionState.NotConnected);
                return;
            }

            // Initialize server communication and log in
            string serverIP = IPaddressFromTextField;
            _serverCommunication.Init(serverIP);
            _serverCommunication.Login();
        }
        else
        {
            DisconnectWebRTC();
        }
    }

    /// <summary>
    /// Disconnects the WebRTC connection and cleans up resources.
    /// </summary>
    private void DisconnectWebRTC()
    {
        if (_peerConnection == null)
        {
            Debug.Log("No connection to disconnect.");
            SetConnectionState(WebRTCConnectionState.NotConnected);
            return;
        }

        Debug.Log("Disconnecting WebRTC...");
        _serverCommunication.Disconnect();
        RemoveTracks();
        _peerConnection.Close();
        _peerConnection.Dispose();
        _peerConnection = null;

        _waitingForAnswer = false;
        _waitingforICEs = false;
        _waitingForAnswerGetRequest = false;

        if (_coroutinePollServerForICE != null)
        {
            StopCoroutine(_coroutinePollServerForICE);
            _coroutinePollServerForICE = null;
        }

        if (_coroutinePollServerForSDP != null)
        {
            StopCoroutine(_coroutinePollServerForSDP);
            _coroutinePollServerForSDP = null;
        }

        SetConnectionState(WebRTCConnectionState.NotConnected);
    }

    /// <summary>
    /// Removes tracks from the media stream and clears the sender list.
    /// </summary>
    private void RemoveTracks()
    {
        if (_peerConnection != null && _rtcRtpSenders != null)
        {
            foreach (RTCRtpSender sender in _rtcRtpSenders)
            {
                _peerConnection.RemoveTrack(sender);
            }
            _rtcRtpSenders.Clear();
        }

        if (_sendStream != null && _sendStream.GetTracks() != null)
        {
            MediaStreamTrack[] tracks = _sendStream.GetTracks().ToArray();
            foreach (MediaStreamTrack track in tracks)
            {
                _sendStream.RemoveTrack(track);
            }
        }
    }

    /// <summary>
    /// Handles the event when the user has successfully logged on to the server.
    /// Creates the peer connection and initializes the ICE servers.
    /// </summary>
    /// <param name="connectionSuccess">Whether the login was successful.</param>
    private void OnLoggedOnServer(bool connectionSuccess)
    {
        if (!connectionSuccess)
        {
            Debug.Log("Failed to connect to the server.");
            UIController.Instance.LogMessageInPanel("Error connecting with the server. Check if the Python server is running locally.");
            UIController.Instance.ChangeUIForWebRTCConnection();
            SetConnectionState(WebRTCConnectionState.NotConnected);
        }
        else
        {
            Debug.Log("Successfully logged into the server.");
            _iceServers = CreateIceServers();

            _peerConnection = CreatePeerConnection(_iceServers);
            SubscribeToConnection(_peerConnection);

            AddTracksToMediaStream();
            StartCoroutine(WebRTC.Update());

            SetConnectionState(WebRTCConnectionState.Connected);
        }
    }

    /// <summary>
    /// Creates the ICE servers configuration.
    /// </summary>
    /// <returns>An array of ICE servers.</returns>
    private RTCIceServer[] CreateIceServers()
    {
        string stunServer1Uri = "stun:stun.l.google.com:19302";
        string stunServer2Uri = "stun:" + _serverCommunication.GetServerIP() + ":3478";
        string turnServerUri = "turn:" + _serverCommunication.GetServerIP() + ":3478";
        List<string> iceServerList = new List<string> { stunServer1Uri, stunServer2Uri, turnServerUri };

        string userName = "foo";
        string password = "bar";

        RTCIceServer[] iceServers = new RTCIceServer[iceServerList.Count];

        for (int i = 0; i < iceServerList.Count; i++)
        {
            iceServers[i] = new RTCIceServer
            {
                urls = new string[] { iceServerList[i] },
                credential = password,
                username = userName,
                credentialType = RTCIceCredentialType.Password
            };
        }
        return iceServers;
    }

    /// <summary>
    /// Creates the RTCPeerConnection object with the specified ICE servers.
    /// </summary>
    /// <param name="iceServers">The ICE servers configuration.</param>
    /// <returns>The RTCPeerConnection object.</returns>
    private RTCPeerConnection CreatePeerConnection(RTCIceServer[] iceServers)
    {
        Debug.Log("Creating peer connection...");
        RTCConfiguration config = new RTCConfiguration
        {
            iceServers = iceServers
        };
        return new RTCPeerConnection(ref config);
    }

    /// <summary>
    /// Subscribes to the RTCPeerConnection callbacks.
    /// </summary>
    /// <param name="connection">The RTCPeerConnection object.</param>
    private void SubscribeToConnection(RTCPeerConnection connection)
    {
        if (connection == null)
        {
            Debug.LogError("Failed to subscribe to connection. RTCPeerConnection is null.");
            return;
        }

        connection.OnTrack = OnTrack;
        connection.OnNegotiationNeeded = OnNegotiationNeeded;
        connection.OnConnectionStateChange = OnConnectionStateChange;
        connection.OnIceCandidate = OnIceCandidate;
        connection.OnIceConnectionChange = OnIceConnectionChange;
        connection.OnIceGatheringStateChange = OnIceGatheringStateChange;
    }

    /// <summary>
    /// Handles the event when a new track is received on the RTCPeerConnection.
    /// </summary>
    /// <param name="trackEvent">The track event.</param>
    private void OnTrack(RTCTrackEvent trackEvent)
    {
        Debug.Log("Received new track on peer connection.");

        if (trackEvent.Track is VideoStreamTrack video)
        {
            Debug.Log("Remote video track added.");
            video.OnVideoReceived += tex =>
            {
                MediaManager.Instance.RemoteVideoRenderer.texture = tex;
            };
        }

        if (trackEvent.Track is AudioStreamTrack audioTrack)
        {
            Debug.Log("Remote audio track added.");
            MediaManager.Instance.ReceiveAudio.SetTrack(audioTrack);
            MediaManager.Instance.ReceiveAudio.loop = true;
            MediaManager.Instance.ReceiveAudio.Play();
        }
    }

    /// <summary>
    /// Handles the event when the ICE gathering state changes.
    /// </summary>
    /// <param name="state">The ICE gathering state.</param>
    private void OnIceGatheringStateChange(RTCIceGatheringState state)
    {
        Debug.Log($"ICE gathering state changed to: {state}");
        if (state == RTCIceGatheringState.Complete)
        {
            _waitingforICEs = false;
        }
    }

    /// <summary>
    /// Handles the event when the ICE connection state changes.
    /// </summary>
    /// <param name="state">The ICE connection state.</param>
    private void OnIceConnectionChange(RTCIceConnectionState state)
    {
        Debug.Log($"ICE connection state changed to: {state}");
        if (state == RTCIceConnectionState.Completed)
        {
            _waitingforICEs = false;
        }
    }

    /// <summary>
    /// Handles the event when a new ICE candidate is generated.
    /// Sends the local ICE candidate to the signaling server.
    /// </summary>
    /// <param name="candidate">The ICE candidate.</param>
    private void OnIceCandidate(RTCIceCandidate candidate)
    {
        Debug.Log($"Generated local ICE candidate: {candidate.SdpMid} {candidate.Candidate}");
        _serverCommunication.SendICECandidate(candidate);
        Debug.Log("Sent local ICE candidate to signaling server.");
    }

    /// <summary>
    /// Handles the event when the RTCPeerConnection state changes.
    /// </summary>
    /// <param name="state">The connection state.</param>
    private void OnConnectionStateChange(RTCPeerConnectionState state)
    {
        Debug.Log($"Peer connection state changed to: {state}");
        if (state == RTCPeerConnectionState.Connected)
        {
            SetConnectionState(WebRTCConnectionState.Connected);
        }

        if (state == RTCPeerConnectionState.Disconnected)
        {
            DisconnectWebRTC();
            SetConnectionState(WebRTCConnectionState.NotConnected);
        }

        if (state == RTCPeerConnectionState.Failed)
        {
            Debug.LogError("Peer connection state failed.");
            DisconnectWebRTC();
            SetConnectionState(WebRTCConnectionState.NotConnected);
        }
    }

    /// <summary>
    /// Adds video and audio tracks to the media stream.
    /// </summary>
    private void AddTracksToMediaStream()
    {
        Debug.Log("Adding tracks to media stream...");
        if (_peerConnection != null)
        {
            AddVideoTrackToMediaStream();
            AddAudioTrackToMediaStream();
        }
    }

    /// <summary>
    /// Adds a video track to the media stream.
    /// </summary>
    private void AddVideoTrackToMediaStream()
    {
        Debug.Log("Adding video track to media stream...");

        if (_sendStream == null)
        {
            _sendStream = new MediaStream();
        }
        _videoStreamTrack = new VideoStreamTrack(MediaManager.Instance.CameraTexture);
        _sendStream.AddTrack(_videoStreamTrack);
        RTCRtpSender videoSender = _peerConnection.AddTrack(_videoStreamTrack, _sendStream);
        _rtcRtpSenders.Add(videoSender);
    }

    /// <summary>
    /// Triggered when SDP negotiation of connection is needed.
    /// Once we receive this callback that means all tracks are added to the media stream and we are ready to establish
    /// the connection with the peer. Next step will be starting SDP negotiation.
    /// </summary>
    private void OnNegotiationNeeded()
    {
        Debug.Log("SDP negotiation needed.");
        // At this point first of all let's check if there is any available SDP offer from the peer stored in the server.
        _serverCommunication.QueryOffers();
    }

    /// <summary>
    /// Adds an audio track to the media stream.
    /// </summary>
    private void AddAudioTrackToMediaStream()
    {
        Debug.Log("Adding audio track to media stream...");

        if (_sendStream == null)
        {
            _sendStream = new MediaStream();
        }

        _audioStreamTrack = new AudioStreamTrack(MediaManager.Instance.SourceAudio);
        _sendStream.AddTrack(_audioStreamTrack);
        RTCRtpSender audioSender = _peerConnection.AddTrack(_audioStreamTrack, _sendStream);
        _rtcRtpSenders.Add(audioSender);
    }

    private void OnDestroy()
    {
        // Unsubscribe from all events to avoid memory leaks
        UIController.Instance.OnWebRTCConnectionChangeButtonPressed -= WebRtcConnectionChange;
        _serverCommunication.OnLoginAnswer -= OnLoggedOnServer;
        _serverCommunication.OnRemoteSDPOffer -= OnRemoteSDPOfferReceivedFromServer;
        _serverCommunication.OnSDPOfferSentInServer -= OnSDPOfferSent;
        _serverCommunication.OnAnswerChecked -= OnAnswerCheckedOnServer;
        _serverCommunication.OnNewRemoteICECandidate -= ConsumeRemoteIce;
    }
}

