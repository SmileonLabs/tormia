# First development player loop

- **Date:** 2026-07-22
- **Status:** Implemented for the development Authority flow.

## Decision

The first playable development journey keeps identity and world ownership
separate. A development identity may create one default portable character;
the character's selected parts remain account data. At first world entry the
stable local Avatar GUID is placed as an Authority world entity only when it
does not yet exist, then registered to the authenticated user before character
entry proceeds.

The development `social_village` package publishes immutable `help` and
`talk` action definitions and activates its version for the selected world.
When the Authority connection is active, the quest/action UI submits an
`execute_action` intent instead of writing a local durable action result.

## Verification expectations

- A connected account without a selected character creates a portable default
  character before attempting world entry.
- A missing Avatar entity is placed and registered once; subsequent entry uses
  the same stable GUID.
- A shared-world action is rejected visibly if its Authority entities or
  package definition are unavailable; it does not fall back to a local action.
