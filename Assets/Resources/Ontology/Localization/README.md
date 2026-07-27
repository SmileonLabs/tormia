# TOV Ontology Language Pack

These CSV files control what players see and what localized words they may enter.
They do not change the identifiers used by the rule engine.

## Files

- `Localization_en.csv`: English UI labels, term labels, and readable fact sentences.
- `Localization_ko.csv`: Korean equivalents using the same keys.
- `OntologyTerms.csv`: Canonical ontology identifiers, term type, and relation value type.
- `OntologyAliases.csv`: Localized input words that resolve to canonical identifiers.
- `OntologyMigrations.csv`: Old IDs that must be converted while loading saved worlds.

## Identifier rules

- Rules and saves use the `canonical_id` from `OntologyTerms.csv`.
- Canonical concept/state/value IDs use `PascalCase`, such as `WaterRegion`.
- Canonical relation IDs use `snake_case`, such as `water_depth`.
- User input is matched without English letter-case sensitivity.
- A successful match always returns the exact canonical spelling.
- Display translations must never be written directly into rule or save data.
- Free user text and entity instance names are not translated or case-normalized.

## Adding a term

1. Add one canonical row to `OntologyTerms.csv`.
2. Add its English and Korean label using the row's `label_key`.
3. Add accepted Korean/English input words to `OntologyAliases.csv`.
4. Open **Tools > Ontology > Language Pack Validator**.
5. Select **Reload and Validate** and resolve every warning.

Do not give the same alias to multiple canonical terms. If an existing canonical ID
must be renamed after saves exist, add a migration row instead of silently changing it.
