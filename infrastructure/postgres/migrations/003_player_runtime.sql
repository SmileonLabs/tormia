-- Durable authority/security ownership of an in-world player avatar.
-- Controller inputs themselves remain short-lived Redis data and are not Facts.
CREATE TABLE IF NOT EXISTS world_player_avatars
(
    world_id            uuid NOT NULL REFERENCES worlds(world_id) ON DELETE CASCADE,
    user_id             uuid NOT NULL REFERENCES app_users(user_id) ON DELETE CASCADE,
    entity_id           uuid NOT NULL REFERENCES world_entities(entity_id) ON DELETE RESTRICT,
    registered_revision bigint NOT NULL CHECK (registered_revision >= 0),
    created_at          timestamptz NOT NULL DEFAULT now(),
    updated_at          timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (world_id, user_id),
    UNIQUE (world_id, entity_id)
);

CREATE INDEX IF NOT EXISTS ix_world_player_avatars_entity
    ON world_player_avatars (entity_id);
