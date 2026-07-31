# Entity display-name and canonical term coverage

## Summary

- **Date:** 2026-07-30
- **Status:** Implemented; verification recorded below
- **Scope:** Unity presentation, ontology localization registry, editor
  authoring, development harness

## Intent

Keep stable world identity available to Authority and diagnostics while showing
players readable localized names. Make missing canonical concept registration a
development failure instead of silently falling back to a raw English ID.

## Data classification

- Durable entity ID and stored instance name: unchanged world data
- Localized base name and duplicate ordinal: Unity presentation only
- Canonical concept registration: authored ontology vocabulary
- Editor filename suggestion: non-runtime authoring aid

## Decision and boundary

`OntologyEntityDisplayNameResolver` removes only recognized generated suffixes
from presentation. Numeric placement ordinals and eight-character Authority
GUID prefixes are recognized; arbitrary suffixes and user-authored names are
preserved. No saved identifier, Fact, or Authority command is rewritten.

`Damageable`, `Monster`, `Item`, `Sword`, and `Weapon` are registered as
canonical Concepts with English/Korean labels and aliases. Project coverage now
walks profiles, templates, Rule Block presets, rule conditions/effects, and
catalog semantic migrations in reverse, reporting every referenced concept
that is absent or registered with the wrong kind.

The catalog editor can suggest missing display metadata from a prefab filename,
but it fills blanks only and never assigns gameplay meaning.

## Verification

| Case | Expected result | Evidence |
| --- | --- | --- |
| Generated entity name | Localized base name is shown without GUID/ordinal | `EntityDisplayNameAndCanonicalTermCoverageContract` |
| User-authored name | Name is preserved exactly | `EntityDisplayNameAndCanonicalTermCoverageContract` |
| Missing concept | Coverage helper reports the missing canonical Concept | `MissingCanonicalConceptReferenceFailsCoverageValidation` |
| Registered combat concepts | English/Korean labels resolve while canonical IDs remain unchanged | `RegisteredCombatConceptsHaveLocalizedLabels` |

Final Unity test and harness results are recorded in the completion report.

## Updated documents

- `docs/PROJECT_CONTEXT.md`
- `docs/PROJECT_CONTEXT.ko.md`
- `tests/harness/core-regression-scenarios.json`
- Ontology localization CSV files
