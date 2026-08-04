using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using LiteNetLib;
using LiteNetLib.Utils;
using Microsoft.Extensions.Logging.Abstractions;
using Tormia.Ontology.Realtime.Protocol;
using Xunit;

public sealed class AuthorityUdpRealtimeListenerIntegrationTests
{
    [Fact]
    public async Task Loopback_AcceptsFixedBootstrapAndOnlyUnreliableChannelZeroReachesSink()
    {
        var fixture = new ListenerFixture(maximumAttemptsPerAddress: 4);
        await fixture.Start();
        var client = new LoopbackClient();
        try
        {
            client.Connect(fixture.Port, fixture.BootstrapBytes());
            Assert.True(await client.WaitUntil(() => client.Peer is not null));

            var valid = fixture.MotionPacket(1);
            client.Peer!.Send(valid, 0, DeliveryMethod.Unreliable);
            Assert.True(await client.WaitUntil(() => fixture.Sink.Count == 1));

            var wrongLane = fixture.MotionPacket(2);
            client.Peer.Send(wrongLane, 0, DeliveryMethod.ReliableOrdered);
            await client.PumpFor(TimeSpan.FromMilliseconds(150));
            Assert.Equal(1, fixture.Sink.Count);
        }
        finally
        {
            client.Dispose();
            await fixture.DisposeAsync();
        }
    }

    [Fact]
    public async Task Loopback_MalformedAndPerAddressRateLimitedConnectionsAreRejected()
    {
        var fixture = new ListenerFixture(maximumAttemptsPerAddress: 1);
        await fixture.Start();
        var malformed = new LoopbackClient();
        var first = new LoopbackClient();
        var limited = new LoopbackClient();
        try
        {
            malformed.Connect(fixture.Port, new byte[] { 1, 2, 3 });
            Assert.True(await malformed.WaitUntil(() => malformed.Disconnected));
            Assert.Null(malformed.Peer);

            // Malformed frames are rejected before admission accounting. The
            // first valid bootstrap is accepted and consumes this IP window.
            first.Connect(fixture.Port, fixture.BootstrapBytes());
            Assert.True(await first.WaitUntil(() => first.Peer is not null));
            first.Dispose();
            await Task.Delay(30);

            limited.Connect(fixture.Port, fixture.BootstrapBytes());
            Assert.True(await limited.WaitUntil(() => limited.Disconnected));
            Assert.Null(limited.Peer);
        }
        finally
        {
            malformed.Dispose();
            first.Dispose();
            limited.Dispose();
            await fixture.DisposeAsync();
        }
    }

    [Fact]
    public async Task Loopback_ShutdownCancelsPendingRedemptionAndRejectsConnection()
    {
        var port = ListenerFixture.ReserveUdpPort();
        var options = AuthorityUdpListenerOptions.Disabled with
        {
            Enabled = true,
            Port = port,
            PendingHandshakeCapacity = 1,
            HandshakeTimeout = TimeSpan.FromSeconds(5)
        };
        var binding = ListenerFixture.CreateBinding();
        var key = RandomNumberGenerator.GetBytes(32);
        var store = new BlockingRedeemTicketStore(binding, key);
        using var diagnostics = new AuthorityUdpListenerDiagnostics();
        using var sessions = new AuthorityUdpPeerSessionRegistry();
        var sink = new CapturingSink();
        var fence = new AuthorityUdpTransportGenerationFence(store, options);
        var processor = new AuthorityUdpDatagramProcessor(
            sessions, fence, sink, diagnostics);
        var outbound = new AuthorityUdpOutboundSnapshotChannel(
            options.OutboundSnapshotCapacity,
            options.OutboundSnapshotByteCapacity);
        using var service = new AuthorityUdpRealtimeListener(
            options,
            store,
            new AcceptingValidator(),
            sessions,
            fence,
            processor,
            outbound,
            diagnostics,
            NullLogger<AuthorityUdpRealtimeListener>.Instance);
        var client = new LoopbackClient();
        try
        {
            await service.StartAsync(CancellationToken.None);
            client.Connect(port, ListenerFixture.BootstrapBytes(binding));
            Assert.True(await client.WaitUntil(() => store.Started.IsCompleted));

            await service.StopAsync(CancellationToken.None);
            await client.PumpFor(TimeSpan.FromMilliseconds(100));

            Assert.True(store.CancellationObserved);
            Assert.Null(client.Peer);
        }
        finally
        {
            client.Dispose();
            CryptographicOperations.ZeroMemory(key);
        }
    }

    [Fact]
    public async Task Loopback_ReconnectRequiresFreshGenerationAndRejectsOldBootstrap()
    {
        var port = ListenerFixture.ReserveUdpPort();
        var options = AuthorityUdpListenerOptions.Disabled with
        {
            Enabled = true,
            Port = port,
            PendingHandshakeCapacity = 4,
            DatagramProcessingCapacity = 8,
            AuthenticatedIntentCapacity = 8,
            HandshakeTimeout = TimeSpan.FromSeconds(1),
            GenerationFenceTimeout = TimeSpan.FromSeconds(1)
        };
        var firstBinding = ListenerFixture.CreateBinding();
        var secondBinding = firstBinding with
        {
            TransportGeneration = firstBinding.TransportGeneration + 1,
            AuthenticatedTransportSessionId = Guid.NewGuid()
        };
        var firstKey = RandomNumberGenerator.GetBytes(32);
        var secondKey = RandomNumberGenerator.GetBytes(32);
        var store = new RotatingTicketStore(firstBinding, firstKey);
        using var diagnostics = new AuthorityUdpListenerDiagnostics();
        using var sessions = new AuthorityUdpPeerSessionRegistry();
        var sink = new CapturingSink();
        var fence = new AuthorityUdpTransportGenerationFence(store, options);
        var processor = new AuthorityUdpDatagramProcessor(
            sessions, fence, sink, diagnostics);
        var outbound = new AuthorityUdpOutboundSnapshotChannel(
            options.OutboundSnapshotCapacity,
            options.OutboundSnapshotByteCapacity);
        using var service = new AuthorityUdpRealtimeListener(
            options, store, new AcceptingValidator(), sessions, fence,
            processor, outbound, diagnostics,
            NullLogger<AuthorityUdpRealtimeListener>.Instance);
        var first = new LoopbackClient();
        var fresh = new LoopbackClient();
        var stale = new LoopbackClient();
        try
        {
            await service.StartAsync(CancellationToken.None);
            first.Connect(port,
                ListenerFixture.BootstrapBytes(firstBinding));
            Assert.True(await first.WaitUntil(() => first.Peer is not null));
            first.Peer!.Send(
                ListenerFixture.MotionPacket(
                    firstBinding, firstKey, 1),
                0, DeliveryMethod.Unreliable);
            Assert.True(await first.WaitUntil(() => sink.Count == 1));
            first.Dispose();

            store.Rotate(secondBinding, secondKey);
            fresh.Connect(port,
                ListenerFixture.BootstrapBytes(secondBinding));
            Assert.True(await fresh.WaitUntil(() => fresh.Peer is not null));
            fresh.Peer!.Send(
                ListenerFixture.MotionPacket(
                    secondBinding, secondKey, 2),
                0, DeliveryMethod.Unreliable);
            Assert.True(await fresh.WaitUntil(() => sink.Count == 2));

            stale.Connect(port,
                ListenerFixture.BootstrapBytes(firstBinding));
            Assert.True(await stale.WaitUntil(() => stale.Disconnected));
            Assert.Null(stale.Peer);
        }
        finally
        {
            first.Dispose();
            fresh.Dispose();
            stale.Dispose();
            await service.StopAsync(CancellationToken.None);
            CryptographicOperations.ZeroMemory(firstKey);
            CryptographicOperations.ZeroMemory(secondKey);
        }
    }

    private sealed class ListenerFixture : IAsyncDisposable
    {
        private readonly byte[] key = Enumerable.Range(1, 32)
            .Select(value => (byte)value).ToArray();
        private readonly UdpTransportTicketBinding binding = CreateBinding();
        private readonly AuthorityUdpRealtimeListener listener;
        private readonly AuthorityUdpListenerDiagnostics diagnostics = new();

        internal ListenerFixture(int maximumAttemptsPerAddress)
        {
            Port = ReserveUdpPort();
            var options = AuthorityUdpListenerOptions.Disabled with
            {
                Enabled = true,
                Port = Port,
                PendingHandshakeCapacity = 2,
                DatagramProcessingCapacity = 8,
                AuthenticatedIntentCapacity = 8,
                HandshakeTimeout = TimeSpan.FromSeconds(1),
                GenerationFenceTimeout = TimeSpan.FromSeconds(1),
                Admission = new(
                    MaximumPending: 2,
                    MaximumPendingPerAddress: 2,
                    MaximumAttemptsPerAddressPerWindow: maximumAttemptsPerAddress,
                    AttemptWindow: TimeSpan.FromSeconds(10),
                    MaximumTrackedAddresses: 8)
            };
            var store = new FixtureTicketStore(binding, key);
            var sessions = new AuthorityUdpPeerSessionRegistry();
            Sink = new CapturingSink();
            var fence = new AuthorityUdpTransportGenerationFence(store, options);
            var processor = new AuthorityUdpDatagramProcessor(
                sessions,
                fence,
                Sink,
                diagnostics);
            var outbound = new AuthorityUdpOutboundSnapshotChannel(
                options.OutboundSnapshotCapacity,
                options.OutboundSnapshotByteCapacity);
            listener = new(
                options,
                store,
                new AcceptingValidator(),
                sessions,
                fence,
                processor,
                outbound,
                diagnostics,
                NullLogger<AuthorityUdpRealtimeListener>.Instance);
        }

        internal int Port { get; }
        internal CapturingSink Sink { get; }
        internal Task Start() => listener.StartAsync(CancellationToken.None);

        internal byte[] BootstrapBytes()
            => BootstrapBytes(binding);

        internal static byte[] BootstrapBytes(UdpTransportTicketBinding binding)
        {
            var ticket = Enumerable.Repeat((byte)1, 32).ToArray();
            var nonce = Enumerable.Repeat((byte)2, 16).ToArray();
            var proof = Enumerable.Repeat((byte)3, 32).ToArray();
            return AuthorityUdpBootstrapCodec.Encode(new(
                binding.AuthenticatedTransportSessionId,
                binding.TransportGeneration,
                ticket,
                nonce,
                proof));
        }

        internal byte[] MotionPacket(ulong sequence)
            => MotionPacket(binding, key, sequence);

        internal static byte[] MotionPacket(
            UdpTransportTicketBinding packetBinding,
            byte[] packetKey,
            ulong sequence)
        {
            var payload = new MotionIntentPayload
            {
                WorldId = packetBinding.WorldId,
                ZoneKey = packetBinding.ZoneKey,
                ActorEntityId = packetBinding.AvatarEntityId,
                InputSequence = sequence,
                ClientTimestampMilliseconds = 1,
                MoveZ = 1,
                RequestedSpeed = 4,
                FacingZ = 1
            };
            Assert.True(RealtimeWireCodec.TryCreateMotionIntentForAuthentication(
                new(
                    packetBinding.AuthenticatedTransportSessionId,
                    packetBinding.TransportGeneration,
                    sequence,
                    0),
                payload,
                out var datagram,
                out var error), error.ToString());
            Assert.True(RealtimeWireCodec.TryGetAuthenticatedRegion(
                datagram,
                out var authenticatedRegion,
                out error), error.ToString());
            var digest = HMACSHA256.HashData(packetKey, authenticatedRegion);
            Assert.True(RealtimeWireCodec.TryWriteAuthenticationTag(
                datagram,
                digest.AsSpan(0, RealtimeWireContract.RequiredAuthenticationTagLength),
                out error), error.ToString());
            CryptographicOperations.ZeroMemory(digest);
            return datagram;
        }

        public async ValueTask DisposeAsync()
        {
            await listener.StopAsync(CancellationToken.None);
            listener.Dispose();
            diagnostics.Dispose();
            CryptographicOperations.ZeroMemory(key);
        }

        internal static UdpTransportTicketBinding CreateBinding() => new(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            "main_zone",
            Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
            1,
            Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
            RealtimeWireContract.ProtocolVersion);

        internal static int ReserveUdpPort()
        {
            using var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            return ((IPEndPoint)socket.Client.LocalEndPoint!).Port;
        }
    }

    private sealed class LoopbackClient : IDisposable
    {
        private readonly EventBasedNetListener listener = new();
        private readonly NetManager manager;

        internal LoopbackClient()
        {
            manager = new(listener)
            {
                AutoRecycle = true,
                ReconnectDelay = 50,
                MaxConnectAttempts = 2,
                DisconnectTimeout = 300
            };
            listener.PeerConnectedEvent += peer => Peer = peer;
            listener.PeerDisconnectedEvent += (_, info) =>
            {
                Disconnected = true;
                Rejected |= info.Reason == DisconnectReason.ConnectionRejected;
            };
            Assert.True(manager.Start());
        }

        internal NetPeer? Peer { get; private set; }
        internal bool Rejected { get; private set; }
        internal bool Disconnected { get; private set; }

        internal void Connect(int port, byte[] bootstrap)
        {
            var writer = new NetDataWriter();
            writer.Put(bootstrap);
            manager.Connect(IPAddress.Loopback.ToString(), port, writer);
        }

        internal async Task<bool> WaitUntil(Func<bool> condition)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
            while (DateTime.UtcNow < deadline)
            {
                manager.PollEvents();
                if (condition()) return true;
                await Task.Delay(5);
            }
            manager.PollEvents();
            return condition();
        }

        internal async Task PumpFor(TimeSpan duration)
        {
            var deadline = DateTime.UtcNow + duration;
            while (DateTime.UtcNow < deadline)
            {
                manager.PollEvents();
                await Task.Delay(5);
            }
        }

        public void Dispose()
        {
            manager.Stop();
        }
    }

    private sealed class CapturingSink : IUdpAuthenticatedMotionIntentSink
    {
        private readonly object gate = new();
        private List<AuthenticatedUdpMotionIntent> Values { get; } = new();
        internal int Count
        {
            get { lock (gate) return Values.Count; }
        }
        public bool TryWrite(AuthenticatedUdpMotionIntent value)
        {
            lock (gate) Values.Add(value);
            return true;
        }
    }

    private sealed class FixtureTicketStore(
        UdpTransportTicketBinding binding,
        byte[] key) : IUdpTransportTicketStore
    {
        public string BackendName => "loopback_test";
        public bool SupportsMultipleAuthorityInstances => true;
        public ValueTask<UdpTransportTicketIssueResult> Issue(
            UdpTransportTicketIssueContext context,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<UdpTransportTicketRedeemResult> Redeem(
            UdpTransportTicketRedemption redemption,
            IUdpTransportRuntimeSessionValidator runtimeValidator,
            CancellationToken cancellationToken) => ValueTask.FromResult(
                new UdpTransportTicketRedeemResult(
                    UdpTransportTicketRedeemStatus.Redeemed,
                    binding,
                    key.ToArray()));
        public ValueTask<bool> MarkPromotedTransportSessionConnected(
            UdpTransportTicketBinding candidate,
            CancellationToken cancellationToken) => ValueTask.FromResult(
                candidate == binding);
        public ValueTask<bool> IsTransportGenerationCurrent(
            UdpTransportTicketBinding candidate,
            CancellationToken cancellationToken) => ValueTask.FromResult(
                candidate.AuthenticatedTransportSessionId ==
                    binding.AuthenticatedTransportSessionId &&
                candidate.TransportGeneration == binding.TransportGeneration);
        public ValueTask<IReadOnlyList<bool>> AreTransportGenerationsCurrent(
            IReadOnlyList<UdpTransportTicketBinding> bindings,
            CancellationToken cancellationToken) => ValueTask.FromResult<
                IReadOnlyList<bool>>(bindings.Select(candidate =>
                    candidate.AuthenticatedTransportSessionId ==
                        binding.AuthenticatedTransportSessionId &&
                    candidate.TransportGeneration ==
                        binding.TransportGeneration).ToArray());
        public ValueTask RevokePromotedTransportSession(
            UdpTransportTicketBinding candidate,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask RevokeConnectedTransportSession(
            UdpTransportTicketBinding candidate,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask RevokeRuntimeSession(
            Guid worldId,
            Guid userId,
            Guid avatarEntityId,
            Guid runtimeSessionId,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    private sealed class RotatingTicketStore : IUdpTransportTicketStore
    {
        private readonly object gate = new();
        private UdpTransportTicketBinding binding;
        private byte[] key;

        internal RotatingTicketStore(
            UdpTransportTicketBinding binding,
            byte[] key)
        {
            this.binding = binding;
            this.key = key.ToArray();
        }

        internal void Rotate(
            UdpTransportTicketBinding nextBinding,
            byte[] nextKey)
        {
            lock (gate)
            {
                CryptographicOperations.ZeroMemory(key);
                binding = nextBinding;
                key = nextKey.ToArray();
            }
        }

        public string BackendName => "rotating_loopback_test";
        public bool SupportsMultipleAuthorityInstances => true;
        public ValueTask<UdpTransportTicketIssueResult> Issue(
            UdpTransportTicketIssueContext context,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public ValueTask<UdpTransportTicketRedeemResult> Redeem(
            UdpTransportTicketRedemption redemption,
            IUdpTransportRuntimeSessionValidator runtimeValidator,
            CancellationToken cancellationToken)
        {
            lock (gate)
            {
                return ValueTask.FromResult(
                    redemption.AuthenticatedTransportSessionId ==
                        binding.AuthenticatedTransportSessionId &&
                    redemption.TransportGeneration ==
                        binding.TransportGeneration
                        ? new UdpTransportTicketRedeemResult(
                            UdpTransportTicketRedeemStatus.Redeemed,
                            binding, key.ToArray())
                        : new UdpTransportTicketRedeemResult(
                            UdpTransportTicketRedeemStatus
                                .BindingNoLongerCurrent));
            }
        }
        public ValueTask<bool> MarkPromotedTransportSessionConnected(
            UdpTransportTicketBinding candidate,
            CancellationToken cancellationToken)
        {
            lock (gate)
                return ValueTask.FromResult(candidate == binding);
        }
        public ValueTask<bool> IsTransportGenerationCurrent(
            UdpTransportTicketBinding candidate,
            CancellationToken cancellationToken)
        {
            lock (gate)
                return ValueTask.FromResult(candidate == binding);
        }
        public ValueTask<IReadOnlyList<bool>> AreTransportGenerationsCurrent(
            IReadOnlyList<UdpTransportTicketBinding> candidates,
            CancellationToken cancellationToken)
        {
            lock (gate)
                return ValueTask.FromResult<IReadOnlyList<bool>>(
                    candidates.Select(candidate => candidate == binding)
                        .ToArray());
        }
        public ValueTask RevokePromotedTransportSession(
            UdpTransportTicketBinding candidate,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask RevokeConnectedTransportSession(
            UdpTransportTicketBinding candidate,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask RevokeRuntimeSession(
            Guid worldId, Guid userId, Guid avatarEntityId,
            Guid runtimeSessionId,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    private sealed class BlockingRedeemTicketStore(
        UdpTransportTicketBinding binding,
        byte[] key) : IUdpTransportTicketStore
    {
        private readonly TaskCompletionSource started = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Started => started.Task;
        public bool CancellationObserved { get; private set; }
        public string BackendName => "blocking_loopback_test";
        public bool SupportsMultipleAuthorityInstances => true;
        public ValueTask<UdpTransportTicketIssueResult> Issue(
            UdpTransportTicketIssueContext context,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public async ValueTask<UdpTransportTicketRedeemResult> Redeem(
            UdpTransportTicketRedemption redemption,
            IUdpTransportRuntimeSessionValidator runtimeValidator,
            CancellationToken cancellationToken)
        {
            started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                CancellationObserved = true;
                throw;
            }
            return new(
                UdpTransportTicketRedeemStatus.Redeemed,
                binding,
                key.ToArray());
        }
        public ValueTask<bool> MarkPromotedTransportSessionConnected(
            UdpTransportTicketBinding candidate,
            CancellationToken cancellationToken) => ValueTask.FromResult(
                candidate == binding);
        public ValueTask<bool> IsTransportGenerationCurrent(
            UdpTransportTicketBinding candidate,
            CancellationToken cancellationToken) => ValueTask.FromResult(true);
        public ValueTask<IReadOnlyList<bool>> AreTransportGenerationsCurrent(
            IReadOnlyList<UdpTransportTicketBinding> bindings,
            CancellationToken cancellationToken) => ValueTask.FromResult<
                IReadOnlyList<bool>>(bindings.Select(_ => true).ToArray());
        public ValueTask RevokePromotedTransportSession(
            UdpTransportTicketBinding candidate,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask RevokeConnectedTransportSession(
            UdpTransportTicketBinding candidate,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask RevokeRuntimeSession(
            Guid worldId,
            Guid userId,
            Guid avatarEntityId,
            Guid runtimeSessionId,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    private sealed class AcceptingValidator : IUdpTransportRuntimeSessionValidator
    {
        public ValueTask<bool> IsCurrent(
            UdpTransportTicketBinding binding,
            CancellationToken cancellationToken) => ValueTask.FromResult(true);
    }
}
