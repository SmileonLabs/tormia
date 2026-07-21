-- Versioned ontology content is separate from a world instance. A Rule Block
-- references a published definition version, so later edits cannot silently
-- change an already authored world.
CREATE TABLE IF NOT EXISTS content_packages
(
    package_id      text PRIMARY KEY,
    owner_user_id   uuid NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    created_at      timestamptz NOT NULL DEFAULT now(),
    updated_at      timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS content_package_members
(
    package_id      text NOT NULL REFERENCES content_packages(package_id) ON DELETE CASCADE,
    user_id         uuid NOT NULL REFERENCES app_users(user_id) ON DELETE CASCADE,
    role            text NOT NULL CHECK (role IN ('owner', 'editor', 'viewer')),
    created_at      timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (package_id, user_id)
);

CREATE INDEX IF NOT EXISTS ix_content_package_members_user
    ON content_package_members (user_id, package_id);
