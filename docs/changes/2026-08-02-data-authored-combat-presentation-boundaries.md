# Data-authored combat presentation boundaries

## Summary

- Date: 2026-08-02
- State: Verified
- Scope: Unity presentation and input boundaries

## Intent

Remove input, collision-query, and animation-selection assumptions that made
combat presentation depend on a fixed key, arbitrary Collider order, fixed
query capacity, or a weapon template ID.

## Ownership

- Input Action asset owns device bindings, not gameplay permission.
- Authored collision Triples own the combat query proxy dimensions.
- Assigned Rule Blocks and World Authority continue to own equip and damage.
- Projected animation-intent Facts own equipped locomotion presentation.

## Verification

| Case | Expected result |
| --- | --- |
| Complete proxy Triples | Query-only capsule is materialized |
| Missing/ambiguous proxy Triple | Damageable presenter fails closed |
| Contact query exceeds fast buffer | Complete query retries without omission |
| Equipped entity projects idle/move intent | Matching intent is selected |
| Intent Fact removed | No catalog/template fallback remains |

Evidence: development harness passed; focused combat EditMode tests passed
55/55; focused semantic adapter PlayMode tests passed 2/2; Unity Console had
no errors after compilation and tests.
