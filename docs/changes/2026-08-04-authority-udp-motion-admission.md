# Authority UDP motion admission

## Decision

Route HTTP and authenticated UDP motion through one Rule-evaluated Authority
admission and one atomic single-writer intent registry.

## Integrity and performance

- The transport binding, runtime session, world revision and writer generation
  are verified atomically at publish time.
- Rule changes use a pre-commit pending fence and post-commit exact revision.
- Crash reconciliation and contract builds are single-flight and bounded.
- Cached contracts are revision-scoped results, not a fallback rule engine.

## Evidence

- Rule removal, revision invalidation, stale session/generation, duplicate
  HTTP/UDP sequence, writer promotion, pending commit and cache pressure tests.
- Full World Authority suite: 235 passed, 0 failed on 2026-08-04.
