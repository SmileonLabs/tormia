# Authority attack occurrence pipeline

## Decision

Autonomous attacks are no longer an immediate damage side effect of each scheduler tick. World Authority previews the immutable attack action through the actor's assigned Rule Block, creates one ephemeral `AttackOccurrence`, and advances it through `windup -> contact -> recovery` using authored `attack_windup_seconds` and `attack_recovery_seconds` Triples.

Player melee follows the same boundary. Input requests an ephemeral occurrence; Unity reports a projected contact candidate only inside the authored contact window; Authority validates current semantic eligibility and capsule contact, consumes the occurrence once, and only then invokes the immutable damage action and its assigned Rule Block. Direct generic execution of an occurrence-required action fails closed.

At contact, Authority executes the same immutable action and assigned Rule Block again. This revalidates current eligibility, life state, hostility, runtime position, range, and cooldown before applying damage once. Accepted durable changes publish an immediate revision notification; Unity presents the projected animation and health result and does not calculate damage.

## Removed path

Removing the autonomous or player attack Rule Block, its action link, timing/contact data, target eligibility, faction relation, or collision contract removes the corresponding attack/contact result. There is no renderer-, prefab-, animation-, scheduler-, or Unity-damage fallback.

## Evidence

- `AutonomousActorOntologyContractTests`
- `PlayerAttackOccurrenceRuntimeTests`
- `OntologyAutonomousMonsterContractTests`
- `OntologyCombatVerticalSliceAssetTests`
- `monster-ontology-production` and `weapon-ontology-production` harness contracts
