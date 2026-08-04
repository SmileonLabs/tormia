# Authority UDP ticket admission

## Decision

UDP transport admission starts on the authenticated HTTPS control lane. The
Authority issues a short-lived opaque ticket and a separate datagram key only
for an account-owned avatar with an active runtime session. The server assigns
the transport generation; the client cannot choose it.

Redeeming a ticket requires a nonce and domain-separated HMAC proof. Invalid
proof does not consume the ticket. Redis atomically revalidates the current
runtime session and Zone, consumes the exact ticket once, and promotes a short
authenticated transport handshake. Datagram keys are AES-GCM protected at rest
with an explicit key ID.

The feature is disabled by default. Production admission requires Redis,
credential protection, HTTPS through an explicitly trusted proxy, and the
multi-instance store. Deactivation revokes pending tickets, generation state,
and promoted transport sessions. This transport state is ephemeral and authors
no Triple, Rule Block, gameplay result, or durable world event.

## Remaining gate

No UDP socket is enabled by this change. The listener must still prove bounded
`RejectForce`, authentication-before-payload-decode, replay-window enforcement,
and single-writer ownership before UDP can carry motion.

