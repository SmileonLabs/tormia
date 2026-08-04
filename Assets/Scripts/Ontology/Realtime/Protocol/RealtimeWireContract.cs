using System;

namespace Tormia.Ontology.Realtime.Protocol
{
    /// <summary>
    /// Versioned, transport-library-independent binary contract for ephemeral
    /// realtime messages. Every numeric scalar is explicitly little-endian.
    /// Guids use the stable mixed-endian field layout defined by
    /// Guid.TryWriteBytes and Guid(ReadOnlySpan&lt;byte&gt;).
    /// </summary>
    public static class RealtimeWireContract
    {
        public const uint Magic = 0x52564f54; // ASCII "TOVR" in LE bytes.
        // Protocol v2 adds authenticated snapshot occurrence/page metadata and
        // per-actor Authority ticks. A v1 peer must fail closed instead of
        // silently interpreting the incompatible payload layout.
        public const ushort ProtocolVersion = 2;
        public const ushort FixedHeaderLength = 56;
        public const int MaximumDatagramLength = 1200;
        public const int RequiredAuthenticationTagLength = 16;
        public const int MaximumZoneKeyUtf8Bytes = 128;
        public const int MaximumMotionStatusUtf8Bytes = 64;

        /// <summary>
        /// Defensive parser allocation bound, not a promised batch capacity.
        /// The MTU and encoded string sizes determine actual capacity.
        /// </summary>
        public const int MaximumSnapshotItemsParseBound = 32;
        public const int MaximumSnapshotTotalItems = 512;
        public const int MaximumSnapshotPageCount = 512;
    }

    public enum RealtimeMessageKind : ushort
    {
        Unknown = 0,
        MotionIntent = 1,
        AuthorityMotionSnapshot = 2
    }

    /// <summary>
    /// Protocol v1 reserves this field for motion-only extensions. Gameplay
    /// actions such as jump or attack must use their own Authority contract.
    /// Every non-zero bit is rejected in protocol v1.
    /// </summary>
    [Flags]
    public enum MotionIntentFlags : uint
    {
        None = 0
    }

    [Flags]
    public enum AuthorityMotionStateFlags : uint
    {
        None = 0,
        Grounded = 1 << 0
    }

    public enum RealtimeWireError
    {
        None = 0,
        EmptyDatagram,
        DatagramTooLarge,
        HeaderTruncated,
        InvalidMagic,
        UnsupportedVersion,
        UnsupportedMessageKind,
        InvalidHeaderLength,
        InvalidHeaderReservedBits,
        AuthenticationTagLengthMismatch,
        AuthenticationTagMissing,
        DatagramLengthMismatch,
        InvalidPayload,
        InvalidIdentifier,
        InvalidTextValue,
        StringTooLong,
        InvalidUtf8,
        NonFiniteNumber,
        InvalidBoolean,
        InvalidSequence,
        InvalidTick,
        InvalidTimestamp,
        UnknownFlags,
        OutOfRangeNumber,
        InvalidQuaternion,
        ItemCountOutOfRange,
        PageMetadataOutOfRange,
        PageMetadataMismatch,
        PageSetIncomplete,
        DuplicatePage,
        DuplicateActor,
        PayloadTruncated,
        PayloadHasTrailingBytes
    }

    /// <summary>
    /// Header metadata shared by all message kinds. MessageTick is the last
    /// acknowledged Authority tick for MotionIntent and the producing
    /// Authority tick for AuthorityMotionSnapshot.
    /// </summary>
    public readonly struct RealtimePacketMetadata
    {
        public RealtimePacketMetadata(
            Guid authenticatedTransportSessionId,
            ulong transportGeneration,
            ulong packetSequence,
            long messageTick)
        {
            AuthenticatedTransportSessionId =
                authenticatedTransportSessionId;
            TransportGeneration = transportGeneration;
            PacketSequence = packetSequence;
            MessageTick = messageTick;
        }

        public Guid AuthenticatedTransportSessionId { get; }
        public ulong TransportGeneration { get; }
        public ulong PacketSequence { get; }
        public long MessageTick { get; }
    }

    public readonly struct RealtimePacketHeader
    {
        public RealtimePacketHeader(
            ushort protocolVersion,
            RealtimeMessageKind messageKind,
            ushort headerLength,
            ushort payloadLength,
            Guid authenticatedTransportSessionId,
            ulong transportGeneration,
            ulong packetSequence,
            long messageTick,
            ushort authenticationTagLength)
        {
            ProtocolVersion = protocolVersion;
            MessageKind = messageKind;
            HeaderLength = headerLength;
            PayloadLength = payloadLength;
            AuthenticatedTransportSessionId =
                authenticatedTransportSessionId;
            TransportGeneration = transportGeneration;
            PacketSequence = packetSequence;
            MessageTick = messageTick;
            AuthenticationTagLength = authenticationTagLength;
        }

        public ushort ProtocolVersion { get; }
        public RealtimeMessageKind MessageKind { get; }
        public ushort HeaderLength { get; }
        public ushort PayloadLength { get; }
        public Guid AuthenticatedTransportSessionId { get; }
        public ulong TransportGeneration { get; }
        public ulong PacketSequence { get; }
        public long MessageTick { get; }
        public ushort AuthenticationTagLength { get; }
    }

    /// <summary>
    /// Allocation-free structural view. Authenticity is not established until
    /// AuthenticationTag is verified over AuthenticatedRegion by the caller.
    /// </summary>
    public readonly ref struct RealtimePacketEnvelope
    {
        public RealtimePacketEnvelope(
            RealtimePacketHeader header,
            ReadOnlySpan<byte> authenticatedRegion,
            ReadOnlySpan<byte> payload,
            ReadOnlySpan<byte> authenticationTag)
        {
            Header = header;
            AuthenticatedRegion = authenticatedRegion;
            Payload = payload;
            AuthenticationTag = authenticationTag;
        }

        public RealtimePacketHeader Header { get; }
        public ReadOnlySpan<byte> AuthenticatedRegion { get; }
        public ReadOnlySpan<byte> Payload { get; }
        public ReadOnlySpan<byte> AuthenticationTag { get; }
    }

    public sealed class MotionIntentPayload
    {
        public Guid WorldId { get; set; }
        public string ZoneKey { get; set; } = string.Empty;
        public Guid ActorEntityId { get; set; }
        public ulong InputSequence { get; set; }
        public long ClientTimestampMilliseconds { get; set; }
        public float MoveX { get; set; }
        public float MoveZ { get; set; }
        public float RequestedSpeed { get; set; }
        public bool HasDestination { get; set; }
        public float DestinationX { get; set; }
        public float DestinationZ { get; set; }
        public float DestinationStopDistance { get; set; }
        public float FacingX { get; set; }
        public float FacingZ { get; set; }
        public MotionIntentFlags Flags { get; set; }
    }

    public sealed class AuthorityMotionSnapshotPayload
    {
        public Guid WorldId { get; set; }
        public string ZoneKey { get; set; } = string.Empty;
        public Guid FrameOccurrenceId { get; set; }
        public ushort PageIndex { get; set; }
        public ushort PageCount { get; set; }
        public ushort TotalItemCount { get; set; }
        public ushort Reserved { get; set; }
        public long ObservedAtUnixMilliseconds { get; set; }
        public AuthorityMotionSnapshotItem[] Items { get; set; } =
            Array.Empty<AuthorityMotionSnapshotItem>();
    }

    public sealed class AuthorityMotionSnapshotItem
    {
        public Guid ActorEntityId { get; set; }
        public Guid ActorRuntimeSessionId { get; set; }
        public long ActorServerTick { get; set; }
        public float PositionX { get; set; }
        public float PositionY { get; set; }
        public float PositionZ { get; set; }
        public float RotationX { get; set; }
        public float RotationY { get; set; }
        public float RotationZ { get; set; }
        public float RotationW { get; set; }
        public float VelocityX { get; set; }
        public float VelocityY { get; set; }
        public float VelocityZ { get; set; }
        public string MotionStatus { get; set; } = string.Empty;
        public ulong LastProcessedInputSequence { get; set; }
        public AuthorityMotionStateFlags Flags { get; set; }
    }
}
