# Explicit motion-writer transitions

## Decision

Replace packet-driven UDP writer promotion with authenticated, explicit and
idempotent Authority transitions. Runtime activation starts with an HTTP writer;
ticket issue and redemption do not change it.

## Server contract

- UDP promotion: `POST /v1/worlds/{worldId}/runtime/avatars/{avatarId}/transport/udp/promote`
- HTTP fallback: `POST /v1/worlds/{worldId}/runtime/avatars/{avatarId}/transport/http/fallback`
- Both requests carry the exact runtime session, authenticated transport
  session, generation, expected writer epoch, and expected world revision.
- HTTP motion intent carries `writerEpoch`. UDP resolves the epoch from the
  exact active writer binding.
- Redis Lua and the in-memory equivalent atomically fence writer mode, epoch,
  runtime, revision, generation, promoted binding, and stale intent lease.

## Ticket reissue and connected-presence policy

Ticket issue fails with `udp_rekey_requires_fallback` while UDP is active. A
client must explicitly fall back to HTTP before requesting a fresh ticket;
Stage 11 does not permit a direct UDP-to-UDP writer swap. Redeemed handshake
proof and connected-peer presence are separate records. Disconnect revokes only
presence, while fallback atomically validates the exact current writer and
advances the generation fence. Datagram admission requires exact connected
presence, so queued packets fail after disconnect or fallback.

Fallback uses the current Authority revision after validating the exact current
runtime, binding and writer epoch. A world edit after UDP promotion therefore
does not permanently block reliable recovery. If promotion failed and HTTP is
still current at the expected epoch, fallback cleans candidate transport state
and returns the unchanged authoritative HTTP writer as an idempotent success.
Idempotent success and rejection writer evidence require exact world, user,
avatar, Zone and runtime scope; the HTTP layer does not perform an unfiltered
writer override.

## Ontology boundary

The state machine owns transport metadata only. Locomotion permission still
comes from authored Triples, assigned Rule Blocks, Authority evaluation, and the
fixed-tick motion runtime. No transition creates a Fact or gameplay result.

## Evidence

- Docker server publish build: passed.
- World Authority test suite: 267 passed, 0 failed on 2026-08-04.
- Harness scenario: `hybrid-udp-single-writer-transition`.
