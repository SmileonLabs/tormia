using System;
using NUnit.Framework;
using Tormia.Ontology.Realtime.Protocol;

namespace Tormia.Ontology.Tests
{
    public sealed class RealtimeWireCodecEditModeTests
    {
        private static readonly byte[] AuthenticationTag =
        {
            1, 2, 3, 4, 5, 6, 7, 8,
            9, 10, 11, 12, 13, 14, 15, 16
        };

        [Test]
        public void SharedCodecMatchesPinnedAuthorityFixture()
        {
            var expected = HexToBytes(
                "544F5652020001003800580033221100554477668899AABBCCDDEEFF" +
                "08070605040302011817161514131211640000000000000010000000" +
                "433221106554877698A9BACBDCEDFE0F01007ACCDDEEFFAABB889977" +
                "6655443322110063000000000000007B58686691010000000080BE00" +
                "00403F000090400100004841000004C1CDCC4C3E9A99193FCDCC4C" +
                "3F000000000102030405060708090A0B0C0D0E0F10");

            Assert.That(RealtimeWireCodec.TryEncodeMotionIntent(
                CreateMetadata(),
                CreatePayload(),
                AuthenticationTag,
                out var actual,
                out var error), Is.True, error.ToString());
            CollectionAssert.AreEqual(expected, actual);
            Assert.That(RealtimeWireCodec.TryDecodeMotionIntentStructure(
                actual, out _, out var decoded, out error),
                Is.True,
                error.ToString());
            Assert.That(decoded.InputSequence, Is.EqualTo(99));
        }

        [Test]
        public void PlaceholderRequiresInPlaceAuthenticationBeforeDecode()
        {
            Assert.That(RealtimeWireCodec.TryCreateMotionIntentForAuthentication(
                CreateMetadata(), CreatePayload(), out var datagram, out _),
                Is.True);
            Assert.That(RealtimeWireCodec.TryReadStructuralEnvelope(
                datagram, out var envelope, out _), Is.True);
            Assert.That(envelope.AuthenticatedRegion.Length,
                Is.EqualTo(datagram.Length - AuthenticationTag.Length));
            Assert.That(RealtimeWireCodec.TryDecodeMotionIntentStructure(
                datagram, out _, out _, out var missingTagError), Is.False);
            Assert.That(missingTagError,
                Is.EqualTo(RealtimeWireError.AuthenticationTagMissing));

            Assert.That(RealtimeWireCodec.TryWriteAuthenticationTag(
                datagram, AuthenticationTag, out _), Is.True);
            Assert.That(RealtimeWireCodec.TryDecodeMotionIntentStructure(
                datagram, out _, out _, out _), Is.True);
        }

        [Test]
        public void GameplayActionBitsCannotEnterMotionIntent()
        {
            var payload = CreatePayload();
            payload.Flags = (MotionIntentFlags)1;
            Assert.That(RealtimeWireCodec.TryEncodeMotionIntent(
                CreateMetadata(),
                payload,
                AuthenticationTag,
                out _,
                out var error), Is.False);
            Assert.That(error, Is.EqualTo(RealtimeWireError.UnknownFlags));
        }

        private static RealtimePacketMetadata CreateMetadata() =>
            new RealtimePacketMetadata(
                Guid.Parse("00112233-4455-6677-8899-aabbccddeeff"),
                0x0102030405060708,
                0x1112131415161718,
                100);

        private static MotionIntentPayload CreatePayload() =>
            new MotionIntentPayload
            {
                WorldId = Guid.Parse("10213243-5465-7687-98a9-bacbdcedfe0f"),
                ZoneKey = "z",
                ActorEntityId =
                    Guid.Parse("ffeeddcc-bbaa-9988-7766-554433221100"),
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

        private static byte[] HexToBytes(string hex)
        {
            var bytes = new byte[hex.Length / 2];
            for (var index = 0; index < bytes.Length; index++)
            {
                bytes[index] = Convert.ToByte(
                    hex.Substring(index * 2, 2),
                    16);
            }
            return bytes;
        }
    }
}
