# Remote avatar transport stability

## Decision

- REST traffic and SignalR WebSocket upgrades use separate Nginx locations.
  Normal Authority API requests no longer carry a forced `Connection: upgrade`.
- Bursty `zoneRuntimeChanged` notifications are coalesced into a bounded remote
  avatar snapshot refresh instead of starting one HTTP read per notification.
- One Authority avatar has exactly one active runtime controller session. Local
  Editor and mobile multiplayer tests must use different accounts/avatars.

## Evidence

- Before the proxy split, high-rate HTTPS health probes intermittently ended
  with an empty TLS response. After deployment, 100/100 probes returned 200.
- Runtime logs showed repeated `stale_player_intent` and
  `player_runtime_session_mismatch` while Editor and mobile used the same test2
  avatar. The Editor session was restored to test1.
- The remote snapshot notification coalescing PlayMode test passes.

## Ontology and Authority boundary

The change does not move position authority into Unity. Input remains an
ephemeral intent, server motion remains authoritative, and Unity only reads and
interpolates projected runtime snapshots.

