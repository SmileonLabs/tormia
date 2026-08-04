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
        private const string ZoneMotionFrameEventName = "zoneMotionFrame";

        [SerializeField] private OntologyWorldAuthorityClient authorityClient;
        [SerializeField, Tooltip(
            "Session-local realtime and remote-motion diagnostics. It records " +
            "transport health only and never owns Authority state.")]
        private OntologyRemoteMotionRuntimeMetrics runtimeMetrics;
        [SerializeField, TextArea] private string lastStatus;

        private readonly ConcurrentQueue<InboundMessage> inboundMessages = new();
        private ClientWebSocket socket;
        private CancellationTokenSource cancellation;
        private Task socketTask;
        private bool isConnecting;
        private bool handshakeConnected;
        private float reconnectAt;
        private string subscribedWorldId;
        private string subscribedZoneKey;
        private float nextHeartbeatAt;
        private int heartbeatInFlight;
        private long connectionGeneration;
        private int consecutiveConnectionFailures;

        public string LastStatus => lastStatus;
        public OntologyRemoteMotionRuntimeMetrics RuntimeMetrics => runtimeMetrics;
        public bool IsConnected => handshakeConnected &&
                                   socket != null &&
                                   socket.State == WebSocketState.Open;
        public long ConnectionGeneration => connectionGeneration;
        public event Action<OntologyAuthorityZoneRuntimeNotification> ZoneRuntimeChanged;
        public event Action<OntologyAuthorityRevisionOccurrence>
            RevisionOccurrenceReceived;
        public event Action<OntologyAuthorityZoneMotionFrame>
            ZoneMotionFrameReceived;

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
            consecutiveConnectionFailures = 0;
            reconnectAt = Time.unscaledTime;
        }

        private bool ShouldConnect()
        {
            return authorityClient != null && authorityClient.Settings != null &&
                   authorityClient.Settings.useRealtimeNotifications &&
                   authorityClient.IsWorldRuntimeReady;
        }

        private IEnumerator ConnectRoutine()
        {
            isConnecting = true;
            runtimeMetrics?.RecordConnectionAttempt(
                consecutiveConnectionFailures > 0);
            var generation = Interlocked.Increment(
                ref connectionGeneration);
            SetStatus("Connecting to authority realtime notifications...");

            var negotiationUrl = BuildHttpHubUrl("/negotiate?negotiateVersion=1");
            using var negotiate = new UnityWebRequest(negotiationUrl, UnityWebRequest.kHttpVerbPOST)
            {
                downloadHandler = new DownloadHandlerBuffer()
            };
            negotiate.SetRequestHeader("Authorization", "Bearer " + authorityClient.AccessToken);
            negotiate.SetRequestHeader("Content-Type", "application/json");
            yield return negotiate.SendWebRequest();

            if (negotiate.result != UnityWebRequest.Result.Success ||
                negotiate.responseCode < 200 || negotiate.responseCode >= 300)
            {
                FailConnection(
                    generation,
                    "Realtime negotiation failed: " + negotiate.error);
                yield break;
            }

            if (generation != Interlocked.Read(ref connectionGeneration) ||
                !ShouldConnect())
            {
                if (generation ==
                    Interlocked.Read(ref connectionGeneration))
                {
                    isConnecting = false;
                }
                yield break;
            }

            var negotiation = JsonUtility.FromJson<SignalRNegotiation>(negotiate.downloadHandler.text);
            if (negotiation == null || string.IsNullOrWhiteSpace(negotiation.connectionToken))
            {
                FailConnection(
                    generation,
                    "Realtime negotiation returned no connection token.");
                yield break;
            }

            var nextCancellation = new CancellationTokenSource();
            var nextSocket = new ClientWebSocket();
            nextSocket.Options.SetRequestHeader(
                "Authorization",
                "Bearer " + authorityClient.AccessToken);
            cancellation = nextCancellation;
            socket = nextSocket;
            socketTask = RunSocketAsync(
                nextSocket,
                BuildWebSocketHubUrl(negotiation.connectionToken),
                generation,
                nextCancellation.Token);
            subscribedWorldId = authorityClient.CurrentWorldId;
            subscribedZoneKey = authorityClient.CurrentProjectionZoneKey;
            // isConnecting deliberately remains true until the SignalR
            // handshake acknowledgement for this exact generation arrives.
        }

        private async Task RunSocketAsync(
            ClientWebSocket activeSocket,
            Uri hubUri,
            long generation,
            CancellationToken token)
        {
            try
            {
                await activeSocket.ConnectAsync(hubUri, token).ConfigureAwait(false);
                await SendTextAsync(
                    activeSocket,
                    "{\"protocol\":\"json\",\"version\":1}" + RecordSeparator,
                    token).ConfigureAwait(false);
                await ReceiveLoopAsync(
                        activeSocket,
                        generation,
                        token)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (!(exception is OperationCanceledException))
            {
                inboundMessages.Enqueue(
                    InboundMessage.Failed(
                        generation,
                        exception.Message));
            }
        }

        private async Task ReceiveLoopAsync(
            ClientWebSocket activeSocket,
            long generation,
            CancellationToken token)
        {
            var awaitingHandshake = true;
            var buffer = new byte[4096];
            while (!token.IsCancellationRequested &&
                   activeSocket.State == WebSocketState.Open)
            {
                using var message = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await activeSocket.ReceiveAsync(
                            new ArraySegment<byte>(buffer),
                            token)
                        .ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        throw new WebSocketException(
                            "Authority realtime socket was closed.");
                    }
                    message.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage);

                var text = Encoding.UTF8.GetString(message.ToArray());
                foreach (var frame in text.Split(RecordSeparator))
                {
                    if (string.IsNullOrWhiteSpace(frame))
                    {
                        continue;
                    }

                    if (awaitingHandshake)
                    {
                        var handshake = JsonUtility.FromJson<
                            SignalRHandshakeResponse>(frame);
                        if (handshake != null &&
                            !string.IsNullOrWhiteSpace(handshake.error))
                        {
                            throw new WebSocketException(
                                "Authority SignalR handshake failed: " +
                                handshake.error);
                        }
                        awaitingHandshake = false;
                        inboundMessages.Enqueue(
                            InboundMessage.Connected(generation));
                        continue;
                    }

                    inboundMessages.Enqueue(
                        InboundMessage.Json(generation, frame));
                }
            }

            if (!token.IsCancellationRequested)
            {
                throw new WebSocketException(
                    "Authority realtime socket ended without a close frame.");
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
                if (inbound.generation !=
                    Interlocked.Read(ref connectionGeneration))
                {
                    continue;
                }

                if (inbound.connected)
                {
                    isConnecting = false;
                    handshakeConnected = true;
                    consecutiveConnectionFailures = 0;
                    runtimeMetrics?.RecordConnectionSucceeded();
                    authorityClient.SetRealtimeNotificationState(true);
                    nextHeartbeatAt = Time.unscaledTime;
                    SetStatus("Authority realtime notifications connected.");
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(inbound.error))
                {
                    FailConnection(
                        inbound.generation,
                        "Realtime connection lost: " + inbound.error);
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
                    var occurrence = new OntologyAuthorityRevisionOccurrence
                    {
                        worldId = revision.worldId,
                        revision = revision.revision,
                        eventId = revision.eventId,
                        commandType = revision.commandType,
                        targetEntityId = revision.targetEntityId,
                        damageResult = revision.damageResult
                    };
                    RevisionOccurrenceReceived?.Invoke(occurrence);
                    authorityClient.BeginLoadWorld();
                    continue;
                }

                if (!string.Equals(header.target, ZoneRuntimeChangedEventName, StringComparison.Ordinal))
                {
                    if (string.Equals(
                            header.target,
                            ZoneMotionFrameEventName,
                            StringComparison.Ordinal))
                    {
                        ProcessZoneMotionFrame(inbound.json);
                    }
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
            Interlocked.Increment(ref connectionGeneration);
            var previousCancellation = cancellation;
            var previousSocket = socket;
            var previousTask = socketTask;

            cancellation = null;
            socket = null;
            socketTask = null;
            isConnecting = false;
            handshakeConnected = false;
            subscribedWorldId = string.Empty;
            subscribedZoneKey = string.Empty;
            nextHeartbeatAt = 0f;
            Interlocked.Exchange(ref heartbeatInFlight, 0);
            while (inboundMessages.TryDequeue(out _))
            {
            }

            previousCancellation?.Cancel();
            previousSocket?.Dispose();
            previousCancellation?.Dispose();
            if (previousTask != null)
            {
                _ = ObserveTaskCompletion(previousTask);
            }
        }

        private void FailConnection(long generation, string status)
        {
            if (generation != Interlocked.Read(ref connectionGeneration))
            {
                return;
            }

            authorityClient?.SetRealtimeNotificationState(false);
            SetStatus(status);
            consecutiveConnectionFailures++;
            var baseDelay = authorityClient?.Settings == null
                ? 2f
                : Mathf.Max(
                    0.25f,
                    authorityClient.Settings.realtimeReconnectDelaySeconds);
            var delay = ComputeReconnectDelay(
                baseDelay,
                consecutiveConnectionFailures,
                UnityEngine.Random.value);
            runtimeMetrics?.RecordConnectionFailure(
                status,
                delay,
                consecutiveConnectionFailures);
            Disconnect();
            reconnectAt = Time.unscaledTime + delay;
        }

        public static float ComputeReconnectDelay(
            float baseDelaySeconds,
            int consecutiveFailures,
            float jitterSample)
        {
            var baseDelay = Mathf.Max(0.25f, baseDelaySeconds);
            var exponent = Mathf.Clamp(consecutiveFailures - 1, 0, 5);
            var bounded = Mathf.Min(30f, baseDelay * (1 << exponent));
            var jitter = Mathf.Lerp(
                0.8f,
                1.2f,
                Mathf.Clamp01(jitterSample));
            return Mathf.Max(0.25f, bounded * jitter);
        }

        private string BuildHttpHubUrl(string suffix)
        {
            return authorityClient.Settings.RuntimeBaseUrl + HubPath + suffix;
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
            _ = SendHeartbeatAsync(
                socket,
                Interlocked.Read(ref connectionGeneration),
                cancellation.Token);
        }

        private async Task SendHeartbeatAsync(
            ClientWebSocket activeSocket,
            long generation,
            CancellationToken token)
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
                inboundMessages.Enqueue(
                    InboundMessage.Failed(
                        generation,
                        exception.Message));
            }
            finally
            {
                Interlocked.Exchange(ref heartbeatInFlight, 0);
            }
        }

        private Uri BuildWebSocketHubUrl(string connectionToken)
        {
            var baseUrl = authorityClient.Settings.RuntimeBaseUrl;
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
            if (runtimeMetrics == null)
            {
                runtimeMetrics = GetComponent<OntologyRemoteMotionRuntimeMetrics>();
                if (runtimeMetrics == null)
                {
                    runtimeMetrics = gameObject.AddComponent<OntologyRemoteMotionRuntimeMetrics>();
                }
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

        private void ProcessZoneMotionFrame(string json)
        {
            var message = JsonUtility.FromJson<
                SignalRZoneMotionFrameInvocation>(json);
            var frame = message?.arguments != null &&
                        message.arguments.Length > 0
                ? message.arguments[0]
                : null;
            if (frame == null ||
                !string.Equals(
                    frame.worldId,
                    authorityClient.CurrentWorldId,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(
                    frame.zoneKey,
                    authorityClient.CurrentProjectionZoneKey,
                    StringComparison.Ordinal))
            {
                return;
            }

            frame.items ??= Array.Empty<
                OntologyAuthorityPlayerMotionState>();
            runtimeMetrics?.RecordMotionFrameReceived(
                frame.serverTick,
                frame.observedAtUnixMilliseconds,
                frame.items.Length);
            ZoneMotionFrameReceived?.Invoke(frame);
        }

        private static async Task ObserveTaskCompletion(Task task)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch
            {
                // RunSocketAsync reports transport failures through the
                // generation-tagged queue. Cleanup must only observe the task.
            }
        }

        [Serializable] private sealed class SignalRNegotiation { public string connectionToken; }
        [Serializable] private sealed class SignalRHandshakeResponse
        {
            public string error;
        }
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
            public string targetEntityId;
            public bool damageResult;
        }
        [Serializable] private sealed class SignalRZoneMotionFrameInvocation
        {
            public int type;
            public string target;
            public OntologyAuthorityZoneMotionFrame[] arguments;
        }

        private readonly struct InboundMessage
        {
            public readonly string json;
            public readonly string error;
            public readonly bool connected;
            public readonly long generation;

            private InboundMessage(
                long generation,
                string json,
                string error = null,
                bool connected = false)
            {
                this.generation = generation;
                this.json = json;
                this.error = error;
                this.connected = connected;
            }

            public static InboundMessage Connected(long generation) =>
                new(generation, "", null, true);
            public static InboundMessage Failed(
                long generation,
                string error) =>
                new(generation, "", error);
            public static InboundMessage Json(
                long generation,
                string json) =>
                new(generation, json);
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

    /// <summary>
    /// Ephemeral server-owned motion frame. It is presentation transport only:
    /// it never authors Facts or replaces the durable world projection.
    /// </summary>
    [Serializable]
    public sealed class OntologyAuthorityZoneMotionFrame
    {
        public string worldId;
        public string zoneKey;
        public string frameOccurrenceId;
        public long serverTick;
        public long observedAtUnixMilliseconds;
        public OntologyAuthorityPlayerMotionState[] items;
    }

    [Serializable]
    public sealed class OntologyAuthorityRevisionOccurrence
    {
        public string worldId;
        public long revision;
        public string eventId;
        public string commandType;
        public string targetEntityId;
        public bool damageResult;
    }
}
