# Fixed-tick Authority player motion

Date: 2026-07-31

## Decision

Shared player motion advances in World Authority at a 50 ms fixed tick from
canonical intent, evaluated runtime action occurrences, authored movement
tuning, Zone bounds, and authored collision proxies. A Unity collision-resolved
pose is an ordered ephemeral observation and never overwrites Authority state.

## Production line

`input trigger -> canonical ephemeral intent/action -> authored Triples and
assigned Rule Block -> evaluated action occurrence -> fixed-tick Authority
integration -> authored collision proxy/Zone resolution -> shared motion
projection -> Unity prediction and presentation`

## Enabled evidence

- Direction is normalized and requested speed is capped by
  `movement_speed`.
- The full player Capsule stays inside the Zone and cannot pass through an
  authored `DynamicProp`.
- An accepted occurrence matching authored `jump_action` uses authored
  takeoff, gravity, and ground-stick tuning and lands once.

## Removed and tamper evidence

- Removing required movement, jump, or collision semantics fails closed.
- Stale pose observations are rejected.
- A client pose observation cannot overwrite Authority motion.

## Deferred boundary

Server-side `WalkableSupport` elevation/height-field resolution and smooth local
prediction reconciliation are a later phase. The durable checkpoint Y is the
current vertical ground reference, so local Transform correction is not enabled
against an incomplete support model.
