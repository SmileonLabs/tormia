# TOV Player-Facing Product Brand

## Summary

- **Date:** 2026-07-26
- **Owner:** TOV team
- **Status:** Implemented and verified
- **Related request:** Adopt TOV as the product name without renaming the repository or technical contracts

## Intent

Present one consistent TOV brand in Unity builds and user-facing project
documentation while avoiding a high-risk rewrite of stable implementation and
data identifiers.

## Data classification

- [ ] Account profile
- [ ] Durable authored world data
- [ ] Runtime observation
- [ ] Inferred state
- [x] Unity presentation
- [x] Transport / authority / infrastructure

## Decision and boundary

Unity uses product name `TOV`, company `Smileon Labs`, and application
identifier `com.smileonlabs.tov`. User-facing documentation and harness output
use TOV.

The `tormia` folder, `Tormia.*` namespaces/assemblies, scene and script paths,
Docker resources, API/storage prefixes, database identifiers, and canonical
ontology IDs remain stable. No hidden compatibility alias or duplicate data
owner is introduced.

## Ontology expression

No ontology term, Fact, profile, rule, rule block, or package identifier changes.
TOV is a presentation/product label only.

## Verification

| Case | Expected result | Evidence |
| --- | --- | --- |
| Enabled / added | Unity and project-facing documentation display TOV | `ProjectSettings/ProjectSettings.asset`; `README.md` |
| Disabled / removed | Stable `Tormia.*` and durable IDs are not rewritten | Repository identifier audit |
| Regression / edge case | Account/world Authority data resumes after a fresh login | Account/world resume smoke |

## Performance and multiplayer impact

There is no runtime polling or network protocol change. Some platforms use a
new local Unity preference/storage scope after the product/company change, so
credentials are not migrated and a one-time sign-in may be required. Durable
Authority state is unchanged.

## Documentation updated

- [x] `PROJECT_CONTEXT.md`
- [x] `PROJECT_CONTEXT.ko.md`
- [ ] Localization CSV / migration (not required)
- [x] Test scenario evidence
