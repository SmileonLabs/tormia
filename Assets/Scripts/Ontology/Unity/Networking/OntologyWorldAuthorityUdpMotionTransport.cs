using System;
using System.Collections;
using System.Collections.Generic;
using System.Security.Cryptography;
using Tormia.Ontology.Realtime.Protocol;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Optional LiteNetLib carrier for already canonical motion intents.
    ///
    /// Stage 7 deliberately keeps this transport inactive by default. Until
    /// Authority consumes authenticated UDP intents and returns ordered input
    /// acknowledgements, this component delegates the active writer to the
    /// reliable HTTP transport. It can obtain and validate a UDP admission
    /// session without creating gameplay state; a later stage enables it as
    /// the sole writer after the Authority pipeline is ready.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OntologyWorldAuthorityUdpMotionTransport : MonoBehaviour,
        IPlayerMotionIntentTransport
    {
        [SerializeField] private OntologyWorldAuthorityClient authorityClient;
        [SerializeField] private OntologyWorldAuthorityHttpMotionTransport
            reliableFallback;
        [SerializeField] private OntologyWorldAuthorityMotionSnapshotFeed
            motionSnapshotFeed;
        [SerializeField, Tooltip(
            "Remains off until Authority UDP intent consumption and ordered " +
            "acknowledgement are enabled. HTTP stays the only active writer.")]
        private bool enableUdpMotionWriter;
        [SerializeField, Tooltip(
            "Receives authenticated Authority motion snapshots. This is " +
            "independent from the UDP input writer and defaults off until " +
            "the realtime rollout is enabled.")]
        private bool enableUdpSnapshotReceiver;
        [SerializeField] private string udpHost = string.Empty;
        [SerializeField, Min(1)] private int udpPort;
        [SerializeField, Min(0.25f)] private float connectTimeoutSeconds = 3f;
        [SerializeField, Min(0.25f)] private float authorityAckTimeoutSeconds = 1.5f;
        [SerializeField, TextArea] private string lastStatus;
        [SerializeField] private OntologyRealtimeTransportCounters counters = new();

        private IAuthorityRealtimeClock realtimeClock;
        private IAuthorityUdpClientDriver injectedUdpDriver;
        private IAuthorityUdpClientDriver boundUdpDriver;
        private IAuthorityMotionControlPlane controlPlane;
        private byte[] datagramAuthenticationKey = Array.Empty<byte>();
        private Guid transportSessionId;
        private Guid runtimeSessionId;
        private ulong transportGeneration;
        private ulong nextPacketSequence = 1;
        private ulong admissionGeneration;
        private float connectionDeadline;
        private bool udpConnectionAttemptActive;
        private bool connected;
        private ulong activeTicketRequestGeneration;
        private AdmissionContext pendingAdmissionContext;
        private bool hasPendingAdmissionContext;
        private Guid admittedWorldId;
        private Guid admittedAvatarEntityId;
        private string admittedZoneKey = string.Empty;
        private readonly Dictionary<Guid, SnapshotAssembly>
            snapshotAssemblies = new();
        private readonly Queue<Guid> snapshotAssemblyOrder = new();
        private ulong highestReceivedPacketSequence;
        private ulong receivedPacketWindow;
        private int snapshotAssemblyBytes;
        private const int MaximumSnapshotAssemblies = 8;
        private const int MaximumSnapshotAssemblyBytes = 1024 * 1024;
        private const float MaximumSnapshotAssemblyAgeSeconds = 0.75f;
        private readonly OntologyAuthorityMotionWriterStateMachine
            writerState = new();
        private OntologyAuthorityUdpTransportTicket writerTicket;
        private bool writerTransitionInFlight;
        private float nextFallbackRetryAt;
        private bool reactivationRecoveryInFlight;
        private int promotionRevisionRetryCount;

        public bool IsUdpFeatureEnabled =>
            enableUdpMotionWriter || enableUdpSnapshotReceiver;
        public bool IsUdpMotionWriterEnabled => enableUdpMotionWriter;
        public bool IsUdpSnapshotReceiverEnabled => enableUdpSnapshotReceiver;
        public bool IsUdpReady => connected &&
            injectedUdpDriver != null && injectedUdpDriver.IsReady &&
            datagramAuthenticationKey.Length == 32;
        public bool IsUdpWriterActive => writerState.CanWriteUdp;
        public string LastStatus => lastStatus;
        public OntologyRealtimeTransportCounters Counters => counters;
        public int PendingSnapshotAssemblyCount => snapshotAssemblies.Count;
        public int PendingSnapshotAssemblyBytes => snapshotAssemblyBytes;

        public void ConfigureRuntimeSeams(
            IAuthorityRealtimeClock clock,
            IAuthorityUdpClientDriver udpDriver,
            IAuthorityMotionControlPlane authorityControlPlane)
        {
            if (clock != null) realtimeClock = clock;
            if (udpDriver != null && !ReferenceEquals(
                    injectedUdpDriver, udpDriver))
            {
                try { injectedUdpDriver?.Close(); }
                catch { }
                UnbindUdpDriver();
                injectedUdpDriver = udpDriver;
                BindUdpDriver();
            }
            if (authorityControlPlane != null)
                controlPlane = authorityControlPlane;
        }

        public void Configure(
            OntologyWorldAuthorityClient authority,
            OntologyWorldAuthorityHttpMotionTransport fallback = null)
        {
            if (authority != null) authorityClient = authority;
            if (fallback != null) reliableFallback = fallback;
        }

        public void ConfigureEndpoint(string host, int port,
            bool enableWriter, bool enableReceiver)
        {
            udpHost = host?.Trim() ?? string.Empty;
            udpPort = Mathf.Max(0, port);
            enableUdpMotionWriter = enableWriter && udpPort > 0 &&
                !string.IsNullOrWhiteSpace(udpHost);
            enableUdpSnapshotReceiver = enableReceiver && udpPort > 0 &&
                !string.IsNullOrWhiteSpace(udpHost);
        }

        /// <summary>
        /// Gets an HTTPS-issued one-time credential and sends the exact 112-byte
        /// LiteNetLib connection bootstrap. This has no gameplay effect and is
        /// safe to call only for the currently active Authority avatar session.
        /// </summary>
        public IEnumerator PrepareUdpAdmissionRoutine(
            Guid avatarEntityId,
            Action<bool> completed = null)
        {
            ResolveDependencies();
            if (writerState.State != OntologyMotionWriterState.HttpActive ||
                activeTicketRequestGeneration != 0 || !TryCaptureAdmissionContext(
                    avatarEntityId, out var context) ||
                string.IsNullOrWhiteSpace(udpHost) || udpPort <= 0)
            {
                SetStatus("UDP transport admission is not configured.");
                completed?.Invoke(false);
                yield break;
            }

            // A late HTTPS ticket callback must never recreate a socket after a
            // disable, re-entry, or runtime-session swap. The captured context
            // is checked again after every asynchronous boundary below.
            var requestGeneration = ++admissionGeneration;
            StopUdpTransport("UDP transport re-admission.", false);
            activeTicketRequestGeneration = requestGeneration;
            pendingAdmissionContext = context;
            hasPendingAdmissionContext = true;
            OntologyAuthorityUdpTransportTicket ticket = null;
            var bootstrap = Array.Empty<byte>();
            try
            {
                yield return controlPlane.IssueTicket(
                    avatarEntityId,
                    context.RuntimeSessionId.ToString("D"),
                    RealtimeWireContract.ProtocolVersion,
                    value => ticket = value);

                if (!IsAdmissionContextCurrent(context, requestGeneration) ||
                    !TryAcceptTicket(ticket, context, out bootstrap))
                {
                    completed?.Invoke(false);
                    yield break;
                }

                try
                {
                    StartUdpTransport(bootstrap);
                    udpConnectionAttemptActive = true;
                    connectionDeadline = Clock.MonotonicSeconds +
                        Mathf.Max(0.25f, connectTimeoutSeconds);
                    SetStatus("UDP transport admission requested.");
                }
                catch (Exception exception)
                {
                    StopUdpTransport("UDP transport start failed: " +
                        exception.GetType().Name);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(bootstrap);
                }

                while (!writerState.CanWriteUdp &&
                       Clock.MonotonicSeconds < connectionDeadline)
                {
                    if (!IsAdmissionContextCurrent(context, requestGeneration))
                    {
                        SuspendUdpWriterFailClosed(
                            "UDP transport admission superseded.");
                        completed?.Invoke(false);
                        yield break;
                    }

                    if ((injectedUdpDriver == null ||
                         !injectedUdpDriver.IsReady) &&
                        writerState.State ==
                            OntologyMotionWriterState.HttpActive)
                        break;

                    yield return null;
                }
                if (!writerState.CanWriteUdp &&
                    (writerState.State ==
                        OntologyMotionWriterState.AdmissionConnected ||
                     writerState.State ==
                        OntologyMotionWriterState.PromotionPending))
                    BeginAuthorityFallback(
                        "UDP transport admission timed out.");
                completed?.Invoke(writerState.CanWriteUdp &&
                    IsAdmissionContextCurrent(context, requestGeneration));
            }
            finally
            {
                if (activeTicketRequestGeneration == requestGeneration)
                {
                    activeTicketRequestGeneration = 0;
                    pendingAdmissionContext = default;
                    hasPendingAdmissionContext = false;
                }
                ClearSensitiveTicketFields(ticket);
            }
        }

        /// <summary>
        /// Produces and emits one authenticated motion datagram over unreliable
        /// channel zero. This method does not report Authority acceptance; only
        /// the stage-8 ordered acknowledgement path may make it the active
        /// IPlayerMotionIntentTransport writer.
        /// </summary>
        public bool TrySendAuthenticatedMotionIntent(
            Guid avatarEntityId,
            string zoneKey,
            long inputSequence,
            Vector2 move,
            float requestedSpeed,
            bool hasDestination,
            Vector2 destination,
            float destinationStopDistance,
            long lastAcknowledgedAuthorityTick = 0)
        {
            if (!enableUdpMotionWriter || !writerState.CanWriteUdp ||
                !IsUdpReady || authorityClient == null ||
                !Guid.TryParse(authorityClient.CurrentWorldId, out var worldId) ||
                avatarEntityId == Guid.Empty || inputSequence <= 0 ||
                string.IsNullOrWhiteSpace(zoneKey) ||
                !IsCurrentSendContext(worldId, avatarEntityId, zoneKey))
            {
                return false;
            }

            var facing = move.sqrMagnitude > 0.0001f
                ? move.normalized : Vector2.up;
            var payload = new MotionIntentPayload
            {
                WorldId = worldId,
                ZoneKey = zoneKey.Trim(),
                ActorEntityId = avatarEntityId,
                InputSequence = checked((ulong)inputSequence),
                ClientTimestampMilliseconds = Clock.UtcMilliseconds,
                MoveX = Mathf.Clamp(move.x, -1f, 1f),
                MoveZ = Mathf.Clamp(move.y, -1f, 1f),
                RequestedSpeed = Mathf.Max(0f, requestedSpeed),
                HasDestination = hasDestination,
                DestinationX = hasDestination ? destination.x : 0f,
                DestinationZ = hasDestination ? destination.y : 0f,
                DestinationStopDistance = hasDestination
                    ? Mathf.Max(0f, destinationStopDistance) : 0f,
                FacingX = facing.x,
                FacingZ = facing.y,
                Flags = MotionIntentFlags.None
            };
            var metadata = new RealtimePacketMetadata(
                transportSessionId,
                transportGeneration,
                nextPacketSequence++,
                Math.Max(0L, lastAcknowledgedAuthorityTick));
            if (!RealtimeWireCodec.TryCreateMotionIntentForAuthentication(
                    metadata, payload, out var datagram, out var error) ||
                !TryAuthenticateDatagram(datagram, datagramAuthenticationKey, out error))
            {
                SetStatus("UDP motion packet rejected locally: " + error + ".");
                return false;
            }

            try
            {
                if (injectedUdpDriver == null ||
                    !injectedUdpDriver.TrySend(datagram))
                    throw new InvalidOperationException(
                        "UDP driver rejected datagram.");
                IncrementCounter(ref counters.udpSent);
                return true;
            }
            catch (Exception exception)
            {
                IncrementCounter(ref counters.udpDropped);
                BeginAuthorityFallback("UDP motion send failed: " +
                    exception.GetType().Name + ".");
                return false;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(datagram);
            }
        }

        public IEnumerator SendPlayerIntentRoutine(
            Guid avatarEntityId, string zoneKey, long sequence, Vector2 move,
            float requestedSpeed, bool hasDestination, Vector2 destination,
            float destinationStopDistance,
            OntologyAuthorityActionDefinitionProjection action,
            Action<OntologyAuthorityRuntimeIntentResult> completed)
        {
            ResolveDependencies();
            if (writerState.CanWriteUdp)
            {
                if (!TrySendAuthenticatedMotionIntent(
                        avatarEntityId, zoneKey, sequence, move,
                        requestedSpeed, hasDestination, destination,
                        destinationStopDistance,
                        writerState.LastProcessedIntentSequence))
                {
                    BeginAuthorityFallback("UDP motion send failed.");
                    completed?.Invoke(OntologyAuthorityRuntimeIntentResult.Rejected(
                        "authority_udp_motion_send_failed"));
                    yield break;
                }

                var acknowledgementTimeout = Mathf.Max(
                    0.25f, authorityAckTimeoutSeconds);
                var deadline = Clock.MonotonicSeconds + acknowledgementTimeout;
                while (writerState.CanWriteUdp &&
                       Clock.MonotonicSeconds < deadline)
                {
                    if (motionSnapshotFeed != null &&
                        motionSnapshotFeed.TryGetLatestPlayerMotion(
                            avatarEntityId, acknowledgementTimeout,
                            out var state) && state != null &&
                        Guid.TryParse(state.runtimeSessionId,
                            out var observedSession) &&
                        Guid.TryParse(state.worldId, out var observedWorld) &&
                        state.lastProcessedIntentSequence >= sequence)
                    {
                        if (writerState.ObserveAuthorityProgress(
                                observedSession, observedWorld, state.zoneKey,
                                avatarEntityId,
                                state.lastProcessedIntentSequence))
                        {
                            completed?.Invoke(
                                new OntologyAuthorityRuntimeIntentResult
                                {
                                    accepted = true
                                });
                            yield break;
                        }
                    }
                    yield return null;
                }

                BeginAuthorityFallback("UDP Authority acknowledgement timed out.");
                IncrementCounter(ref counters.ackTimeouts);
                completed?.Invoke(OntologyAuthorityRuntimeIntentResult.Rejected(
                    "authority_udp_acknowledgement_timeout"));
                yield break;
            }

            if (!writerState.CanWriteHttp || reliableFallback == null)
            {
                completed?.Invoke(OntologyAuthorityRuntimeIntentResult.Rejected(
                    "authority_motion_writer_fenced"));
                yield break;
            }

            yield return reliableFallback.SendPlayerIntentRoutine(
                avatarEntityId, zoneKey, sequence, move, requestedSpeed,
                hasDestination, destination, destinationStopDistance, action,
                completed);
        }

        private void Awake() => ResolveDependencies();

        private void Update()
        {
            if (!IsUdpFeatureEnabled)
            {
                if (writerState.State ==
                        OntologyMotionWriterState.RecoveryPending)
                {
                    if (!writerTransitionInFlight &&
                        Clock.MonotonicSeconds >= nextFallbackRetryAt)
                    {
                        nextFallbackRetryAt = Clock.MonotonicSeconds + 0.5f;
                        StartCoroutine(CompleteFallbackRecoveryRoutine());
                    }
                }
                else if (writerState.State !=
                         OntologyMotionWriterState.HttpActive)
                    BeginAuthorityFallback("UDP transport feature disabled.");
                else if (injectedUdpDriver != null ||
                         datagramAuthenticationKey.Length > 0 ||
                         activeTicketRequestGeneration != 0)
                    StopUdpTransport("UDP transport feature disabled.");
                return;
            }

            if (activeTicketRequestGeneration != 0 &&
                (!hasPendingAdmissionContext ||
                 !IsAdmissionContextCurrent(pendingAdmissionContext,
                     activeTicketRequestGeneration)))
            {
                SuspendUdpWriterFailClosed(
                    "UDP transport admission binding changed.");
                return;
            }

            if (writerState.State != OntologyMotionWriterState.HttpActive &&
                !IsCurrentAdmissionBinding())
            {
                if (!reactivationRecoveryInFlight &&
                    CanRecoverAuthoritativeHttpReactivation())
                    StartCoroutine(RecoverAuthoritativeHttpReactivation());
                else
                    SuspendUdpWriterFailClosed(
                        "UDP transport runtime binding changed.");
                return;
            }

            if (writerState.State == OntologyMotionWriterState.FallbackPending &&
                !writerTransitionInFlight &&
                Clock.MonotonicSeconds >= nextFallbackRetryAt)
            {
                nextFallbackRetryAt = Clock.MonotonicSeconds + 0.5f;
                StartCoroutine(FallbackToHttpRoutine());
            }
            if (writerState.State == OntologyMotionWriterState.RecoveryPending &&
                !writerTransitionInFlight &&
                Clock.MonotonicSeconds >= nextFallbackRetryAt)
            {
                nextFallbackRetryAt = Clock.MonotonicSeconds + 0.5f;
                StartCoroutine(CompleteFallbackRecoveryRoutine());
            }
            if (injectedUdpDriver == null) return;
            injectedUdpDriver.Poll();
            PruneExpiredSnapshotAssemblies();
            if (udpConnectionAttemptActive && !connected &&
                Clock.MonotonicSeconds >= connectionDeadline)
            {
                if (writerState.State == OntologyMotionWriterState.HttpActive)
                    StopUdpTransport("UDP transport admission timed out.");
                else
                    BeginAuthorityFallback(
                        "UDP transport admission timed out.");
            }
        }

        private void OnDisable() => SuspendUdpWriterFailClosed(
            "UDP transport disabled.");

        private void OnDestroy()
        {
            SuspendUdpWriterFailClosed("UDP transport destroyed.");
            UnbindUdpDriver();
        }

        private bool TryAcceptTicket(
            OntologyAuthorityUdpTransportTicket ticket,
            AdmissionContext expected,
            out byte[] bootstrap)
        {
            bootstrap = Array.Empty<byte>();
            byte[] key = Array.Empty<byte>();
            if (ticket == null || !ticket.accepted ||
                ticket.protocolVersion != RealtimeWireContract.ProtocolVersion ||
                !string.Equals(ticket.writerMode, "http",
                    StringComparison.Ordinal) ||
                ticket.writerEpoch <= 0 || ticket.worldRevision < 0 ||
                ticket.expiresAtUnixMilliseconds <= Clock.UtcMilliseconds ||
                !Guid.TryParse(ticket.runtimeSessionId, out var issuedRuntime) ||
                issuedRuntime != expected.RuntimeSessionId ||
                !Guid.TryParse(ticket.authenticatedTransportSessionId,
                    out var session) || ticket.transportGeneration == 0 ||
                !OntologyUdpRealtimeBootstrap.TryCreateFrame(ticket.ticket,
                    ticket.datagramAuthenticationKey, session,
                    ticket.transportGeneration, out bootstrap) ||
                !OntologyUdpRealtimeBootstrap.TryDecode(
                    ticket.datagramAuthenticationKey, 32,
                    out key))
            {
                if (bootstrap.Length > 0)
                    CryptographicOperations.ZeroMemory(bootstrap);
                bootstrap = Array.Empty<byte>();
                if (key.Length > 0)
                    CryptographicOperations.ZeroMemory(key);
                SetStatus("UDP transport ticket was rejected: " +
                    (ticket?.rejectionCode ?? "invalid_ticket") + ".");
                return false;
            }

            runtimeSessionId = issuedRuntime;
            transportSessionId = session;
            transportGeneration = ticket.transportGeneration;
            datagramAuthenticationKey = key;
            admittedWorldId = expected.WorldId;
            admittedAvatarEntityId = expected.AvatarEntityId;
            admittedZoneKey = expected.ZoneKey;
            writerTicket = new OntologyAuthorityUdpTransportTicket
            {
                accepted = true,
                authenticatedTransportSessionId =
                    ticket.authenticatedTransportSessionId,
                runtimeSessionId = ticket.runtimeSessionId,
                transportGeneration = ticket.transportGeneration,
                protocolVersion = ticket.protocolVersion,
                writerMode = ticket.writerMode,
                writerEpoch = ticket.writerEpoch,
                worldRevision = ticket.worldRevision
            };
            promotionRevisionRetryCount = 0;
            reliableFallback.SetAuthorityWriterEpoch(writerTicket.writerEpoch);
            nextPacketSequence = 1;
            return true;
        }

        private IEnumerator PromoteUdpWriterRoutine()
        {
            if (writerTransitionInFlight || writerTicket == null ||
                authorityClient == null)
                yield break;
            if (writerState.State ==
                    OntologyMotionWriterState.AdmissionConnected &&
                !writerState.BeginPromotion()) yield break;
            if (writerState.State !=
                OntologyMotionWriterState.PromotionPending) yield break;
            writerTransitionInFlight = true;
            OntologyAuthorityMotionTransportTransition result = null;
            yield return controlPlane.TransitionWriter(
                admittedAvatarEntityId, writerTicket, true,
                value => result = value);
            writerTransitionInFlight = false;
            if (writerState.State ==
                OntologyMotionWriterState.FallbackPending)
            {
                if (TryValidateTransition(result, "udp") &&
                    writerState.ResolvePromotionDuringFallback(
                        runtimeSessionId, transportSessionId,
                        transportGeneration, result.writerEpoch))
                    ApplyTransition(result);
                nextFallbackRetryAt = Clock.MonotonicSeconds;
                yield break;
            }
            if (!TryValidateTransition(result, "udp") ||
                !writerState.AcceptPromotion(
                    runtimeSessionId, transportSessionId,
                    transportGeneration, result.writerEpoch))
            {
                if (promotionRevisionRetryCount < 3 &&
                    TryValidateCurrentHttpWriter(result, false) &&
                    result.writerEpoch == writerTicket.writerEpoch &&
                    result.worldRevision != writerTicket.worldRevision)
                {
                    promotionRevisionRetryCount++;
                    writerTicket.worldRevision = result.worldRevision;
                    StartCoroutine(PromoteUdpWriterRoutine());
                    yield break;
                }
                BeginAuthorityFallback(
                    "UDP writer promotion result was not authoritative.");
                yield break;
            }
            ApplyTransition(result);
            SetStatus("Authority promoted UDP as the sole motion writer.");
        }

        private void BeginAuthorityFallback(string reason)
        {
            if (writerState.State == OntologyMotionWriterState.HttpActive)
            {
                StopUdpTransport(reason);
                return;
            }
            if (writerState.State !=
                    OntologyMotionWriterState.FallbackPending &&
                !writerState.RequireFallback()) return;
            CloseUdpSocket();
            SetStatus(reason + " Awaiting Authority HTTP fallback.");
            if (isActiveAndEnabled && !writerTransitionInFlight)
                StartCoroutine(FallbackToHttpRoutine());
        }

        private void SuspendUdpWriterFailClosed(string reason)
        {
            if (writerState.State != OntologyMotionWriterState.HttpActive)
                writerState.RequireFallback();
            CloseUdpSocket();
            SetStatus(reason + " Motion writers remain fenced.");
        }

        private bool CanRecoverAuthoritativeHttpReactivation() =>
            authorityClient != null && admittedWorldId != Guid.Empty &&
            admittedAvatarEntityId != Guid.Empty &&
            Guid.TryParse(authorityClient.CurrentWorldId,
                out var currentWorld) && currentWorld == admittedWorldId &&
            authorityClient.IsPlayerRuntimeActiveFor(admittedAvatarEntityId) &&
            Guid.TryParse(authorityClient.ActiveRuntimeSessionId,
                out var currentRuntime) && currentRuntime != runtimeSessionId &&
            string.Equals(authorityClient.ActiveMotionWriterMode, "http",
                StringComparison.Ordinal) &&
            authorityClient.ActiveMotionWriterEpoch > 0 &&
            authorityClient.ActiveMotionWriterWorldRevision >= 0;

        private IEnumerator RecoverAuthoritativeHttpReactivation()
        {
            reactivationRecoveryInFlight = true;
            SuspendUdpWriterFailClosed(
                "Authority runtime session was replaced.");
            var expectedSessionText = authorityClient.ActiveRuntimeSessionId;
            var expectedEpoch = authorityClient.ActiveMotionWriterEpoch;
            var expectedZone = authorityClient.CurrentProjectionZoneKey;
            IReadOnlyList<OntologyAuthorityPlayerMotionState> recovery = null;
            yield return controlPlane.LoadCompleteZoneRecovery(
                expectedZone, value => recovery = value);
            var valid = false;
            if (recovery != null)
            foreach (var state in recovery)
            {
                if (state != null &&
                    string.Equals(state.avatarEntityId,
                        admittedAvatarEntityId.ToString("D"),
                        StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(state.runtimeSessionId,
                        expectedSessionText,
                        StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(state.zoneKey, expectedZone,
                        StringComparison.Ordinal))
                {
                    valid = true;
                    break;
                }
            }
            if (valid && authorityClient.ActiveRuntimeSessionId ==
                    expectedSessionText &&
                authorityClient.ActiveMotionWriterEpoch == expectedEpoch &&
                string.Equals(authorityClient.ActiveMotionWriterMode, "http",
                    StringComparison.Ordinal))
            {
                reliableFallback.SetAuthorityWriterEpoch(expectedEpoch);
                StopUdpTransport(
                    "Authority HTTP writer recovered for the new runtime session.");
            }
            else
            {
                SetStatus(
                    "Authority runtime replacement awaits reliable recovery.");
            }
            reactivationRecoveryInFlight = false;
        }

        private IEnumerator FallbackToHttpRoutine()
        {
            if (writerTransitionInFlight || writerTicket == null ||
                authorityClient == null) yield break;
            writerTransitionInFlight = true;
            IncrementCounter(ref counters.fallbackAttempts);
            OntologyAuthorityMotionTransportTransition result = null;
            yield return controlPlane.TransitionWriter(
                admittedAvatarEntityId, writerTicket, false,
                value => result = value);
            writerTransitionInFlight = false;
            if (!TryValidateTransition(result, "http") ||
                !writerState.AcceptFallback(
                    runtimeSessionId, transportSessionId,
                    transportGeneration, result.writerEpoch))
            {
                if (TryValidateCurrentHttpWriter(result, false) &&
                    result.writerEpoch == writerTicket.writerEpoch &&
                    writerState.ResolveVerifiedHttpWriter(
                        result.writerEpoch))
                {
                    writerTicket.worldRevision = result.worldRevision;
                    nextFallbackRetryAt = Clock.MonotonicSeconds;
                    yield break;
                }
                if (TryValidateCurrentHttpWriter(result, true) &&
                    result.writerEpoch == writerTicket.writerEpoch + 1 &&
                    writerState.AcceptFallback(
                        runtimeSessionId, transportSessionId,
                        transportGeneration, result.writerEpoch))
                {
                    ApplyTransition(result);
                    nextFallbackRetryAt = Clock.MonotonicSeconds;
                    yield break;
                }
                if (TryValidateCurrentUdpWriter(result) &&
                    result.writerEpoch == writerTicket.writerEpoch &&
                    result.worldRevision != writerTicket.worldRevision)
                {
                    writerTicket.worldRevision = result.worldRevision;
                    nextFallbackRetryAt = Clock.MonotonicSeconds;
                    yield break;
                }
                SetStatus("Authority HTTP fallback not yet acknowledged: " +
                    (result?.rejectionCode ?? "invalid_transition") + ".");
                nextFallbackRetryAt = Clock.MonotonicSeconds + 0.5f;
                yield break;
            }

            ApplyTransition(result);
            yield return CompleteFallbackRecoveryRoutine();
        }

        private IEnumerator CompleteFallbackRecoveryRoutine()
        {
            if (writerTransitionInFlight ||
                writerState.State != OntologyMotionWriterState.RecoveryPending)
                yield break;
            writerTransitionInFlight = true;
            IReadOnlyList<OntologyAuthorityPlayerMotionState> recovery = null;
            yield return controlPlane.LoadCompleteZoneRecovery(
                admittedZoneKey, value => recovery = value);
            writerTransitionInFlight = false;
            var hasLocalRecovery = false;
            if (recovery != null)
            foreach (var state in recovery)
            {
                if (state != null &&
                    string.Equals(state.worldId,
                        admittedWorldId.ToString("D"),
                        StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(state.zoneKey, admittedZoneKey,
                        StringComparison.Ordinal) &&
                    Guid.TryParse(state.avatarEntityId, out var actor) &&
                    actor == admittedAvatarEntityId &&
                    Guid.TryParse(state.runtimeSessionId, out var session) &&
                    session == runtimeSessionId &&
                    state.lastProcessedIntentSequence >=
                        writerState.LastProcessedIntentSequence)
                {
                    hasLocalRecovery = true;
                    break;
                }
            }
            if (!hasLocalRecovery || !writerState.CompleteReliableRecovery(
                    runtimeSessionId, admittedWorldId, admittedZoneKey,
                    admittedAvatarEntityId, writerState.WriterEpoch))
            {
                nextFallbackRetryAt = Clock.MonotonicSeconds + 0.5f;
                SetStatus("HTTP writer fallback awaits complete reliable recovery.");
                yield break;
            }
            reliableFallback.SetAuthorityWriterEpoch(writerTicket.writerEpoch);
            IncrementCounter(ref counters.fallbackSuccesses);
            StopUdpTransport("Authority restored HTTP as the sole motion writer.");
        }

        private bool TryValidateTransition(
            OntologyAuthorityMotionTransportTransition result,
            string expectedMode)
        {
            if (result == null || !result.accepted || writerTicket == null ||
                result.writerEpoch != writerTicket.writerEpoch + 1 ||
                result.worldRevision != writerTicket.worldRevision ||
                !string.Equals(result.writerMode, expectedMode,
                    StringComparison.Ordinal) ||
                !Guid.TryParse(result.runtimeSessionId,
                    out var resultRuntime) || resultRuntime != runtimeSessionId)
                return false;
            if (string.Equals(expectedMode, "udp", StringComparison.Ordinal))
                return Guid.TryParse(
                           result.authenticatedTransportSessionId,
                           out var resultTransport) &&
                       resultTransport == transportSessionId &&
                       result.transportGeneration == transportGeneration;
            return (!Guid.TryParse(result.authenticatedTransportSessionId,
                        out var fallbackTransport) ||
                    fallbackTransport == Guid.Empty) &&
                   result.transportGeneration == 0 &&
                   Guid.TryParse(
                       result.previousAuthenticatedTransportSessionId,
                       out var previousTransport) &&
                   previousTransport == transportSessionId &&
                   result.previousTransportGeneration == transportGeneration;
        }

        private void ApplyTransition(
            OntologyAuthorityMotionTransportTransition result)
        {
            writerTicket.writerMode = result.writerMode;
            writerTicket.writerEpoch = result.writerEpoch;
            writerTicket.worldRevision = result.worldRevision;
        }

        private bool TryValidateCurrentHttpWriter(
            OntologyAuthorityMotionTransportTransition result,
            bool requirePreviousBinding) =>
            result != null && writerTicket != null &&
            string.Equals(result.writerMode, "http",
                StringComparison.Ordinal) &&
            result.writerEpoch > 0 && result.worldRevision >= 0 &&
            Guid.TryParse(result.runtimeSessionId, out var resultRuntime) &&
            resultRuntime == runtimeSessionId &&
            (!Guid.TryParse(result.authenticatedTransportSessionId,
                 out var currentTransport) ||
             currentTransport == Guid.Empty) &&
            result.transportGeneration == 0 &&
            (!requirePreviousBinding ||
             (Guid.TryParse(
                  result.previousAuthenticatedTransportSessionId,
                  out var previousTransport) &&
              previousTransport == transportSessionId &&
              result.previousTransportGeneration == transportGeneration));

        private bool TryValidateCurrentUdpWriter(
            OntologyAuthorityMotionTransportTransition result) =>
            result != null && writerTicket != null &&
            string.Equals(result.writerMode, "udp",
                StringComparison.Ordinal) &&
            result.writerEpoch > 0 && result.worldRevision >= 0 &&
            Guid.TryParse(result.runtimeSessionId, out var resultRuntime) &&
            resultRuntime == runtimeSessionId &&
            Guid.TryParse(result.authenticatedTransportSessionId,
                out var currentTransport) &&
            currentTransport == transportSessionId &&
            result.transportGeneration == transportGeneration;

        private void CloseUdpSocket()
        {
            connected = false;
            udpConnectionAttemptActive = false;
            if (injectedUdpDriver != null)
            {
                try { injectedUdpDriver.Close(); }
                catch { }
            }
        }

        private void StartUdpTransport(byte[] bootstrap)
        {
            BindUdpDriver();
            if (injectedUdpDriver == null ||
                !injectedUdpDriver.Start(udpHost.Trim(), udpPort, bootstrap))
                throw new InvalidOperationException(
                    "UDP client driver failed to start.");
        }

        private void HandlePeerConnected()
        {
            connected = true;
            udpConnectionAttemptActive = false;
            SetStatus("UDP transport admission connected.");
            if (enableUdpMotionWriter && writerTicket != null &&
                writerState.RegisterConnectedAdmission(
                    runtimeSessionId, transportSessionId,
                    transportGeneration, admittedWorldId,
                    admittedZoneKey, admittedAvatarEntityId,
                    writerTicket.writerEpoch))
                StartCoroutine(PromoteUdpWriterRoutine());
        }

        private void HandlePeerDisconnected(string reason) =>
            BeginAuthorityFallback(
                "UDP transport disconnected: " + reason + ".");

        private void HandleNetworkError(string error) =>
            BeginAuthorityFallback(
                "UDP transport network error: " + error + ".");

        private void HandleNetworkReceive(byte[] datagram)
        {
            if (!enableUdpSnapshotReceiver || datagram == null ||
                datagram.Length == 0 ||
                datagram.Length > RealtimeWireContract.MaximumDatagramLength)
                return;
            TryProcessAuthoritySnapshotDatagram(datagram);
        }

        internal bool TryProcessAuthoritySnapshotDatagram(byte[] datagram)
        {
            if (!enableUdpSnapshotReceiver || !connected ||
                datagramAuthenticationKey.Length != 32 || datagram == null ||
                !RealtimeWireCodec.TryReadStructuralEnvelope(
                    datagram, out var envelope, out _) ||
                envelope.Header.MessageKind !=
                    RealtimeMessageKind.AuthorityMotionSnapshot ||
                envelope.Header.AuthenticatedTransportSessionId !=
                    transportSessionId ||
                envelope.Header.TransportGeneration != transportGeneration ||
                !TryVerifyAuthenticationTag(datagram,
                    datagramAuthenticationKey) ||
                !RealtimeWireCodec.TryDecodeAuthorityMotionSnapshotStructure(
                    datagram, out var header, out var payload, out _) ||
                payload.WorldId != admittedWorldId ||
                !string.Equals(payload.ZoneKey, admittedZoneKey,
                    StringComparison.Ordinal) ||
                !IsCurrentAdmissionBinding())
                return false;

            if (!IsPacketSequenceCandidate(envelope.Header.PacketSequence))
            {
                IncrementCounter(ref counters.replayRejected);
                return false;
            }

            MarkPacketSequenceReceived(header.PacketSequence);
            return TryAddSnapshotPage(payload, header.MessageTick,
                datagram.Length);
        }

        private bool TryAddSnapshotPage(
            AuthorityMotionSnapshotPayload page,
            long serverTick,
            int datagramBytes)
        {
            PruneExpiredSnapshotAssemblies();
            if (!snapshotAssemblies.TryGetValue(
                    page.FrameOccurrenceId, out var assembly))
            {
                if (snapshotAssemblies.Count >= MaximumSnapshotAssemblies ||
                    snapshotAssemblyBytes >
                        MaximumSnapshotAssemblyBytes - datagramBytes)
                {
                    IncrementCounter(ref counters.assemblyRejected);
                    return false;
                }
                assembly = new SnapshotAssembly(page, serverTick,
                    Clock.MonotonicSeconds);
                snapshotAssemblies.Add(page.FrameOccurrenceId, assembly);
                snapshotAssemblyOrder.Enqueue(page.FrameOccurrenceId);
            }
            if (!assembly.TryAdd(page, serverTick, datagramBytes,
                    MaximumSnapshotAssemblyBytes - snapshotAssemblyBytes))
            {
                IncrementCounter(ref counters.assemblyRejected);
                return false;
            }
            snapshotAssemblyBytes += datagramBytes;
            if (!assembly.IsComplete) return true;

            snapshotAssemblies.Remove(page.FrameOccurrenceId);
            snapshotAssemblyBytes -= assembly.ByteCount;
            var pages = assembly.GetOrderedPages();
            if (!RealtimeWireCodec.TryValidateCompleteAuthorityMotionSnapshotPages(
                    pages, out _))
            {
                IncrementCounter(ref counters.assemblyRejected);
                return false;
            }
            PublishSnapshotPages(pages, serverTick);
            return true;
        }

        private void PublishSnapshotPages(
            IReadOnlyList<AuthorityMotionSnapshotPayload> pages,
            long serverTick)
        {
            if (pages.Count == 0 || motionSnapshotFeed == null) return;
            var first = pages[0];
            var states = new OntologyAuthorityPlayerMotionState[
                first.TotalItemCount];
            var outputIndex = 0;
            foreach (var page in pages)
            foreach (var item in page.Items)
            {
                states[outputIndex++] = new OntologyAuthorityPlayerMotionState
                {
                    worldId = first.WorldId.ToString("D"),
                    avatarEntityId = item.ActorEntityId.ToString("D"),
                    zoneKey = first.ZoneKey,
                    positionX = item.PositionX,
                    positionY = item.PositionY,
                    positionZ = item.PositionZ,
                    velocityX = item.VelocityX,
                    velocityY = item.VelocityY,
                    velocityZ = item.VelocityZ,
                    grounded = (item.Flags &
                        AuthorityMotionStateFlags.Grounded) != 0,
                    serverTick = item.ActorServerTick,
                    lastProcessedIntentSequence = (long)Math.Min(
                        item.LastProcessedInputSequence, (ulong)long.MaxValue),
                    runtimeSessionId =
                        item.ActorRuntimeSessionId.ToString("D"),
                    motionStatus = item.MotionStatus,
                    updatedAtUnixMilliseconds =
                        first.ObservedAtUnixMilliseconds
                };
            }
            motionSnapshotFeed.PublishAuthenticatedUdpFrame(
                new OntologyAuthorityZoneMotionFrame
                {
                    worldId = first.WorldId.ToString("D"),
                    zoneKey = first.ZoneKey,
                    frameOccurrenceId =
                        first.FrameOccurrenceId.ToString("D"),
                    serverTick = serverTick,
                    observedAtUnixMilliseconds =
                        first.ObservedAtUnixMilliseconds,
                    items = states
                });
        }

        private bool IsPacketSequenceCandidate(ulong sequence)
        {
            if (sequence == 0) return false;
            if (sequence > highestReceivedPacketSequence) return true;
            var distance = highestReceivedPacketSequence - sequence;
            return distance < 64 &&
                   (receivedPacketWindow & (1UL << (int)distance)) == 0;
        }

        private void MarkPacketSequenceReceived(ulong sequence)
        {
            if (sequence > highestReceivedPacketSequence)
            {
                var shift = sequence - highestReceivedPacketSequence;
                receivedPacketWindow = shift >= 64
                    ? 1UL : (receivedPacketWindow << (int)shift) | 1UL;
                highestReceivedPacketSequence = sequence;
                return;
            }
            receivedPacketWindow |= 1UL <<
                (int)(highestReceivedPacketSequence - sequence);
        }

        private void PruneExpiredSnapshotAssemblies()
        {
            var now = Clock.MonotonicSeconds;
            while (snapshotAssemblyOrder.Count > 0)
            {
                var id = snapshotAssemblyOrder.Peek();
                if (!snapshotAssemblies.TryGetValue(id, out var value))
                {
                    snapshotAssemblyOrder.Dequeue();
                    continue;
                }
                if (now - value.StartedAt <=
                    MaximumSnapshotAssemblyAgeSeconds) break;
                snapshotAssemblyOrder.Dequeue();
                snapshotAssemblies.Remove(id);
                snapshotAssemblyBytes -= value.ByteCount;
                IncrementCounter(ref counters.assemblyExpired);
            }
        }

        private void StopUdpTransport(string status,
            bool invalidateAdmission = true)
        {
            if (invalidateAdmission)
            {
                ++admissionGeneration;
                activeTicketRequestGeneration = 0;
                pendingAdmissionContext = default;
                hasPendingAdmissionContext = false;
            }
            CloseUdpSocket();
            if (datagramAuthenticationKey.Length > 0)
                CryptographicOperations.ZeroMemory(datagramAuthenticationKey);
            datagramAuthenticationKey = Array.Empty<byte>();
            transportSessionId = Guid.Empty;
            runtimeSessionId = Guid.Empty;
            transportGeneration = 0;
            nextPacketSequence = 1;
            admittedWorldId = Guid.Empty;
            admittedAvatarEntityId = Guid.Empty;
            admittedZoneKey = string.Empty;
            snapshotAssemblies.Clear();
            snapshotAssemblyOrder.Clear();
            snapshotAssemblyBytes = 0;
            highestReceivedPacketSequence = 0;
            receivedPacketWindow = 0;
            writerTicket = null;
            writerTransitionInFlight = false;
            reactivationRecoveryInFlight = false;
            nextFallbackRetryAt = 0f;
            promotionRevisionRetryCount = 0;
            writerState.Reset();
            SetStatus(status);
        }

        private bool TryCaptureAdmissionContext(Guid avatarEntityId,
            out AdmissionContext context)
        {
            context = default;
            if (!IsUdpFeatureEnabled || !isActiveAndEnabled ||
                authorityClient == null || avatarEntityId == Guid.Empty ||
                !authorityClient.IsPlayerRuntimeActiveFor(avatarEntityId) ||
                !Guid.TryParse(authorityClient.CurrentWorldId, out var worldId) ||
                !Guid.TryParse(authorityClient.ActiveRuntimeSessionId,
                    out var activeRuntimeSession))
            {
                return false;
            }

            var zoneKey = authorityClient.CurrentProjectionZoneKey?.Trim();
            if (string.IsNullOrWhiteSpace(zoneKey)) return false;
            context = new AdmissionContext(
                worldId, avatarEntityId, activeRuntimeSession, zoneKey);
            return true;
        }

        private bool IsAdmissionContextCurrent(AdmissionContext expected,
            ulong expectedGeneration)
        {
            return isActiveAndEnabled && IsUdpFeatureEnabled &&
                   admissionGeneration == expectedGeneration &&
                   authorityClient != null &&
                   authorityClient.IsPlayerRuntimeActiveFor(
                       expected.AvatarEntityId) &&
                   Guid.TryParse(authorityClient.CurrentWorldId,
                       out var currentWorldId) &&
                   currentWorldId == expected.WorldId &&
                   Guid.TryParse(authorityClient.ActiveRuntimeSessionId,
                       out var currentRuntimeSessionId) &&
                   currentRuntimeSessionId == expected.RuntimeSessionId &&
                   string.Equals(authorityClient.CurrentProjectionZoneKey?.Trim(),
                       expected.ZoneKey, StringComparison.Ordinal);
        }

        private bool IsCurrentAdmissionBinding()
        {
            if (admittedWorldId == Guid.Empty ||
                admittedAvatarEntityId == Guid.Empty ||
                runtimeSessionId == Guid.Empty ||
                string.IsNullOrWhiteSpace(admittedZoneKey))
            {
                return false;
            }

            return IsAdmissionContextCurrent(new AdmissionContext(
                admittedWorldId, admittedAvatarEntityId, runtimeSessionId,
                admittedZoneKey), admissionGeneration);
        }

        private bool IsCurrentSendContext(Guid worldId, Guid avatarEntityId,
            string zoneKey)
        {
            return worldId == admittedWorldId &&
                   avatarEntityId == admittedAvatarEntityId &&
                   string.Equals(zoneKey.Trim(), admittedZoneKey,
                       StringComparison.Ordinal) &&
                   IsCurrentAdmissionBinding();
        }

        private static void ClearSensitiveTicketFields(
            OntologyAuthorityUdpTransportTicket ticket)
        {
            if (ticket == null) return;
            // .NET strings are immutable and cannot be deterministically
            // zeroed. Remove references as soon as the decoded byte buffers
            // have been consumed so the GC can reclaim the sensitive payload.
            ticket.ticket = null;
            ticket.datagramAuthenticationKey = null;
        }

        private readonly struct AdmissionContext
        {
            public AdmissionContext(Guid worldId, Guid avatarEntityId,
                Guid runtimeSessionId, string zoneKey)
            {
                WorldId = worldId;
                AvatarEntityId = avatarEntityId;
                RuntimeSessionId = runtimeSessionId;
                ZoneKey = zoneKey;
            }

            public Guid WorldId { get; }
            public Guid AvatarEntityId { get; }
            public Guid RuntimeSessionId { get; }
            public string ZoneKey { get; }
        }

        private static bool TryAuthenticateDatagram(
            byte[] datagram, byte[] key, out RealtimeWireError error)
        {
            error = RealtimeWireError.None;
            if (datagram == null || key == null || key.Length != 32 ||
                !RealtimeWireCodec.TryGetAuthenticatedRegion(datagram,
                    out var region, out error))
            {
                return false;
            }

            byte[] digest = null;
            try
            {
                using var hmac = new HMACSHA256(key);
                digest = hmac.ComputeHash(datagram, 0, region.Length);
                return RealtimeWireCodec.TryWriteAuthenticationTag(datagram,
                    digest.AsSpan(0,
                        RealtimeWireContract.RequiredAuthenticationTagLength),
                    out error);
            }
            finally
            {
                if (digest != null) CryptographicOperations.ZeroMemory(digest);
            }
        }

        private static bool TryVerifyAuthenticationTag(
            byte[] datagram, byte[] key)
        {
            if (datagram == null || key == null || key.Length != 32 ||
                !RealtimeWireCodec.TryReadStructuralEnvelope(
                    datagram, out var envelope, out _)) return false;
            byte[] digest = null;
            try
            {
                using var hmac = new HMACSHA256(key);
                digest = hmac.ComputeHash(
                    datagram, 0, envelope.AuthenticatedRegion.Length);
                return CryptographicOperations.FixedTimeEquals(
                    digest.AsSpan(0,
                        RealtimeWireContract.RequiredAuthenticationTagLength),
                    envelope.AuthenticationTag);
            }
            finally
            {
                if (digest != null) CryptographicOperations.ZeroMemory(digest);
            }
        }

        private void ResolveDependencies()
        {
            authorityClient ??= GetComponent<OntologyWorldAuthorityClient>();
            reliableFallback ??= GetComponent<
                OntologyWorldAuthorityHttpMotionTransport>();
            if (reliableFallback == null)
                reliableFallback = gameObject.AddComponent<
                    OntologyWorldAuthorityHttpMotionTransport>();
            reliableFallback.Configure(authorityClient);
            motionSnapshotFeed ??=
                GetComponent<OntologyWorldAuthorityMotionSnapshotFeed>();
            if (motionSnapshotFeed == null)
                motionSnapshotFeed = gameObject.AddComponent<
                    OntologyWorldAuthorityMotionSnapshotFeed>();
            motionSnapshotFeed.Configure(authorityClient,
                GetComponent<OntologyWorldAuthorityRealtimeClient>());
            controlPlane ??= new OntologyAuthorityMotionControlPlaneAdapter(
                authorityClient, motionSnapshotFeed);
            injectedUdpDriver ??= new OntologyLiteNetUdpClientDriver();
            BindUdpDriver();
        }

        private void BindUdpDriver()
        {
            if (injectedUdpDriver == null || ReferenceEquals(
                    boundUdpDriver, injectedUdpDriver)) return;
            UnbindUdpDriver();
            boundUdpDriver = injectedUdpDriver;
            boundUdpDriver.Connected += HandlePeerConnected;
            boundUdpDriver.Disconnected += HandlePeerDisconnected;
            boundUdpDriver.NetworkFailed += HandleNetworkError;
            boundUdpDriver.DatagramReceived += HandleNetworkReceive;
        }

        private void UnbindUdpDriver()
        {
            if (boundUdpDriver == null) return;
            boundUdpDriver.Connected -= HandlePeerConnected;
            boundUdpDriver.Disconnected -= HandlePeerDisconnected;
            boundUdpDriver.NetworkFailed -= HandleNetworkError;
            boundUdpDriver.DatagramReceived -= HandleNetworkReceive;
            boundUdpDriver = null;
        }

        private IAuthorityRealtimeClock Clock =>
            realtimeClock ??= new UnityAuthorityRealtimeClock();

        private static void IncrementCounter(ref long counter)
        {
            if (counter < long.MaxValue) counter++;
        }

        private void SetStatus(string value) => lastStatus = value ?? string.Empty;

        private sealed class SnapshotAssembly
        {
            private readonly AuthorityMotionSnapshotPayload[] pages;
            private int pageCount;

            internal SnapshotAssembly(AuthorityMotionSnapshotPayload first,
                long serverTick, float startedAt)
            {
                OccurrenceId = first.FrameOccurrenceId;
                WorldId = first.WorldId;
                ZoneKey = first.ZoneKey;
                ObservedAt = first.ObservedAtUnixMilliseconds;
                ServerTick = serverTick;
                TotalItemCount = first.TotalItemCount;
                pages = new AuthorityMotionSnapshotPayload[first.PageCount];
                StartedAt = startedAt;
            }

            internal Guid OccurrenceId { get; }
            internal Guid WorldId { get; }
            internal string ZoneKey { get; }
            internal long ObservedAt { get; }
            internal long ServerTick { get; }
            internal ushort TotalItemCount { get; }
            internal float StartedAt { get; }
            internal int ByteCount { get; private set; }
            internal bool IsComplete => pageCount == pages.Length;

            internal bool TryAdd(AuthorityMotionSnapshotPayload page,
                long serverTick, int bytes, int remainingByteBudget)
            {
                if (page.FrameOccurrenceId != OccurrenceId ||
                    page.WorldId != WorldId ||
                    !string.Equals(page.ZoneKey, ZoneKey,
                        StringComparison.Ordinal) ||
                    page.ObservedAtUnixMilliseconds != ObservedAt ||
                    page.TotalItemCount != TotalItemCount ||
                    page.PageCount != pages.Length ||
                    serverTick != ServerTick || page.PageIndex >= pages.Length ||
                    pages[page.PageIndex] != null || bytes <= 0 ||
                    bytes > remainingByteBudget)
                    return false;
                pages[page.PageIndex] = page;
                pageCount++;
                ByteCount += bytes;
                return true;
            }

            internal IReadOnlyList<AuthorityMotionSnapshotPayload>
                GetOrderedPages() => pages;
        }

    }
}
