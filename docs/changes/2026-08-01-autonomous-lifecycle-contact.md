# Autonomous lifecycle and melee contact

## Decision

Autonomous combat now gates runtime behavior with the current living Authority
projection and resolves melee contact from authored capsule geometry rather
than broad center-to-center action range.

## Production path

`life/collision Triples -> assigned attack Rule Block -> Authority occurrence
preview -> authored capsule contact -> Rule evaluation -> durable damage or
defeat -> immediate ephemeral lifecycle eviction -> Unity presentation`

## Evidence

- Contact fails closed for missing or separated authored geometry.
- Cached actors absent from the current living projection are evicted.
- Contact re-previews the Rule Block and current live position before damage.
- Server regression and development-harness evidence cover enabled and removed
  paths.
- Semantic baseline upgrades exclude mutable health/alive state and normalize
  legacy conflicts without converting a defeated entity back to alive.
