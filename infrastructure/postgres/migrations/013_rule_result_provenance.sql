ALTER TABLE world_facts
    ADD COLUMN IF NOT EXISTS source_rule_binding_id uuid
        REFERENCES world_rule_bindings(binding_id);

ALTER TABLE world_facts
    DROP CONSTRAINT IF EXISTS ck_world_facts_rule_result_source;

ALTER TABLE world_facts
    ADD CONSTRAINT ck_world_facts_rule_result_source
    CHECK (
        source_rule_binding_id IS NULL
        OR source_type = 'action'
    );

DROP INDEX IF EXISTS ux_world_facts_active_value;

CREATE UNIQUE INDEX IF NOT EXISTS ux_world_facts_active_value
    ON world_facts (
        world_id,
        subject_entity_id,
        predicate_id,
        object_kind,
        COALESCE(object_entity_id::text, ''),
        COALESCE(object_canonical_id, ''),
        COALESCE(object_value::text, ''),
        COALESCE(source_rule_binding_id::text, '')
    )
    WHERE retracted_revision IS NULL;

CREATE INDEX IF NOT EXISTS ix_world_facts_rule_result
    ON world_facts (world_id, source_rule_binding_id)
    WHERE retracted_revision IS NULL
      AND source_rule_binding_id IS NOT NULL;
