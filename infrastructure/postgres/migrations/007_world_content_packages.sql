-- A world explicitly chooses the immutable content package versions it may use.
-- Merely publishing a package never enables its rules/actions in another world.
CREATE TABLE IF NOT EXISTS world_content_packages
(
    world_id        uuid NOT NULL REFERENCES worlds(world_id) ON DELETE CASCADE,
    package_id      text NOT NULL REFERENCES content_packages(package_id) ON DELETE RESTRICT,
    package_version text NOT NULL,
    enabled         boolean NOT NULL DEFAULT true,
    updated_revision bigint NOT NULL,
    updated_at      timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (world_id, package_id)
);

CREATE INDEX IF NOT EXISTS ix_world_content_packages_enabled
    ON world_content_packages(world_id, package_id)
    WHERE enabled;
