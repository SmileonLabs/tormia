-- Account-owned character profiles are intentionally separate from world Facts.
-- A profile can later be hydrated into a world avatar, but it is not itself a
-- world entity and changing a saved profile must not rewrite a shared world.
CREATE TABLE IF NOT EXISTS player_characters
(
    character_id      uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id           uuid NOT NULL REFERENCES app_users(user_id) ON DELETE CASCADE,
    display_name      text NOT NULL,
    template_id       text NOT NULL DEFAULT 'player_default',
    equipped_part_ids jsonb NOT NULL DEFAULT '[]'::jsonb,
    is_default        boolean NOT NULL DEFAULT false,
    created_at        timestamptz NOT NULL DEFAULT now(),
    updated_at        timestamptz NOT NULL DEFAULT now(),
    CHECK (jsonb_typeof(equipped_part_ids) = 'array')
);

CREATE INDEX IF NOT EXISTS ix_player_characters_user
    ON player_characters (user_id, created_at);

CREATE UNIQUE INDEX IF NOT EXISTS ux_player_characters_default
    ON player_characters (user_id)
    WHERE is_default;

-- One account may use different saved characters in different worlds. The
-- avatar entity remains world-owned, while this link records the selected
-- account character without turning account ownership into an ontology Fact.
ALTER TABLE world_player_avatars
    ADD COLUMN IF NOT EXISTS character_id uuid REFERENCES player_characters(character_id) ON DELETE SET NULL;

ALTER TABLE world_player_avatars
    ADD COLUMN IF NOT EXISTS character_selected_at timestamptz;

CREATE INDEX IF NOT EXISTS ix_world_player_avatars_character
    ON world_player_avatars (character_id)
    WHERE character_id IS NOT NULL;
