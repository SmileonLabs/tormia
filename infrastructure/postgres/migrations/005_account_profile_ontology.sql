-- Account-owned ontology relationships are intentionally separate from world Facts.
-- They describe a character's durable profile (for example owned pets, inventory,
-- and permanent skills) and are projected only when a character enters a world.
ALTER TABLE player_characters
    ADD COLUMN IF NOT EXISTS profile_revision bigint NOT NULL DEFAULT 1;

ALTER TABLE player_characters
    ADD COLUMN IF NOT EXISTS profile_relations jsonb NOT NULL DEFAULT '[]'::jsonb;

ALTER TABLE player_characters
    ADD CONSTRAINT ck_player_characters_profile_relations_array
    CHECK (jsonb_typeof(profile_relations) = 'array');

-- Account profile writes have their own idempotency history. This is separate
-- from world_commands because an account profile edit must not advance a world
-- revision or author a shared-world event.
CREATE TABLE IF NOT EXISTS player_character_profile_commands
(
    character_id     uuid NOT NULL REFERENCES player_characters(character_id) ON DELETE CASCADE,
    command_id       uuid NOT NULL,
    result_revision  bigint NOT NULL,
    created_at       timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (character_id, command_id)
);
