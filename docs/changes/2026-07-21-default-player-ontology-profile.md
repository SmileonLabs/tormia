# Default Player Ontology Profile

## Decision

The minimum player ontology is declared by the account/profile asset, not by
mesh names or hidden Unity fallbacks. The profile now owns canonical default
concepts and optional authored facts. Existing animation capability projection
is retained for compatibility while the same values are also exposed through
the canonical `has_capability` relation.

## Scope

- Account/profile data: `OntologyActorProfile.defaultConcepts` and
  `defaultFacts`.
- World projection: the synchronizer publishes those values when the actor
  enters the ontology world.
- The scene Player object no longer owns the baseline `Agent`,
  `PlayerControlled`, and `Humanoid` concepts; `Creature` remains scene-authored
  because it is not a universal player baseline.
- `ontologyCapabilities` owns gameplay capabilities such as `Locomotion` and
  `Interaction`; the existing `capabilities` list remains an animation setup
  compatibility field and is projected only as `animation_capability`.
- Runtime observations and inferred facts remain separate and are not stored
  in the profile.

## Verification

- `scripts/verify-development.ps1 -SkipServerBuild`: passed.
- Unity harness wrapper currently stops before running tests because it reads
  an unset PowerShell `$LASTEXITCODE`; this is a pre-existing harness-script
  issue and must be fixed before treating Unity test results as green.
