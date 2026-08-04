# Ontology runtime performance evidence

## Summary

- **Date:** 2026-08-02
- **Owner:** TOV multi-agent performance audit
- **Status:** implemented and partially verified
- **Request:** Measure actual ontology runtime amplification before adding incremental evaluation or runtime-contract caches.

## Intent and ownership

The change adds runtime-observation counters only. Authored Triples, assigned
Rule Blocks, inferred results, durable world state, and Unity presentation
ownership remain unchanged. Projection indexing stores only rows already
approved by Authority and cannot infer missing permission.

## Decisions

- Measure rule evaluation, headless input/compile work, and Projection apply work.
- Honor the existing active/reduced Zone schedule instead of evaluating reduced Zones every second.
- Index one received Projection by entity/subject to remove repeated full-array scans.
- Do not add a persistent runtime-contract cache until snapshot and compilation measurements include revision/package invalidation evidence.

## Verification

| Case | Expected | Evidence |
| --- | --- | --- |
| Core scale | Counters report stable evaluation without changing results | Explicit EditMode measurement: 1,000 entities, 100 rules, 100,100 Facts; evaluation 0.426 ms; complete test 286 ms |
| Projection replacement | Old indexed rows are unavailable | `OntologyAuthorityProjectionIndexTests` |
| Reduced Zone | Runs at five-second boundary, not every scheduler loop | `WorldZoneSimulationSchedulePolicyTests` |
| Server regression | Existing Authority behavior remains valid | 92 server tests passed |

## Remaining evidence gate

Measure action snapshot rows/time, rule/action deserialization and compilation,
cache hit/miss, Projection bytes/apply time, and revision/package invalidation
before implementing a persistent runtime-contract cache or persistent headless
evaluation context.

## Second measurement and improvement

- Action metrics use bounded tags and exclude raw UGC Action IDs.
- Representative 100,000-Fact / 20-Action evidence measured 8,495 ms before
  request-scoped reuse and 6,236 ms after it.
- Pure evaluation p95 fell from 6,383 ms to 27.998 ms; per-request snapshot
  compilation remains the dominant p50 cost at about 3,498 ms.
- Headless input now uses one Repeatable Read source revision and discards stale
  inferred output.
- Persistent revision-wide caching remains deferred pending a non-mutating
  matcher overlay and a multi-instance publication fence.

## Third measurement and safety boundary

- Canonical intent matching now uses a non-mutating request overlay; concurrent
  evaluators neither mutate the base snapshot nor serialize through a matcher
  lock.
- Primary and post-Rule effects share one command-scoped provenance-aware
  overlay. Post-Rules observe prior successful mutations in order without a
  full snapshot reload.
- At 100,000 Facts, post-Rule counts 0/1/4 all recorded one prepare load and
  zero post-Rule reloads. Warmed snapshot compilation measured
  610.753/422.798/562.736 ms and the four-rule chain measured 0.210 ms.
- Headless publication now rejects stale multi-instance results through an
  atomic revision/completion/observation compare-and-set fence and a final
  PostgreSQL revision check.
- Full server regression passed 122/122. The next performance candidate is an
  exact-revision immutable contract cache. A cooldown reservation-cancel API
  remains a correctness follow-up for the rare post-acquisition commit failure.

## Fourth measurement: exact-revision compiled contract cache

- The cache key is world ID + exact revision + evaluator schema version + a
  canonical SHA-256 enabled-content manifest. Build key and Fact rows are read
  in one dedicated Repeatable Read database snapshot.
- Cached contracts are immutable. Command mutations use key-local raw-row
  deltas and semantic additions/tombstones instead of cloning the complete
  world. Rule and package removal therefore produces a new exact key and fails
  closed without an older cache fallback.
- Same-key requests, including overflow keys, use one cancellation-independent
  build. Resident capacity, pending keys, concurrent builds, and estimated
  retained bytes are bounded; excess pending keys fail closed with backpressure.
  Metrics contain no unbounded UGC identifiers.
- At 100,000 Facts: cold compile 464.556 ms, cache-hit read 15.742 ms, first
  committed delta mutation 4.902 ms. Server regression passed 136/136.

## Fifth boundary: service lifetime and retained memory

- Shared builds use the host stopping token and a configurable 30-second owner
  timeout. Request cancellation remains waiter-local, while database reads and
  long compilation/estimation loops cooperatively observe owner cancellation.
- Retained-memory accounting now includes the compiled world indexes and raw
  provenance containers with saturating arithmetic. Oversized contracts are
  returned once but never admitted as residents.
- MeterListener regression evidence covers resident entries/bytes, overflow
  pending keys, oversized bypass, timeout cleanup, and disposal during an
  active build. Full server regression passed 149/149.
