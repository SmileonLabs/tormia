using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace Tormia.Ontology.Realtime.Protocol
{
    public static class RealtimeWireCodec
    {
        private const int MotionIntentFixedPayloadLength = 87;
        private const int SnapshotFixedPayloadLength = 52;
        private const int SnapshotItemFixedPayloadLength = 94;
        private const float DirectionMagnitudeTolerance = 0.0001f;
        private const float QuaternionMagnitudeTolerance = 0.01f;

        private static readonly UTF8Encoding StrictUtf8 =
            new UTF8Encoding(false, true);

        public static bool TryEncodeMotionIntent(
            RealtimePacketMetadata metadata,
            MotionIntentPayload payload,
            ReadOnlySpan<byte> authenticationTag,
            out byte[] datagram,
            out RealtimeWireError error)
        {
            if (!TryValidateAuthenticationTag(authenticationTag, out error))
            {
                datagram = Array.Empty<byte>();
                return false;
            }
            return TryEncodeMotionIntentCore(
                metadata,
                payload,
                authenticationTag,
                false,
                out datagram,
                out error);
        }

        /// <summary>
        /// Creates a structurally complete datagram with a zeroed tag slot.
        /// Authenticate AuthenticatedRegion and call TryWriteAuthenticationTag
        /// before sending or structurally decoding the packet.
        /// </summary>
        public static bool TryCreateMotionIntentForAuthentication(
            RealtimePacketMetadata metadata,
            MotionIntentPayload payload,
            out byte[] datagram,
            out RealtimeWireError error) =>
            TryEncodeMotionIntentCore(
                metadata,
                payload,
                ReadOnlySpan<byte>.Empty,
                true,
                out datagram,
                out error);

        private static bool TryEncodeMotionIntentCore(
            RealtimePacketMetadata metadata,
            MotionIntentPayload payload,
            ReadOnlySpan<byte> authenticationTag,
            bool leaveAuthenticationTagZeroed,
            out byte[] datagram,
            out RealtimeWireError error)
        {
            datagram = Array.Empty<byte>();
            if (!TryValidateMetadata(
                    metadata,
                    RealtimeMessageKind.MotionIntent,
                    out error) ||
                payload is null)
            {
                if (payload is null)
                    error = RealtimeWireError.InvalidPayload;
                return false;
            }
            if (!TryEncodeText(
                    payload.ZoneKey,
                    RealtimeWireContract.MaximumZoneKeyUtf8Bytes,
                    out var zoneBytes,
                    out error) ||
                payload.WorldId == Guid.Empty ||
                payload.ActorEntityId == Guid.Empty)
            {
                if (error == RealtimeWireError.None)
                    error = RealtimeWireError.InvalidIdentifier;
                return false;
            }
            if (payload.InputSequence == 0)
            {
                error = RealtimeWireError.InvalidSequence;
                return false;
            }
            if (payload.ClientTimestampMilliseconds <= 0)
            {
                error = RealtimeWireError.InvalidTimestamp;
                return false;
            }
            if (payload.Flags != MotionIntentFlags.None)
            {
                error = RealtimeWireError.UnknownFlags;
                return false;
            }
            if (!AllFinite(
                    payload.MoveX,
                    payload.MoveZ,
                    payload.RequestedSpeed,
                    payload.DestinationX,
                    payload.DestinationZ,
                    payload.DestinationStopDistance,
                    payload.FacingX,
                    payload.FacingZ))
            {
                error = RealtimeWireError.NonFiniteNumber;
                return false;
            }
            if (payload.RequestedSpeed < 0f ||
                payload.DestinationStopDistance < 0f ||
                !IsUnitDirection(payload.MoveX, payload.MoveZ) ||
                !IsUnitDirection(payload.FacingX, payload.FacingZ))
            {
                error = RealtimeWireError.OutOfRangeNumber;
                return false;
            }
            if (!payload.HasDestination &&
                (!IsCanonicalZero(payload.DestinationX) ||
                 !IsCanonicalZero(payload.DestinationZ) ||
                 !IsCanonicalZero(payload.DestinationStopDistance)))
            {
                error = RealtimeWireError.InvalidPayload;
                return false;
            }

            var payloadLength =
                MotionIntentFixedPayloadLength + zoneBytes.Length;
            if (!TryCreateDatagram(
                    metadata,
                    RealtimeMessageKind.MotionIntent,
                    payloadLength,
                    out datagram,
                    out var writer,
                    out error))
            {
                return false;
            }

            writer.WriteGuid(payload.WorldId);
            writer.WriteUtf8(zoneBytes);
            writer.WriteGuid(payload.ActorEntityId);
            writer.WriteUInt64(payload.InputSequence);
            writer.WriteInt64(payload.ClientTimestampMilliseconds);
            writer.WriteSingle(payload.MoveX);
            writer.WriteSingle(payload.MoveZ);
            writer.WriteSingle(payload.RequestedSpeed);
            writer.WriteByte(payload.HasDestination ? (byte)1 : (byte)0);
            writer.WriteSingle(payload.DestinationX);
            writer.WriteSingle(payload.DestinationZ);
            writer.WriteSingle(payload.DestinationStopDistance);
            writer.WriteSingle(payload.FacingX);
            writer.WriteSingle(payload.FacingZ);
            writer.WriteUInt32((uint)payload.Flags);
            if (leaveAuthenticationTagZeroed)
                writer.Advance(RealtimeWireContract.RequiredAuthenticationTagLength);
            else
                writer.WriteBytes(authenticationTag);
            error = RealtimeWireError.None;
            return true;
        }

        /// <summary>
        /// Structurally decodes a MotionIntent. This method does not establish
        /// authenticity. Callers must first verify the packet tag over the
        /// allocation-free AuthenticatedRegion returned by
        /// TryReadStructuralEnvelope.
        /// </summary>
        public static bool TryDecodeMotionIntentStructure(
            ReadOnlySpan<byte> datagram,
            out RealtimePacketHeader header,
            out MotionIntentPayload payload,
            out RealtimeWireError error)
        {
            payload = new MotionIntentPayload();
            if (!TryReadStructuralEnvelope(
                    datagram,
                    out var envelope,
                    out error))
            {
                header = default;
                return false;
            }
            header = envelope.Header;
            if (header.MessageKind != RealtimeMessageKind.MotionIntent)
            {
                error = RealtimeWireError.UnsupportedMessageKind;
                return false;
            }
            if (IsAllZero(envelope.AuthenticationTag))
            {
                error = RealtimeWireError.AuthenticationTagMissing;
                return false;
            }

            var reader = new BufferReader(envelope.Payload);
            if (!reader.TryReadGuid(out var worldId) ||
                !reader.TryReadUtf8(
                    RealtimeWireContract.MaximumZoneKeyUtf8Bytes,
                    out var zoneKey,
                    out error) ||
                !reader.TryReadGuid(out var actorId) ||
                !reader.TryReadUInt64(out var inputSequence) ||
                !reader.TryReadInt64(out var clientTimestamp) ||
                !reader.TryReadSingle(out var moveX) ||
                !reader.TryReadSingle(out var moveZ) ||
                !reader.TryReadSingle(out var requestedSpeed) ||
                !reader.TryReadByte(out var hasDestination) ||
                !reader.TryReadSingle(out var destinationX) ||
                !reader.TryReadSingle(out var destinationZ) ||
                !reader.TryReadSingle(out var destinationStopDistance) ||
                !reader.TryReadSingle(out var facingX) ||
                !reader.TryReadSingle(out var facingZ) ||
                !reader.TryReadUInt32(out var flags))
            {
                if (error == RealtimeWireError.None)
                    error = RealtimeWireError.PayloadTruncated;
                return false;
            }
            if (reader.Remaining != 0)
            {
                error = RealtimeWireError.PayloadHasTrailingBytes;
                return false;
            }
            if (worldId == Guid.Empty ||
                actorId == Guid.Empty ||
                string.IsNullOrEmpty(zoneKey))
            {
                error = RealtimeWireError.InvalidIdentifier;
                return false;
            }
            if (inputSequence == 0)
            {
                error = RealtimeWireError.InvalidSequence;
                return false;
            }
            if (clientTimestamp <= 0)
            {
                error = RealtimeWireError.InvalidTimestamp;
                return false;
            }
            if (hasDestination > 1)
            {
                error = RealtimeWireError.InvalidBoolean;
                return false;
            }
            if (flags != (uint)MotionIntentFlags.None)
            {
                error = RealtimeWireError.UnknownFlags;
                return false;
            }
            if (!AllFinite(
                    moveX,
                    moveZ,
                    requestedSpeed,
                    destinationX,
                    destinationZ,
                    destinationStopDistance,
                    facingX,
                    facingZ))
            {
                error = RealtimeWireError.NonFiniteNumber;
                return false;
            }
            if (requestedSpeed < 0f ||
                destinationStopDistance < 0f ||
                !IsUnitDirection(moveX, moveZ) ||
                !IsUnitDirection(facingX, facingZ))
            {
                error = RealtimeWireError.OutOfRangeNumber;
                return false;
            }
            if (hasDestination == 0 &&
                (!IsCanonicalZero(destinationX) ||
                 !IsCanonicalZero(destinationZ) ||
                 !IsCanonicalZero(destinationStopDistance)))
            {
                error = RealtimeWireError.InvalidPayload;
                return false;
            }

            payload = new MotionIntentPayload
            {
                WorldId = worldId,
                ZoneKey = zoneKey,
                ActorEntityId = actorId,
                InputSequence = inputSequence,
                ClientTimestampMilliseconds = clientTimestamp,
                MoveX = moveX,
                MoveZ = moveZ,
                RequestedSpeed = requestedSpeed,
                HasDestination = hasDestination == 1,
                DestinationX = destinationX,
                DestinationZ = destinationZ,
                DestinationStopDistance = destinationStopDistance,
                FacingX = facingX,
                FacingZ = facingZ,
                Flags = MotionIntentFlags.None
            };
            error = RealtimeWireError.None;
            return true;
        }

        public static bool TryEncodeAuthorityMotionSnapshot(
            RealtimePacketMetadata metadata,
            AuthorityMotionSnapshotPayload payload,
            ReadOnlySpan<byte> authenticationTag,
            out byte[] datagram,
            out RealtimeWireError error)
        {
            if (!TryValidateAuthenticationTag(authenticationTag, out error))
            {
                datagram = Array.Empty<byte>();
                return false;
            }
            return TryEncodeAuthorityMotionSnapshotCore(
                metadata,
                payload,
                authenticationTag,
                false,
                out datagram,
                out error);
        }

        public static bool TryCreateAuthorityMotionSnapshotForAuthentication(
            RealtimePacketMetadata metadata,
            AuthorityMotionSnapshotPayload payload,
            out byte[] datagram,
            out RealtimeWireError error) =>
            TryEncodeAuthorityMotionSnapshotCore(
                metadata,
                payload,
                ReadOnlySpan<byte>.Empty,
                true,
                out datagram,
                out error);

        private static bool TryEncodeAuthorityMotionSnapshotCore(
            RealtimePacketMetadata metadata,
            AuthorityMotionSnapshotPayload payload,
            ReadOnlySpan<byte> authenticationTag,
            bool leaveAuthenticationTagZeroed,
            out byte[] datagram,
            out RealtimeWireError error)
        {
            datagram = Array.Empty<byte>();
            if (!TryValidateMetadata(
                    metadata,
                    RealtimeMessageKind.AuthorityMotionSnapshot,
                    out error) ||
                payload is null ||
                payload.Items is null)
            {
                if (payload is null || payload?.Items is null)
                    error = RealtimeWireError.InvalidPayload;
                return false;
            }
            if (payload.WorldId == Guid.Empty ||
                payload.FrameOccurrenceId == Guid.Empty ||
                !TryEncodeText(
                    payload.ZoneKey,
                    RealtimeWireContract.MaximumZoneKeyUtf8Bytes,
                    out var zoneBytes,
                    out error))
            {
                if (error == RealtimeWireError.None)
                    error = RealtimeWireError.InvalidIdentifier;
                return false;
            }
            if (payload.ObservedAtUnixMilliseconds <= 0)
            {
                error = RealtimeWireError.InvalidTimestamp;
                return false;
            }
            if (payload.Items.Length == 0 ||
                payload.Items.Length >
                RealtimeWireContract.MaximumSnapshotItemsParseBound)
            {
                error = RealtimeWireError.ItemCountOutOfRange;
                return false;
            }
            if (!IsValidSnapshotPageMetadata(payload))
            {
                error = RealtimeWireError.PageMetadataOutOfRange;
                return false;
            }

            var statusBytes = new byte[payload.Items.Length][];
            var actors = new HashSet<Guid>();
            var payloadLength = SnapshotFixedPayloadLength + zoneBytes.Length;
            for (var index = 0; index < payload.Items.Length; index++)
            {
                var item = payload.Items[index];
                if (item is null ||
                    item.ActorEntityId == Guid.Empty ||
                    item.ActorRuntimeSessionId == Guid.Empty ||
                    item.ActorServerTick <= 0)
                {
                    error = RealtimeWireError.InvalidIdentifier;
                    return false;
                }
                if (!actors.Add(item.ActorEntityId))
                {
                    error = RealtimeWireError.DuplicateActor;
                    return false;
                }
                if (!TryEncodeText(
                        item.MotionStatus,
                        RealtimeWireContract.MaximumMotionStatusUtf8Bytes,
                        out statusBytes[index],
                        out error))
                {
                    return false;
                }
                if (!AllFinite(
                        item.PositionX,
                        item.PositionY,
                        item.PositionZ,
                        item.RotationX,
                        item.RotationY,
                        item.RotationZ,
                        item.RotationW,
                        item.VelocityX,
                        item.VelocityY,
                        item.VelocityZ))
                {
                    error = RealtimeWireError.NonFiniteNumber;
                    return false;
                }
                if (!IsValidQuaternion(
                        item.RotationX,
                        item.RotationY,
                        item.RotationZ,
                        item.RotationW))
                {
                    error = RealtimeWireError.InvalidQuaternion;
                    return false;
                }
                if (HasUnknownAuthorityMotionFlags((uint)item.Flags))
                {
                    error = RealtimeWireError.UnknownFlags;
                    return false;
                }
                payloadLength +=
                    SnapshotItemFixedPayloadLength + statusBytes[index].Length;
            }

            if (!TryCreateDatagram(
                    metadata,
                    RealtimeMessageKind.AuthorityMotionSnapshot,
                    payloadLength,
                    out datagram,
                    out var writer,
                    out error))
            {
                return false;
            }

            writer.WriteGuid(payload.WorldId);
            writer.WriteUtf8(zoneBytes);
            writer.WriteGuid(payload.FrameOccurrenceId);
            writer.WriteUInt16(payload.PageIndex);
            writer.WriteUInt16(payload.PageCount);
            writer.WriteUInt16(payload.TotalItemCount);
            writer.WriteUInt16(payload.Reserved);
            writer.WriteInt64(payload.ObservedAtUnixMilliseconds);
            writer.WriteUInt16((ushort)payload.Items.Length);
            for (var index = 0; index < payload.Items.Length; index++)
            {
                var item = payload.Items[index];
                writer.WriteGuid(item.ActorEntityId);
                writer.WriteGuid(item.ActorRuntimeSessionId);
                writer.WriteInt64(item.ActorServerTick);
                writer.WriteSingle(item.PositionX);
                writer.WriteSingle(item.PositionY);
                writer.WriteSingle(item.PositionZ);
                writer.WriteSingle(item.RotationX);
                writer.WriteSingle(item.RotationY);
                writer.WriteSingle(item.RotationZ);
                writer.WriteSingle(item.RotationW);
                writer.WriteSingle(item.VelocityX);
                writer.WriteSingle(item.VelocityY);
                writer.WriteSingle(item.VelocityZ);
                writer.WriteUtf8(statusBytes[index]);
                writer.WriteUInt64(item.LastProcessedInputSequence);
                writer.WriteUInt32((uint)item.Flags);
            }
            if (leaveAuthenticationTagZeroed)
                writer.Advance(RealtimeWireContract.RequiredAuthenticationTagLength);
            else
                writer.WriteBytes(authenticationTag);
            error = RealtimeWireError.None;
            return true;
        }

        /// <summary>
        /// Structurally decodes a snapshot after, but not instead of, external
        /// tag verification over the envelope's AuthenticatedRegion.
        /// </summary>
        public static bool TryDecodeAuthorityMotionSnapshotStructure(
            ReadOnlySpan<byte> datagram,
            out RealtimePacketHeader header,
            out AuthorityMotionSnapshotPayload payload,
            out RealtimeWireError error)
        {
            payload = new AuthorityMotionSnapshotPayload();
            if (!TryReadStructuralEnvelope(
                    datagram,
                    out var envelope,
                    out error))
            {
                header = default;
                return false;
            }
            header = envelope.Header;
            if (header.MessageKind !=
                RealtimeMessageKind.AuthorityMotionSnapshot)
            {
                error = RealtimeWireError.UnsupportedMessageKind;
                return false;
            }
            if (IsAllZero(envelope.AuthenticationTag))
            {
                error = RealtimeWireError.AuthenticationTagMissing;
                return false;
            }

            var reader = new BufferReader(envelope.Payload);
            if (!reader.TryReadGuid(out var worldId) ||
                !reader.TryReadUtf8(
                    RealtimeWireContract.MaximumZoneKeyUtf8Bytes,
                    out var zoneKey,
                    out error) ||
                !reader.TryReadGuid(out var frameOccurrenceId) ||
                !reader.TryReadUInt16(out var pageIndex) ||
                !reader.TryReadUInt16(out var pageCount) ||
                !reader.TryReadUInt16(out var totalItemCount) ||
                !reader.TryReadUInt16(out var reserved) ||
                !reader.TryReadInt64(out var observedAt) ||
                !reader.TryReadUInt16(out var itemCount))
            {
                if (error == RealtimeWireError.None)
                    error = RealtimeWireError.PayloadTruncated;
                return false;
            }
            if (worldId == Guid.Empty ||
                frameOccurrenceId == Guid.Empty ||
                string.IsNullOrEmpty(zoneKey))
            {
                error = RealtimeWireError.InvalidIdentifier;
                return false;
            }
            if (observedAt <= 0)
            {
                error = RealtimeWireError.InvalidTimestamp;
                return false;
            }
            if (itemCount == 0 ||
                itemCount > RealtimeWireContract.MaximumSnapshotItemsParseBound)
            {
                error = RealtimeWireError.ItemCountOutOfRange;
                return false;
            }
            var pageMetadata = new AuthorityMotionSnapshotPayload
            {
                FrameOccurrenceId = frameOccurrenceId,
                PageIndex = pageIndex,
                PageCount = pageCount,
                TotalItemCount = totalItemCount,
                Reserved = reserved,
                Items = new AuthorityMotionSnapshotItem[itemCount]
            };
            if (!IsValidSnapshotPageMetadata(pageMetadata))
            {
                error = RealtimeWireError.PageMetadataOutOfRange;
                return false;
            }

            var items = new AuthorityMotionSnapshotItem[itemCount];
            var actors = new HashSet<Guid>();
            for (var index = 0; index < itemCount; index++)
            {
                if (!reader.TryReadGuid(out var actorId) ||
                    !reader.TryReadGuid(out var actorRuntimeSessionId) ||
                    !reader.TryReadInt64(out var actorServerTick) ||
                    !reader.TryReadSingle(out var positionX) ||
                    !reader.TryReadSingle(out var positionY) ||
                    !reader.TryReadSingle(out var positionZ) ||
                    !reader.TryReadSingle(out var rotationX) ||
                    !reader.TryReadSingle(out var rotationY) ||
                    !reader.TryReadSingle(out var rotationZ) ||
                    !reader.TryReadSingle(out var rotationW) ||
                    !reader.TryReadSingle(out var velocityX) ||
                    !reader.TryReadSingle(out var velocityY) ||
                    !reader.TryReadSingle(out var velocityZ) ||
                    !reader.TryReadUtf8(
                        RealtimeWireContract.MaximumMotionStatusUtf8Bytes,
                        out var motionStatus,
                        out error) ||
                    !reader.TryReadUInt64(out var lastProcessedInput) ||
                    !reader.TryReadUInt32(out var flags))
                {
                    if (error == RealtimeWireError.None)
                        error = RealtimeWireError.PayloadTruncated;
                    return false;
                }
                if (actorId == Guid.Empty ||
                    actorRuntimeSessionId == Guid.Empty ||
                    actorServerTick <= 0)
                {
                    error = RealtimeWireError.InvalidIdentifier;
                    return false;
                }
                if (!actors.Add(actorId))
                {
                    error = RealtimeWireError.DuplicateActor;
                    return false;
                }
                if (!AllFinite(
                        positionX,
                        positionY,
                        positionZ,
                        rotationX,
                        rotationY,
                        rotationZ,
                        rotationW,
                        velocityX,
                        velocityY,
                        velocityZ))
                {
                    error = RealtimeWireError.NonFiniteNumber;
                    return false;
                }
                if (!IsValidQuaternion(
                        rotationX,
                        rotationY,
                        rotationZ,
                        rotationW))
                {
                    error = RealtimeWireError.InvalidQuaternion;
                    return false;
                }
                if (HasUnknownAuthorityMotionFlags(flags))
                {
                    error = RealtimeWireError.UnknownFlags;
                    return false;
                }
                items[index] = new AuthorityMotionSnapshotItem
                {
                    ActorEntityId = actorId,
                    ActorRuntimeSessionId = actorRuntimeSessionId,
                    ActorServerTick = actorServerTick,
                    PositionX = positionX,
                    PositionY = positionY,
                    PositionZ = positionZ,
                    RotationX = rotationX,
                    RotationY = rotationY,
                    RotationZ = rotationZ,
                    RotationW = rotationW,
                    VelocityX = velocityX,
                    VelocityY = velocityY,
                    VelocityZ = velocityZ,
                    MotionStatus = motionStatus,
                    LastProcessedInputSequence = lastProcessedInput,
                    Flags = (AuthorityMotionStateFlags)flags
                };
            }
            if (reader.Remaining != 0)
            {
                error = RealtimeWireError.PayloadHasTrailingBytes;
                return false;
            }

            payload = new AuthorityMotionSnapshotPayload
            {
                WorldId = worldId,
                ZoneKey = zoneKey,
                FrameOccurrenceId = frameOccurrenceId,
                PageIndex = pageIndex,
                PageCount = pageCount,
                TotalItemCount = totalItemCount,
                Reserved = reserved,
                ObservedAtUnixMilliseconds = observedAt,
                Items = items
            };
            error = RealtimeWireError.None;
            return true;
        }

        public static bool TryValidateCompleteAuthorityMotionSnapshotPages(
            IReadOnlyList<AuthorityMotionSnapshotPayload> pages,
            out RealtimeWireError error)
        {
            if (pages is null || pages.Count == 0 ||
                pages.Count > RealtimeWireContract.MaximumSnapshotPageCount)
            {
                error = RealtimeWireError.PageSetIncomplete;
                return false;
            }
            var first = pages[0];
            if (first is null || !IsValidSnapshotPageMetadata(first) ||
                pages.Count != first.PageCount)
            {
                error = RealtimeWireError.PageSetIncomplete;
                return false;
            }

            var pageIndexes = new HashSet<ushort>();
            var actors = new HashSet<Guid>();
            var itemTotal = 0;
            foreach (var page in pages)
            {
                if (page is null || !IsValidSnapshotPageMetadata(page) ||
                    page.FrameOccurrenceId != first.FrameOccurrenceId ||
                    page.WorldId != first.WorldId ||
                    !string.Equals(page.ZoneKey, first.ZoneKey,
                        StringComparison.Ordinal) ||
                    page.ObservedAtUnixMilliseconds !=
                        first.ObservedAtUnixMilliseconds ||
                    page.PageCount != first.PageCount ||
                    page.TotalItemCount != first.TotalItemCount)
                {
                    error = RealtimeWireError.PageMetadataMismatch;
                    return false;
                }
                if (!pageIndexes.Add(page.PageIndex))
                {
                    error = RealtimeWireError.DuplicatePage;
                    return false;
                }
                itemTotal = checked(itemTotal + page.Items.Length);
                foreach (var item in page.Items)
                {
                    if (item is null || !actors.Add(item.ActorEntityId))
                    {
                        error = RealtimeWireError.DuplicateActor;
                        return false;
                    }
                }
            }
            for (ushort index = 0; index < first.PageCount; index++)
            {
                if (!pageIndexes.Contains(index))
                {
                    error = RealtimeWireError.PageSetIncomplete;
                    return false;
                }
            }
            if (itemTotal != first.TotalItemCount)
            {
                error = RealtimeWireError.PageMetadataMismatch;
                return false;
            }
            error = RealtimeWireError.None;
            return true;
        }

        private static bool IsValidSnapshotPageMetadata(
            AuthorityMotionSnapshotPayload payload)
        {
            if (payload.FrameOccurrenceId == Guid.Empty ||
                payload.Reserved != 0 ||
                payload.PageCount == 0 ||
                payload.PageCount >
                    RealtimeWireContract.MaximumSnapshotPageCount ||
                payload.PageIndex >= payload.PageCount ||
                payload.TotalItemCount == 0 ||
                payload.TotalItemCount >
                    RealtimeWireContract.MaximumSnapshotTotalItems ||
                payload.Items is null ||
                payload.Items.Length == 0 ||
                payload.Items.Length > payload.TotalItemCount ||
                payload.PageCount > payload.TotalItemCount)
                return false;
            if (payload.PageCount == 1)
                return payload.PageIndex == 0 &&
                       payload.TotalItemCount == payload.Items.Length;
            return payload.TotalItemCount > payload.Items.Length;
        }

        /// <summary>
        /// Performs only allocation-free structural envelope validation. It
        /// does not verify the authentication tag.
        /// </summary>
        public static bool TryReadStructuralEnvelope(
            ReadOnlySpan<byte> datagram,
            out RealtimePacketEnvelope envelope,
            out RealtimeWireError error)
        {
            envelope = default;
            if (datagram.Length == 0)
            {
                error = RealtimeWireError.EmptyDatagram;
                return false;
            }
            if (datagram.Length > RealtimeWireContract.MaximumDatagramLength)
            {
                error = RealtimeWireError.DatagramTooLarge;
                return false;
            }
            if (datagram.Length < RealtimeWireContract.FixedHeaderLength)
            {
                error = RealtimeWireError.HeaderTruncated;
                return false;
            }
            if (BinaryPrimitives.ReadUInt32LittleEndian(
                    datagram.Slice(0, 4)) != RealtimeWireContract.Magic)
            {
                error = RealtimeWireError.InvalidMagic;
                return false;
            }

            var protocolVersion = BinaryPrimitives.ReadUInt16LittleEndian(
                datagram.Slice(4, 2));
            if (protocolVersion != RealtimeWireContract.ProtocolVersion)
            {
                error = RealtimeWireError.UnsupportedVersion;
                return false;
            }
            var messageKind = (RealtimeMessageKind)
                BinaryPrimitives.ReadUInt16LittleEndian(datagram.Slice(6, 2));
            if (messageKind != RealtimeMessageKind.MotionIntent &&
                messageKind != RealtimeMessageKind.AuthorityMotionSnapshot)
            {
                error = RealtimeWireError.UnsupportedMessageKind;
                return false;
            }
            var headerLength = BinaryPrimitives.ReadUInt16LittleEndian(
                datagram.Slice(8, 2));
            if (headerLength != RealtimeWireContract.FixedHeaderLength)
            {
                error = RealtimeWireError.InvalidHeaderLength;
                return false;
            }
            var payloadLength = BinaryPrimitives.ReadUInt16LittleEndian(
                datagram.Slice(10, 2));
            var authenticatedTransportSessionId =
                ReadGuid(datagram.Slice(12, 16));
            if (authenticatedTransportSessionId == Guid.Empty)
            {
                error = RealtimeWireError.InvalidIdentifier;
                return false;
            }
            var transportGeneration =
                BinaryPrimitives.ReadUInt64LittleEndian(datagram.Slice(28, 8));
            var packetSequence =
                BinaryPrimitives.ReadUInt64LittleEndian(datagram.Slice(36, 8));
            if (transportGeneration == 0 || packetSequence == 0)
            {
                error = RealtimeWireError.InvalidSequence;
                return false;
            }
            var messageTick =
                BinaryPrimitives.ReadInt64LittleEndian(datagram.Slice(44, 8));
            if (messageTick < 0 ||
                (messageKind == RealtimeMessageKind.AuthorityMotionSnapshot &&
                 messageTick == 0))
            {
                error = RealtimeWireError.InvalidTick;
                return false;
            }
            var authenticationTagLength =
                BinaryPrimitives.ReadUInt16LittleEndian(datagram.Slice(52, 2));
            if (authenticationTagLength !=
                RealtimeWireContract.RequiredAuthenticationTagLength)
            {
                error = RealtimeWireError.AuthenticationTagLengthMismatch;
                return false;
            }
            if (BinaryPrimitives.ReadUInt16LittleEndian(
                    datagram.Slice(54, 2)) != 0)
            {
                error = RealtimeWireError.InvalidHeaderReservedBits;
                return false;
            }

            var expectedLength =
                headerLength + payloadLength + authenticationTagLength;
            if (expectedLength != datagram.Length)
            {
                error = RealtimeWireError.DatagramLengthMismatch;
                return false;
            }

            var header = new RealtimePacketHeader(
                protocolVersion,
                messageKind,
                headerLength,
                payloadLength,
                authenticatedTransportSessionId,
                transportGeneration,
                packetSequence,
                messageTick,
                authenticationTagLength);
            envelope = new RealtimePacketEnvelope(
                header,
                datagram.Slice(0, headerLength + payloadLength),
                datagram.Slice(headerLength, payloadLength),
                datagram.Slice(
                    headerLength + payloadLength,
                    authenticationTagLength));
            error = RealtimeWireError.None;
            return true;
        }

        public static bool TryGetAuthenticatedRegion(
            ReadOnlySpan<byte> datagram,
            out ReadOnlySpan<byte> authenticatedRegion,
            out RealtimeWireError error)
        {
            if (!TryReadStructuralEnvelope(datagram, out var envelope, out error))
            {
                authenticatedRegion = default;
                return false;
            }
            authenticatedRegion = envelope.AuthenticatedRegion;
            return true;
        }

        public static bool TryReadAuthenticationTag(
            ReadOnlySpan<byte> datagram,
            out ReadOnlySpan<byte> authenticationTag,
            out RealtimeWireError error)
        {
            if (!TryReadStructuralEnvelope(datagram, out var envelope, out error))
            {
                authenticationTag = default;
                return false;
            }
            if (IsAllZero(envelope.AuthenticationTag))
            {
                authenticationTag = default;
                error = RealtimeWireError.AuthenticationTagMissing;
                return false;
            }
            authenticationTag = envelope.AuthenticationTag;
            return true;
        }

        public static bool TryWriteAuthenticationTag(
            Span<byte> datagram,
            ReadOnlySpan<byte> authenticationTag,
            out RealtimeWireError error)
        {
            if (!TryValidateAuthenticationTag(authenticationTag, out error) ||
                !TryReadStructuralEnvelope(datagram, out var envelope, out error))
            {
                return false;
            }
            authenticationTag.CopyTo(datagram.Slice(
                envelope.Header.HeaderLength + envelope.Header.PayloadLength,
                envelope.Header.AuthenticationTagLength));
            error = RealtimeWireError.None;
            return true;
        }

        /// <summary>
        /// Calculates MTU-safe capacity for a batch whose items use one known
        /// status byte length. The parse bound is applied after the size limit.
        /// </summary>
        public static bool TryGetMaximumSnapshotItemCapacity(
            int zoneKeyUtf8ByteLength,
            int motionStatusUtf8ByteLength,
            out int capacity)
        {
            capacity = 0;
            if (zoneKeyUtf8ByteLength <= 0 ||
                zoneKeyUtf8ByteLength >
                RealtimeWireContract.MaximumZoneKeyUtf8Bytes ||
                motionStatusUtf8ByteLength <= 0 ||
                motionStatusUtf8ByteLength >
                RealtimeWireContract.MaximumMotionStatusUtf8Bytes)
            {
                return false;
            }
            var bytesAvailableForItems =
                RealtimeWireContract.MaximumDatagramLength -
                RealtimeWireContract.FixedHeaderLength -
                RealtimeWireContract.RequiredAuthenticationTagLength -
                SnapshotFixedPayloadLength -
                zoneKeyUtf8ByteLength;
            var encodedItemLength =
                SnapshotItemFixedPayloadLength + motionStatusUtf8ByteLength;
            capacity = Math.Min(
                RealtimeWireContract.MaximumSnapshotItemsParseBound,
                bytesAvailableForItems / encodedItemLength);
            return capacity > 0;
        }

        private static bool TryValidateMetadata(
            RealtimePacketMetadata metadata,
            RealtimeMessageKind messageKind,
            out RealtimeWireError error)
        {
            if (metadata.AuthenticatedTransportSessionId == Guid.Empty)
            {
                error = RealtimeWireError.InvalidIdentifier;
                return false;
            }
            if (metadata.TransportGeneration == 0 ||
                metadata.PacketSequence == 0)
            {
                error = RealtimeWireError.InvalidSequence;
                return false;
            }
            if (metadata.MessageTick < 0 ||
                (messageKind ==
                     RealtimeMessageKind.AuthorityMotionSnapshot &&
                 metadata.MessageTick == 0))
            {
                error = RealtimeWireError.InvalidTick;
                return false;
            }
            error = RealtimeWireError.None;
            return true;
        }

        private static bool TryCreateDatagram(
            RealtimePacketMetadata metadata,
            RealtimeMessageKind kind,
            int payloadLength,
            out byte[] datagram,
            out BufferWriter writer,
            out RealtimeWireError error)
        {
            datagram = Array.Empty<byte>();
            writer = default;
            if (payloadLength < 0 || payloadLength > ushort.MaxValue)
            {
                error = RealtimeWireError.DatagramTooLarge;
                return false;
            }
            var datagramLength =
                RealtimeWireContract.FixedHeaderLength + payloadLength +
                RealtimeWireContract.RequiredAuthenticationTagLength;
            if (datagramLength > RealtimeWireContract.MaximumDatagramLength)
            {
                error = RealtimeWireError.DatagramTooLarge;
                return false;
            }

            datagram = new byte[datagramLength];
            writer = new BufferWriter(datagram);
            writer.WriteUInt32(RealtimeWireContract.Magic);
            writer.WriteUInt16(RealtimeWireContract.ProtocolVersion);
            writer.WriteUInt16((ushort)kind);
            writer.WriteUInt16(RealtimeWireContract.FixedHeaderLength);
            writer.WriteUInt16((ushort)payloadLength);
            writer.WriteGuid(metadata.AuthenticatedTransportSessionId);
            writer.WriteUInt64(metadata.TransportGeneration);
            writer.WriteUInt64(metadata.PacketSequence);
            writer.WriteInt64(metadata.MessageTick);
            writer.WriteUInt16(
                RealtimeWireContract.RequiredAuthenticationTagLength);
            writer.WriteUInt16(0);
            error = RealtimeWireError.None;
            return true;
        }

        private static bool TryValidateAuthenticationTag(
            ReadOnlySpan<byte> authenticationTag,
            out RealtimeWireError error)
        {
            if (authenticationTag.Length !=
                RealtimeWireContract.RequiredAuthenticationTagLength)
            {
                error = RealtimeWireError.AuthenticationTagLengthMismatch;
                return false;
            }
            if (IsAllZero(authenticationTag))
            {
                error = RealtimeWireError.AuthenticationTagMissing;
                return false;
            }
            error = RealtimeWireError.None;
            return true;
        }

        private static bool TryEncodeText(
            string value,
            int maximumBytes,
            out byte[] bytes,
            out RealtimeWireError error)
        {
            bytes = Array.Empty<byte>();
            if (string.IsNullOrEmpty(value))
            {
                error = RealtimeWireError.InvalidTextValue;
                return false;
            }
            try
            {
                bytes = StrictUtf8.GetBytes(value);
            }
            catch (EncoderFallbackException)
            {
                error = RealtimeWireError.InvalidUtf8;
                return false;
            }
            if (bytes.Length > maximumBytes)
            {
                error = RealtimeWireError.StringTooLong;
                return false;
            }
            error = RealtimeWireError.None;
            return true;
        }

        private static bool IsUnitDirection(float x, float z)
        {
            if (x < -1f || x > 1f || z < -1f || z > 1f)
                return false;
            return (x * x) + (z * z) <= 1f + DirectionMagnitudeTolerance;
        }

        private static bool IsValidQuaternion(
            float x,
            float y,
            float z,
            float w)
        {
            var magnitudeSquared = (x * x) + (y * y) + (z * z) + (w * w);
            return Math.Abs(magnitudeSquared - 1f) <=
                QuaternionMagnitudeTolerance;
        }

        private static bool HasUnknownAuthorityMotionFlags(uint flags) =>
            (flags & ~(uint)AuthorityMotionStateFlags.Grounded) != 0;

        private static bool IsCanonicalZero(float value) =>
            BitConverter.SingleToInt32Bits(value) == 0;

        private static bool IsAllZero(ReadOnlySpan<byte> bytes)
        {
            byte aggregate = 0;
            for (var index = 0; index < bytes.Length; index++)
                aggregate |= bytes[index];
            return aggregate == 0;
        }

        private static bool AllFinite(
            float first,
            float second,
            float third,
            float fourth,
            float fifth,
            float sixth,
            float seventh,
            float eighth) =>
            IsFinite(first) &&
            IsFinite(second) &&
            IsFinite(third) &&
            IsFinite(fourth) &&
            IsFinite(fifth) &&
            IsFinite(sixth) &&
            IsFinite(seventh) &&
            IsFinite(eighth);

        private static bool AllFinite(
            float first,
            float second,
            float third,
            float fourth,
            float fifth,
            float sixth,
            float seventh,
            float eighth,
            float ninth,
            float tenth) =>
            AllFinite(
                first,
                second,
                third,
                fourth,
                fifth,
                sixth,
                seventh,
                eighth) &&
            IsFinite(ninth) &&
            IsFinite(tenth);

        private static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);

        private static Guid ReadGuid(ReadOnlySpan<byte> bytes) =>
            new Guid(bytes);

        private ref struct BufferWriter
        {
            private readonly Span<byte> buffer;
            private int offset;

            public BufferWriter(Span<byte> buffer)
            {
                this.buffer = buffer;
                offset = 0;
            }

            public void Advance(int length) => offset += length;
            public void WriteByte(byte value) => buffer[offset++] = value;

            public void WriteUInt16(ushort value)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(
                    buffer.Slice(offset, 2), value);
                offset += 2;
            }

            public void WriteUInt32(uint value)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(
                    buffer.Slice(offset, 4), value);
                offset += 4;
            }

            public void WriteUInt64(ulong value)
            {
                BinaryPrimitives.WriteUInt64LittleEndian(
                    buffer.Slice(offset, 8), value);
                offset += 8;
            }

            public void WriteInt64(long value)
            {
                BinaryPrimitives.WriteInt64LittleEndian(
                    buffer.Slice(offset, 8), value);
                offset += 8;
            }

            public void WriteSingle(float value)
            {
                BinaryPrimitives.WriteInt32LittleEndian(
                    buffer.Slice(offset, 4),
                    BitConverter.SingleToInt32Bits(value));
                offset += 4;
            }

            public void WriteGuid(Guid value)
            {
                if (!value.TryWriteBytes(buffer.Slice(offset, 16)))
                    throw new InvalidOperationException("Guid write failed.");
                offset += 16;
            }

            public void WriteUtf8(byte[] bytes)
            {
                WriteUInt16((ushort)bytes.Length);
                WriteBytes(bytes);
            }

            public void WriteBytes(ReadOnlySpan<byte> bytes)
            {
                bytes.CopyTo(buffer.Slice(offset, bytes.Length));
                offset += bytes.Length;
            }
        }

        private ref struct BufferReader
        {
            private readonly ReadOnlySpan<byte> buffer;
            private int offset;

            public BufferReader(ReadOnlySpan<byte> buffer)
            {
                this.buffer = buffer;
                offset = 0;
            }

            public int Remaining => buffer.Length - offset;

            public bool TryReadByte(out byte value)
            {
                value = 0;
                if (Remaining < 1)
                    return false;
                value = buffer[offset++];
                return true;
            }

            public bool TryReadUInt16(out ushort value)
            {
                value = 0;
                if (Remaining < 2)
                    return false;
                value = BinaryPrimitives.ReadUInt16LittleEndian(
                    buffer.Slice(offset, 2));
                offset += 2;
                return true;
            }

            public bool TryReadUInt32(out uint value)
            {
                value = 0;
                if (Remaining < 4)
                    return false;
                value = BinaryPrimitives.ReadUInt32LittleEndian(
                    buffer.Slice(offset, 4));
                offset += 4;
                return true;
            }

            public bool TryReadUInt64(out ulong value)
            {
                value = 0;
                if (Remaining < 8)
                    return false;
                value = BinaryPrimitives.ReadUInt64LittleEndian(
                    buffer.Slice(offset, 8));
                offset += 8;
                return true;
            }

            public bool TryReadInt64(out long value)
            {
                value = 0;
                if (Remaining < 8)
                    return false;
                value = BinaryPrimitives.ReadInt64LittleEndian(
                    buffer.Slice(offset, 8));
                offset += 8;
                return true;
            }

            public bool TryReadSingle(out float value)
            {
                value = 0f;
                if (Remaining < 4)
                    return false;
                value = BitConverter.Int32BitsToSingle(
                    BinaryPrimitives.ReadInt32LittleEndian(
                        buffer.Slice(offset, 4)));
                offset += 4;
                return true;
            }

            public bool TryReadGuid(out Guid value)
            {
                value = Guid.Empty;
                if (Remaining < 16)
                    return false;
                value = ReadGuid(buffer.Slice(offset, 16));
                offset += 16;
                return true;
            }

            public bool TryReadUtf8(
                int maximumBytes,
                out string value,
                out RealtimeWireError error)
            {
                value = string.Empty;
                if (!TryReadUInt16(out var length))
                {
                    error = RealtimeWireError.PayloadTruncated;
                    return false;
                }
                if (length == 0)
                {
                    error = RealtimeWireError.InvalidTextValue;
                    return false;
                }
                if (length > maximumBytes)
                {
                    error = RealtimeWireError.StringTooLong;
                    return false;
                }
                if (Remaining < length)
                {
                    error = RealtimeWireError.PayloadTruncated;
                    return false;
                }
                try
                {
                    value = StrictUtf8.GetString(buffer.Slice(offset, length));
                }
                catch (DecoderFallbackException)
                {
                    error = RealtimeWireError.InvalidUtf8;
                    return false;
                }
                offset += length;
                error = RealtimeWireError.None;
                return true;
            }
        }
    }
}
