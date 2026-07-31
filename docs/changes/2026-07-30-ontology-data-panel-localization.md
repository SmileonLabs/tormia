# Ontology data-panel localization

## Summary

- **Date:** 2026-07-30
- **Owner:** TOV development
- **Status:** Implemented and verified
- **Request:** Remove untranslated English from the ontology data-panel tabs
  without localizing canonical stored IDs or adding per-object UI exceptions.

## Intent

Keep canonical ontology IDs language-neutral while making every catalogued
relation, value, rule variable, physical choice, and static data-panel control
readable in the selected display language.

## Data ownership

- `OntologyTerms.csv` owns the canonical ID to display-key mapping.
- `Localization_en.csv` and `Localization_ko.csv` own display text and readable
  fact templates.
- `OntologyRuntimeWorldFactEditorPanel` binds hierarchy-authored controls to
  language keys. It does not own translated Korean strings or gameplay meaning.
- Known generated instance-name suffixes resolve to the catalog display key
  without changing identity. User-authored names and free text remain unchanged.

## Verification

| Case | Expected | Evidence |
| --- | --- | --- |
| Korean or English enabled | Static controls, durable relations, values, rule variables, and physical choices resolve through language keys | `OntologyWorldFactEditorLocalizationTests` |
| Missing or duplicate entry | CSV validation fails instead of silently shipping an English fallback | `OntologyLanguagePackServiceTests.ShippedCsvFilesValidateWithoutWarnings` |
| Canonical persistence | Display language changes labels only and never rewrites saved IDs | Existing language-pack canonical-ID tests |
| Generated duplicate name | `BeholderBasic_c267940a_Copy` displays through `placeable.BeholderBasic`, while an authored name remains intact | `OntologyLanguagePackServiceTests.EntityDisplayNameAndCanonicalTermCoverageContract` |

## Updated documents

- `docs/PROJECT_CONTEXT.md`
- `docs/PROJECT_CONTEXT.ko.md`
- `tests/harness/core-regression-scenarios.json`
