# Authority UDP realtime listener

## Decision

Add a default-disabled LiteNetLib listener that authenticates and fences motion
datagrams before they can enter an isolated bounded intent channel. Do not
connect that channel to gameplay until the Authority movement integration stage.

## Boundaries

- HTTPS owns ticket issuance and SignalR/HTTP own fallback and recovery.
- UDP owns no ontology rule, gameplay result, durable fact, or identity.
- Exact current generation, HMAC and replay validation fail closed.
- Only `Unreliable` channel 0 is accepted for motion intent.

## Evidence

- `AuthorityUdpRealtimeListenerTests`: protocol, proof, replay, generation and
  capacity tests.
- `AuthorityUdpRealtimeListenerIntegrationTests`: real localhost LiteNetLib
  handshake, delivery filter, malformed/rate-limit rejection and shutdown.
- Server suite: 212 passed, 0 failed on 2026-08-04.
