# Deterministic combat runtime recovery

## Problem

After publishing newer immutable action definitions, historical published
versions also joined the autonomous-actor query. One monster expanded into
multiple candidates and the ambiguity guard removed all of them, stopping chase
and contact damage. Residual colliders on defeated presentations and pose
observations referencing expired locomotion intents also polluted input and
contact evidence.

## Decision

- Select only the highest published action definition version within the active
  package.
- Fail closed for ambiguity only when multiple enabled packages provide a
  complete contract for the same actor.
- Remove defeated targets from all owned colliders and pointer candidates.
- Drive equipment prompts only from projected equipment relations and enabled
  equip/unequip actions.
- Publish collision-resolved poses only while locomotion intent is active.

## Verification

Verify with server unit tests, Unity combat and motion EditMode tests, the
development harness, and the live equip/player-attack/monster-contact loop. The
removed Rule Block or lifecycle-contract paths must remain disabled.
## Follow-up: current player contact evidence

Autonomous melee damage against a player now fails closed unless a fresh
collision-resolved pose observation corroborates the Authority motion contact.
Zero-input locomotion leases are renewed while standing so that this evidence
remains current without making Unity authoritative.
The player-motion configuration query now also reduces immutable locomotion
action history to the newest published version per enabled package.
