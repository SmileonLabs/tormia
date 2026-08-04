using System.Buffers.Binary;
using System.Net;
using System.Security.Cryptography;
using Tormia.Ontology.Realtime.Protocol;

internal static class AuthorityUdpBootstrapContract
{
    internal const uint Magic = 0x55464f54; // ASCII "TOVU" in LE bytes.
    internal const ushort Version = 1;
    internal const int TicketLength = 32;
    internal const int ClientNonceLength = 16;
    internal const int ProofLength = 32;
    internal const int FrameLength =
        4 + 2 + 2 + 16 + 8 + TicketLength + ClientNonceLength + ProofLength;
}

internal readonly record struct AuthorityUdpBootstrapFrame(
    Guid AuthenticatedTransportSessionId,
    ulong TransportGeneration,
    byte[] Ticket,
    byte[] ClientNonce,
    byte[] Proof)
{
    internal UdpTransportTicketRedemption ToRedemption() => new(
        UdpTransportEncoding.Encode(Ticket),
        UdpTransportEncoding.Encode(ClientNonce),
        UdpTransportEncoding.Encode(Proof),
        AuthenticatedTransportSessionId,
        TransportGeneration);
}

internal static class AuthorityUdpBootstrapCodec
{
    internal static bool TryParse(
        ReadOnlySpan<byte> source,
        out AuthorityUdpBootstrapFrame frame)
    {
        frame = default;
        if (source.Length != AuthorityUdpBootstrapContract.FrameLength ||
            BinaryPrimitives.ReadUInt32LittleEndian(source) !=
                AuthorityUdpBootstrapContract.Magic ||
            BinaryPrimitives.ReadUInt16LittleEndian(source[4..]) !=
                AuthorityUdpBootstrapContract.Version ||
            BinaryPrimitives.ReadUInt16LittleEndian(source[6..]) != 0)
        {
            return false;
        }

        var transportSession = new Guid(source.Slice(8, 16));
        var generation = BinaryPrimitives.ReadUInt64LittleEndian(source[24..]);
        if (transportSession == Guid.Empty || generation == 0)
            return false;

        var offset = 32;
        var ticket = source.Slice(
            offset,
            AuthorityUdpBootstrapContract.TicketLength).ToArray();
        offset += ticket.Length;
        var nonce = source.Slice(
            offset,
            AuthorityUdpBootstrapContract.ClientNonceLength).ToArray();
        offset += nonce.Length;
        var proof = source.Slice(
            offset,
            AuthorityUdpBootstrapContract.ProofLength).ToArray();
        if (IsAllZero(ticket) || IsAllZero(nonce) || IsAllZero(proof))
        {
            CryptographicOperations.ZeroMemory(ticket);
            CryptographicOperations.ZeroMemory(nonce);
            CryptographicOperations.ZeroMemory(proof);
            return false;
        }

        frame = new(transportSession, generation, ticket, nonce, proof);
        return true;
    }

    internal static byte[] Encode(AuthorityUdpBootstrapFrame frame)
    {
        if (frame.AuthenticatedTransportSessionId == Guid.Empty ||
            frame.TransportGeneration == 0 ||
            frame.Ticket.Length != AuthorityUdpBootstrapContract.TicketLength ||
            frame.ClientNonce.Length != AuthorityUdpBootstrapContract.ClientNonceLength ||
            frame.Proof.Length != AuthorityUdpBootstrapContract.ProofLength)
        {
            throw new ArgumentException("Invalid UDP bootstrap frame.", nameof(frame));
        }

        var result = new byte[AuthorityUdpBootstrapContract.FrameLength];
        BinaryPrimitives.WriteUInt32LittleEndian(
            result,
            AuthorityUdpBootstrapContract.Magic);
        BinaryPrimitives.WriteUInt16LittleEndian(
            result.AsSpan(4),
            AuthorityUdpBootstrapContract.Version);
        frame.AuthenticatedTransportSessionId.TryWriteBytes(result.AsSpan(8, 16));
        BinaryPrimitives.WriteUInt64LittleEndian(
            result.AsSpan(24),
            frame.TransportGeneration);
        frame.Ticket.CopyTo(result, 32);
        frame.ClientNonce.CopyTo(result, 64);
        frame.Proof.CopyTo(result, 80);
        return result;
    }

    private static bool IsAllZero(ReadOnlySpan<byte> value)
    {
        byte aggregate = 0;
        foreach (var item in value) aggregate |= item;
        return aggregate == 0;
    }
}

/// <summary>
/// Lock-free 64-packet anti-replay window. The immutable state reference lets
/// highest sequence and bitmap advance in one compare-and-swap operation.
/// </summary>
internal sealed class AuthorityUdpReplayWindow
{
    private sealed record State(ulong HighestSequence, ulong SeenBitmap);
    private State state = new(0, 0);

    internal bool TryAccept(ulong sequence)
    {
        if (sequence == 0) return false;
        while (true)
        {
            var prior = Volatile.Read(ref state);
            State next;
            if (sequence > prior.HighestSequence)
            {
                var shift = sequence - prior.HighestSequence;
                var bitmap = shift >= 64
                    ? 1UL
                    : (prior.SeenBitmap << (int)shift) | 1UL;
                next = new(sequence, bitmap);
            }
            else
            {
                var distance = prior.HighestSequence - sequence;
                if (distance >= 64) return false;
                var mask = 1UL << (int)distance;
                if ((prior.SeenBitmap & mask) != 0) return false;
                next = new(prior.HighestSequence, prior.SeenBitmap | mask);
            }

            if (ReferenceEquals(
                    Interlocked.CompareExchange(ref state, next, prior),
                    prior))
            {
                return true;
            }
        }
    }
}

internal static class AuthorityUdpDatagramAuthentication
{
    internal static bool Verify(
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> authenticatedRegion,
        ReadOnlySpan<byte> authenticationTag)
    {
        if (key.Length != 32 ||
            authenticationTag.Length !=
                RealtimeWireContract.RequiredAuthenticationTagLength)
        {
            return false;
        }

        Span<byte> digest = stackalloc byte[32];
        if (!HMACSHA256.TryHashData(
                key,
                authenticatedRegion,
                digest,
                out var written) ||
            written != digest.Length)
        {
            CryptographicOperations.ZeroMemory(digest);
            return false;
        }

        var valid = CryptographicOperations.FixedTimeEquals(
            digest[..RealtimeWireContract.RequiredAuthenticationTagLength],
            authenticationTag);
        CryptographicOperations.ZeroMemory(digest);
        return valid;
    }
}

internal sealed record AuthorityUdpHandshakeAdmissionOptions(
    int MaximumPending,
    int MaximumPendingPerAddress,
    int MaximumAttemptsPerAddressPerWindow,
    TimeSpan AttemptWindow,
    int MaximumTrackedAddresses)
{
    internal static AuthorityUdpHandshakeAdmissionOptions Default { get; } = new(
        128,
        4,
        12,
        TimeSpan.FromSeconds(10),
        1024);
}

internal sealed class AuthorityUdpHandshakeAdmissionGate
{
    private readonly object gate = new();
    private readonly AuthorityUdpHandshakeAdmissionOptions options;
    private readonly Dictionary<IPAddress, AddressState> addresses = new();
    private int totalPending;

    internal AuthorityUdpHandshakeAdmissionGate(
        AuthorityUdpHandshakeAdmissionOptions options) =>
        this.options = options;

    internal bool TryEnter(
        IPAddress address,
        long nowMilliseconds,
        out IDisposable? lease)
    {
        lease = null;
        lock (gate)
        {
            Prune(nowMilliseconds);
            if (totalPending >= options.MaximumPending)
                return false;
            if (!addresses.TryGetValue(address, out var state))
            {
                if (addresses.Count >= options.MaximumTrackedAddresses)
                    return false;
                state = new(nowMilliseconds, 0, 0, nowMilliseconds);
            }
            if (nowMilliseconds - state.WindowStartedAtMilliseconds >=
                options.AttemptWindow.TotalMilliseconds)
            {
                state = state with
                {
                    WindowStartedAtMilliseconds = nowMilliseconds,
                    Attempts = 0
                };
            }
            if (state.Pending >= options.MaximumPendingPerAddress ||
                state.Attempts >= options.MaximumAttemptsPerAddressPerWindow)
            {
                addresses[address] = state with
                {
                    LastSeenAtMilliseconds = nowMilliseconds
                };
                return false;
            }

            addresses[address] = state with
            {
                Pending = state.Pending + 1,
                Attempts = state.Attempts + 1,
                LastSeenAtMilliseconds = nowMilliseconds
            };
            totalPending++;
            lease = new Lease(this, address);
            return true;
        }
    }

    private void Exit(IPAddress address)
    {
        lock (gate)
        {
            if (totalPending > 0) totalPending--;
            if (!addresses.TryGetValue(address, out var state)) return;
            addresses[address] = state with
            {
                Pending = Math.Max(0, state.Pending - 1)
            };
        }
    }

    private void Prune(long nowMilliseconds)
    {
        var staleAfter = Math.Max(
            1d,
            options.AttemptWindow.TotalMilliseconds * 2d);
        foreach (var pair in addresses.ToArray())
        {
            if (pair.Value.Pending == 0 &&
                nowMilliseconds - pair.Value.LastSeenAtMilliseconds >= staleAfter)
            {
                addresses.Remove(pair.Key);
            }
        }
    }

    private sealed class Lease : IDisposable
    {
        private AuthorityUdpHandshakeAdmissionGate? owner;
        private readonly IPAddress address;
        public Lease(AuthorityUdpHandshakeAdmissionGate owner, IPAddress address)
        {
            this.owner = owner;
            this.address = address;
        }
        public void Dispose() =>
            Interlocked.Exchange(ref owner, null)?.Exit(address);
    }

    private sealed record AddressState(
        long WindowStartedAtMilliseconds,
        int Attempts,
        int Pending,
        long LastSeenAtMilliseconds);
}
