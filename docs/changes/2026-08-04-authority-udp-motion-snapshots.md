# Authority UDP motion snapshots

## Decision

Publish replaceable UDP Zone motion snapshots only from the canonical
Authority fixed-tick frame. Keep HTTP and SignalR as the complete control,
fallback, and recovery paths.

## Boundaries

- UDP owns no Rule evaluation, gameplay state, or persistence.
- SignalR and UDP share one source `FrameOccurrenceId`; Redis is only the UDP
  backplane and never rewrites frame identity or a distributed scalar tick.
  Its failure cannot stop Authority simulation or SignalR.
- Snapshot ingress, retained bytes, item count, datagram size, recipients, and
  generation lookups are bounded.
- Accepted changed-actor occurrences remain FIFO. A full count/byte-bounded
  queue drops the newest occurrence and never replaces an earlier Zone delta.
- Exact generation, local session, HMAC, and server packet sequence are checked
  before Unreliable channel 0 transmission.

## Evidence

- `AuthorityUdpMotionSnapshotIntegrationTests` covers authenticated pagination,
  occurrence replay, FIFO delta retention, and drop-newest saturation.
- Full `Tormia.WorldAuthority.Tests`: 254/254 passed in .NET 8 Docker.
- Docker server publish build succeeded.

## Removed or failed path

Disabled UDP, stale generation, missing peer, malformed or oversized frame,
backplane failure, Redis timeout, exact occurrence replay, and queue saturation
fail closed for UDP while the canonical Authority and SignalR paths continue.
