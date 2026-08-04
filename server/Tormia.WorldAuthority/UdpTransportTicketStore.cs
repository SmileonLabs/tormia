using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using StackExchange.Redis;
using Tormia.Ontology.Realtime.Protocol;

internal sealed record UdpTransportOptions(
    bool Enabled,
    bool RequireHttps,
    bool AllowSingleInstanceDevelopment,
    TimeSpan TicketLifetime);

internal static class UdpTransportConfigurationPolicy
{
    internal static void Validate(
        UdpTransportOptions transport,
        AuthorityUdpListenerOptions listener,
        bool hasRedis,
        bool hasCredentialProtector,
        bool isDevelopment)
    {
        if (listener.Enabled && !transport.Enabled)
            throw new InvalidOperationException(
                "Realtime:UdpListenerEnabled requires " +
                "Realtime:UdpEnabled.");
        if (!transport.Enabled) return;

        var permittedSingleInstanceDevelopment =
            isDevelopment && transport.AllowSingleInstanceDevelopment;
        if ((!hasRedis || !hasCredentialProtector) &&
            !permittedSingleInstanceDevelopment)
        {
            throw new InvalidOperationException(
                "Enabled UDP transport requires Redis and a valid " +
                "credential protection key outside explicit " +
                "single-instance development.");
        }
        if (listener.Enabled && listener.Port < 1024)
        {
            throw new InvalidOperationException(
                "The non-root Authority container requires an unprivileged " +
                "UDP listener port (1024 or greater).");
        }
    }
}

/// <summary>Ephemeral transport identity; never a gameplay permission or Fact.</summary>
internal sealed record UdpTransportTicketBinding(
    Guid WorldId,
    Guid UserId,
    Guid AvatarEntityId,
    string ZoneKey,
    Guid RuntimeSessionId,
    ulong TransportGeneration,
    Guid AuthenticatedTransportSessionId,
    ushort ProtocolVersion);

internal sealed record UdpTransportTicketIssueContext(
    Guid WorldId,
    Guid UserId,
    Guid AvatarEntityId,
    string ZoneKey,
    Guid RuntimeSessionId,
    ushort ProtocolVersion);

internal sealed record UdpTransportTicketGrant(
    string Ticket,
    string DatagramAuthenticationKey,
    long ExpiresAtUnixMilliseconds,
    UdpTransportTicketBinding Binding);

internal enum UdpTransportTicketIssueStatus
{
    Issued,
    InvalidBinding,
    ActiveUdpWriterRequiresFallback
}
internal sealed record UdpTransportTicketIssueResult(
    UdpTransportTicketIssueStatus Status,
    UdpTransportTicketGrant? Grant = null);

internal sealed record UdpTransportTicketRedemption(
    string Ticket,
    string ClientNonce,
    string Proof,
    Guid AuthenticatedTransportSessionId,
    ulong TransportGeneration);

internal enum UdpTransportTicketRedeemStatus
{
    Redeemed,
    InvalidRequest,
    UnknownTicket,
    Expired,
    AlreadyRedeemed,
    ProofRejected,
    BindingNoLongerCurrent
}

internal sealed record UdpTransportTicketRedeemResult(
    UdpTransportTicketRedeemStatus Status,
    UdpTransportTicketBinding? Binding = null,
    byte[]? DatagramAuthenticationKey = null);

internal interface IUdpTransportRuntimeSessionValidator
{
    ValueTask<bool> IsCurrent(
        UdpTransportTicketBinding binding,
        CancellationToken cancellationToken);
}

internal sealed class AuthorityUdpTransportRuntimeSessionValidator(
    WorldAuthorityRepository repository,
    IWorldPlayerMotionRuntimeRegistry motion)
    : IUdpTransportRuntimeSessionValidator
{
    public async ValueTask<bool> IsCurrent(
        UdpTransportTicketBinding binding,
        CancellationToken cancellationToken)
    {
        if (!await repository.AvatarBelongsToUser(
                binding.WorldId,
                binding.UserId,
                binding.AvatarEntityId,
                cancellationToken)) return false;
        var current = await motion.Get(
            binding.WorldId,
            binding.AvatarEntityId,
            cancellationToken);
        return current is not null &&
               current.RuntimeSessionId == binding.RuntimeSessionId &&
               string.Equals(current.ZoneKey, binding.ZoneKey,
                   StringComparison.Ordinal);
    }
}

internal interface IUdpTransportTicketStore
{
    string BackendName { get; }
    bool SupportsMultipleAuthorityInstances { get; }
    ValueTask<UdpTransportTicketIssueResult> Issue(
        UdpTransportTicketIssueContext context,
        CancellationToken cancellationToken);
    /// <summary>
    /// Called only after a future UDP listener has performed bounded binary
    /// framing checks. The listener must not allocate peer/session state before
    /// this proof-authenticated redemption succeeds and must RejectForce excess
    /// pending handshakes. No UDP listener is introduced in this stage.
    /// </summary>
    ValueTask<UdpTransportTicketRedeemResult> Redeem(
        UdpTransportTicketRedemption redemption,
        IUdpTransportRuntimeSessionValidator runtimeValidator,
        CancellationToken cancellationToken);
    /// <summary>
    /// Promotes a redeemed handshake record to connected peer presence. This
    /// is transport readiness only; it never changes the active intent writer.
    /// </summary>
    ValueTask<bool> MarkPromotedTransportSessionConnected(
        UdpTransportTicketBinding binding,
        CancellationToken cancellationToken);
    /// <summary>
    /// Fences an already promoted peer against a ticket generation superseded
    /// on another Authority instance. This is transport identity only and must
    /// be checked before authenticating or decoding a datagram.
    /// </summary>
    ValueTask<bool> IsTransportGenerationCurrent(
        UdpTransportTicketBinding binding,
        CancellationToken cancellationToken);
    ValueTask<IReadOnlyList<bool>> AreTransportGenerationsCurrent(
        IReadOnlyList<UdpTransportTicketBinding> bindings,
        CancellationToken cancellationToken);
    /// <summary>
    /// Removes only the exact connected-peer presence. The redeemed handshake
    /// proof remains available for an explicit HTTPS fallback CAS.
    /// </summary>
    ValueTask RevokeConnectedTransportSession(
        UdpTransportTicketBinding binding,
        CancellationToken cancellationToken);
    /// <summary>Removes one promoted transport identity without revoking gameplay session state.</summary>
    ValueTask RevokePromotedTransportSession(
        UdpTransportTicketBinding binding,
        CancellationToken cancellationToken);
    ValueTask RevokeRuntimeSession(
        Guid worldId,
        Guid userId,
        Guid avatarEntityId,
        Guid runtimeSessionId,
        CancellationToken cancellationToken);
}

/// <summary>
/// Process-local equivalent of the Redis Lua generation CAS. The callback is
/// executed while ticket issuance is excluded, so an old generation cannot be
/// committed between a current-generation check and intent publication.
/// </summary>
internal interface IUdpTransportGenerationAtomicBoundary
{
    bool TryExecuteIfConnected(
        UdpTransportTicketBinding binding, Func<bool> operation);

    bool TryExecutePromotionIfCurrentCandidate(
        UdpTransportTicketBinding binding, Func<bool> operation);

    bool TryExecuteFallbackGenerationFence(
        UdpTransportTicketBinding binding, Func<bool> operation);
}

internal static class UdpTransportTicketProof
{
    private static readonly byte[] Domain =
        Encoding.ASCII.GetBytes("TOV-UDP-REDEEM-V1");

    public static string Create(
        ReadOnlySpan<byte> authenticationKey,
        string ticket,
        string clientNonce,
        Guid authenticatedTransportSessionId,
        ulong transportGeneration)
    {
        if (!UdpTransportEncoding.TryDecode(ticket, 32, out var ticketBytes) ||
            !UdpTransportEncoding.TryDecode(clientNonce, 16, out var nonceBytes))
            return string.Empty;
        try
        {
            var payload = new byte[Domain.Length + 32 + 16 + 16 + 8];
            var offset = 0;
            Domain.CopyTo(payload, offset); offset += Domain.Length;
            ticketBytes.CopyTo(payload, offset); offset += ticketBytes.Length;
            nonceBytes.CopyTo(payload, offset); offset += nonceBytes.Length;
            authenticatedTransportSessionId.TryWriteBytes(payload.AsSpan(offset, 16));
            offset += 16;
            BinaryPrimitives.WriteUInt64LittleEndian(
                payload.AsSpan(offset, 8), transportGeneration);
            var proof = HMACSHA256.HashData(authenticationKey, payload);
            CryptographicOperations.ZeroMemory(payload);
            return UdpTransportEncoding.Encode(proof);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(ticketBytes);
            CryptographicOperations.ZeroMemory(nonceBytes);
        }
    }

    public static bool Verify(
        ReadOnlySpan<byte> authenticationKey,
        UdpTransportTicketRedemption redemption)
    {
        var expected = Create(authenticationKey, redemption.Ticket,
            redemption.ClientNonce, redemption.AuthenticatedTransportSessionId,
            redemption.TransportGeneration);
        if (!UdpTransportEncoding.TryDecode(expected, 32, out var expectedBytes) ||
            !UdpTransportEncoding.TryDecode(redemption.Proof, 32, out var actualBytes))
            return false;
        try { return CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes); }
        finally
        {
            CryptographicOperations.ZeroMemory(expectedBytes);
            CryptographicOperations.ZeroMemory(actualBytes);
        }
    }
}

/// <summary>
/// Development-only process-local implementation. Production feature enablement
/// must use Redis so HTTPS issuance and UDP redemption may land on different
/// Authority instances.
/// </summary>
internal sealed class InMemoryUdpTransportTicketStore :
    IUdpTransportTicketStore,
    IUdpTransportGenerationAtomicBoundary
{
    internal static readonly TimeSpan DefaultLifetime = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan Retention = TimeSpan.FromMinutes(2);
    private readonly object gate = new();
    private readonly Dictionary<string, ActiveTicket> active = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> redeemed = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, UdpTransportTicketBinding> promoted = new();
    private readonly Dictionary<Guid, UdpTransportTicketBinding> connected = new();
    private readonly Dictionary<TicketScope, ScopeState> scopes = new();
    private readonly TimeProvider timeProvider;
    private readonly TimeSpan lifetime;

    public InMemoryUdpTransportTicketStore(TimeProvider? timeProvider = null,
        TimeSpan? lifetime = null)
    {
        this.timeProvider = timeProvider ?? TimeProvider.System;
        this.lifetime = lifetime ?? DefaultLifetime;
        ValidateLifetime(this.lifetime);
    }

    public string BackendName => "in_memory_single_instance_development";
    public bool SupportsMultipleAuthorityInstances => false;

    public ValueTask<UdpTransportTicketIssueResult> Issue(
        UdpTransportTicketIssueContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsValid(context)) return ValueTask.FromResult(
            new UdpTransportTicketIssueResult(UdpTransportTicketIssueStatus.InvalidBinding));
        var now = timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
        var expires = now + (long)lifetime.TotalMilliseconds;
        var ticketBytes = RandomNumberGenerator.GetBytes(32);
        var ticket = UdpTransportEncoding.Encode(ticketBytes);
        var fingerprint = UdpTransportEncoding.Fingerprint(ticketBytes);
        CryptographicOperations.ZeroMemory(ticketBytes);
        var key = RandomNumberGenerator.GetBytes(32);
        UdpTransportTicketBinding binding;

        lock (gate)
        {
            Prune(now, fingerprint);
            var scope = TicketScope.From(context);
            scopes.TryGetValue(scope, out var state);
            if (state?.UdpWriterActive == true)
            {
                CryptographicOperations.ZeroMemory(key);
                return ValueTask.FromResult(
                    new UdpTransportTicketIssueResult(
                        UdpTransportTicketIssueStatus
                            .ActiveUdpWriterRequiresFallback));
            }
            var generation = checked((state?.LatestGeneration ?? 0) + 1);
            if (state?.ActiveFingerprint is { } prior &&
                active.Remove(prior, out var priorTicket))
                CryptographicOperations.ZeroMemory(priorTicket.AuthenticationKey);
            binding = new(context.WorldId, context.UserId, context.AvatarEntityId,
                context.ZoneKey, context.RuntimeSessionId, generation,
                Guid.NewGuid(), context.ProtocolVersion);
            active[fingerprint] = new(binding, key, expires, scope);
            scopes[scope] = new(generation, fingerprint,
                expires + (long)Retention.TotalMilliseconds);
        }

        return ValueTask.FromResult(new UdpTransportTicketIssueResult(
            UdpTransportTicketIssueStatus.Issued,
            new(ticket, UdpTransportEncoding.Encode(key), expires, binding)));
    }

    public async ValueTask<UdpTransportTicketRedeemResult> Redeem(
        UdpTransportTicketRedemption redemption,
        IUdpTransportRuntimeSessionValidator runtimeValidator,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryFingerprint(redemption.Ticket, out var fingerprint))
            return new(UdpTransportTicketRedeemStatus.InvalidRequest);
        ActiveTicket candidate;
        var now = timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
        lock (gate)
        {
            Prune(now, fingerprint);
            if (!active.TryGetValue(fingerprint, out candidate!))
                return new(redeemed.ContainsKey(fingerprint)
                    ? UdpTransportTicketRedeemStatus.AlreadyRedeemed
                    : UdpTransportTicketRedeemStatus.UnknownTicket);
            if (candidate.ExpiresAtUnixMilliseconds <= now)
                return Expire(fingerprint, candidate, now);
            if (candidate.Binding.AuthenticatedTransportSessionId !=
                    redemption.AuthenticatedTransportSessionId ||
                candidate.Binding.TransportGeneration != redemption.TransportGeneration ||
                !UdpTransportTicketProof.Verify(candidate.AuthenticationKey, redemption))
                return new(UdpTransportTicketRedeemStatus.ProofRejected);
        }

        if (!await runtimeValidator.IsCurrent(candidate.Binding, cancellationToken))
            return new(UdpTransportTicketRedeemStatus.BindingNoLongerCurrent);

        lock (gate)
        {
            now = timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
            if (!active.TryGetValue(fingerprint, out var current) ||
                !ReferenceEquals(current, candidate))
                return new(redeemed.ContainsKey(fingerprint)
                    ? UdpTransportTicketRedeemStatus.AlreadyRedeemed
                    : UdpTransportTicketRedeemStatus.UnknownTicket);
            if (current.ExpiresAtUnixMilliseconds <= now)
                return Expire(fingerprint, current, now);
            active.Remove(fingerprint);
            ClearActiveScope(current.Scope, fingerprint);
            redeemed[fingerprint] = now + (long)Retention.TotalMilliseconds;
            promoted[current.Binding.AuthenticatedTransportSessionId] =
                current.Binding;
            var returnedKey = current.AuthenticationKey.ToArray();
            CryptographicOperations.ZeroMemory(current.AuthenticationKey);
            return new(UdpTransportTicketRedeemStatus.Redeemed,
                current.Binding, returnedKey);
        }
    }

    public ValueTask<bool> MarkPromotedTransportSessionConnected(
        UdpTransportTicketBinding binding,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (!promoted.TryGetValue(
                    binding.AuthenticatedTransportSessionId,
                    out var current) || current != binding)
                return ValueTask.FromResult(false);
            connected[binding.AuthenticatedTransportSessionId] = binding;
            return ValueTask.FromResult(true);
        }
    }

    public ValueTask<bool> IsTransportGenerationCurrent(
        UdpTransportTicketBinding binding,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            return ValueTask.FromResult(IsCurrentUnsafe(binding));
        }
    }

    public ValueTask<IReadOnlyList<bool>> AreTransportGenerationsCurrent(
        IReadOnlyList<UdpTransportTicketBinding> bindings,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            return ValueTask.FromResult<IReadOnlyList<bool>>(bindings
                .Select(IsCurrentUnsafe)
                .ToArray());
        }
    }

    public bool TryExecuteIfConnected(
        UdpTransportTicketBinding binding, Func<bool> operation)
    {
        lock (gate)
        {
            return IsPromotedUnsafe(binding) &&
                   operation();
        }
    }

    public bool TryExecutePromotionIfCurrentCandidate(
        UdpTransportTicketBinding binding, Func<bool> operation)
    {
        lock (gate)
        {
            var scope = TicketScope.From(binding);
            if (!scopes.TryGetValue(scope, out var state) ||
                state.LatestGeneration != binding.TransportGeneration ||
                state.UdpWriterActive ||
                !IsPromotedUnsafe(binding) ||
                !operation())
                return false;
            scopes[scope] = state with { UdpWriterActive = true };
            return true;
        }
    }

    public bool TryExecuteFallbackGenerationFence(
        UdpTransportTicketBinding binding, Func<bool> operation)
    {
        lock (gate)
        {
            var scope = TicketScope.From(binding);
            scopes.TryGetValue(scope, out var state);
            if (!operation()) return false;
            foreach (var pair in promoted
                         .Where(pair => TicketScope.From(pair.Value) == scope)
                         .ToArray())
                promoted.Remove(pair.Key);
            foreach (var pair in connected
                         .Where(pair => TicketScope.From(pair.Value) == scope)
                         .ToArray())
                connected.Remove(pair.Key);
            if (state?.ActiveFingerprint is { } fingerprint &&
                active.Remove(fingerprint, out var ticket))
                CryptographicOperations.ZeroMemory(ticket.AuthenticationKey);
            scopes[scope] = new(
                checked(Math.Max(
                    state?.LatestGeneration ?? 0,
                    binding.TransportGeneration) + 1),
                null,
                DateTimeOffset.MaxValue.ToUnixTimeMilliseconds());
            return true;
        }
    }

    public ValueTask RevokePromotedTransportSession(
        UdpTransportTicketBinding binding,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (IsPromotedUnsafe(binding))
            {
                promoted.Remove(binding.AuthenticatedTransportSessionId);
                connected.Remove(binding.AuthenticatedTransportSessionId);
            }
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask RevokeConnectedTransportSession(
        UdpTransportTicketBinding binding,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (connected.TryGetValue(
                    binding.AuthenticatedTransportSessionId,
                    out var current) && current == binding)
                connected.Remove(binding.AuthenticatedTransportSessionId);
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask RevokeRuntimeSession(
        Guid worldId, Guid userId, Guid avatarEntityId, Guid runtimeSessionId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scope = new TicketScope(worldId, userId, avatarEntityId, runtimeSessionId);
        lock (gate)
        {
            if (scopes.Remove(scope, out var state) &&
                state.ActiveFingerprint is { } fingerprint &&
                active.Remove(fingerprint, out var ticket))
                CryptographicOperations.ZeroMemory(ticket.AuthenticationKey);
            foreach (var pair in promoted
                         .Where(pair =>
                             TicketScope.From(pair.Value) == scope)
                         .ToArray())
                promoted.Remove(pair.Key);
            foreach (var pair in connected
                         .Where(pair => TicketScope.From(pair.Value) == scope)
                         .ToArray())
                connected.Remove(pair.Key);
        }
        return ValueTask.CompletedTask;
    }

    private UdpTransportTicketRedeemResult Expire(
        string fingerprint, ActiveTicket ticket, long now)
    {
        active.Remove(fingerprint);
        ClearActiveScope(ticket.Scope, fingerprint);
        redeemed[fingerprint] = now + (long)Retention.TotalMilliseconds;
        CryptographicOperations.ZeroMemory(ticket.AuthenticationKey);
        return new(UdpTransportTicketRedeemStatus.Expired);
    }

    private void ClearActiveScope(TicketScope scope, string fingerprint)
    {
        if (scopes.TryGetValue(scope, out var state) &&
            string.Equals(state.ActiveFingerprint, fingerprint, StringComparison.Ordinal))
            scopes[scope] = state with { ActiveFingerprint = null };
    }

    private void Prune(long now, string? preserve = null)
    {
        foreach (var pair in active.ToArray())
        {
            if (pair.Key == preserve || pair.Value.ExpiresAtUnixMilliseconds > now) continue;
            active.Remove(pair.Key);
            ClearActiveScope(pair.Value.Scope, pair.Key);
            CryptographicOperations.ZeroMemory(pair.Value.AuthenticationKey);
        }
        foreach (var pair in redeemed.ToArray())
            if (pair.Value <= now) redeemed.Remove(pair.Key);
        // Generation state intentionally lives for the whole Authority runtime
        // session and is removed by RevokeRuntimeSession. Expiring it with the
        // ticket would let a long-running session reset to generation one.
    }

    private static bool TryFingerprint(string ticket, out string fingerprint)
    {
        fingerprint = string.Empty;
        if (!UdpTransportEncoding.TryDecode(ticket, 32, out var bytes)) return false;
        try { fingerprint = UdpTransportEncoding.Fingerprint(bytes); return true; }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    private static bool IsValid(UdpTransportTicketIssueContext value) =>
        value.WorldId != Guid.Empty && value.UserId != Guid.Empty &&
        value.AvatarEntityId != Guid.Empty && !string.IsNullOrWhiteSpace(value.ZoneKey) &&
        value.RuntimeSessionId != Guid.Empty &&
        value.ProtocolVersion == RealtimeWireContract.ProtocolVersion;
    internal static void ValidateLifetime(TimeSpan value)
    {
        if (value <= TimeSpan.Zero || value > TimeSpan.FromMinutes(1))
            throw new ArgumentOutOfRangeException(nameof(value));
    }

    private bool IsCurrentUnsafe(UdpTransportTicketBinding binding) =>
        IsPromotedUnsafe(binding);

    private bool IsPromotedUnsafe(UdpTransportTicketBinding binding) =>
        connected.TryGetValue(
            binding.AuthenticatedTransportSessionId,
            out var current) &&
        current == binding;

    private sealed record ActiveTicket(UdpTransportTicketBinding Binding,
        byte[] AuthenticationKey, long ExpiresAtUnixMilliseconds, TicketScope Scope);
    private sealed record ScopeState(ulong LatestGeneration,
        string? ActiveFingerprint, long RetainUntil,
        bool UdpWriterActive = false);
    private readonly record struct TicketScope(Guid WorldId, Guid UserId,
        Guid AvatarEntityId, Guid RuntimeSessionId)
    {
        public static TicketScope From(UdpTransportTicketIssueContext c) =>
            new(c.WorldId, c.UserId, c.AvatarEntityId, c.RuntimeSessionId);
        public static TicketScope From(UdpTransportTicketBinding b) =>
            new(b.WorldId, b.UserId, b.AvatarEntityId, b.RuntimeSessionId);
    }
}

internal interface IUdpTicketCredentialProtector
{
    string KeyId { get; }
    byte[] Protect(ReadOnlySpan<byte> plaintext);
    bool TryUnprotect(string keyId, ReadOnlySpan<byte> protectedValue, out byte[] plaintext);
}

internal sealed class AesGcmUdpTicketCredentialProtector : IUdpTicketCredentialProtector
{
    private readonly byte[] key;
    public AesGcmUdpTicketCredentialProtector(string keyId, byte[] key)
    {
        if (string.IsNullOrWhiteSpace(keyId)) throw new ArgumentException(nameof(keyId));
        if (key.Length != 32) throw new ArgumentException("AES-256 key required.", nameof(key));
        KeyId = keyId;
        this.key = key.ToArray();
    }
    public string KeyId { get; }
    public byte[] Protect(ReadOnlySpan<byte> plaintext)
    {
        var result = new byte[12 + plaintext.Length + 16];
        RandomNumberGenerator.Fill(result.AsSpan(0, 12));
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(result.AsSpan(0, 12), plaintext,
            result.AsSpan(12, plaintext.Length), result.AsSpan(12 + plaintext.Length, 16));
        return result;
    }
    public bool TryUnprotect(string keyId, ReadOnlySpan<byte> value, out byte[] plaintext)
    {
        plaintext = Array.Empty<byte>();
        if (!string.Equals(keyId, KeyId, StringComparison.Ordinal) || value.Length < 28)
            return false;
        plaintext = new byte[value.Length - 28];
        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(value[..12], value.Slice(12, plaintext.Length), value[^16..], plaintext);
            return true;
        }
        catch (CryptographicException)
        {
            CryptographicOperations.ZeroMemory(plaintext);
            plaintext = Array.Empty<byte>();
            return false;
        }
    }
}

/// <summary>Multi-instance store with Redis-atomic generation and redemption.</summary>
internal sealed class RedisUdpTransportTicketStore : IUdpTransportTicketStore
{
    private const string IssueScript = """
        local writerText = redis.call('GET', KEYS[4])
        if writerText then
            local writer = cjson.decode(writerText)
            if writer.mode == 'udp' and
               writer.worldId == ARGV[2] and
               writer.userId == ARGV[3] and
               writer.avatarEntityId == ARGV[4] and
               writer.runtimeSessionId == ARGV[6] then return -1 end
        end
        local prior = redis.call('GET', KEYS[2])
        if prior then redis.call('DEL', prior) end
        local generation = redis.call('INCR', KEYS[3])
        redis.call('PEXPIRE', KEYS[3], ARGV[12])
        redis.call('SET', KEYS[2], KEYS[1], 'PX', ARGV[1])
        redis.call('HSET', KEYS[1],
          'world', ARGV[2], 'user', ARGV[3], 'avatar', ARGV[4],
          'zone', ARGV[5], 'runtime', ARGV[6], 'transportSession', ARGV[7],
          'protocol', ARGV[8], 'expires', ARGV[9], 'protectedKey', ARGV[10],
          'keyId', ARGV[11], 'generation', generation, 'scopeKey', KEYS[2],
          'generationKey', KEYS[3])
        redis.call('PEXPIRE', KEYS[1], ARGV[1])
        return generation
        """;
    private const string RedeemScript = """
        local expires = redis.call('HGET', KEYS[1], 'expires')
        if not expires then return 0 end
        if tonumber(expires) <= tonumber(ARGV[14]) then
            if redis.call('GET', KEYS[2]) == KEYS[1] then
                redis.call('DEL', KEYS[2])
            end
            redis.call('DEL', KEYS[1])
            redis.call('SET', KEYS[3], '1', 'PX', ARGV[1])
            return -3
        end
        local motion = redis.call('GET', KEYS[4])
        if not motion then return -2 end
        local decoded = cjson.decode(motion)
        if decoded.runtimeSessionId ~= ARGV[2] or decoded.zoneKey ~= ARGV[3] then return -2 end
        if redis.call('EXISTS', KEYS[1]) == 0 then return 0 end
        if redis.call('GET', KEYS[2]) ~= KEYS[1] then return -1 end
        redis.call('DEL', KEYS[1])
        redis.call('DEL', KEYS[2])
        redis.call('SET', KEYS[3], '1', 'PX', ARGV[1])
        redis.call('HSET', KEYS[5],
          'world', ARGV[4], 'user', ARGV[5], 'avatar', ARGV[6],
          'zone', ARGV[3], 'runtime', ARGV[2], 'transportSession', ARGV[7],
          'generation', ARGV[8], 'protocol', ARGV[9],
          'protectedKey', ARGV[10], 'keyId', ARGV[11])
        redis.call('PEXPIRE', KEYS[5], ARGV[12])
        redis.call('SADD', KEYS[6], KEYS[5])
        redis.call('PEXPIRE', KEYS[6], ARGV[13])
        return 1
        """;
    private const string ConsumeExpiredScript = """
        local expires = redis.call('HGET', KEYS[1], 'expires')
        if not expires then
            if redis.call('EXISTS', KEYS[3]) == 1 then return 2 end
            return 0
        end
        if tonumber(expires) > tonumber(ARGV[1]) then return -1 end
        if redis.call('GET', KEYS[2]) == KEYS[1] then
            redis.call('DEL', KEYS[2])
        end
        redis.call('DEL', KEYS[1])
        redis.call('SET', KEYS[3], '1', 'PX', ARGV[2])
        return 1
        """;
    private const string AreConnectedBindingsCurrentScript = """
        local result = {}
        local resultIndex = 1
        for keyIndex = 1, #KEYS, 2 do
            local key = KEYS[keyIndex]
            local generationKey = KEYS[keyIndex + 1]
            local offset = (resultIndex - 1) * 8
            local current =
                tonumber(redis.call('GET', generationKey)) ==
                    tonumber(ARGV[offset + 7]) and
                redis.call('HGET', key, 'world') == ARGV[offset + 1] and
                redis.call('HGET', key, 'user') == ARGV[offset + 2] and
                redis.call('HGET', key, 'avatar') == ARGV[offset + 3] and
                redis.call('HGET', key, 'zone') == ARGV[offset + 4] and
                redis.call('HGET', key, 'runtime') == ARGV[offset + 5] and
                redis.call('HGET', key, 'transportSession') == ARGV[offset + 6] and
                tonumber(redis.call('HGET', key, 'generation')) ==
                    tonumber(ARGV[offset + 7]) and
                tonumber(redis.call('HGET', key, 'protocol')) ==
                    tonumber(ARGV[offset + 8])
            result[resultIndex] = current and 1 or 0
            resultIndex = resultIndex + 1
        end
        return result
        """;
    private const string RevokeScript = """
        local ticket = redis.call('GET', KEYS[1])
        if ticket then redis.call('DEL', ticket) end
        local promoted = redis.call('SMEMBERS', KEYS[3])
        for _, key in ipairs(promoted) do redis.call('DEL', key) end
        redis.call('DEL', KEYS[1])
        redis.call('DEL', KEYS[2])
        redis.call('DEL', KEYS[3])
        return 1
        """;
    private const string RevokePromotedScript = """
        local generation = redis.call('HGET', KEYS[1], 'generation')
        local transport = redis.call('HGET', KEYS[1], 'transportSession')
        if generation ~= ARGV[1] or transport ~= ARGV[2] then return 0 end
        redis.call('DEL', KEYS[1])
        local connectedGeneration = redis.call('HGET', KEYS[2], 'generation')
        local connectedTransport = redis.call('HGET', KEYS[2], 'transportSession')
        if connectedGeneration == ARGV[1] and connectedTransport == ARGV[2] then
            redis.call('DEL', KEYS[2])
        end
        redis.call('SREM', KEYS[3], KEYS[1], KEYS[2])
        return 1
        """;
    private const string MarkConnectedScript = """
        if redis.call('HGET', KEYS[1], 'world') ~= ARGV[1] or
           redis.call('HGET', KEYS[1], 'user') ~= ARGV[2] or
           redis.call('HGET', KEYS[1], 'avatar') ~= ARGV[3] or
           redis.call('HGET', KEYS[1], 'zone') ~= ARGV[4] or
           redis.call('HGET', KEYS[1], 'runtime') ~= ARGV[5] or
           redis.call('HGET', KEYS[1], 'transportSession') ~= ARGV[6] or
           tonumber(redis.call('HGET', KEYS[1], 'generation')) ~= tonumber(ARGV[7]) or
           tonumber(redis.call('HGET', KEYS[1], 'protocol')) ~= tonumber(ARGV[8]) then return 0 end
        redis.call('HSET', KEYS[2],
          'world', ARGV[1], 'user', ARGV[2], 'avatar', ARGV[3],
          'zone', ARGV[4], 'runtime', ARGV[5], 'transportSession', ARGV[6],
          'generation', ARGV[7], 'protocol', ARGV[8])
        redis.call('PEXPIRE', KEYS[2], ARGV[9])
        redis.call('SADD', KEYS[3], KEYS[2])
        redis.call('PEXPIRE', KEYS[3], ARGV[10])
        return 1
        """;
    private const string RevokeConnectedScript = """
        local generation = redis.call('HGET', KEYS[1], 'generation')
        local transport = redis.call('HGET', KEYS[1], 'transportSession')
        if generation ~= ARGV[1] or transport ~= ARGV[2] then return 0 end
        redis.call('DEL', KEYS[1])
        redis.call('SREM', KEYS[2], KEYS[1])
        return 1
        """;
    private static readonly TimeSpan Retention = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan AbandonedSessionSafetyTtl = TimeSpan.FromDays(30);
    private static readonly TimeSpan PromotedTransportHandshakeTtl = TimeSpan.FromSeconds(30);
    private readonly IDatabase database;
    private readonly IUdpTicketCredentialProtector protector;
    private readonly TimeProvider clock;
    private readonly TimeSpan lifetime;
    private readonly int maximumGenerationBatchSize;

    public RedisUdpTransportTicketStore(IConnectionMultiplexer redis,
        IUdpTicketCredentialProtector protector, TimeProvider? clock = null,
        TimeSpan? lifetime = null,
        int maximumGenerationBatchSize = 64)
    {
        database = redis.GetDatabase();
        this.protector = protector;
        this.clock = clock ?? TimeProvider.System;
        this.lifetime = lifetime ?? InMemoryUdpTransportTicketStore.DefaultLifetime;
        InMemoryUdpTransportTicketStore.ValidateLifetime(this.lifetime);
        if (maximumGenerationBatchSize is <= 0 or > 1024)
            throw new ArgumentOutOfRangeException(
                nameof(maximumGenerationBatchSize));
        this.maximumGenerationBatchSize = maximumGenerationBatchSize;
    }
    public string BackendName => "redis_multi_instance";
    public bool SupportsMultipleAuthorityInstances => true;
    internal int MaximumGenerationBatchSize => maximumGenerationBatchSize;

    public async ValueTask<UdpTransportTicketIssueResult> Issue(
        UdpTransportTicketIssueContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (context.WorldId == Guid.Empty || context.UserId == Guid.Empty ||
            context.AvatarEntityId == Guid.Empty || context.RuntimeSessionId == Guid.Empty ||
            string.IsNullOrWhiteSpace(context.ZoneKey) ||
            context.ProtocolVersion != RealtimeWireContract.ProtocolVersion)
            return new(UdpTransportTicketIssueStatus.InvalidBinding);
        var ticketBytes = RandomNumberGenerator.GetBytes(32);
        var ticket = UdpTransportEncoding.Encode(ticketBytes);
        var fingerprint = UdpTransportEncoding.Fingerprint(ticketBytes);
        CryptographicOperations.ZeroMemory(ticketBytes);
        var authenticationKey = RandomNumberGenerator.GetBytes(32);
        var protectedKey = protector.Protect(authenticationKey);
        var transportSession = Guid.NewGuid();
        var expires = clock.GetUtcNow().Add(lifetime).ToUnixTimeMilliseconds();
        var ticketKey = TicketKey(fingerprint);
        var scope = ScopePrefix(context);
        var result = await database.ScriptEvaluateAsync(IssueScript,
            new RedisKey[]
            {
                ticketKey,
                scope + ":active",
                scope + ":generation",
                WriterKey(context.WorldId, context.AvatarEntityId)
            },
            new RedisValue[] {
                (long)(lifetime + Retention).TotalMilliseconds,
                context.WorldId.ToString("D"), context.UserId.ToString("D"),
                context.AvatarEntityId.ToString("D"), context.ZoneKey,
                context.RuntimeSessionId.ToString("D"), transportSession.ToString("D"),
                (int)context.ProtocolVersion, expires, Convert.ToBase64String(protectedKey),
                protector.KeyId,
                (long)AbandonedSessionSafetyTtl.TotalMilliseconds });
        var generationValue = (long)result;
        if (generationValue == -1)
        {
            CryptographicOperations.ZeroMemory(protectedKey);
            CryptographicOperations.ZeroMemory(authenticationKey);
            return new(
                UdpTransportTicketIssueStatus
                    .ActiveUdpWriterRequiresFallback);
        }
        var generation = checked((ulong)generationValue);
        var binding = new UdpTransportTicketBinding(context.WorldId, context.UserId,
            context.AvatarEntityId, context.ZoneKey, context.RuntimeSessionId,
            generation, transportSession, context.ProtocolVersion);
        CryptographicOperations.ZeroMemory(protectedKey);
        var encodedAuthenticationKey = UdpTransportEncoding.Encode(authenticationKey);
        CryptographicOperations.ZeroMemory(authenticationKey);
        return new(UdpTransportTicketIssueStatus.Issued,
            new(ticket, encodedAuthenticationKey, expires, binding));
    }

    public async ValueTask<UdpTransportTicketRedeemResult> Redeem(
        UdpTransportTicketRedemption redemption,
        IUdpTransportRuntimeSessionValidator runtimeValidator,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!UdpTransportEncoding.TryDecode(redemption.Ticket, 32, out var bytes))
            return new(UdpTransportTicketRedeemStatus.InvalidRequest);
        var fingerprint = UdpTransportEncoding.Fingerprint(bytes);
        CryptographicOperations.ZeroMemory(bytes);
        var ticketKey = TicketKey(fingerprint);
        var entries = await database.HashGetAllAsync(ticketKey);
        if (entries.Length == 0)
            return new(await database.KeyExistsAsync(ConsumedKey(fingerprint))
                ? UdpTransportTicketRedeemStatus.AlreadyRedeemed
                : UdpTransportTicketRedeemStatus.UnknownTicket);
        var map = entries.ToDictionary(x => x.Name.ToString(), x => x.Value.ToString());
        if (!TryBinding(map, out var binding, out var expires, out var scopeKey))
            return new(UdpTransportTicketRedeemStatus.Expired);
        var now = clock.GetUtcNow().ToUnixTimeMilliseconds();
        if (expires <= now)
        {
            var expiry = (int)await database.ScriptEvaluateAsync(
                ConsumeExpiredScript,
                new RedisKey[]
                {
                    ticketKey,
                    scopeKey,
                    ConsumedKey(fingerprint)
                },
                new RedisValue[]
                {
                    now,
                    (long)Retention.TotalMilliseconds
                });
            return new(expiry == 1
                ? UdpTransportTicketRedeemStatus.Expired
                : expiry == 2
                    ? UdpTransportTicketRedeemStatus.AlreadyRedeemed
                    : UdpTransportTicketRedeemStatus.UnknownTicket);
        }
        byte[] protectedBytes;
        try
        {
            protectedBytes = Convert.FromBase64String(
                map.GetValueOrDefault("protectedKey") ?? string.Empty);
        }
        catch (FormatException)
        {
            return new(UdpTransportTicketRedeemStatus.ProofRejected);
        }
        var hasProtectedText = map.TryGetValue("protectedKey", out var protectedText);
        var hasKeyId = map.TryGetValue("keyId", out var keyId);
        var key = Array.Empty<byte>();
        var unprotected = hasProtectedText && hasKeyId &&
            protector.TryUnprotect(keyId!, protectedBytes, out key);
        CryptographicOperations.ZeroMemory(protectedBytes);
        if (!unprotected)
            return new(UdpTransportTicketRedeemStatus.ProofRejected);
        try
        {
            if (binding.AuthenticatedTransportSessionId != redemption.AuthenticatedTransportSessionId ||
                binding.TransportGeneration != redemption.TransportGeneration ||
                !UdpTransportTicketProof.Verify(key, redemption))
                return new(UdpTransportTicketRedeemStatus.ProofRejected);
            if (!await runtimeValidator.IsCurrent(binding, cancellationToken))
                return new(UdpTransportTicketRedeemStatus.BindingNoLongerCurrent);
            var motionKey = MotionKey(binding.WorldId, binding.AvatarEntityId);
            var promotedKey = PromotedKey(binding.AuthenticatedTransportSessionId);
            var promotionSetKey = ScopePrefix(binding) + ":promoted";
            var consumed = (int)await database.ScriptEvaluateAsync(RedeemScript,
                new RedisKey[] { ticketKey, scopeKey, ConsumedKey(fingerprint),
                    motionKey, promotedKey, promotionSetKey },
                new RedisValue[] { (long)Retention.TotalMilliseconds,
                    binding.RuntimeSessionId.ToString("D"), binding.ZoneKey,
                    binding.WorldId.ToString("D"), binding.UserId.ToString("D"),
                    binding.AvatarEntityId.ToString("D"),
                    binding.AuthenticatedTransportSessionId.ToString("D"),
                    binding.TransportGeneration.ToString(),
                    (int)binding.ProtocolVersion, protectedText, keyId,
                    (long)PromotedTransportHandshakeTtl.TotalMilliseconds,
                    (long)AbandonedSessionSafetyTtl.TotalMilliseconds,
                    clock.GetUtcNow().ToUnixTimeMilliseconds() });
            if (consumed == -3)
                return new(UdpTransportTicketRedeemStatus.Expired);
            if (consumed == -2)
                return new(UdpTransportTicketRedeemStatus.BindingNoLongerCurrent);
            if (consumed != 1) return new(consumed == 0 &&
                await database.KeyExistsAsync(ConsumedKey(fingerprint))
                ? UdpTransportTicketRedeemStatus.AlreadyRedeemed
                : UdpTransportTicketRedeemStatus.UnknownTicket);
            return new(UdpTransportTicketRedeemStatus.Redeemed, binding, key.ToArray());
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    public async ValueTask<bool> IsTransportGenerationCurrent(
        UdpTransportTicketBinding binding,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await IsPromotedBindingCurrent(binding);
    }

    public async ValueTask<bool> MarkPromotedTransportSessionConnected(
        UdpTransportTicketBinding binding,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = await database.ScriptEvaluateAsync(
            MarkConnectedScript,
            new RedisKey[]
            {
                PromotedKey(binding.AuthenticatedTransportSessionId),
                ConnectedKey(binding.AuthenticatedTransportSessionId),
                ScopePrefix(binding) + ":promoted"
            },
            new RedisValue[]
            {
                binding.WorldId.ToString("D"),
                binding.UserId.ToString("D"),
                binding.AvatarEntityId.ToString("D"),
                binding.ZoneKey,
                binding.RuntimeSessionId.ToString("D"),
                binding.AuthenticatedTransportSessionId.ToString("D"),
                binding.TransportGeneration,
                (int)binding.ProtocolVersion,
                (long)PromotedTransportHandshakeTtl.TotalMilliseconds,
                (long)AbandonedSessionSafetyTtl.TotalMilliseconds
            });
        return (int)result == 1;
    }

    public async ValueTask<IReadOnlyList<bool>> AreTransportGenerationsCurrent(
        IReadOnlyList<UdpTransportTicketBinding> bindings,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (bindings.Count == 0) return Array.Empty<bool>();
        var result = new bool[bindings.Count];
        foreach (var chunk in CreateGenerationLookupChunks(
                     bindings.Count,
                     maximumGenerationBatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var keys = new RedisKey[checked(chunk.Count * 2)];
            var arguments = new RedisValue[checked(chunk.Count * 8)];
            for (var localIndex = 0; localIndex < chunk.Count; localIndex++)
            {
                var binding = bindings[chunk.Offset + localIndex];
                keys[localIndex * 2] = ConnectedKey(
                    binding.AuthenticatedTransportSessionId);
                keys[localIndex * 2 + 1] =
                    ScopePrefix(binding) + ":generation";
                var offset = localIndex * 8;
                arguments[offset] = binding.WorldId.ToString("D");
                arguments[offset + 1] = binding.UserId.ToString("D");
                arguments[offset + 2] = binding.AvatarEntityId.ToString("D");
                arguments[offset + 3] = binding.ZoneKey;
                arguments[offset + 4] =
                    binding.RuntimeSessionId.ToString("D");
                arguments[offset + 5] = binding
                    .AuthenticatedTransportSessionId.ToString("D");
                arguments[offset + 6] = binding.TransportGeneration;
                arguments[offset + 7] = (int)binding.ProtocolVersion;
            }
            try
            {
                var raw = (RedisResult[]?)await database.ScriptEvaluateAsync(
                    AreConnectedBindingsCurrentScript, keys, arguments);
                if (raw is null || raw.Length != chunk.Count)
                    continue;
                for (var localIndex = 0;
                     localIndex < chunk.Count;
                     localIndex++)
                {
                    result[chunk.Offset + localIndex] =
                        (int)raw[localIndex] == 1;
                }
            }
            catch (RedisException)
            {
                // One failed bounded Redis chunk fails closed without
                // discarding valid results from independent chunks.
            }
            catch (InvalidCastException)
            {
                // Malformed Redis results fail closed for this chunk only.
            }
            catch (OverflowException)
            {
                // Out-of-range Redis results fail closed for this chunk only.
            }
        }
        return result;
    }

    internal static IReadOnlyList<(int Offset, int Count)>
        CreateGenerationLookupChunks(int count, int maximumBatchSize)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        if (maximumBatchSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumBatchSize));
        var chunks = new List<(int Offset, int Count)>(
            count == 0 ? 0 : (count + maximumBatchSize - 1) /
                maximumBatchSize);
        for (var offset = 0; offset < count; offset += maximumBatchSize)
            chunks.Add((offset, Math.Min(maximumBatchSize, count - offset)));
        return chunks;
    }

    public async ValueTask RevokePromotedTransportSession(
        UdpTransportTicketBinding binding,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await database.ScriptEvaluateAsync(
            RevokePromotedScript,
            new RedisKey[]
            {
                PromotedKey(binding.AuthenticatedTransportSessionId),
                ConnectedKey(binding.AuthenticatedTransportSessionId),
                ScopePrefix(binding) + ":promoted"
            },
            new RedisValue[]
            {
                binding.TransportGeneration.ToString(),
                binding.AuthenticatedTransportSessionId.ToString("D")
            });
    }

    public async ValueTask RevokeConnectedTransportSession(
        UdpTransportTicketBinding binding,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await database.ScriptEvaluateAsync(
            RevokeConnectedScript,
            new RedisKey[]
            {
                ConnectedKey(binding.AuthenticatedTransportSessionId),
                ScopePrefix(binding) + ":promoted"
            },
            new RedisValue[]
            {
                binding.TransportGeneration.ToString(),
                binding.AuthenticatedTransportSessionId.ToString("D")
            });
    }

    public async ValueTask RevokeRuntimeSession(
        Guid worldId, Guid userId, Guid avatarEntityId, Guid runtimeSessionId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var context = new UdpTransportTicketIssueContext(worldId, userId,
            avatarEntityId, string.Empty, runtimeSessionId,
            RealtimeWireContract.ProtocolVersion);
        var scope = ScopePrefix(context);
        await database.ScriptEvaluateAsync(RevokeScript,
            new RedisKey[] { scope + ":active", scope + ":generation",
                scope + ":promoted" });
    }

    private static bool TryBinding(Dictionary<string, string> map,
        out UdpTransportTicketBinding binding, out long expires, out string scopeKey)
    {
        binding = null!; expires = 0; scopeKey = string.Empty;
        if (!Guid.TryParse(map.GetValueOrDefault("world"), out var world) ||
            !Guid.TryParse(map.GetValueOrDefault("user"), out var user) ||
            !Guid.TryParse(map.GetValueOrDefault("avatar"), out var avatar) ||
            !Guid.TryParse(map.GetValueOrDefault("runtime"), out var runtime) ||
            !Guid.TryParse(map.GetValueOrDefault("transportSession"), out var transport) ||
            !ulong.TryParse(map.GetValueOrDefault("generation"), out var generation) ||
            !ushort.TryParse(map.GetValueOrDefault("protocol"), out var protocol) ||
            !long.TryParse(map.GetValueOrDefault("expires"), out expires) ||
            string.IsNullOrWhiteSpace(map.GetValueOrDefault("zone"))) return false;
        var parsedScopeKey = map.GetValueOrDefault("scopeKey");
        if (string.IsNullOrWhiteSpace(parsedScopeKey)) return false;
        scopeKey = parsedScopeKey;
        binding = new(world, user, avatar, map["zone"], runtime, generation, transport, protocol);
        return true;
    }

    private async Task<bool> IsPromotedBindingCurrent(
        UdpTransportTicketBinding binding)
    {
        var results = await AreTransportGenerationsCurrent(
            new[] { binding }, CancellationToken.None);
        return results.Count == 1 && results[0];
    }
    private static string ScopePrefix(UdpTransportTicketIssueContext c) =>
        $"tormia:udp-ticket:scope:{c.WorldId:N}:{c.UserId:N}:{c.AvatarEntityId:N}:{c.RuntimeSessionId:N}";
    private static string ScopePrefix(UdpTransportTicketBinding b) =>
        $"tormia:udp-ticket:scope:{b.WorldId:N}:{b.UserId:N}:{b.AvatarEntityId:N}:{b.RuntimeSessionId:N}";
    private static string TicketKey(string fingerprint) => "tormia:udp-ticket:value:" + fingerprint;
    private static string ConsumedKey(string fingerprint) => "tormia:udp-ticket:consumed:" + fingerprint;
    private static string MotionKey(Guid worldId, Guid avatarEntityId) =>
        $"tormia:world:{worldId:N}:avatar:{avatarEntityId:N}:motion";
    private static string WriterKey(Guid worldId, Guid avatarEntityId) =>
        $"tormia:world:{worldId:N}:avatar:{avatarEntityId:N}:intent-writer";
    private static string PromotedKey(Guid transportSessionId) =>
        $"tormia:udp-transport:active:{transportSessionId:N}";
    private static string ConnectedKey(Guid transportSessionId) =>
        $"tormia:udp-transport:connected:{transportSessionId:N}";
}

internal static class UdpTransportEncoding
{
    public static string Encode(ReadOnlySpan<byte> value) => Convert.ToBase64String(value)
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    public static string Fingerprint(ReadOnlySpan<byte> value) =>
        Convert.ToHexString(SHA256.HashData(value));
    public static bool TryDecode(string? value, int requiredLength, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128) return false;
        var normalized = value.Replace('-', '+').Replace('_', '/');
        if (normalized.Length % 4 == 1) return false;
        if (normalized.Length % 4 != 0)
            normalized += new string('=', 4 - normalized.Length % 4);
        try
        {
            bytes = Convert.FromBase64String(normalized);
            if (bytes.Length == requiredLength) return true;
            CryptographicOperations.ZeroMemory(bytes); bytes = Array.Empty<byte>(); return false;
        }
        catch (FormatException) { return false; }
    }
}

internal sealed record IssueUdpTransportTicketRequest(
    Guid RuntimeSessionId,
    ushort ProtocolVersion)
{
    public bool IsValid => RuntimeSessionId != Guid.Empty &&
        ProtocolVersion == RealtimeWireContract.ProtocolVersion;
}

internal sealed record IssueUdpTransportTicketResponse(
    bool Accepted, string Ticket, string DatagramAuthenticationKey,
    long ExpiresAtUnixMilliseconds, Guid AuthenticatedTransportSessionId,
    Guid RuntimeSessionId, ulong TransportGeneration, ushort ProtocolVersion,
    string WriterMode, long WriterEpoch, long WorldRevision);

internal sealed record PlayerMotionTransportTransitionRequest(
    Guid RuntimeSessionId,
    Guid AuthenticatedTransportSessionId,
    ulong TransportGeneration,
    long ExpectedWriterEpoch,
    long ExpectedWorldRevision)
{
    internal bool IsValid => RuntimeSessionId != Guid.Empty &&
        AuthenticatedTransportSessionId != Guid.Empty &&
        TransportGeneration > 0 && ExpectedWriterEpoch > 0 &&
        ExpectedWorldRevision >= 0;

    internal UdpTransportTicketBinding ToBinding(
        Guid worldId, Guid userId, Guid avatarEntityId, string zoneKey) =>
        new(worldId, userId, avatarEntityId, zoneKey, RuntimeSessionId,
            TransportGeneration, AuthenticatedTransportSessionId,
            RealtimeWireContract.ProtocolVersion);
}

internal sealed record PlayerMotionTransportTransitionResponse(
    bool Accepted,
    string? RejectionCode,
    string WriterMode,
    long WriterEpoch,
    long WorldRevision,
    Guid RuntimeSessionId,
    Guid AuthenticatedTransportSessionId,
    ulong TransportGeneration,
    Guid PreviousAuthenticatedTransportSessionId,
    ulong PreviousTransportGeneration);
