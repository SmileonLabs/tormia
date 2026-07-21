# Change Record Template

Create a copy of this file in `docs/changes/YYYY-MM-DD-short-name.md` for a
system-level change, a data-policy decision, or a change that crosses Unity,
ontology, server, database, or account boundaries. Create and update the Korean
companion in the same change.

## Summary

- **Date:**
- **Owner:**
- **Status:** Proposed / Implemented / Verified / Reverted
- **Related issue or request:**

## Intent

What player or product outcome is being changed?

## Data classification

- [ ] Account profile
- [ ] Durable authored world data
- [ ] Runtime observation
- [ ] Inferred state
- [ ] Unity presentation
- [ ] Transport / authority / infrastructure

## Decision and boundary

Describe the source of truth. State explicitly what this change must **not**
own or duplicate.

## Ontology expression

List canonical relations, concepts, profiles, rule blocks, and rule outcomes.
Do not put display translations here; link the language-pack change if needed.

## Verification

| Case | Expected result | Evidence |
| --- | --- | --- |
| Enabled / added | | |
| Disabled / removed | | |
| Regression / edge case | | |

## Performance and multiplayer impact

State polling, allocation, database-write, zone-scope, and authority impact.

## Documentation updated

- [ ] `PROJECT_CONTEXT.md`
- [ ] `PROJECT_CONTEXT.ko.md`
- [ ] Localization CSV / migration, if needed
- [ ] Test scenario manifest, if needed
