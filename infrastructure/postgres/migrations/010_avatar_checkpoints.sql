BEGIN;

CREATE TABLE IF NOT EXISTS world_avatar_checkpoints
(
    world_id          uuid NOT NULL,
    avatar_entity_id  uuid NOT NULL,
    zone_key          text,
    position_x        double precision NOT NULL,
    position_y        double precision NOT NULL,
    position_z        double precision NOT NULL,
    rotation_x        double precision NOT NULL,
    rotation_y        double precision NOT NULL,
    rotation_z        double precision NOT NULL,
    updated_revision  bigint NOT NULL CHECK (updated_revision >= 0),
    updated_at        timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (world_id, avatar_entity_id),
    CONSTRAINT fk_world_avatar_checkpoint_avatar
        FOREIGN KEY (world_id, avatar_entity_id)
        REFERENCES world_player_avatars(world_id, entity_id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ix_world_avatar_checkpoints_zone
    ON world_avatar_checkpoints(world_id, zone_key);

COMMIT;
