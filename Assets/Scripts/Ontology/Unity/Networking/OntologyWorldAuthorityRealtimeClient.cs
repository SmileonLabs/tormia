using System;
using System.Collections;
using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Receives only post-commit revision notices from the authority SignalR hub.
    /// It deliberately reloads the HTTP projection instead of mutating world facts
    /// from a socket payload, preserving one authoritative ontology boundary.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OntologyWorldAuthorityRealtimeClient : MonoBehaviour
    {
        private const char RecordSeparator = '\u001e';
        private const string HubPath = "/hubs/world-zone";
        private const string RevisionEventName = "worldRevision";
        private const string ZoneRuntimeChangedEventName = "zoneRuntimeChanged";

        [SerializeField] private OntologyWorldAuthorityClient authorityClient;
        [SerializeField, TextArea] private string lastStatus;

        private readonly ConcurrentQueue<InboundMessage> inboundMessages = new();
        private ClientWebSocket socket;
        private CancellationTokenSource cancellation;
        private Task socketTask;
        private bool isConnecting;
        private float reconnectAt;
        private string subscribedWorldId;
        private string subscribedZoneKey;
        private float nextHeartbeatAt;
        private int heartbeatInFlight;

        public string LastStatus => lastStatus;
        public bool IsConnected => socket != null && socket.State == WebSocketState.Open;
        public event Action<OntologyAuthorityZoneRuntimeNotification> ZoneRuntimeChanged;

        public void Configure(OntologyWorldAuthorityClient value)
        {
            authorityClient = value;
        }

        private void Awake()
        {
            ResolveDependencies();
        }

        private void OnEnable()
        {
            ResolveDependencies();
            if (authorityClient != null)
            {
                authorityClient.StateChanged += HandleAuthorityStateChanged;
            }
        }

        private void OnDisable()
        {
            if (authorityClient != null)
            {
                authorityClient.StateChanged -= HandleAuthorityStateChanged;
                authorityClient.SetRealtimeNotificationState(false);
            }
            Disconnect();
        }

        private void Update()
        {
            ProcessInboundMessages();

            if (IsConnected && Time.unscaledTime >= nextHeartbeatAt)
            {
                BeginHeartbeat();
            }

            if (!ShouldConnect() || isConnecting || IsConnected ||
                Time.unscaledTime < reconnectAt)
            {
                return;
            }

            StartCoroutine(ConnectRoutine());
        }

        [ContextMenu("Reconnect Authority Realtime Notifications")]
        public void Reconnect()
        {
            Disconnect();
            reconnectAt = Time.unscaledTime;
        }

        private bool ShouldConnect()
        {
            return authorityClient != null && authorityClient.Settings != null &&
                   authorityClient.Settings.useRealtimeNotifications &&
                   authorityClient.IsReady;
        }

        private IEnumerator ConnectRoutine()
        {
            isConnecting = true;
            SetStatus("Connecting to authority realtime notifications...");

            var negotiationUrl = BuildHttpHubUrl("/negotiate?negotiateVersion=1");
            using var negotiate = new UnityWebRequest(negotiationUrl, UnityWebRequest.kHttpVerbPOST)
            {
                downloadHandler = new DownloadHandlerBuffer()
            };
            negotiate.SetRequestHeader("X-Tormia-User-Id", authorityClient.CurrentUserId);
            negotiate.SetRequestHeader("Content-Type", "application/json");
            yield return negotiate.SendWebRequest();

            if (negotiate.result != UnityWebRequest.Result.Success ||
                negotiate.responseCode < 200 || negotiate.responseCode >= 300)
            {
                FailConnection("Realtime negotiation failed: " + negotiate.error);
                yield break;
            }

            var negotiation = JsonUtility.FromJson<SignalRNegotiation>(negotiate.downloadHandler.text);
            if (negotiation == null || string.IsNullOrWhiteSpace(negotiation.connectionToken))
            {
                FailConnection("Realtime negotiation returned no connection token.");
                yield break;
            }

            cancellation = new CancellationTokenSource();
            socket = new ClientWebSocket();
            socket.Options.SetRequestHeader("X-Tormia-User-Id", authorityClient.CurrentUserId);
            socketTask = RunSocketAsync(
                socket,
                BuildWebSocketHubUrl(negotiation.connectionToken),
                cancellation.Token);
            subscribedWorldId = authorityClient.CurrentWorldId;
            subscribedZoneKey = authorityClient.CurrentProjectionZoneKey;
            isConnecting = false;
        }

        private async Task RunSocketAsync(
            ClientWebSocket activeSocket,
            Uri hubUri,
            CancellationToken token)
        {
            try
            {
                await activeSocket.ConnectAsync(hubUri, token).ConfigureAwait(false);
                await SendTextAsync(
                    activeSocket,
                    "{\"protocol\":\"json\",\"version\":1}" + RecordSeparator,
                    token).ConfigureAwait(false);
                inboundMessages.Enqueue(InboundMessage.Connected());
                await ReceiveLoopAsync(activeSocket, token).ConfigureAwait(false);
            }
            catch (Exception exception) when (!(exception is OperationCanceledException))
            {
                inboundMessages.Enqueue(new InboundMessage("", exception.Message));
            }
        }

        private async Task ReceiveLoopAsync(ClientWebSocket activeSocket, CancellationToken token)
        {
            try
            {
                var buffer = new byte[4096];
                while (!token.IsCancellationRequested && activeSocket.State == WebSocketState.Open)
                {
                    using var message = new MemoryStream();
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await activeSocket.ReceiveAsync(new ArraySegment<byte>(buffer), token)
                            .ConfigureAwait(false);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            throw new WebSocketException("Authority realtime socket was closed.");
                        }
                        message.Write(buffer, 0, result.Count);
                    }
                    while (!result.EndOfMessage);

                    var text = Encoding.UTF8.GetString(message.ToArray());
                    foreach (var frame in text.Split(RecordSeparator))
                    {
                        if (!string.IsNullOrWhiteSpace(frame))
                        {
                            inboundMessages.Enqueue(new InboundMessage(frame));
                        }
                    }
                }
            }
            catch (Exception exception) when (!(exception is OperationCanceledException))
            {
                inboundMessages.Enqueue(new InboundMessage("", exception.Message));
            }
        }

        private static async Task SendTextAsync(
            ClientWebSocket activeSocket,
            string value,
            CancellationToken token)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            await activeSocket.SendAsync(
                    new ArraySegment<byte>(bytes),
                    WebSocketMessageType.Text,
                    true,
                    token)
                .ConfigureAwait(false);
        }

        private void ProcessInboundMessages()
        {
            while (inboundMessages.TryDequeue(out var inbound))
            {
                if (inbound.connected)
                {
                    authorityClient.SetRealtimeNotificationState(true);
                    nextHeartbeatAt = Time.unscaledTime;
                    SetStatus("Authority realtime notifications connected.");
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(inbound.error))
                {
                    FailConnection("Realtime connection lost: " + inbound.error);
                    continue;
                }

                var header = JsonUtility.FromJson<SignalRInvocationHeader>(inbound.json);
                if (header == null || header.type != 1 || string.IsNullOrWhiteSpace(header.target))
                {
                    continue; // SignalR handshake acknowledgement or unsupported hub message.
                }

                if (string.Equals(header.target, RevisionEventName, StringComparison.Ordinal))
                {
                    var message = JsonUtility.FromJson<SignalRRevisionInvocation>(inbound.json);
                    var revision = message?.arguments != null && message.arguments.Length > 0
                        ? message.arguments[0]
                        : null;
                    if (revision == null || revision.revision <= authorityClient.CurrentRevision)
                    {
                        continue;
                    }

                    SetStatus("Authority revision " + revision.revision + " received; refreshing projection.");
                    authorityClient.BeginLoadWorld();
                    continue;
                }

                if (!string.Equals(header.target, ZoneRuntimeChangedEventName, StringComparison.Ordinal))
                {
                    continue;
                }

                var runtimeMessage = JsonUtility.FromJson<SignalRZoneRuntimeInvocation>(inbound.json);
                var notification = runtimeMessage?.arguments != null && runtimeMessage.arguments.Length > 0
                    ? runtimeMessage.arguments[0]
                    : null;
                if (notification == null ||
                    !string.Equals(notification.worldId, authorityClient.CurrentWorldId, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(notification.zoneKey, authorityClient.CurrentProjectionZoneKey, StringComparison.Ordinal))
                {
                    continue;
                }

                ZoneRuntimeChanged?.Invoke(notification);
            }
        }

        private void HandleAuthorityStateChanged()
        {
            if (!ShouldConnect())
            {
                Disconnect();
                return;
            }

            if ((IsConnected || isConnecting) &&
                (!string.Equals(subscribedWorldId, authorityClient.CurrentWorldId, StringComparison.Ordinal) ||
                 !string.Equals(subscribedZoneKey, authorityClient.CurrentProjectionZoneKey, StringComparison.Ordinal)))
            {
                Reconnect();
            }
        }

        private void Disconnect()
        {
            cancellation?.Cancel();
            cancellation?.Dispose();
            cancellation = null;

            if (socket != null)
            {
                socket.Dispose();
                socket = null;
            }

            socketTask = null;
            isConnecting = false;
            subscribedWorldId = string.Empty;
            subscribedZoneKey = string.Empty;
            nextHeartbeatAt = 0f;
            Interlocked.Exchange(ref heartbeatInFlight, 0);
        }

        private void FailConnection(string status)
        {
            authorityClient?.SetRealtimeNotificationState(false);
            SetStatus(status);
            Disconnect();
            var delay = authorityClient?.Settings == null
                ? 2f
                : Mathf.Max(0.25f, authorityClient.Settings.realtimeReconnectDelaySeconds);
            reconnectAt = Time.unscaledTime + delay;
        }

        private string BuildHttpHubUrl(string suffix)
        {
            return authorityClient.Settings.baseUrl.Trim().TrimEnd('/') + HubPath + suffix;
        }

        private void BeginHeartbeat()
        {
            if (socket == null || cancellation == null ||
                Interlocked.Exchange(ref heartbeatInFlight, 1) != 0)
            {
                return;
            }

            var interval = authorityClient?.Settings == null
                ? 25f
                : Mathf.Max(5f, authorityClient.Settings.realtimeSessionHeartbeatSeconds);
            nextHeartbeatAt = Time.unscaledTime + interval;
            _ = SendHeartbeatAsync(socket, cancellation.Token);
        }

        private async Task SendHeartbeatAsync(ClientWebSocket activeSocket, CancellationToken token)
        {
            try
            {
                await SendTextAsync(
                    activeSocket,
                    "{\"type\":1,\"target\":\"Heartbeat\",\"arguments\":[]}" + RecordSeparator,
                    token).ConfigureAwait(false);
            }
            catch (Exception exception) when (!(exception is OperationCanceledException))
            {
                inboundMessages.Enqueue(new InboundMessage("", exception.Message));
            }
            finally
            {
                Interlocked.Exchange(ref heartbeatInFlight, 0);
            }
        }

        private Uri BuildWebSocketHubUrl(string connectionToken)
        {
            var baseUrl = authorityClient.Settings.baseUrl.Trim().TrimEnd('/');
            if (baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                baseUrl = "wss://" + baseUrl.Substring("https://".Length);
            }
            else if (baseUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                baseUrl = "ws://" + baseUrl.Substring("http://".Length);
            }

            var query = "?worldId=" + Uri.EscapeDataString(authorityClient.CurrentWorldId) +
                        "&id=" + Uri.EscapeDataString(connectionToken);
            var zoneKey = authorityClient.CurrentProjectionZoneKey;
            if (!string.IsNullOrWhiteSpace(zoneKey))
            {
                query += "&zoneKey=" + Uri.EscapeDataString(zoneKey.Trim());
            }
            return new Uri(baseUrl + HubPath + query);
        }

        private void ResolveDependencies()
        {
            if (authorityClient == null)
            {
                authorityClient = GetComponent<OntologyWorldAuthorityClient>() ??
                                  FindAnyObjectByType<OntologyWorldAuthorityClient>();
            }
        }

        private void SetStatus(string value)
        {
            lastStatus = value ?? string.Empty;
        }

        private static string TaskError(Task task)
        {
            return task.Exception?.GetBaseException().Message ?? "unknown transport error";
        }

        [Serializable] private sealed class SignalRNegotiation { public string connectionToken; }
        [Serializable] private sealed class SignalRInvocationHeader
        {
            public int type;
            public string target;
        }
        [Serializable] private sealed class SignalRRevisionInvocation
        {
            public int type;
            public string target;
            public AuthorityRevisionNotification[] arguments;
        }
        [Serializable] private sealed class SignalRZoneRuntimeInvocation
        {
            public int type;
            public string target;
            public OntologyAuthorityZoneRuntimeNotification[] arguments;
        }
        [Serializable] private sealed class AuthorityRevisionNotification
        {
            public string worldId;
            public long revision;
            public string eventId;
            public string commandType;
            public string zoneKey;
        }

        private readonly struct InboundMessage
        {
            public readonly string json;
            public readonly string error;
            public readonly bool connected;

            public InboundMessage(string json, string error = null, bool connected = false)
            {
                this.json = json;
                this.error = error;
                this.connected = connected;
            }

            public static InboundMessage Connected() => new("", null, true);
        }
    }

    /// <summary>Small SignalR hint. Position data remains in the authority HTTP snapshot.</summary>
    [Serializable]
    public sealed class OntologyAuthorityZoneRuntimeNotification
    {
        public string worldId;
        public string zoneKey;
        public long observedAtUnixMilliseconds;
    }
}
