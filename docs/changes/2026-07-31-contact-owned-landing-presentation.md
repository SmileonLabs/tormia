# Contact-Owned Landing Presentation

Date: 2026-07-31

## Decision

Nearby support observation and physical ground contact are separate transient
signals. Landing presentation requires authored `WalkableSupport` plus the
CharacterController grounded/`Below` collision result. Airborne and Fall use
separate canonical Manifest entries, and Landing blends to the authored base
intent only after its authored segment completes.

## Ontology boundary

- Jump permission remains owned by the immutable jump action and assigned Rule
  Block.
- Vertical velocity and collision contact are ephemeral observations.
- Animation intent and playback segments are Manifest-authored presentation
  data.
- Unity selects presentation only; it creates no gameplay permission or
  durable movement Fact.

## Removed path

Support proximity cannot manufacture landing. Removing an Airborne, Fall, or
Landing Manifest contract fails validation rather than selecting Idle through
a clip-name, fixed-timer, or object-name fallback.

## Existing-character repertoire synchronization

The runtime world can be replaced after an ActorProfile initially publishes
its presentation repertoire. Profile synchronizers now republish their
`ActorProfile`-origin contributions on `WorldRebuilt`. Removing a profile
animation retracts that contribution, so existing characters receive newly
registered Manifest content without a stale or name-based fallback.
