ALTER TABLE world_facts
    ADD COLUMN IF NOT EXISTS rule_result_lifetime text
        NOT NULL DEFAULT 'rule_bound';

ALTER TABLE world_facts
    DROP CONSTRAINT IF EXISTS ck_world_facts_rule_result_lifetime;

ALTER TABLE world_facts
    ADD CONSTRAINT ck_world_facts_rule_result_lifetime
    CHECK (
        rule_result_lifetime IN ('rule_bound', 'durable_state')
        AND (
            rule_result_lifetime = 'rule_bound'
            OR source_type = 'action'
        )
    );

CREATE INDEX IF NOT EXISTS ix_world_facts_retractable_rule_result
    ON world_facts (world_id, source_rule_binding_id)
    WHERE retracted_revision IS NULL
      AND source_rule_binding_id IS NOT NULL
      AND rule_result_lifetime = 'rule_bound';
