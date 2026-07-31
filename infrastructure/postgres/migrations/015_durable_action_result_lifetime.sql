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
