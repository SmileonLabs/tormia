BEGIN;

ALTER TABLE worlds
    ADD COLUMN IF NOT EXISTS entry_content_package_id text,
    ADD COLUMN IF NOT EXISTS entry_content_package_version text;

WITH selected AS (
    SELECT DISTINCT ON (p.world_id)
           p.world_id, p.package_id, p.package_version
    FROM world_content_packages p
    WHERE p.enabled
    ORDER BY p.world_id, p.updated_revision DESC, p.updated_at DESC,
             p.package_id
)
UPDATE worlds w
SET entry_content_package_id = selected.package_id,
    entry_content_package_version = selected.package_version
FROM selected
WHERE selected.world_id = w.world_id
  AND (w.entry_content_package_id IS NULL
       OR w.entry_content_package_version IS NULL);

ALTER TABLE worlds
    ADD CONSTRAINT ck_world_entry_content_package_pair
    CHECK (
        (entry_content_package_id IS NULL AND
         entry_content_package_version IS NULL) OR
        (entry_content_package_id IS NOT NULL AND
         entry_content_package_version IS NOT NULL));

INSERT INTO platform_schema_migrations(version)
VALUES ('018_world_entry_content_package')
ON CONFLICT (version) DO NOTHING;

COMMIT;
