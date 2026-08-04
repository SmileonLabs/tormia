-- Normalize active meaning-package ownership so Fact/Binding identity can be
-- constrained and projected without interpreting JSON arrays. The legacy JSON
-- columns remain as immutable command evidence during the migration.
CREATE TABLE IF NOT EXISTS world_meaning_package_fact_ownership
(
    application_id uuid NOT NULL
        REFERENCES world_meaning_package_applications(application_id)
        ON DELETE CASCADE,
    fact_id uuid NOT NULL REFERENCES world_facts(fact_id),
    released_revision bigint,
    PRIMARY KEY (application_id, fact_id)
);

CREATE TABLE IF NOT EXISTS world_meaning_package_binding_ownership
(
    application_id uuid NOT NULL
        REFERENCES world_meaning_package_applications(application_id)
        ON DELETE CASCADE,
    binding_id uuid NOT NULL REFERENCES world_rule_bindings(binding_id),
    released_revision bigint,
    PRIMARY KEY (application_id, binding_id)
);

INSERT INTO world_meaning_package_fact_ownership
    (application_id, fact_id, released_revision)
SELECT p.application_id, value::uuid, p.removed_revision
FROM world_meaning_package_applications p
CROSS JOIN LATERAL jsonb_array_elements_text(p.inserted_fact_ids) owned(value)
ON CONFLICT DO NOTHING;

INSERT INTO world_meaning_package_binding_ownership
    (application_id, binding_id, released_revision)
SELECT p.application_id, value::uuid, p.removed_revision
FROM world_meaning_package_applications p
CROSS JOIN LATERAL jsonb_array_elements_text(p.inserted_binding_ids) owned(value)
ON CONFLICT DO NOTHING;

CREATE INDEX IF NOT EXISTS ix_meaning_package_fact_ownership_application
    ON world_meaning_package_fact_ownership(application_id);

CREATE INDEX IF NOT EXISTS ix_meaning_package_binding_ownership_application
    ON world_meaning_package_binding_ownership(application_id);

CREATE UNIQUE INDEX IF NOT EXISTS ux_meaning_package_fact_active_owner
    ON world_meaning_package_fact_ownership(fact_id)
    WHERE released_revision IS NULL;

CREATE UNIQUE INDEX IF NOT EXISTS ux_meaning_package_binding_active_owner
    ON world_meaning_package_binding_ownership(binding_id)
    WHERE released_revision IS NULL;
