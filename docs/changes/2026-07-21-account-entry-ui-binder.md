# Account entry UI binder foundation

## Summary

- **Decision:** Account, character, and world entry UI is hierarchy-owned.
- **Status:** Implemented and verified in Unity Play Mode.
- **Scope:** Unity presentation over account/session data.

## Change

Added `OntologyAccountEntryPanel` as a binder component and placed a disabled
`OntologyAccountEntryPanel` root under `OntologyGameCanvas` in `TormiaMain`.
The binder consumes `OntologyWorldAuthorityAccountEntryFlow`, fills authored
TMP dropdowns/text/buttons, and does not create visual controls or choose
layout values at runtime. Development startup can show the authored panel and
connect the configured local development identity automatically; production
login remains an external identity-provider concern. The status area reports
the selected appearance parts and a player ontology fact count without turning
account data into world facts.

## Verification

- Local World Authority account API: passed (account, one character, one world).
- World entry: passed (avatar registration and character entry accepted).
- Command idempotency: passed (replayed command returned the same revision).
- Docker/PostgreSQL/Redis/World Authority health: passed.
- Account-entry Unity smoke: passed.
- Unity console: no new errors or warnings were introduced.
- Unity Play Mode: panel auto-connected the development account, loaded the
  saved character and world, entered the world, and wrote a snapshot.

The full in-editor EditMode suite currently has five pre-existing unrelated
failures in character-part adapter and placeable semantic-validator tests.
The account-entry compile error was corrected and Unity Console has no current
compile error.

## Next

The authored child hierarchy is now present in `TormiaMain` under the binder
root. Its status block was resized in the scene so account, appearance, and
player-ontology summaries do not overlap the action buttons. The local
account/character/world selection, entry, snapshot save, and subsequent
Play-Mode snapshot load are verified. Production authentication remains a
separate identity-provider integration.
