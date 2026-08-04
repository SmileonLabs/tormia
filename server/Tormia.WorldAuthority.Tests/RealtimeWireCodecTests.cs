using System.Buffers.Binary;
using System.Text;
using Tormia.Ontology.Realtime.Protocol;
using Xunit;

namespace Tormia.WorldAuthority.Tests;

public sealed class RealtimeWireCodecTests
{
    private static readonly Guid TransportSessionId =
        Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
    private static readonly Guid WorldId =
        Guid.Parse("10213243-5465-7687-98a9-bacbdcedfe0f");
    private static readonly Guid ActorId =
        Guid.Parse("ffeeddcc-bbaa-9988-7766-554433221100");
    private static readonly Guid FrameOccurrenceId =
        Guid.Parse("0f1e2d3c-4b5a-6978-8796-a5b4c3d2e1f0");
    private static readonly byte[] ValidTag =
        Enumerable.Range(1, RealtimeWireContract.RequiredAuthenticationTagLength)
            .Select(value => (byte)value)
            .ToArray();

    [Fact]
    public void MotionIntentRoundTripsWithExplicitHeaderSemantics()
    {
        var metadata = CreateMetadata();
        var source = CreateMotionIntent("zone_main");

        Assert.True(RealtimeWireCodec.TryEncodeMotionIntent(
            metadata, source, ValidTag, out var datagram, out var encodeError));
        Assert.Equal(RealtimeWireError.None, encodeError);
        Assert.True(RealtimeWireCodec.TryReadStructuralEnvelope(
            datagram, out var envelope, out var envelopeError));
        Assert.Equal(RealtimeWireError.None, envelopeError);
        Assert.Equal(
            RealtimeWireContract.FixedHeaderLength + envelope.Header.PayloadLength,
            envelope.AuthenticatedRegion.Length);
        Assert.True(envelope.AuthenticationTag.SequenceEqual(ValidTag));
        Assert.True(RealtimeWireCodec.TryDecodeMotionIntentStructure(
            datagram, out var header, out var decoded, out var decodeError));

        Assert.Equal(RealtimeWireError.None, decodeError);
        Assert.Equal(RealtimeMessageKind.MotionIntent, header.MessageKind);
        Assert.Equal(TransportSessionId, header.AuthenticatedTransportSessionId);
        Assert.Equal(metadata.TransportGeneration, header.TransportGeneration);
        Assert.Equal(metadata.PacketSequence, header.PacketSequence);
        Assert.Equal(metadata.MessageTick, header.MessageTick);
        Assert.Equal(
            RealtimeWireContract.RequiredAuthenticationTagLength,
            header.AuthenticationTagLength);
        Assert.Equal(source.WorldId, decoded.WorldId);
        Assert.Equal(source.ZoneKey, decoded.ZoneKey);
        Assert.Equal(source.ActorEntityId, decoded.ActorEntityId);
        Assert.Equal(source.InputSequence, decoded.InputSequence);
        Assert.Equal(source.ClientTimestampMilliseconds, decoded.ClientTimestampMilliseconds);
        Assert.Equal(source.MoveX, decoded.MoveX);
        Assert.Equal(source.MoveZ, decoded.MoveZ);
        Assert.Equal(source.RequestedSpeed, decoded.RequestedSpeed);
        Assert.Equal(source.HasDestination, decoded.HasDestination);
        Assert.Equal(source.DestinationX, decoded.DestinationX);
        Assert.Equal(source.DestinationZ, decoded.DestinationZ);
        Assert.Equal(source.DestinationStopDistance, decoded.DestinationStopDistance);
        Assert.Equal(source.FacingX, decoded.FacingX);
        Assert.Equal(source.FacingZ, decoded.FacingZ);
        Assert.Equal(MotionIntentFlags.None, decoded.Flags);
    }

    [Fact]
    public void AuthenticationRegionCanBeReadAndTagWrittenBeforePayloadDecode()
    {
        Assert.True(RealtimeWireCodec.TryCreateMotionIntentForAuthentication(
            CreateMetadata(), CreateMotionIntent("z"), out var datagram, out _));
        Assert.True(RealtimeWireCodec.TryGetAuthenticatedRegion(
            datagram, out var authenticatedRegion, out _));
        Assert.Equal(datagram.Length - ValidTag.Length, authenticatedRegion.Length);
        Assert.False(RealtimeWireCodec.TryReadAuthenticationTag(
            datagram, out _, out var missingTagError));
        Assert.Equal(RealtimeWireError.AuthenticationTagMissing, missingTagError);
        Assert.False(RealtimeWireCodec.TryDecodeMotionIntentStructure(
            datagram, out _, out _, out var unauthenticatedDecodeError));
        Assert.Equal(
            RealtimeWireError.AuthenticationTagMissing,
            unauthenticatedDecodeError);

        Assert.True(RealtimeWireCodec.TryWriteAuthenticationTag(
            datagram, ValidTag, out var writeError));
        Assert.Equal(RealtimeWireError.None, writeError);
        Assert.True(RealtimeWireCodec.TryReadAuthenticationTag(
            datagram, out var tag, out _));
        Assert.True(tag.SequenceEqual(ValidTag));
        Assert.True(RealtimeWireCodec.TryDecodeMotionIntentStructure(
            datagram, out _, out _, out _));
    }

    [Fact]
    public void FixedAuthenticationTagLengthAndNonZeroValueAreRequired()
    {
        foreach (var invalidTag in new[]
                 {
                     Array.Empty<byte>(),
                     new byte[15],
                     new byte[17],
                     new byte[RealtimeWireContract.RequiredAuthenticationTagLength]
                 })
        {
            Assert.False(RealtimeWireCodec.TryEncodeMotionIntent(
                CreateMetadata(),
                CreateMotionIntent("z"),
                invalidTag,
                out _,
                out var error));
            Assert.True(
                error == RealtimeWireError.AuthenticationTagLengthMismatch ||
                error == RealtimeWireError.AuthenticationTagMissing);
        }

        var datagram = EncodeMotionIntent(CreateMotionIntent("z"));
        BinaryPrimitives.WriteUInt16LittleEndian(datagram.AsSpan(52, 2), 0);
        AssertEnvelopeError(
            datagram,
            RealtimeWireError.AuthenticationTagLengthMismatch);
    }

    [Fact]
    public void SnapshotRoundTripsAndEncodingIsDeterministic()
    {
        var source = CreateSnapshot(
            CreateSnapshotItem("이동", 11),
            CreateSnapshotItem("airborne", 12));
        Assert.True(RealtimeWireCodec.TryEncodeAuthorityMotionSnapshot(
            CreateMetadata(), source, ValidTag, out var first, out _));
        Assert.True(RealtimeWireCodec.TryEncodeAuthorityMotionSnapshot(
            CreateMetadata(), source, ValidTag, out var second, out _));
        Assert.Equal(first, second);
        Assert.True(RealtimeWireCodec.TryDecodeAuthorityMotionSnapshotStructure(
            first, out var header, out var decoded, out var error));

        Assert.Equal(RealtimeWireError.None, error);
        Assert.Equal(RealtimeMessageKind.AuthorityMotionSnapshot, header.MessageKind);
        Assert.Equal(source.FrameOccurrenceId, decoded.FrameOccurrenceId);
        Assert.Equal((ushort)0, decoded.PageIndex);
        Assert.Equal((ushort)1, decoded.PageCount);
        Assert.Equal((ushort)2, decoded.TotalItemCount);
        Assert.Equal(2, decoded.Items.Length);
        Assert.Equal("이동", decoded.Items[0].MotionStatus);
        Assert.Equal(source.Items[0].ActorRuntimeSessionId,
            decoded.Items[0].ActorRuntimeSessionId);
        Assert.Equal(source.Items[0].ActorServerTick,
            decoded.Items[0].ActorServerTick);
        Assert.Equal(11UL, decoded.Items[0].LastProcessedInputSequence);
        Assert.Equal(AuthorityMotionStateFlags.Grounded, decoded.Items[0].Flags);
        Assert.Equal("airborne", decoded.Items[1].MotionStatus);
    }

    [Fact]
    public void HeaderValidationFailsClosedForMalformedDatagrams()
    {
        AssertEnvelopeError(Array.Empty<byte>(), RealtimeWireError.EmptyDatagram);
        AssertEnvelopeError(
            new byte[RealtimeWireContract.FixedHeaderLength - 1],
            RealtimeWireError.HeaderTruncated);
        AssertEnvelopeError(
            new byte[RealtimeWireContract.MaximumDatagramLength + 1],
            RealtimeWireError.DatagramTooLarge);

        var valid = EncodeMotionIntent(CreateMotionIntent("z"));
        AssertEnvelopeMutationError(valid, bytes => bytes[0] ^= 0xff,
            RealtimeWireError.InvalidMagic);
        AssertEnvelopeMutationError(valid, bytes =>
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4, 2), 1),
            RealtimeWireError.UnsupportedVersion);
        AssertEnvelopeMutationError(valid, bytes =>
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(6, 2), 999),
            RealtimeWireError.UnsupportedMessageKind);
        AssertEnvelopeMutationError(valid, bytes =>
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(8, 2), 55),
            RealtimeWireError.InvalidHeaderLength);
        AssertEnvelopeMutationError(valid, bytes =>
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(54, 2), 1),
            RealtimeWireError.InvalidHeaderReservedBits);
        AssertEnvelopeMutationError(valid, bytes =>
            BinaryPrimitives.WriteUInt16LittleEndian(
                bytes.AsSpan(10, 2),
                checked((ushort)(BinaryPrimitives.ReadUInt16LittleEndian(
                    bytes.AsSpan(10, 2)) + 1))),
            RealtimeWireError.DatagramLengthMismatch);
    }

    [Fact]
    public void SequenceAndTickSemanticsFailClosed()
    {
        AssertEncodeIntentMetadataError(
            new RealtimePacketMetadata(TransportSessionId, 0, 1, 0),
            RealtimeWireError.InvalidSequence);
        AssertEncodeIntentMetadataError(
            new RealtimePacketMetadata(TransportSessionId, 1, 0, 0),
            RealtimeWireError.InvalidSequence);
        AssertEncodeIntentMetadataError(
            new RealtimePacketMetadata(TransportSessionId, 1, 1, -1),
            RealtimeWireError.InvalidTick);

        Assert.True(RealtimeWireCodec.TryEncodeMotionIntent(
            new RealtimePacketMetadata(TransportSessionId, 1, 1, 0),
            CreateMotionIntent("z"),
            ValidTag,
            out _,
            out _));
        Assert.False(RealtimeWireCodec.TryEncodeAuthorityMotionSnapshot(
            new RealtimePacketMetadata(TransportSessionId, 1, 1, 0),
            CreateSnapshot(CreateSnapshotItem("moving", 0)),
            ValidTag,
            out _,
            out var snapshotTickError));
        Assert.Equal(RealtimeWireError.InvalidTick, snapshotTickError);

        var intent = CreateMotionIntent("z");
        intent.InputSequence = 0;
        AssertIntentEncodeError(intent, RealtimeWireError.InvalidSequence);
    }

    [Fact]
    public void MotionIntentRejectsGameplayFlagsAndInvalidMovementValues()
    {
        var flags = CreateMotionIntent("z");
        flags.Flags = (MotionIntentFlags)1;
        AssertIntentEncodeError(flags, RealtimeWireError.UnknownFlags);

        var negativeSpeed = CreateMotionIntent("z");
        negativeSpeed.RequestedSpeed = -0.1f;
        AssertIntentEncodeError(negativeSpeed, RealtimeWireError.OutOfRangeNumber);

        var negativeStop = CreateMotionIntent("z");
        negativeStop.DestinationStopDistance = -0.1f;
        AssertIntentEncodeError(negativeStop, RealtimeWireError.OutOfRangeNumber);

        var invalidMove = CreateMotionIntent("z");
        invalidMove.MoveX = 1f;
        invalidMove.MoveZ = 1f;
        AssertIntentEncodeError(invalidMove, RealtimeWireError.OutOfRangeNumber);

        var invalidFacing = CreateMotionIntent("z");
        invalidFacing.FacingX = 1.01f;
        AssertIntentEncodeError(invalidFacing, RealtimeWireError.OutOfRangeNumber);

        var nonCanonicalDestination = CreateMotionIntent("z");
        nonCanonicalDestination.HasDestination = false;
        nonCanonicalDestination.DestinationX = 1f;
        AssertIntentEncodeError(nonCanonicalDestination, RealtimeWireError.InvalidPayload);

        var canonicalNoDestination = CreateMotionIntent("z");
        canonicalNoDestination.HasDestination = false;
        canonicalNoDestination.DestinationX = 0f;
        canonicalNoDestination.DestinationZ = 0f;
        canonicalNoDestination.DestinationStopDistance = 0f;
        Assert.True(RealtimeWireCodec.TryEncodeMotionIntent(
            CreateMetadata(), canonicalNoDestination, ValidTag, out _, out _));
    }

    [Fact]
    public void SnapshotRejectsInvalidTimestampQuaternionAndUnknownFlags()
    {
        var timestamp = CreateSnapshot(CreateSnapshotItem("moving", 1));
        timestamp.ObservedAtUnixMilliseconds = 0;
        AssertSnapshotEncodeError(timestamp, RealtimeWireError.InvalidTimestamp);

        var quaternion = CreateSnapshot(CreateSnapshotItem("moving", 1));
        quaternion.Items[0].RotationW = 0f;
        AssertSnapshotEncodeError(quaternion, RealtimeWireError.InvalidQuaternion);

        var flags = CreateSnapshot(CreateSnapshotItem("moving", 1));
        flags.Items[0].Flags = (AuthorityMotionStateFlags)2;
        AssertSnapshotEncodeError(flags, RealtimeWireError.UnknownFlags);
    }

    [Fact]
    public void SnapshotPageMetadataFailsClosed()
    {
        var snapshot = CreateSnapshot(CreateSnapshotItem("moving", 1));

        snapshot.FrameOccurrenceId = Guid.Empty;
        AssertSnapshotEncodeError(
            snapshot,
            RealtimeWireError.InvalidIdentifier);

        snapshot = CreateSnapshot(CreateSnapshotItem("moving", 1));
        snapshot.Reserved = 1;
        AssertSnapshotEncodeError(
            snapshot,
            RealtimeWireError.PageMetadataOutOfRange);

        snapshot = CreateSnapshot(CreateSnapshotItem("moving", 1));
        snapshot.PageIndex = 1;
        AssertSnapshotEncodeError(
            snapshot,
            RealtimeWireError.PageMetadataOutOfRange);

        snapshot = CreateSnapshot(CreateSnapshotItem("moving", 1));
        snapshot.TotalItemCount = 2;
        AssertSnapshotEncodeError(
            snapshot,
            RealtimeWireError.PageMetadataOutOfRange);
    }

    [Fact]
    public void CompleteSnapshotPageSetRequiresEveryConsistentUniquePageAndActor()
    {
        var first = CreateSnapshotPage(
            0,
            2,
            2,
            CreateSnapshotItem("moving", 1));
        var second = CreateSnapshotPage(
            1,
            2,
            2,
            CreateSnapshotItem("moving", 2));

        Assert.True(RealtimeWireCodec.TryValidateCompleteAuthorityMotionSnapshotPages(
            new[] { second, first },
            out var completeError), completeError.ToString());
        Assert.False(RealtimeWireCodec.TryValidateCompleteAuthorityMotionSnapshotPages(
            new[] { first },
            out var missingError));
        Assert.Equal(RealtimeWireError.PageSetIncomplete, missingError);
        Assert.False(RealtimeWireCodec.TryValidateCompleteAuthorityMotionSnapshotPages(
            new[] { first, first },
            out var duplicatePageError));
        Assert.Equal(RealtimeWireError.DuplicatePage, duplicatePageError);

        var mismatched = CreateSnapshotPage(
            1,
            2,
            2,
            CreateSnapshotItem("moving", 2));
        mismatched.ObservedAtUnixMilliseconds++;
        Assert.False(RealtimeWireCodec.TryValidateCompleteAuthorityMotionSnapshotPages(
            new[] { first, mismatched },
            out var mismatchError));
        Assert.Equal(RealtimeWireError.PageMetadataMismatch, mismatchError);

        var duplicateActor = CreateSnapshotPage(
            1,
            2,
            2,
            CreateSnapshotItem("moving", 1));
        Assert.False(RealtimeWireCodec.TryValidateCompleteAuthorityMotionSnapshotPages(
            new[] { first, duplicateActor },
            out var duplicateActorError));
        Assert.Equal(RealtimeWireError.DuplicateActor, duplicateActorError);
    }

    [Fact]
    public void StructuralDecodeRejectsNonFiniteReservedFlagsAndMalformedPayload()
    {
        var source = EncodeMotionIntent(CreateMotionIntent("zone"));
        var zoneLength = Encoding.UTF8.GetByteCount("zone");
        var moveXOffset = 56 + 16 + 2 + zoneLength + 16 + 8 + 8;
        var nonFinite = (byte[])source.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(
            nonFinite.AsSpan(moveXOffset, 4),
            BitConverter.SingleToInt32Bits(float.NaN));
        AssertMotionDecodeError(nonFinite, RealtimeWireError.NonFiniteNumber);

        var flags = (byte[])source.Clone();
        var payloadLength = BinaryPrimitives.ReadUInt16LittleEndian(
            flags.AsSpan(10, 2));
        BinaryPrimitives.WriteUInt32LittleEndian(
            flags.AsSpan(56 + payloadLength - 4, 4), 1);
        AssertMotionDecodeError(flags, RealtimeWireError.UnknownFlags);

        var invalidBoolean = (byte[])source.Clone();
        invalidBoolean[118 + zoneLength] = 2;
        AssertMotionDecodeError(invalidBoolean, RealtimeWireError.InvalidBoolean);

        var truncated = RemoveLastPayloadByte(source);
        AssertMotionDecodeError(truncated, RealtimeWireError.PayloadTruncated);

        var trailing = InsertTrailingPayloadByte(source);
        AssertMotionDecodeError(trailing, RealtimeWireError.PayloadHasTrailingBytes);
    }

    [Fact]
    public void Utf8ValidationEnforcesByteBoundaryAndRejectsMalformedText()
    {
        Assert.True(RealtimeWireCodec.TryEncodeMotionIntent(
            CreateMetadata(),
            CreateMotionIntent(new string('a',
                RealtimeWireContract.MaximumZoneKeyUtf8Bytes)),
            ValidTag,
            out _,
            out _));
        AssertIntentEncodeError(
            CreateMotionIntent(new string('a',
                RealtimeWireContract.MaximumZoneKeyUtf8Bytes + 1)),
            RealtimeWireError.StringTooLong);
        AssertIntentEncodeError(
            CreateMotionIntent(new string('한', 43)),
            RealtimeWireError.StringTooLong);
        AssertIntentEncodeError(
            CreateMotionIntent("\ud800"),
            RealtimeWireError.InvalidUtf8);

        var malformed = EncodeMotionIntent(CreateMotionIntent("a"));
        malformed[56 + 16 + 2] = 0xc3;
        AssertMotionDecodeError(malformed, RealtimeWireError.InvalidUtf8);
    }

    [Fact]
    public void SnapshotParseBoundAndMtuCapacityAreDistinct()
    {
        Assert.False(RealtimeWireCodec.TryGetMaximumSnapshotItemCapacity(
            0, 1, out _));
        Assert.True(RealtimeWireCodec.TryGetMaximumSnapshotItemCapacity(
            1, 1, out var bestCaseCapacity));
        Assert.Equal(11, bestCaseCapacity);
        Assert.True(RealtimeWireCodec.TryGetMaximumSnapshotItemCapacity(
            RealtimeWireContract.MaximumZoneKeyUtf8Bytes,
            RealtimeWireContract.MaximumMotionStatusUtf8Bytes,
            out var worstCaseCapacity));
        Assert.Equal(6, worstCaseCapacity);
        Assert.True(bestCaseCapacity <
            RealtimeWireContract.MaximumSnapshotItemsParseBound);

        var tooMany = Enumerable.Range(
                0,
                RealtimeWireContract.MaximumSnapshotItemsParseBound + 1)
            .Select(index => CreateSnapshotItem("m", (ulong)index))
            .ToArray();
        AssertSnapshotEncodeError(
            CreateSnapshot(tooMany),
            RealtimeWireError.ItemCountOutOfRange);
    }

    [Fact]
    public void DatagramAtMaximumSizeIsAcceptedAndOneByteOverIsRejected()
    {
        var items = Enumerable.Range(0, 8)
            .Select(index => CreateSnapshotItem(
                new string('s', index == 7 ? 43 : 40),
                (ulong)index))
            .ToArray();
        var snapshot = new AuthorityMotionSnapshotPayload
        {
            WorldId = WorldId,
            ZoneKey = "z",
            FrameOccurrenceId = FrameOccurrenceId,
            PageIndex = 0,
            PageCount = 1,
            TotalItemCount = checked((ushort)items.Length),
            ObservedAtUnixMilliseconds = 1,
            Items = items
        };

        Assert.True(RealtimeWireCodec.TryEncodeAuthorityMotionSnapshot(
            CreateMetadata(), snapshot, ValidTag, out var maximum, out _));
        Assert.Equal(RealtimeWireContract.MaximumDatagramLength, maximum.Length);

        items[7].MotionStatus += "s";
        AssertSnapshotEncodeError(snapshot, RealtimeWireError.DatagramTooLarge);
    }

    [Fact]
    public void GuidUsesDocumentedDotNetMixedEndianByteVector()
    {
        var datagram = EncodeMotionIntent(CreateMotionIntent("z"));
        Assert.Equal(
            Convert.FromHexString("33221100554477668899AABBCCDDEEFF"),
            datagram.AsSpan(12, 16).ToArray());
        Assert.True(RealtimeWireCodec.TryReadStructuralEnvelope(
            datagram, out var envelope, out _));
        Assert.Equal(TransportSessionId,
            envelope.Header.AuthenticatedTransportSessionId);
    }

    [Fact]
    public void MotionIntentMatchesPinnedFullByteFixture()
    {
        var expected = Convert.FromHexString(
            "544F5652020001003800580033221100554477668899AABBCCDDEEFF" +
            "08070605040302011817161514131211640000000000000010000000" +
            "433221106554877698A9BACBDCEDFE0F01007ACCDDEEFFAABB889977" +
            "6655443322110063000000000000007B58686691010000000080BE00" +
            "00403F000090400100004841000004C1CDCC4C3E9A99193FCDCC4C" +
            "3F000000000102030405060708090A0B0C0D0E0F10");

        Assert.True(RealtimeWireCodec.TryEncodeMotionIntent(
            CreateMetadata(100),
            CreateMotionIntent("z"),
            ValidTag,
            out var actual,
            out _));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void AuthorityMotionSnapshotMatchesPinnedFullByteFixture()
    {
        var expected = Convert.FromHexString(
            "544F5652020002003800A10033221100554477668899AABBCCDDEEFF" +
            "08070605040302011817161514131211640000000000000010000000" +
            "433221106554877698A9BACBDCEDFE0F09007A6F6E655F6D61696E" +
            "3C2D1E0F5A4B78698796A5B4C3D2E1F00000010001000000C85968" +
            "6691010000010001000000000000000123456789ABCDEF1032547698" +
            "BADCFE3210FEDCBA98765402000000000000000000A03F0000204000" +
            "0070C000000000F304353F00000000F304353F00008040000080BF00" +
            "00004006006D6F76696E670100000000000000010000000102030405" +
            "060708090A0B0C0D0E0F10");
        Assert.True(RealtimeWireCodec.TryEncodeAuthorityMotionSnapshot(
            CreateMetadata(100),
            CreateSnapshot(CreateSnapshotItem("moving", 1)),
            ValidTag,
            out var actual,
            out _));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void DecoderRejectsWrongMessageKind()
    {
        var snapshot = EncodeSnapshot(
            CreateSnapshot(CreateSnapshotItem("moving", 1)));
        AssertMotionDecodeError(snapshot, RealtimeWireError.UnsupportedMessageKind);

        var intent = EncodeMotionIntent(CreateMotionIntent("zone"));
        Assert.False(RealtimeWireCodec.TryDecodeAuthorityMotionSnapshotStructure(
            intent, out _, out _, out var error));
        Assert.Equal(RealtimeWireError.UnsupportedMessageKind, error);
    }

    private static RealtimePacketMetadata CreateMetadata(long messageTick = 100) =>
        new RealtimePacketMetadata(
            TransportSessionId,
            0x0102030405060708,
            0x1112131415161718,
            messageTick);

    private static MotionIntentPayload CreateMotionIntent(string zoneKey) => new()
    {
        WorldId = WorldId,
        ZoneKey = zoneKey,
        ActorEntityId = ActorId,
        InputSequence = 99,
        ClientTimestampMilliseconds = 1_724_000_000_123,
        MoveX = -0.25f,
        MoveZ = 0.75f,
        RequestedSpeed = 4.5f,
        HasDestination = true,
        DestinationX = 12.5f,
        DestinationZ = -8.25f,
        DestinationStopDistance = 0.2f,
        FacingX = 0.6f,
        FacingZ = 0.8f,
        Flags = MotionIntentFlags.None
    };

    private static AuthorityMotionSnapshotPayload CreateSnapshot(
        params AuthorityMotionSnapshotItem[] items) => new()
    {
        WorldId = WorldId,
        ZoneKey = "zone_main",
        FrameOccurrenceId = FrameOccurrenceId,
        PageIndex = 0,
        PageCount = 1,
        TotalItemCount = checked((ushort)items.Length),
        ObservedAtUnixMilliseconds = 1_724_000_000_456,
        Items = items
    };

    private static AuthorityMotionSnapshotPayload CreateSnapshotPage(
        ushort pageIndex,
        ushort pageCount,
        ushort totalItemCount,
        params AuthorityMotionSnapshotItem[] items) => new()
    {
        WorldId = WorldId,
        ZoneKey = "zone_main",
        FrameOccurrenceId = FrameOccurrenceId,
        PageIndex = pageIndex,
        PageCount = pageCount,
        TotalItemCount = totalItemCount,
        ObservedAtUnixMilliseconds = 1_724_000_000_456,
        Items = items
    };

    private static AuthorityMotionSnapshotItem CreateSnapshotItem(
        string status,
        ulong inputSequence) => new()
    {
        ActorEntityId = GuidFromSequence(inputSequence),
        ActorRuntimeSessionId =
            Guid.Parse("76543210-ba98-fedc-3210-fedcba987654"),
        ActorServerTick = checked((long)inputSequence + 1),
        PositionX = 1.25f,
        PositionY = 2.5f,
        PositionZ = -3.75f,
        RotationX = 0f,
        RotationY = 0.70710677f,
        RotationZ = 0f,
        RotationW = 0.70710677f,
        VelocityX = 4f,
        VelocityY = -1f,
        VelocityZ = 2f,
        MotionStatus = status,
        LastProcessedInputSequence = inputSequence,
        Flags = AuthorityMotionStateFlags.Grounded
    };

    private static Guid GuidFromSequence(ulong value)
    {
        var bytes = Guid.Parse("01234567-89ab-cdef-0123-456789abcdef")
            .ToByteArray();
        BitConverter.GetBytes(value).CopyTo(bytes, 0);
        return new Guid(bytes);
    }

    private static byte[] EncodeMotionIntent(MotionIntentPayload payload)
    {
        Assert.True(RealtimeWireCodec.TryEncodeMotionIntent(
            CreateMetadata(), payload, ValidTag, out var datagram, out _));
        return datagram;
    }

    private static byte[] EncodeSnapshot(AuthorityMotionSnapshotPayload payload)
    {
        Assert.True(RealtimeWireCodec.TryEncodeAuthorityMotionSnapshot(
            CreateMetadata(), payload, ValidTag, out var datagram, out _));
        return datagram;
    }

    private static byte[] RemoveLastPayloadByte(byte[] source)
    {
        var payloadLength = BinaryPrimitives.ReadUInt16LittleEndian(
            source.AsSpan(10, 2));
        var oldTagOffset = RealtimeWireContract.FixedHeaderLength + payloadLength;
        var result = new byte[source.Length - 1];
        source.AsSpan(0, oldTagOffset - 1).CopyTo(result);
        source.AsSpan(oldTagOffset, ValidTag.Length)
            .CopyTo(result.AsSpan(oldTagOffset - 1));
        BinaryPrimitives.WriteUInt16LittleEndian(
            result.AsSpan(10, 2), checked((ushort)(payloadLength - 1)));
        return result;
    }

    private static byte[] InsertTrailingPayloadByte(byte[] source)
    {
        var payloadLength = BinaryPrimitives.ReadUInt16LittleEndian(
            source.AsSpan(10, 2));
        var oldTagOffset = RealtimeWireContract.FixedHeaderLength + payloadLength;
        var result = new byte[source.Length + 1];
        source.AsSpan(0, oldTagOffset).CopyTo(result);
        result[oldTagOffset] = 0;
        source.AsSpan(oldTagOffset, ValidTag.Length)
            .CopyTo(result.AsSpan(oldTagOffset + 1));
        BinaryPrimitives.WriteUInt16LittleEndian(
            result.AsSpan(10, 2), checked((ushort)(payloadLength + 1)));
        return result;
    }

    private static void AssertEnvelopeError(
        byte[] datagram,
        RealtimeWireError expected)
    {
        Assert.False(RealtimeWireCodec.TryReadStructuralEnvelope(
            datagram, out _, out var error));
        Assert.Equal(expected, error);
    }

    private static void AssertEnvelopeMutationError(
        byte[] source,
        Action<byte[]> mutate,
        RealtimeWireError expected)
    {
        var malformed = (byte[])source.Clone();
        mutate(malformed);
        AssertEnvelopeError(malformed, expected);
    }

    private static void AssertMotionDecodeError(
        byte[] datagram,
        RealtimeWireError expected)
    {
        Assert.False(RealtimeWireCodec.TryDecodeMotionIntentStructure(
            datagram, out _, out _, out var error));
        Assert.Equal(expected, error);
    }

    private static void AssertIntentEncodeError(
        MotionIntentPayload payload,
        RealtimeWireError expected)
    {
        Assert.False(RealtimeWireCodec.TryEncodeMotionIntent(
            CreateMetadata(), payload, ValidTag, out _, out var error));
        Assert.Equal(expected, error);
    }

    private static void AssertSnapshotEncodeError(
        AuthorityMotionSnapshotPayload payload,
        RealtimeWireError expected)
    {
        Assert.False(RealtimeWireCodec.TryEncodeAuthorityMotionSnapshot(
            CreateMetadata(), payload, ValidTag, out _, out var error));
        Assert.Equal(expected, error);
    }

    private static void AssertEncodeIntentMetadataError(
        RealtimePacketMetadata metadata,
        RealtimeWireError expected)
    {
        Assert.False(RealtimeWireCodec.TryEncodeMotionIntent(
            metadata,
            CreateMotionIntent("z"),
            ValidTag,
            out _,
            out var error));
        Assert.Equal(expected, error);
    }
}
