/// <summary>
/// ServerCommunication manages the communication with the signaling server for WebRTC connections.
/// It handles login, sending and receiving SDP offers and answers, and managing ICE candidates.
/// </summary>
using MagicLeap;
using SimpleJson;
using System;
using System.Collections.Generic;
using Unity.WebRTC;
using UnityEngine;
using UnityEngine.Networking;

public class ServerCommunication : MonoBehaviour
{
    // Action to notify when login to the server is complete
    public Action<bool> OnLoginAnswer;

    // Action to handle incoming SDP offers
    public Action<string> OnRemoteSDPOffer;

    // Action to notify when SDP offer has been sent to the server
    public Action OnSDPOfferSentInServer;

    // Action to handle checking of SDP answers from the server
    public Action<string> OnAnswerChecked;

    // Action to handle new remote ICE candidates
    public Action<string, string, int> OnNewRemoteICECandidate;

    // Manages concurrent web requests
    private ConcurrentWebRequestManager _webRequestManager = new ConcurrentWebRequestManager();

    // Server and participant information
    private string _serverIP = "";
    private string _serverUri = "";
    private string _localId = ""; // Local ID given by the server
    private string _remoteId = ""; // Remote participant ID

    /// <summary>
    /// Log in to the server.
    /// </summary>
    public void Login()
    {
        try
        {
            Debug.Log("Sending POST LOGIN request");
            _webRequestManager.HttpPost(_serverUri + "/login", string.Empty, (AsyncOperation asyncOp) =>
            {
                UnityWebRequestAsyncOperation webRequestAsyncOp = asyncOp as UnityWebRequestAsyncOperation;
                if (webRequestAsyncOp == null)
                {
                    Debug.LogError("WebRequest is NULL");
                    OnLoginAnswer?.Invoke(false);
                    return;
                }

                UIController.Instance.LogMessageInPanel(webRequestAsyncOp.webRequest.result.ToString());
                if (webRequestAsyncOp.webRequest.result != UnityWebRequest.Result.Success || string.IsNullOrEmpty(webRequestAsyncOp.webRequest.downloadHandler.text))
                {
                    Debug.LogError("Operation Error connecting to the server");
                    UIController.Instance.LogMessageInPanel("Error connecting with the server, check if you are running the Python server locally.");
                    UIController.Instance.ChangeUIForWebRTCConnection(); // Allow user to try to reconnect
                    OnLoginAnswer?.Invoke(false);
                    return;
                }

                // Save ID given by the server
                _localId = webRequestAsyncOp.webRequest.downloadHandler.text;
                OnLoginAnswer?.Invoke(true);
            });
        }
        catch (UriFormatException)
        {
            Debug.LogError($"Bad URI: hostname \"{_serverUri}\" could not be parsed.");
        }
    }

    private void Update()
    {
        // Process web request manager queue
        _webRequestManager.UpdateWebRequests();
    }

    /// <summary>
    /// Check the server for any awaiting SDP offers.
    /// </summary>
    public void QueryOffers()
    {
        _webRequestManager.HttpGet(_serverUri + "/offers", (AsyncOperation asyncOp) =>
        {
            UnityWebRequestAsyncOperation webRequestAsyncOp = asyncOp as UnityWebRequestAsyncOperation;
            string offers = webRequestAsyncOp.webRequest.downloadHandler.text;
            if (ParseOffers(offers, out string remoteId, out string sdp))
            {
                _remoteId = remoteId;
                OnRemoteSDPOffer?.Invoke(sdp);
            }
            else
            {
                OnRemoteSDPOffer?.Invoke("");
            }
        });
    }

    private bool ParseOffers(string data, out string remoteId, out string sdp)
    {
        bool result = false;
        sdp = "";
        remoteId = "";

        if (data == "{}" || data == string.Empty)
        {
            return result;
        }

        SimpleJson.SimpleJson.TryDeserializeObject(data, out object obj);
        JsonObject jsonObj = (JsonObject)obj;
        foreach (var pair in jsonObj)
        {
            remoteId = pair.Key;
            JsonObject offerObj = (JsonObject)pair.Value;
            sdp = (string)offerObj["sdp"];
            result = true;
        }

        return result;
    }

    /// <summary>
    /// Initialize the server communication with the given server IP.
    /// </summary>
    /// <param name="serverIP">Server IP address.</param>
    public void Init(string serverIP)
    {
        _serverIP = serverIP;
        _serverUri = CreateServerURI(serverIP);
    }

    private string CreateServerURI(string serverAddress)
    {
        return "http://" + serverAddress + ":8080";
    }

    public string GetServerIP()
    {
        return _serverIP;
    }

    /// <summary>
    /// Send an SDP answer to the signaling server.
    /// </summary>
    /// <param name="answerSdp">The SDP answer.</param>
    public void SendAnswerToSignalServer(string answerSdp)
    {
        Debug.Log("Sending SDP answer to the server...");
        _webRequestManager.HttpPost(_serverUri + "/post_answer/" + _localId + "/" + _remoteId, FormatSdpOffer("answer", answerSdp));
    }

    public static string FormatSdpOffer(string offer, string sdp)
    {
        JsonObject jsonObj = new JsonObject
        {
            ["sdp"] = sdp,
            ["type"] = offer
        };
        return jsonObj.ToString();
    }

    /// <summary>
    /// Send an SDP offer to the signaling server.
    /// </summary>
    /// <param name="sdpOffer">The SDP offer.</param>
    public void SendOfferToSignalServer(string sdpOffer)
    {
        Debug.Log("Sending SDP offer to the server...");
        _webRequestManager.HttpPost(_serverUri + "/post_offer/" + _localId, FormatSdpOffer("offer", sdpOffer), (AsyncOperation ao) =>
        {
            OnSDPOfferSentInServer?.Invoke();
        });
    }

    /// <summary>
    /// Check the server for any SDP answers.
    /// </summary>
    public void CheckAnswers()
    {
        _webRequestManager.HttpGet(_serverUri + "/answer/" + _localId, (AsyncOperation asyncOp) =>
        {
            UnityWebRequestAsyncOperation webRequestAsyncOp = asyncOp as UnityWebRequestAsyncOperation;
            string response = webRequestAsyncOp.webRequest.downloadHandler.text;
            if (ParseAnswer(response, out string remoteId, out string remoteAnswer))
            {
                _remoteId = remoteId;
                OnAnswerChecked?.Invoke(remoteAnswer);
            }
            else
            {
                OnAnswerChecked?.Invoke("");
            }
        });
    }

    private bool ParseAnswer(string data, out string remoteId, out string sdp)
    {
        bool result = false;
        sdp = "";
        remoteId = "";

        if (data == "{}" || data == string.Empty)
        {
            return result;
        }

        SimpleJson.SimpleJson.TryDeserializeObject(data, out object obj);
        if (obj == null)
        {
            return false;
        }

        JsonObject jsonObj = (JsonObject)obj;
        if (jsonObj.ContainsKey("id") && jsonObj.ContainsKey("answer"))
        {
            remoteId = ((Int64)jsonObj["id"]).ToString();
            JsonObject answerObj = (JsonObject)jsonObj["answer"];
            sdp = (string)answerObj["sdp"];
            result = true;
        }

        return result;
    }

    /// <summary>
    /// Send an ICE candidate to the signaling server.
    /// </summary>
    /// <param name="candidate">The ICE candidate.</param>
    public void SendICECandidate(RTCIceCandidate candidate)
    {
        Debug.Log("Sending ICE candidate...");
        _webRequestManager.HttpPost(_serverUri + "/post_ice/" + _localId, FormatIceCandidate(candidate));
    }

    private string FormatIceCandidate(RTCIceCandidate iceCandidate)
    {
        JsonObject jsonObj = new JsonObject
        {
            ["candidate"] = iceCandidate.Candidate,
            ["sdpMLineIndex"] = iceCandidate.SdpMLineIndex,
            ["sdpMid"] = iceCandidate.SdpMid
        };
        return jsonObj.ToString();
    }

    /// <summary>
    /// Check the server for any remote ICE candidates.
    /// </summary>
    public void CheckRemoteIce()
    {
        if (string.IsNullOrEmpty(_remoteId))
        {
            Debug.LogError("Remote ID is null when checking remote ICEs");
            return;
        }

        _webRequestManager.HttpPost(_serverUri + "/consume_ices/" + _remoteId, "", (AsyncOperation asyncOp) =>
        {
            Debug.Log("Consuming ICE candidates");

            UnityWebRequestAsyncOperation webRequestAsyncOp = asyncOp as UnityWebRequestAsyncOperation;
            string iceCandidates = webRequestAsyncOp.webRequest.downloadHandler.text;

            // Parses all the ice candidates
            JsonObject jsonObjects = (JsonObject)SimpleJson.SimpleJson.DeserializeObject(iceCandidates);
            JsonArray jsonArray = (JsonArray)jsonObjects[0];

            foreach (JsonObject jsonObj in jsonArray)
            {
                OnNewRemoteICECandidate?.Invoke((string)jsonObj["candidate"], (string)jsonObj["sdpMid"], Convert.ToInt32(jsonObj["sdpMLineIndex"]));
            }
        });
    }

    /// <summary>
    /// Log out and disconnect from the server.
    /// </summary>
    public void Disconnect()
    {
        _webRequestManager.HttpPost(_serverUri + "/logout/" + _localId, string.Empty);
    }
}
