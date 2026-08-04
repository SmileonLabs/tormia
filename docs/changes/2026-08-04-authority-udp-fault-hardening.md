# Authority UDP deterministic fault hardening

Date: 2026-08-04

## Decision

Stage 12 keeps transport faults outside gameplay authority and proves the
existing UDP path with deterministic server tests. Production code gains only
a bounded consumer drain seam, precise low-cardinality diagnostics, atomic
expired-ticket consumption, and a configured bounded Redis generation fence.

The complete tested path is:

`authenticated datagram -> replay/generation fence -> bounded consumer ->
canonical motion admission -> writer/revision fence -> ephemeral intent
registry`

## Failure policy

- Loss does not synthesize a missing sequence.
- Reorder may deliver within the replay window but cannot rewind the registry.
- Replay and too-old packets never enter canonical admission.
- A packet queued before fallback or generation revocation cannot commit later.
- The first expired ticket use returns `Expired` and consumes the credential;
  subsequent use returns `AlreadyRedeemed`.
- Snapshot paging, bounded drop, and backplane failure remain replaceable
  presentation behavior and are covered by the existing snapshot integration
  suite.

## Redis integration policy

Redis Lua integration tests run only when `TORMIA_TEST_REDIS_CONNECTION` is
set and explicitly selects an isolated non-default Redis database with
`defaultDatabase=1` or greater. Each test creates unique world, user, avatar,
runtime, and transport IDs, opens two independent connections, and deletes
only the exact keys it created. Database-wide flush and unrelated key
enumeration are forbidden.

## Evidence

- `AuthorityUdpMotionIntentConsumerFaultTests`: loss, reorder, replay,
  too-old, queued-after-fallback, and diagnostic reason preservation.
- `UdpTransportTicketStoreTests`: in-memory expired ticket parity.
- `RedisUdpTransportIntegrationTests`: cross-instance ticket lifecycle,
  expiry/replay parity, simultaneous duplicate redemption, exact multi-chunk
  generation checks, concurrent reissue/promotion generation fencing, and
  concurrent submit/fallback final-writer fencing.
- `UdpTransportTicketStoreTests.RedisGenerationLookupChunking_BoundsEveryLuaInvocation`:
  a 10,001-binding request never creates a Lua chunk larger than the configured
  64-binding cap.
- `AuthorityUdpMotionSnapshotIntegrationTests`: deterministic paging, bounded
  queue/drop, and backplane failure isolation.
- `AuthorityUdpRealtimeListenerIntegrationTests`: a fresh-generation reconnect
  succeeds while the superseded bootstrap is rejected before dataplane input.

No Triple, Rule result, durable event, movement permission, or Unity transform
is created by these transport tests or seams.
