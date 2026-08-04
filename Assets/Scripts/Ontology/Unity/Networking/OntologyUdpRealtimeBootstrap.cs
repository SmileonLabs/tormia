using System;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Tormia.Ontology.Realtime.Protocol;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Client half of the fixed Authority UDP admission frame. This is a
    /// transport credential only; it never authorizes a gameplay action.
    /// </summary>
    public static class OntologyUdpRealtimeBootstrap
    {
        // The fixed admission envelope has its own stable version. It is not
        // the version of the authenticated realtime payload carried after
        // admission.
        public const ushort BootstrapFrameVersion = 1;
        public const int TicketLength = 32;
        public const int ClientNonceLength = 16;
        public const int ProofLength = 32;
        public const int FrameLength = 112;
        private const uint Magic = 0x55464f54; // "TOVU" in little-endian.
        private static readonly byte[] ProofDomain =
            Encoding.ASCII.GetBytes("TOV-UDP-REDEEM-V1");

        public static bool TryCreateFrame(
            string encodedTicket,
            string encodedDatagramAuthenticationKey,
            Guid transportSessionId,
            ulong transportGeneration,
            out byte[] frame)
        {
            frame = Array.Empty<byte>();
            if (transportSessionId == Guid.Empty || transportGeneration == 0 ||
                !TryDecode(encodedTicket, TicketLength, out var ticket) ||
                !TryDecode(encodedDatagramAuthenticationKey, 32, out var key))
            {
                return false;
            }

            var nonce = new byte[ClientNonceLength];
            var proof = Array.Empty<byte>();
            try
            {
                RandomNumberGenerator.Fill(nonce);
                proof = CreateProof(key, ticket, nonce, transportSessionId,
                    transportGeneration);
                if (proof.Length != ProofLength) return false;

                frame = new byte[FrameLength];
                BinaryPrimitives.WriteUInt32LittleEndian(frame, Magic);
                BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(4),
                    BootstrapFrameVersion);
                // Bytes 6..7 are the zero reserved field.
                transportSessionId.TryWriteBytes(frame.AsSpan(8, 16));
                BinaryPrimitives.WriteUInt64LittleEndian(frame.AsSpan(24),
                    transportGeneration);
                ticket.CopyTo(frame, 32);
                nonce.CopyTo(frame, 64);
                proof.CopyTo(frame, 80);
                return true;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(ticket);
                CryptographicOperations.ZeroMemory(key);
                CryptographicOperations.ZeroMemory(nonce);
                if (proof.Length > 0) CryptographicOperations.ZeroMemory(proof);
            }
        }

        public static bool TryDecode(string value, int expectedLength,
            out byte[] decoded)
        {
            decoded = Array.Empty<byte>();
            if (string.IsNullOrWhiteSpace(value) || value.Length > 128 ||
                expectedLength <= 0)
            {
                return false;
            }

            var normalized = value.Replace('-', '+').Replace('_', '/');
            if (normalized.Length % 4 == 1) return false;
            if (normalized.Length % 4 != 0)
                normalized += new string('=', 4 - normalized.Length % 4);
            try
            {
                decoded = Convert.FromBase64String(normalized);
                if (decoded.Length == expectedLength) return true;
                CryptographicOperations.ZeroMemory(decoded);
                decoded = Array.Empty<byte>();
                return false;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        private static byte[] CreateProof(
            ReadOnlySpan<byte> key,
            ReadOnlySpan<byte> ticket,
            ReadOnlySpan<byte> nonce,
            Guid transportSessionId,
            ulong transportGeneration)
        {
            var payload = new byte[ProofDomain.Length + TicketLength +
                ClientNonceLength + 16 + 8];
            try
            {
                var offset = 0;
                ProofDomain.CopyTo(payload, offset); offset += ProofDomain.Length;
                ticket.CopyTo(payload.AsSpan(offset)); offset += TicketLength;
                nonce.CopyTo(payload.AsSpan(offset)); offset += ClientNonceLength;
                transportSessionId.TryWriteBytes(payload.AsSpan(offset, 16));
                offset += 16;
                BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(offset, 8),
                    transportGeneration);
                using var hmac = new HMACSHA256(key.ToArray());
                return hmac.ComputeHash(payload);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(payload);
            }
        }
    }
}
