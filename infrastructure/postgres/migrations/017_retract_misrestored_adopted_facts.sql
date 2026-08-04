-- Before adoption was ordered ahead of predicate replacement, an exact
-- catalog contribution could be captured as displaced and later restored as
-- independent data when the final owned Rule Block closed its package.
-- Retract only those restored rows that (1) came from such a removed package,
-- (2) exactly match both its displaced snapshot and desired package content,
-- and (3) are not currently owned by another active package.
WITH removed_adopting_packages AS
(
    SELECT application_id, world_id, target_entity_id, removed_revision,
           package_spec, displaced_facts
    FROM world_meaning_package_applications
    WHERE removed_revision IS NOT NULL
      AND COALESCE(
          (package_spec->>'adoptExistingContributions')::boolean,
          false)
),
desired_facts AS
(
    SELECT package.application_id,
           authored.value AS desired
    FROM removed_adopting_packages package
    CROSS JOIN LATERAL jsonb_array_elements(
        COALESCE(package.package_spec->'authoredFacts', '[]'::jsonb))
        authored(value)
    UNION ALL
    SELECT package.application_id,
           jsonb_build_object(
               'predicateId', 'has_concept',
               'objectKind', 'canonical',
               'objectEntityId', NULL,
               'objectCanonicalId', concept.value,
               'objectValueJson', NULL)
    FROM removed_adopting_packages package
    CROSS JOIN LATERAL jsonb_array_elements_text(
        COALESCE(package.package_spec->'requiredConceptIds', '[]'::jsonb))
        concept(value)
),
misclassified_snapshots AS
(
    SELECT DISTINCT package.application_id, package.world_id,
           package.target_entity_id, package.removed_revision,
           displaced.value AS snapshot
    FROM removed_adopting_packages package
    CROSS JOIN LATERAL jsonb_array_elements(package.displaced_facts)
        displaced(value)
    INNER JOIN desired_facts desired
        ON desired.application_id = package.application_id
       AND desired.desired->>'predicateId' =
           displaced.value->>'predicateId'
       AND desired.desired->>'objectKind' =
           displaced.value->>'objectKind'
       AND desired.desired->>'objectEntityId' IS NOT DISTINCT FROM
           displaced.value->>'objectEntityId'
       AND desired.desired->>'objectCanonicalId' IS NOT DISTINCT FROM
           displaced.value->>'objectCanonicalId'
       AND desired.desired->>'objectValueJson' IS NOT DISTINCT FROM
           displaced.value->>'objectValueJson'
),
misrestored_facts AS
(
    SELECT fact.fact_id, snapshot.removed_revision
    FROM misclassified_snapshots snapshot
    INNER JOIN world_facts fact
        ON fact.world_id = snapshot.world_id
       AND fact.subject_entity_id = snapshot.target_entity_id
       AND fact.created_revision = snapshot.removed_revision
       AND fact.retracted_revision IS NULL
       AND fact.source_type = 'authored'
       AND fact.predicate_id = snapshot.snapshot->>'predicateId'
       AND fact.object_kind = snapshot.snapshot->>'objectKind'
       AND fact.object_entity_id::text IS NOT DISTINCT FROM
           snapshot.snapshot->>'objectEntityId'
       AND fact.object_canonical_id IS NOT DISTINCT FROM
           snapshot.snapshot->>'objectCanonicalId'
       AND fact.object_value::text IS NOT DISTINCT FROM
           snapshot.snapshot->>'objectValueJson'
    WHERE NOT EXISTS
    (
        SELECT 1
        FROM world_meaning_package_fact_ownership ownership
        WHERE ownership.fact_id = fact.fact_id
          AND ownership.released_revision IS NULL
    )
)
UPDATE world_facts fact
SET retracted_revision = repair.removed_revision
FROM misrestored_facts repair
WHERE fact.fact_id = repair.fact_id;
