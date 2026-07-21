BEGIN;

CREATE TABLE IF NOT EXISTS platform_schema_migrations
(
    version     text PRIMARY KEY,
    applied_at  timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS app_users
(
    user_id            uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    external_subject   text NOT NULL UNIQUE,
    display_name       text NOT NULL,
    created_at         timestamptz NOT NULL DEFAULT now(),
    updated_at         timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS worlds
(
    world_id                 uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    owner_user_id            uuid NOT NULL REFERENCES app_users(user_id),
    slug                     citext NOT NULL UNIQUE,
    title                    text NOT NULL,
    visibility               text NOT NULL DEFAULT 'private'
                               CHECK (visibility IN ('private', 'shared', 'public')),
    status                   text NOT NULL DEFAULT 'draft'
                               CHECK (status IN ('draft', 'published', 'archived')),
    current_revision         bigint NOT NULL DEFAULT 0 CHECK (current_revision >= 0),
    content_schema_version   integer NOT NULL DEFAULT 1 CHECK (content_schema_version > 0),
    created_at               timestamptz NOT NULL DEFAULT now(),
    updated_at               timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS world_members
(
    world_id     uuid NOT NULL REFERENCES worlds(world_id) ON DELETE CASCADE,
    user_id      uuid NOT NULL REFERENCES app_users(user_id) ON DELETE CASCADE,
    role         text NOT NULL CHECK (role IN ('owner', 'editor', 'player', 'viewer')),
    joined_at    timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (world_id, user_id)
);

CREATE TABLE IF NOT EXISTS content_definitions
(
    definition_kind      text NOT NULL CHECK (definition_kind IN
                              ('object_template', 'rule', 'rule_block_preset',
                               'physical_profile', 'attachment_profile', 'localization')),
    definition_id        text NOT NULL,
    definition_version   integer NOT NULL CHECK (definition_version > 0),
    package_id           text NOT NULL DEFAULT 'tormia.core',
    package_version      text NOT NULL DEFAULT '0.1.0',
    payload              jsonb NOT NULL,
    checksum             text NOT NULL,
    is_published         boolean NOT NULL DEFAULT false,
    created_at           timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (definition_kind, definition_id, definition_version)
);

CREATE INDEX IF NOT EXISTS ix_content_definitions_published
    ON content_definitions (definition_kind, definition_id, definition_version DESC)
    WHERE is_published;

CREATE TABLE IF NOT EXISTS world_zones
(
    world_id        uuid NOT NULL REFERENCES worlds(world_id) ON DELETE CASCADE,
    zone_key        text NOT NULL,
    min_x           double precision NOT NULL,
    min_z           double precision NOT NULL,
    max_x           double precision NOT NULL,
    max_z           double precision NOT NULL,
    simulation_mode text NOT NULL DEFAULT 'active'
                    CHECK (simulation_mode IN ('active', 'reduced', 'dormant')),
    PRIMARY KEY (world_id, zone_key),
    CHECK (min_x < max_x),
    CHECK (min_z < max_z)
);

CREATE TABLE IF NOT EXISTS world_entities
(
    entity_id               uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    world_id                uuid NOT NULL REFERENCES worlds(world_id) ON DELETE CASCADE,
    zone_key                text,
    template_id             text NOT NULL,
    template_version        integer NOT NULL CHECK (template_version > 0),
    display_name            text NOT NULL,
    position_x              double precision NOT NULL DEFAULT 0,
    position_y              double precision NOT NULL DEFAULT 0,
    position_z              double precision NOT NULL DEFAULT 0,
    rotation_x              double precision NOT NULL DEFAULT 0,
    rotation_y              double precision NOT NULL DEFAULT 0,
    rotation_z              double precision NOT NULL DEFAULT 0,
    scale_x                 double precision NOT NULL DEFAULT 1,
    scale_y                 double precision NOT NULL DEFAULT 1,
    scale_z                 double precision NOT NULL DEFAULT 1,
    created_revision        bigint NOT NULL,
    deleted_revision        bigint,
    created_at              timestamptz NOT NULL DEFAULT now(),
    updated_at              timestamptz NOT NULL DEFAULT now(),
    CHECK (deleted_revision IS NULL OR deleted_revision >= created_revision),
    FOREIGN KEY (world_id, zone_key) REFERENCES world_zones(world_id, zone_key)
);

CREATE INDEX IF NOT EXISTS ix_world_entities_zone
    ON world_entities (world_id, zone_key)
    WHERE deleted_revision IS NULL;

CREATE TABLE IF NOT EXISTS world_facts
(
    fact_id                 uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    world_id                uuid NOT NULL REFERENCES worlds(world_id) ON DELETE CASCADE,
    subject_entity_id       uuid NOT NULL REFERENCES world_entities(entity_id),
    predicate_id            text NOT NULL,
    object_kind             text NOT NULL CHECK (object_kind IN ('entity', 'canonical', 'number', 'boolean', 'text', 'json')),
    object_entity_id        uuid REFERENCES world_entities(entity_id),
    object_canonical_id     text,
    object_value            jsonb,
    source_type             text NOT NULL CHECK (source_type IN ('authored', 'action', 'system')),
    persistence_scope       text NOT NULL DEFAULT 'permanent' CHECK (persistence_scope = 'permanent'),
    created_revision        bigint NOT NULL,
    retracted_revision      bigint,
    created_at              timestamptz NOT NULL DEFAULT now(),
    CHECK (
        (object_kind = 'entity' AND object_entity_id IS NOT NULL AND object_canonical_id IS NULL AND object_value IS NULL)
        OR (object_kind = 'canonical' AND object_entity_id IS NULL AND object_canonical_id IS NOT NULL AND object_value IS NULL)
        OR (object_kind IN ('number', 'boolean', 'text', 'json') AND object_entity_id IS NULL AND object_canonical_id IS NULL AND object_value IS NOT NULL)
    ),
    CHECK (retracted_revision IS NULL OR retracted_revision >= created_revision)
);

CREATE INDEX IF NOT EXISTS ix_world_facts_subject_predicate
    ON world_facts (world_id, subject_entity_id, predicate_id)
    WHERE retracted_revision IS NULL;

CREATE INDEX IF NOT EXISTS ix_world_facts_object_entity
    ON world_facts (world_id, object_entity_id)
    WHERE retracted_revision IS NULL AND object_entity_id IS NOT NULL;

CREATE UNIQUE INDEX IF NOT EXISTS ux_world_facts_active_value
    ON world_facts (
        world_id,
        subject_entity_id,
        predicate_id,
        object_kind,
        COALESCE(object_entity_id::text, ''),
        COALESCE(object_canonical_id, ''),
        COALESCE(object_value::text, '')
    )
    WHERE retracted_revision IS NULL;

CREATE TABLE IF NOT EXISTS world_rule_bindings
(
    binding_id              uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    world_id                uuid NOT NULL REFERENCES worlds(world_id) ON DELETE CASCADE,
    target_entity_id        uuid NOT NULL REFERENCES world_entities(entity_id),
    rule_id                 text NOT NULL,
    rule_version            integer NOT NULL CHECK (rule_version > 0),
    enabled                 boolean NOT NULL DEFAULT true,
    parameter_values        jsonb NOT NULL DEFAULT '{}'::jsonb,
    created_revision        bigint NOT NULL,
    retracted_revision      bigint,
    created_at              timestamptz NOT NULL DEFAULT now(),
    CHECK (retracted_revision IS NULL OR retracted_revision >= created_revision)
);

CREATE INDEX IF NOT EXISTS ix_world_rule_bindings_active
    ON world_rule_bindings (world_id, target_entity_id, rule_id)
    WHERE enabled AND retracted_revision IS NULL;

CREATE TABLE IF NOT EXISTS world_commands
(
    command_id              uuid PRIMARY KEY,
    world_id                uuid NOT NULL REFERENCES worlds(world_id) ON DELETE CASCADE,
    actor_user_id           uuid NOT NULL REFERENCES app_users(user_id),
    expected_revision       bigint NOT NULL CHECK (expected_revision >= 0),
    command_type            text NOT NULL,
    payload                 jsonb NOT NULL,
    status                  text NOT NULL CHECK (status IN ('accepted', 'rejected')),
    rejection_code          text,
    resolved_revision       bigint,
    received_at             timestamptz NOT NULL DEFAULT now(),
    resolved_at             timestamptz NOT NULL DEFAULT now(),
    CHECK ((status = 'accepted' AND resolved_revision IS NOT NULL AND rejection_code IS NULL)
           OR (status = 'rejected' AND resolved_revision IS NULL AND rejection_code IS NOT NULL))
);

CREATE INDEX IF NOT EXISTS ix_world_commands_world_received
    ON world_commands (world_id, received_at DESC);

CREATE TABLE IF NOT EXISTS world_events
(
    event_id                uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    world_id                uuid NOT NULL REFERENCES worlds(world_id) ON DELETE CASCADE,
    sequence_number         bigint NOT NULL CHECK (sequence_number > 0),
    command_id              uuid REFERENCES world_commands(command_id),
    actor_user_id           uuid REFERENCES app_users(user_id),
    event_type              text NOT NULL,
    payload                 jsonb NOT NULL,
    created_at              timestamptz NOT NULL DEFAULT now(),
    UNIQUE (world_id, sequence_number)
);

CREATE INDEX IF NOT EXISTS ix_world_events_world_sequence
    ON world_events (world_id, sequence_number);

CREATE TABLE IF NOT EXISTS world_snapshots
(
    snapshot_id             uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    world_id                uuid NOT NULL REFERENCES worlds(world_id) ON DELETE CASCADE,
    revision                bigint NOT NULL CHECK (revision >= 0),
    storage_uri             text NOT NULL,
    checksum                text NOT NULL,
    manifest                jsonb NOT NULL DEFAULT '{}'::jsonb,
    created_at              timestamptz NOT NULL DEFAULT now(),
    UNIQUE (world_id, revision)
);

CREATE INDEX IF NOT EXISTS ix_world_snapshots_latest
    ON world_snapshots (world_id, revision DESC);

COMMIT;
