# Commercial readiness audit baseline

## Scope

This audit covers the current Unity/World Authority boundary before adding
more gameplay content.

## Verified safe boundaries

- Durable world edits use revisioned, idempotent commands.
- Runtime movement input is throttled and transient; it is not written as a
  durable Fact or world event per frame.
- Zone directory and projection refreshes are interval-based; realtime
  notifications are an optimization, not the source of truth.
- Animation and physics adapters react to ontology state changes and do not
  add a hidden object-name rule.
- Viewer authoring is gated on the client and rejected by the server.

## Remaining production gates

1. Run a real .NET server build in the provisioned SDK/Docker environment.
2. Execute a concurrent-client load test for command conflicts, zone polling,
   and transient intent leases.
3. Capture Unity Profiler baselines for 1/20/100 placed objects and remote
   avatars; record allocations and frame time.
4. Add production authentication, rate limits, metrics, structured audit
   logging, backups, and deployment secrets before launch.

No gameplay fallback was added by this audit. The listed items are release
gates, not reasons to bypass the ontology or authority boundary.
