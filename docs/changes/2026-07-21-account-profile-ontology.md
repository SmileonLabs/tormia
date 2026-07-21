# Account-owned player profile ontology

## Summary

- **Decision:** Persist each character's durable, account-owned ontology
  relations separately from the shared world Fact graph.
- **Status:** Implemented and locally verified.
- **Scope:** Account profile, durable account data, authority API, and Unity
  account-entry integration.

## Data ownership

`player_characters.profile_relations` stores canonical English triples such as
`Player → has_skill → Build`, `Player → owns → Pet_01`, or
`Player → carries → Item_01`. These are character profile data: they are not
world Facts, observations, inferred states, or a replacement for a world rule.

The profile payload is deliberately generic. A later pet, inventory, or skill
feature can consume the same relationships through its own rule and Unity
adapter contract without adding prefab-name or mesh-name exceptions.

## Authority contract

`PUT /v1/account/characters/{characterId}` accepts the complete character
profile snapshot with a command ID and expected profile revision. The authority
checks account ownership, uses an optimistic revision check, and records the
command ID. Replaying the same command returns the original revision rather
than applying the profile a second time.

Unity uses the World Authority client only. It never communicates with
PostgreSQL or Redis directly. `OntologyWorldAuthorityAccountEntryFlow` can now
save the currently equipped character parts through this contract; visual
presentation remains Unity-owned.

## Verification

| Case | Result | Evidence |
| --- | --- | --- |
| Add profile relation | Passed | `Player → has_skill → Build` persisted and profile revision advanced 1 → 2. |
| Replay identical command | Passed | Same command returned revision 2 with `isReplay=true`; no duplicate relation. |
| Remove profile relation | Passed | A new revision removed the relationship; dashboard returned an empty collection. |
| Local services | Passed | PostgreSQL, Redis, and World Authority healthy after migration `005_account_profile_ontology.sql`. |
| Unity compile | Passed | Unity refresh completed with no new Console errors. |

## Follow-up

This is the data foundation, not the final player-facing flow. The later
character-selection, appearance-preview, profile-ontology, and world-entry
screens will bind to this contract using authored hierarchy UI.
