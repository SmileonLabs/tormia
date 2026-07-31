-- Action execution identifies a definition by the complete immutable tuple:
-- package ID, package version, action ID, and definition version. Reusing an
-- action ID/version in a different package must therefore not collide with an
-- unrelated package. Non-action definitions retain their existing global
-- identity because world rule bindings do not yet carry package identity.

ALTER TABLE content_definitions
    DROP CONSTRAINT IF EXISTS content_definitions_pkey;

CREATE UNIQUE INDEX IF NOT EXISTS
    ux_content_definitions_global_non_action
    ON content_definitions
       (definition_kind, definition_id, definition_version)
    WHERE definition_kind <> 'action_effect';

CREATE UNIQUE INDEX IF NOT EXISTS
    ux_content_action_definitions_package
    ON content_definitions
       (definition_kind, definition_id, definition_version,
        package_id, package_version)
    WHERE definition_kind = 'action_effect';
