-- World-specific avatar ontology is durable world data. It is deliberately
-- separate from account-owned player_character.profile_relations so a class,
-- team, score, or quest role in one world never changes the portable character.
CREATE TABLE IF NOT EXISTS world_avatar_profiles
(
    world_id          uuid NOT NULL REFERENCES worlds(world_id) ON DELETE CASCADE,
    avatar_entity_id  uuid NOT NULL,
    profile_relations jsonb NOT NULL DEFAULT '[]'::jsonb,
    updated_revision  bigint NOT NULL DEFAULT 0,
    updated_at        timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (world_id, avatar_entity_id),
    FOREIGN KEY (world_id, avatar_entity_id)
        REFERENCES world_player_avatars(world_id, entity_id) ON DELETE CASCADE,
    CHECK (jsonb_typeof(profile_relations) = 'array')
);
