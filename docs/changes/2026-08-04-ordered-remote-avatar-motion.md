# Ordered remote-avatar motion transport

## Decision

Replace latest-position chasing with an Authority-timed remote snapshot stream.
The server publishes changed player states as ordered Zone motion frames;
clients buffer and interpolate them while HTTP remains recovery-only.

## Boundaries

- Authority fixed-tick motion remains the only shared position owner.
- SignalR carries ephemeral evaluated results, never authored Facts.
- Unity rejects stale session/tick data and never predicts a destination.
- Delta frames cannot remove an avatar; complete recovery snapshots own presence
  reconciliation.

## Evidence

- Server Docker publish succeeds.
- Player motion and motion-frame policy tests pass.
- Unity compiles without new Console errors; remote buffer tests cover ordering,
  interpolation, bounded extrapolation, session rollover, and long gaps.
