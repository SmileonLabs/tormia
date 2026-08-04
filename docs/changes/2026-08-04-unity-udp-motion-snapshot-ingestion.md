# Unity UDP motion snapshot ingestion

## Decision

Receive authenticated protocol-v2 UDP motion pages into the existing
transport-neutral Authority motion feed without enabling the UDP input writer.

## Boundaries

- Bootstrap frame version 1 and realtime wire version 2 are distinct.
- Only a complete bounded page set is published.
- SignalR/HTTP establish runtime-session barriers; UDP only advances an already
  confirmed actor session.
- `FrameOccurrenceId` deduplicates lanes and per-actor Authority ticks order
  states. Unity transport never changes gameplay state or Transform directly.

## Verification

- Development harness passed.
- Unity script validation and recompile reported no new C# errors.
- Independent review found no P0/P1 after the recovery-barrier fixes.
- PlayMode execution is pending because the MCP runner is stuck on a previous
  zero-progress `tests_running` job. This is recorded as unverified, not passed.

## Disabled and failure path

Default-off receiver, invalid authentication or binding, replay, missing or
inconsistent page, unknown actor session, stale HTTP recovery, disable, or
scope change publishes no UDP frame. SignalR and HTTP remain available.
