using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using LiteNetLib;
using LiteNetLib.Utils;
using Microsoft.Extensions.Logging.Abstractions;
using Tormia.Ontology.Realtime.Protocol;
using Xunit;

public sealed class AuthorityUdpMotionSnapshotIntegrationTests
{
    [Fact]
    public async Task Loopback_PublishesAuthenticatedSnapshotOnlyToMatchingZonePeer()
    {
        await using var fixture = new SnapshotFixture();
        var first = fixture.AddPeer("zone_a", 11);
        var second = fixture.AddPeer("zone_b", 12);
        await fixture.Start();
        using var firstClient = new SnapshotClient();
        using var secondClient = new SnapshotClient();
        firstClient.Connect(fixture.Port, first.Bootstrap);
        secondClient.Connect(fixture.Port, second.Bootstrap);
        Assert.True(await PumpUntil(
            () => firstClient.Connected && secondClient.Connected,
            firstClient,
            secondClient));

        Assert.True(fixture.Publisher.TryPublish(Frame(
            first.Binding.WorldId,
            "zone_a",
            77,
            State(first.Binding, 100))));
        Assert.True(await PumpUntil(
            () => firstClient.Datagrams.Count == 1,
            firstClient,
            secondClient));
        await PumpFor(TimeSpan.FromMilliseconds(100), firstClient, secondClient);

        Assert.Empty(secondClient.Datagrams);
        Assert.Equal(1, fixture.Store.GenerationLookupCount);
        var datagram = Assert.Single(firstClient.Datagrams);
        Assert.InRange(
            datagram.Length,
            1,
            RealtimeWireContract.MaximumDatagramLength);
        AssertSnapshot(datagram, first, expectedAuthorityTick: 77);
    }

    [Fact]
    public async Task Loopback_DeterministicallyPagesWithinMtuWithoutDroppingItems()
    {
        await using var fixture = new SnapshotFixture();
        var peer = fixture.AddPeer("zone_a", 21);
        await fixture.Start();
        using var client = new SnapshotClient();
        client.Connect(fixture.Port, peer.Bootstrap);
        Assert.True(await PumpUntil(() => client.Connected, client));

        var states = Enumerable.Range(1, 32)
            .Select(index => State(
                peer.Binding with
                {
                    AvatarEntityId = GuidFromInt(index),
                    RuntimeSessionId = GuidFromInt(1000 + index)
                },
                index,
                new string('m', 64)))
            .Reverse()
            .ToArray();
        var expectedFrame = Frame(
            peer.Binding.WorldId,
            "zone_a",
            88,
            states);
        var expectedPages = AuthorityUdpMotionSnapshotBatcher.Create(
            expectedFrame,
            out var rejectedItems);
        Assert.Equal(0, rejectedItems);
        Assert.Equal(32, expectedPages.Sum(value => value.Items.Length));
        Assert.True(expectedPages.Count > 1);
        Assert.True(RealtimeWireCodec.TryValidateCompleteAuthorityMotionSnapshotPages(
            expectedPages,
            out var pageSetError), pageSetError.ToString());
        Assert.All(expectedPages, page =>
        {
            Assert.Equal(expectedFrame.FrameOccurrenceId, page.FrameOccurrenceId);
            Assert.Equal((ushort)expectedPages.Count, page.PageCount);
            Assert.Equal((ushort)32, page.TotalItemCount);
        });
        Assert.Equal(
            Enumerable.Range(0, expectedPages.Count).Select(value => (ushort)value),
            expectedPages.Select(value => value.PageIndex));
        Assert.All(expectedPages, page =>
        {
            Assert.True(RealtimeWireCodec.TryCreateAuthorityMotionSnapshotForAuthentication(
                new(Guid.NewGuid(), 1, 1, 88),
                page,
                out var encoded,
                out var error), error.ToString());
            Assert.True(encoded.Length <=
                AuthorityUdpMotionSnapshotBatcher.MaximumSnapshotDatagramLength);
        });
        Assert.True(fixture.Publisher.TryPublish(expectedFrame));
        var received = await PumpUntil(
            () => client.Datagrams.Count > 0,
            client);
        Assert.True(
            received,
            $"Expected at least one of {expectedPages.Count} replaceable pages.");

        Assert.All(client.Datagrams, datagram => Assert.InRange(
            datagram.Length,
            1,
            AuthorityUdpMotionSnapshotBatcher.MaximumSnapshotDatagramLength));
        var items = DecodeItems(client.Datagrams);
        Assert.NotEmpty(items);
        Assert.Equal(
            items.Select(value => value.ActorEntityId).OrderBy(value => value),
            items.Select(value => value.ActorEntityId));
        var sequences = client.Datagrams.Select(ReadPacketSequence).ToArray();
        Assert.Equal(sequences.Distinct().Count(), sequences.Length);
        Assert.All(sequences, value => Assert.True(value > 0));
        Assert.Equal(1, fixture.Store.GenerationLookupCount);
    }

    [Fact]
    public async Task Loopback_ZeroItemsRevokedGenerationAndDisconnectedPeerSendNothing()
    {
        await using var fixture = new SnapshotFixture();
        var peer = fixture.AddPeer("zone_a", 31);
        await fixture.Start();
        using var client = new SnapshotClient();
        client.Connect(fixture.Port, peer.Bootstrap);
        Assert.True(await PumpUntil(() => client.Connected, client));

        Assert.True(fixture.Publisher.TryPublish(new WorldZoneMotionFrame(
            Guid.NewGuid(),
            peer.Binding.WorldId,
            "zone_a",
            1,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Array.Empty<WorldPlayerMotionState>())));
        await PumpFor(TimeSpan.FromMilliseconds(100), client);
        Assert.Empty(client.Datagrams);

        fixture.Store.SetCurrent(peer.Binding, false);
        Assert.True(fixture.Publisher.TryPublish(Frame(
            peer.Binding.WorldId,
            "zone_a",
            2,
            State(peer.Binding, 1))));
        await PumpFor(TimeSpan.FromMilliseconds(150), client);
        Assert.Empty(client.Datagrams);

        fixture.Store.SetCurrent(peer.Binding, true);
        client.Disconnect();
        Assert.True(await PumpUntil(() => client.Disconnected, client));
        Assert.True(fixture.Publisher.TryPublish(Frame(
            peer.Binding.WorldId,
            "zone_a",
            3,
            State(peer.Binding, 2))));
        await Task.Delay(150);
        Assert.Empty(client.Datagrams);
    }

    [Fact]
    public void Batcher_RejectsInvalidItemAndNeverCreatesOversizeDatagram()
    {
        var binding = SnapshotFixture.CreateBinding("zone_a", 41);
        var valid = State(binding, 1);
        var invalid = valid with { RuntimeSessionId = Guid.Empty };
        var pages = AuthorityUdpMotionSnapshotBatcher.Create(
            Frame(binding.WorldId, "zone_a", 1, valid, invalid),
            out var rejected);

        Assert.Equal(1, rejected);
        var page = Assert.Single(pages);
        Assert.True(RealtimeWireCodec.TryCreateAuthorityMotionSnapshotForAuthentication(
            new(Guid.NewGuid(), 1, 1, 1),
            page,
            out var datagram,
            out var error), error.ToString());
        Assert.True(datagram.Length <= RealtimeWireContract.MaximumDatagramLength);
    }

    [Fact]
    public void PeerSession_AssignsMonotonicServerOwnedOutgoingSequence()
    {
        var binding = SnapshotFixture.CreateBinding("zone_a", 42);
        var key = RandomNumberGenerator.GetBytes(32);
        using var session = new AuthorityUdpPeerSession(binding, key.ToArray());
        try
        {
            var page = Assert.Single(AuthorityUdpMotionSnapshotBatcher.Create(
                Frame(binding.WorldId, "zone_a", 9, State(binding, 1)),
                out var rejected));
            Assert.Equal(0, rejected);
            Assert.True(session.TryCreateMotionSnapshotDatagram(
                9, page, out var first, out var firstError),
                firstError.ToString());
            Assert.True(session.TryCreateMotionSnapshotDatagram(
                10, page, out var second, out var secondError),
                secondError.ToString());
            Assert.Equal((ulong)1, ReadPacketSequence(first));
            Assert.Equal((ulong)2, ReadPacketSequence(second));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    [Fact]
    public void OutboundChannel_IsBoundedAndDropsNewestWithoutEvictingQueuedSnapshot()
    {
        var binding = SnapshotFixture.CreateBinding("zone_a", 43);
        var target = new AuthorityUdpPeerTarget(1, binding);
        var channel = new AuthorityUdpOutboundSnapshotChannel(1, 1000);
        var retainedBatch = new PendingAuthorityUdpSnapshotBatch(new[]
        {
            new PendingAuthorityUdpSnapshotTarget(target)
        }, new[]
        {
            new AuthorityMotionSnapshotPayload
            {
                WorldId = binding.WorldId,
                ZoneKey = binding.ZoneKey,
                FrameOccurrenceId = Guid.NewGuid(),
                PageIndex = 0,
                PageCount = 1,
                TotalItemCount = 1,
                ObservedAtUnixMilliseconds = 1,
                Items = new[] { new AuthorityMotionSnapshotItem() }
            }
        }, 1, 100);
        Assert.True(channel.TryWrite(retainedBatch));
        Assert.False(channel.TryWrite(retainedBatch));
        Assert.True(channel.Reader.TryRead(out var retained));
        Assert.Equal(target, Assert.Single(retained.Targets).Target);
        channel.Release(retained);
        Assert.True(channel.TryWrite(retainedBatch));
    }

    [Fact]
    public async Task IngressBuffer_PreservesDistinctOccurrencesInArrivalOrder()
    {
        var binding = SnapshotFixture.CreateBinding("zone_a", 46);
        var newOccurrence = Frame(
            binding.WorldId,
            binding.ZoneKey,
            20,
            State(binding, 20));
        var delayedOlderOccurrence = Frame(
            binding.WorldId,
            binding.ZoneKey,
            10,
            State(binding, 10));
        var buffer = new LatestWorldZoneMotionFrameBuffer(2, 2000, 8);

        Assert.True(buffer.TryWrite(newOccurrence, 900));
        Assert.True(buffer.TryWrite(delayedOlderOccurrence, 900));
        Assert.False(buffer.TryWrite(newOccurrence, 900));
        Assert.Equal(1800, buffer.RetainedBytes);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        await using var reader = buffer.ReadAll(timeout.Token).GetAsyncEnumerator();
        Assert.True(await reader.MoveNextAsync());
        Assert.Equal(20, reader.Current.ServerTick);
        Assert.True(await reader.MoveNextAsync());
        Assert.Equal(10, reader.Current.ServerTick);
        Assert.Equal(0, buffer.RetainedBytes);
        Assert.False(buffer.TryWrite(newOccurrence, 900));
    }

    [Fact]
    public async Task IngressBuffer_PreservesDifferentActorDeltasFromSameZone()
    {
        var firstBinding = SnapshotFixture.CreateBinding("zone_a", 60);
        var secondBinding = SnapshotFixture.CreateBinding("zone_a", 61);
        var first = Frame(
            firstBinding.WorldId,
            firstBinding.ZoneKey,
            1,
            State(firstBinding, 1));
        var second = Frame(
            secondBinding.WorldId,
            secondBinding.ZoneKey,
            2,
            State(secondBinding, 2));
        var buffer = new LatestWorldZoneMotionFrameBuffer(2, 4096, 8);

        Assert.True(buffer.TryWrite(first, 100));
        Assert.True(buffer.TryWrite(second, 100));

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        await using var reader = buffer.ReadAll(timeout.Token).GetAsyncEnumerator();
        Assert.True(await reader.MoveNextAsync());
        Assert.Equal(firstBinding.AvatarEntityId,
            Assert.Single(reader.Current.Items).AvatarEntityId);
        Assert.True(await reader.MoveNextAsync());
        Assert.Equal(secondBinding.AvatarEntityId,
            Assert.Single(reader.Current.Items).AvatarEntityId);
    }

    [Fact]
    public async Task IngressBuffer_WhenFullDropsNewestWithoutEvictingAcceptedFrames()
    {
        var binding = SnapshotFixture.CreateBinding("zone_a", 62);
        var first = Frame(binding.WorldId, binding.ZoneKey, 1, State(binding, 1));
        var second = Frame(binding.WorldId, binding.ZoneKey, 2, State(binding, 2));
        var dropped = Frame(binding.WorldId, binding.ZoneKey, 3, State(binding, 3));
        var buffer = new LatestWorldZoneMotionFrameBuffer(2, 200, 8);

        Assert.True(buffer.TryWrite(first, 100));
        Assert.True(buffer.TryWrite(second, 100));
        Assert.False(buffer.TryWrite(dropped, 100));
        Assert.Equal(200, buffer.RetainedBytes);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        await using var reader = buffer.ReadAll(timeout.Token).GetAsyncEnumerator();
        Assert.True(await reader.MoveNextAsync());
        Assert.Equal(1, reader.Current.ServerTick);
        Assert.True(await reader.MoveNextAsync());
        Assert.Equal(2, reader.Current.ServerTick);
        Assert.Equal(0, buffer.RetainedBytes);
    }

    [Fact]
    public async Task IngressBuffer_RecentOccurrenceCardinalityIsBounded()
    {
        var buffer = new LatestWorldZoneMotionFrameBuffer(1, 4096, 8);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await using var reader = buffer.ReadAll(timeout.Token).GetAsyncEnumerator();
        for (var index = 1; index <= 8; index++)
        {
            var binding = SnapshotFixture.CreateBinding(
                "zone_" + index,
                100 + index);
            Assert.True(buffer.TryWrite(Frame(
                binding.WorldId,
                binding.ZoneKey,
                index,
                State(binding, index)), 100));
            Assert.True(await reader.MoveNextAsync());
        }

        Assert.InRange(buffer.RecentOccurrenceCount, 1, 4);
    }

    [Fact]
    public async Task IngressBuffer_RejectsMalformedFrameAndProcessesNextValidFrame()
    {
        var binding = SnapshotFixture.CreateBinding("zone_a", 47);
        var buffer = new LatestWorldZoneMotionFrameBuffer(2, 4096, 8);
        var malformed = Frame(
            binding.WorldId,
            binding.ZoneKey,
            1,
            State(binding, 1)) with { Items = null! };
        var valid = Frame(
            binding.WorldId,
            binding.ZoneKey,
            2,
            State(binding, 2));

        Assert.False(buffer.TryWrite(malformed, 100));
        Assert.True(buffer.TryWrite(valid, 100));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        await using var reader = buffer.ReadAll(timeout.Token).GetAsyncEnumerator();
        Assert.True(await reader.MoveNextAsync());
        Assert.Equal(2, reader.Current.ServerTick);
    }

    [Fact]
    public async Task BackplanePublishFailure_DropsOnlyUdpFrameAndPublisherContinues()
    {
        var options = AuthorityUdpListenerOptions.Disabled with
        {
            Enabled = true,
            OutboundSnapshotCapacity = 8,
            OutboundSnapshotByteCapacity = 4096,
            MaximumSnapshotFrameItems = 8
        };
        using var diagnostics = new AuthorityUdpListenerDiagnostics();
        using var sessions = new AuthorityUdpPeerSessionRegistry();
        var outbound = new AuthorityUdpOutboundSnapshotChannel(8, 4096);
        var backplane = new CapturingBackplane(failPublishOnce: true);
        var publisher = new AuthorityUdpMotionSnapshotPublisher(
            options,
            sessions,
            outbound,
            diagnostics,
            backplane);
        var binding = SnapshotFixture.CreateBinding("zone_a", 48);

        await publisher.StartAsync(CancellationToken.None);
        try
        {
            Assert.True(publisher.TryPublish(Frame(
                binding.WorldId,
                binding.ZoneKey,
                1,
                State(binding, 1))));
            Assert.True(await WaitUntil(() => backplane.PublishAttempts >= 1));
            Assert.True(publisher.TryPublish(Frame(
                binding.WorldId,
                "zone_b",
                2,
                State(binding with { ZoneKey = "zone_b" }, 2))));
            Assert.True(await WaitUntil(() => backplane.Published.Count == 1));
            Assert.Equal((long)2, Assert.Single(backplane.Published).ServerTick);
        }
        finally
        {
            await publisher.StopAsync(CancellationToken.None);
            publisher.Dispose();
        }
    }

    [Fact]
    public async Task BackplaneStartFailure_RetriesWithoutStoppingPublisher()
    {
        var options = AuthorityUdpListenerOptions.Disabled with
        {
            Enabled = true,
            OutboundSnapshotCapacity = 8,
            OutboundSnapshotByteCapacity = 4096,
            MaximumSnapshotFrameItems = 8
        };
        using var diagnostics = new AuthorityUdpListenerDiagnostics();
        using var sessions = new AuthorityUdpPeerSessionRegistry();
        var outbound = new AuthorityUdpOutboundSnapshotChannel(8, 4096);
        var backplane = new CapturingBackplane(failStartOnce: true);
        var publisher = new AuthorityUdpMotionSnapshotPublisher(
            options,
            sessions,
            outbound,
            diagnostics,
            backplane);
        var binding = SnapshotFixture.CreateBinding("zone_a", 49);

        await publisher.StartAsync(CancellationToken.None);
        try
        {
            Assert.True(publisher.TryPublish(Frame(
                binding.WorldId,
                binding.ZoneKey,
                1,
                State(binding, 1))));
            Assert.True(await WaitUntil(
                () => backplane.StartAttempts >= 2,
                TimeSpan.FromSeconds(3)));
            Assert.True(await WaitUntil(
                () => backplane.Published.Count == 1));
        }
        finally
        {
            await publisher.StopAsync(CancellationToken.None);
            publisher.Dispose();
        }
    }

    [Fact]
    public void PeerRegistry_FiltersWorldAndZoneAndDisposalZerosSessionSecret()
    {
        var first = SnapshotFixture.CreateBinding("zone_a", 44);
        var second = SnapshotFixture.CreateBinding("zone_b", 45);
        var firstKey = RandomNumberGenerator.GetBytes(32);
        var secondKey = RandomNumberGenerator.GetBytes(32);
        using var registry = new AuthorityUdpPeerSessionRegistry();
        try
        {
            Assert.True(registry.TryAdd(1, first, firstKey));
            Assert.True(registry.TryAdd(2, second, secondKey));
            var match = Assert.Single(registry.GetMatching(first.WorldId, "zone_a"));
            Assert.Equal(1, match.PeerId);
            Assert.True(registry.TryGetExact(match, out var session));
            registry.Remove(1);
            Assert.Empty(session.AuthenticationKey.ToArray());
            Assert.False(registry.TryGetExact(match, out _));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(firstKey);
            CryptographicOperations.ZeroMemory(secondKey);
        }
    }

    private static WorldZoneMotionFrame Frame(
        Guid worldId,
        string zone,
        long tick,
        params WorldPlayerMotionState[] states) => new(
        Guid.NewGuid(),
        worldId,
        zone,
        tick,
        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        states);

    private static WorldPlayerMotionState State(
        UdpTransportTicketBinding binding,
        long sequence,
        string status = "moving") => new(
        binding.WorldId,
        binding.AvatarEntityId,
        binding.ZoneKey,
        sequence,
        2,
        3,
        sequence,
        status,
        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        1,
        0,
        0,
        0,
        true,
        sequence,
        RuntimeSessionId: binding.RuntimeSessionId);

    private static Guid GuidFromInt(int value)
    {
        var bytes = new byte[16];
        BitConverter.GetBytes(value).CopyTo(bytes, 0);
        return new Guid(bytes);
    }

    private static List<AuthorityMotionSnapshotItem> DecodeItems(
        IReadOnlyList<byte[]> datagrams)
    {
        var result = new List<AuthorityMotionSnapshotItem>();
        foreach (var datagram in datagrams)
        {
            if (RealtimeWireCodec.TryDecodeAuthorityMotionSnapshotStructure(
                    datagram,
                    out _,
                    out var payload,
                    out _))
            {
                result.AddRange(payload.Items);
            }
        }
        return result;
    }

    private static ulong ReadPacketSequence(byte[] datagram)
    {
        Assert.True(RealtimeWireCodec.TryReadStructuralEnvelope(
            datagram, out var envelope, out var error), error.ToString());
        return envelope.Header.PacketSequence;
    }

    private static void AssertSnapshot(
        byte[] datagram,
        SnapshotPeer peer,
        long expectedAuthorityTick)
    {
        Assert.True(RealtimeWireCodec.TryReadStructuralEnvelope(
            datagram,
            out var envelope,
            out var error), error.ToString());
        Assert.True(AuthorityUdpDatagramAuthentication.Verify(
            peer.Key,
            envelope.AuthenticatedRegion,
            envelope.AuthenticationTag));
        Assert.Equal(expectedAuthorityTick, envelope.Header.MessageTick);
        Assert.Equal((ulong)1, envelope.Header.PacketSequence);
        Assert.True(RealtimeWireCodec.TryDecodeAuthorityMotionSnapshotStructure(
            datagram,
            out var header,
            out var payload,
            out error), error.ToString());
        Assert.Equal(peer.Binding.AuthenticatedTransportSessionId,
            header.AuthenticatedTransportSessionId);
        Assert.Equal("zone_a", payload.ZoneKey);
        Assert.Equal(peer.Binding.RuntimeSessionId,
            Assert.Single(payload.Items).ActorRuntimeSessionId);
        Assert.NotEqual(Guid.Empty, payload.FrameOccurrenceId);
        Assert.Equal((ushort)0, payload.PageIndex);
        Assert.Equal((ushort)1, payload.PageCount);
        Assert.Equal((ushort)1, payload.TotalItemCount);
        Assert.Equal((long)100, Assert.Single(payload.Items).ActorServerTick);
    }

    private static async Task<bool> PumpUntil(
        Func<bool> condition,
        params SnapshotClient[] clients)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(4);
        while (DateTime.UtcNow < deadline)
        {
            foreach (var client in clients) client.Poll();
            if (condition()) return true;
            await Task.Delay(5);
        }
        foreach (var client in clients) client.Poll();
        return condition();
    }

    private static async Task PumpFor(
        TimeSpan duration,
        params SnapshotClient[] clients)
    {
        var deadline = DateTime.UtcNow + duration;
        while (DateTime.UtcNow < deadline)
        {
            foreach (var client in clients) client.Poll();
            await Task.Delay(5);
        }
    }

    private static async Task<bool> WaitUntil(
        Func<bool> condition,
        TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow +
                       (timeout ?? TimeSpan.FromSeconds(2));
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return true;
            await Task.Delay(10);
        }
        return condition();
    }

    private sealed class CapturingBackplane : IAuthorityUdpMotionFrameBackplane
    {
        private readonly System.Threading.Channels.Channel<WorldZoneMotionFrame>
            frames = System.Threading.Channels.Channel.CreateUnbounded<
                WorldZoneMotionFrame>();
        private readonly bool failStartOnce;
        private readonly bool failPublishOnce;
        private int startAttempts;
        private int publishAttempts;
        internal CapturingBackplane(
            bool failStartOnce = false,
            bool failPublishOnce = false)
        {
            this.failStartOnce = failStartOnce;
            this.failPublishOnce = failPublishOnce;
        }
        internal int StartAttempts => Volatile.Read(ref startAttempts);
        internal int PublishAttempts => Volatile.Read(ref publishAttempts);
        internal ConcurrentQueue<WorldZoneMotionFrame> Published { get; } = new();
        public Task Start(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref startAttempts) == 1 && failStartOnce)
                throw new InvalidOperationException("simulated subscribe failure");
            return Task.CompletedTask;
        }
        public ValueTask<bool> Publish(
            WorldZoneMotionFrame frame,
            CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref publishAttempts) == 1 &&
                failPublishOnce)
                throw new InvalidOperationException("simulated publish outage");
            Published.Enqueue(frame);
            return ValueTask.FromResult(frames.Writer.TryWrite(frame));
        }
        public IAsyncEnumerable<WorldZoneMotionFrame> ReadAll(
            CancellationToken cancellationToken) =>
            frames.Reader.ReadAllAsync(cancellationToken);
        public Task Stop(CancellationToken cancellationToken)
        {
            frames.Writer.TryComplete();
            return Task.CompletedTask;
        }
    }

    private sealed class SnapshotFixture : IAsyncDisposable
    {
        private readonly AuthorityUdpListenerDiagnostics diagnostics = new();
        private readonly AuthorityUdpPeerSessionRegistry sessions = new();
        private readonly AuthorityUdpRealtimeListener listener;
        private readonly List<byte[]> keys = new();

        internal SnapshotFixture()
        {
            Port = ReserveUdpPort();
            Options = AuthorityUdpListenerOptions.Disabled with
            {
                Enabled = true,
                Port = Port,
                HandshakeTimeout = TimeSpan.FromSeconds(1),
                GenerationFenceTimeout = TimeSpan.FromSeconds(1),
                OutboundSnapshotCapacity = 64
            };
            Store = new SnapshotTicketStore();
            var fence = new AuthorityUdpTransportGenerationFence(Store, Options);
            var outbound = new AuthorityUdpOutboundSnapshotChannel(
                Options.OutboundSnapshotCapacity,
                Options.OutboundSnapshotByteCapacity);
            var sink = new UdpAuthenticatedMotionIntentChannel(8);
            var processor = new AuthorityUdpDatagramProcessor(
                sessions,
                fence,
                sink,
                diagnostics);
            listener = new(
                Options,
                Store,
                new AcceptingValidator(),
                sessions,
                fence,
                processor,
                outbound,
                diagnostics,
                NullLogger<AuthorityUdpRealtimeListener>.Instance);
            Publisher = new(
                Options,
                sessions,
                outbound,
                diagnostics);
        }

        internal int Port { get; }
        internal AuthorityUdpListenerOptions Options { get; }
        internal SnapshotTicketStore Store { get; }
        internal AuthorityUdpMotionSnapshotPublisher Publisher { get; }

        internal SnapshotPeer AddPeer(string zone, int seed)
        {
            var binding = CreateBinding(zone, seed);
            var key = Enumerable.Range(seed, 32)
                .Select(value => (byte)value)
                .ToArray();
            keys.Add(key);
            Store.Add(binding, key);
            var bootstrap = AuthorityUdpBootstrapCodec.Encode(new(
                binding.AuthenticatedTransportSessionId,
                binding.TransportGeneration,
                Enumerable.Repeat((byte)seed, 32).ToArray(),
                Enumerable.Repeat((byte)(seed + 1), 16).ToArray(),
                Enumerable.Repeat((byte)(seed + 2), 32).ToArray()));
            return new(binding, key, bootstrap);
        }

        internal async Task Start()
        {
            await listener.StartAsync(CancellationToken.None);
            await Publisher.StartAsync(CancellationToken.None);
        }

        public async ValueTask DisposeAsync()
        {
            await Publisher.StopAsync(CancellationToken.None);
            await listener.StopAsync(CancellationToken.None);
            Publisher.Dispose();
            listener.Dispose();
            sessions.Dispose();
            diagnostics.Dispose();
            foreach (var key in keys) CryptographicOperations.ZeroMemory(key);
        }

        internal static UdpTransportTicketBinding CreateBinding(
            string zone,
            int seed) => new(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            GuidFromInt(100 + seed),
            GuidFromInt(200 + seed),
            zone,
            GuidFromInt(300 + seed),
            1,
            GuidFromInt(400 + seed),
            RealtimeWireContract.ProtocolVersion);

        private static int ReserveUdpPort()
        {
            using var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            return ((IPEndPoint)socket.Client.LocalEndPoint!).Port;
        }
    }

    private sealed record SnapshotPeer(
        UdpTransportTicketBinding Binding,
        byte[] Key,
        byte[] Bootstrap);

    private sealed class SnapshotTicketStore : IUdpTransportTicketStore
    {
        private readonly ConcurrentDictionary<Guid, Entry> entries = new();
        private int generationLookupCount;
        public string BackendName => "snapshot_loopback";
        public bool SupportsMultipleAuthorityInstances => true;
        internal int GenerationLookupCount =>
            Volatile.Read(ref generationLookupCount);

        internal void Add(UdpTransportTicketBinding binding, byte[] key) =>
            entries[binding.AuthenticatedTransportSessionId] =
                new(binding, key, true);

        internal void SetCurrent(UdpTransportTicketBinding binding, bool current)
        {
            if (entries.TryGetValue(binding.AuthenticatedTransportSessionId,
                    out var entry))
            {
                entries[binding.AuthenticatedTransportSessionId] =
                    entry with { Current = current };
            }
        }

        public ValueTask<UdpTransportTicketIssueResult> Issue(
            UdpTransportTicketIssueContext context,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<UdpTransportTicketRedeemResult> Redeem(
            UdpTransportTicketRedemption redemption,
            IUdpTransportRuntimeSessionValidator runtimeValidator,
            CancellationToken cancellationToken)
        {
            return entries.TryGetValue(
                       redemption.AuthenticatedTransportSessionId,
                       out var entry) &&
                   entry.Current &&
                   entry.Binding.TransportGeneration ==
                       redemption.TransportGeneration
                ? ValueTask.FromResult(new UdpTransportTicketRedeemResult(
                    UdpTransportTicketRedeemStatus.Redeemed,
                    entry.Binding,
                    entry.Key.ToArray()))
                : ValueTask.FromResult(new UdpTransportTicketRedeemResult(
                    UdpTransportTicketRedeemStatus.BindingNoLongerCurrent));
        }

        public ValueTask<bool> MarkPromotedTransportSessionConnected(
            UdpTransportTicketBinding candidate,
            CancellationToken cancellationToken) => ValueTask.FromResult(
                entries.TryGetValue(
                    candidate.AuthenticatedTransportSessionId,
                    out var entry) && entry.Current && entry.Binding == candidate);

        public ValueTask<bool> IsTransportGenerationCurrent(
            UdpTransportTicketBinding candidate,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref generationLookupCount);
            return ValueTask.FromResult(
                entries.TryGetValue(candidate.AuthenticatedTransportSessionId,
                    out var entry) &&
                entry.Current &&
                entry.Binding == candidate);
        }

        public ValueTask<IReadOnlyList<bool>> AreTransportGenerationsCurrent(
            IReadOnlyList<UdpTransportTicketBinding> bindings,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref generationLookupCount);
            return ValueTask.FromResult<IReadOnlyList<bool>>(
                bindings.Select(candidate =>
                    entries.TryGetValue(
                        candidate.AuthenticatedTransportSessionId,
                        out var entry) &&
                    entry.Current &&
                    entry.Binding == candidate).ToArray());
        }

        public ValueTask RevokePromotedTransportSession(
            UdpTransportTicketBinding binding,
            CancellationToken cancellationToken)
        {
            SetCurrent(binding, false);
            return ValueTask.CompletedTask;
        }
        public ValueTask RevokeConnectedTransportSession(
            UdpTransportTicketBinding binding,
            CancellationToken cancellationToken)
        {
            SetCurrent(binding, false);
            return ValueTask.CompletedTask;
        }

        public ValueTask RevokeRuntimeSession(
            Guid worldId,
            Guid userId,
            Guid avatarEntityId,
            Guid runtimeSessionId,
            CancellationToken cancellationToken)
        {
            foreach (var entry in entries.Values.Where(value =>
                         value.Binding.WorldId == worldId &&
                         value.Binding.UserId == userId &&
                         value.Binding.AvatarEntityId == avatarEntityId &&
                         value.Binding.RuntimeSessionId == runtimeSessionId))
            {
                SetCurrent(entry.Binding, false);
            }
            return ValueTask.CompletedTask;
        }

        private sealed record Entry(
            UdpTransportTicketBinding Binding,
            byte[] Key,
            bool Current);
    }

    private sealed class SnapshotClient : IDisposable
    {
        private readonly EventBasedNetListener listener = new();
        private readonly NetManager manager;

        internal SnapshotClient()
        {
            manager = new(listener) { AutoRecycle = true };
            listener.PeerConnectedEvent += peer =>
            {
                Peer = peer;
                Connected = true;
            };
            listener.PeerDisconnectedEvent += (_, _) =>
            {
                Disconnected = true;
                Connected = false;
            };
            listener.NetworkReceiveEvent += (
                _, reader, channel, delivery) =>
            {
                try
                {
                    if (channel == 0 &&
                        delivery == DeliveryMethod.Unreliable)
                    {
                        Datagrams.Add(reader.GetRemainingBytes());
                    }
                }
                finally { reader.Recycle(); }
            };
            Assert.True(manager.Start());
        }

        internal NetPeer? Peer { get; private set; }
        internal bool Connected { get; private set; }
        internal bool Disconnected { get; private set; }
        internal List<byte[]> Datagrams { get; } = new();

        internal void Connect(int port, byte[] bootstrap)
        {
            var writer = new NetDataWriter();
            writer.Put(bootstrap);
            manager.Connect(IPAddress.Loopback.ToString(), port, writer);
        }

        internal void Poll() => manager.PollEvents();
        internal void Disconnect() => Peer?.Disconnect();
        public void Dispose() => manager.Stop();
    }

    private sealed class AcceptingValidator : IUdpTransportRuntimeSessionValidator
    {
        public ValueTask<bool> IsCurrent(
            UdpTransportTicketBinding binding,
            CancellationToken cancellationToken) => ValueTask.FromResult(true);
    }
}
