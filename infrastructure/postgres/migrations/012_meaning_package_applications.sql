-- A meaning package is one atomic, revisioned authoring operation that owns the
-- Triple + Rule Block + Physical Meaning contribution it adds to one entity.
-- The ledger preserves displaced authored rows so removing/replacing a package
-- can restore the previous baseline without object/prefab-specific fallbacks.
CREATE TABLE IF NOT EXISTS world_meaning_package_applications
(
    application_id          uuid PRIMARY KEY,
    world_id                uuid NOT NULL REFERENCES worlds(world_id) ON DELETE CASCADE,
    target_entity_id        uuid NOT NULL REFERENCES world_entities(entity_id),
    slot_id                 text NOT NULL,
    package_id              text NOT NULL,
    package_spec            jsonb NOT NULL,
    inserted_fact_ids       jsonb NOT NULL DEFAULT '[]'::jsonb,
    inserted_binding_ids    jsonb NOT NULL DEFAULT '[]'::jsonb,
    displaced_facts         jsonb NOT NULL DEFAULT '[]'::jsonb,
    displaced_bindings      jsonb NOT NULL DEFAULT '[]'::jsonb,
    applied_revision        bigint NOT NULL,
    removed_revision        bigint,
    created_at              timestamptz NOT NULL DEFAULT now(),
    CHECK (removed_revision IS NULL OR removed_revision >= applied_revision)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_world_meaning_package_active_slot
    ON world_meaning_package_applications(world_id, target_entity_id, slot_id)
    WHERE removed_revision IS NULL;

CREATE INDEX IF NOT EXISTS ix_world_meaning_package_active_target
    ON world_meaning_package_applications(world_id, target_entity_id)
    WHERE removed_revision IS NULL;
