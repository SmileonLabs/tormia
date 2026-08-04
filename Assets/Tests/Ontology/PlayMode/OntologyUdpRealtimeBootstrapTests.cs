using System;
using System.Buffers.Binary;
using System.Reflection;
using NUnit.Framework;
using Tormia.Ontology.Core;
using Tormia.Ontology.Realtime.Protocol;

namespace Tormia.Ontology.Tests
{
    public sealed class OntologyUdpRealtimeBootstrapTests
    {
        [Test]
        public void BootstrapUsesTheExactFixedAuthorityFrameShape()
        {
            var ticket = new byte[32];
            var key = new byte[32];
            for (var index = 0; index < ticket.Length; index++)
            {
                ticket[index] = (byte)(index + 1);
                key[index] = (byte)(255 - index);
            }
            var session = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");

            Assert.That(OntologyUdpRealtimeBootstrap.TryCreateFrame(
                ToBase64Url(ticket), ToBase64Url(key), session,
                0x0102030405060708UL, out var frame), Is.True);
            Assert.That(frame, Has.Length.EqualTo(
                OntologyUdpRealtimeBootstrap.FrameLength));
            Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(frame),
                Is.EqualTo(0x55464f54));
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(4)),
                Is.EqualTo(OntologyUdpRealtimeBootstrap.BootstrapFrameVersion));
            Assert.That(OntologyUdpRealtimeBootstrap.BootstrapFrameVersion,
                Is.Not.EqualTo(RealtimeWireContract.ProtocolVersion));
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(6)),
                Is.Zero);
            Assert.That(new Guid(frame.AsSpan(8, 16)), Is.EqualTo(session));
            Assert.That(BinaryPrimitives.ReadUInt64LittleEndian(frame.AsSpan(24)),
                Is.EqualTo(0x0102030405060708UL));
            CollectionAssert.AreEqual(ticket, frame.AsSpan(32, 32).ToArray());
            Assert.That(frame.AsSpan(64, 16).ToArray(), Is.Not.EqualTo(new byte[16]));
            Assert.That(frame.AsSpan(80, 32).ToArray(), Is.Not.EqualTo(new byte[32]));
        }

        [Test]
        public void BootstrapRejectsWrongCredentialLengthOrEmptyBinding()
        {
            Assert.That(OntologyUdpRealtimeBootstrap.TryCreateFrame(
                "bad", "bad", Guid.Empty, 0, out var frame), Is.False);
            Assert.That(frame, Is.Empty);
        }

        [Test]
        public void MotionDatagramAuthenticationUsesSharedAuthenticatedRegion()
        {
            var key = new byte[32];
            for (var index = 0; index < key.Length; index++) key[index] = (byte)(index + 7);
            Assert.That(RealtimeWireCodec.TryCreateMotionIntentForAuthentication(
                new RealtimePacketMetadata(
                    Guid.Parse("00112233-4455-6677-8899-aabbccddeeff"), 1, 1, 0),
                new MotionIntentPayload
                {
                    WorldId = Guid.Parse("10213243-5465-7687-98a9-bacbdcedfe0f"),
                    ZoneKey = "zone_a",
                    ActorEntityId = Guid.Parse("ffeeddcc-bbaa-9988-7766-554433221100"),
                    InputSequence = 1, ClientTimestampMilliseconds = 1,
                    MoveX = 0f, MoveZ = 0f, RequestedSpeed = 0f,
                    HasDestination = false, DestinationX = 0f, DestinationZ = 0f,
                    DestinationStopDistance = 0f, FacingX = 0f, FacingZ = 1f
                }, out var datagram, out var error), Is.True, error.ToString());
            var method = typeof(OntologyWorldAuthorityUdpMotionTransport)
                .GetMethod("TryAuthenticateDatagram", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            var args = new object[] { datagram, key, null };
            Assert.That((bool)method.Invoke(null, args), Is.True);
            Assert.That((RealtimeWireError)args[2], Is.EqualTo(RealtimeWireError.None));
            Assert.That(RealtimeWireCodec.TryDecodeMotionIntentStructure(
                datagram, out _, out var decoded, out error), Is.True, error.ToString());
            Assert.That(decoded.ActorEntityId, Is.EqualTo(
                Guid.Parse("ffeeddcc-bbaa-9988-7766-554433221100")));
        }

        private static string ToBase64Url(byte[] value) => Convert
            .ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
