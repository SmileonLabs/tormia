-- Read-only fingerprint for a persistent local database created before the
-- ordered migration ledger became authoritative. An empty result means the
-- schema and the migration-016 ownership backfill are compatible.
WITH checks(name, ok) AS
(
    VALUES
    ('001.extensions',
        EXISTS (SELECT 1 FROM pg_extension WHERE extname = 'pgcrypto') AND
        EXISTS (SELECT 1 FROM pg_extension WHERE extname = 'citext')),
    ('001.base_tables',
        to_regclass('public.app_users') IS NOT NULL AND
        to_regclass('public.worlds') IS NOT NULL AND
        to_regclass('public.world_members') IS NOT NULL AND
        to_regclass('public.content_definitions') IS NOT NULL AND
        to_regclass('public.world_zones') IS NOT NULL AND
        to_regclass('public.world_entities') IS NOT NULL AND
        to_regclass('public.world_facts') IS NOT NULL AND
        to_regclass('public.world_rule_bindings') IS NOT NULL AND
        to_regclass('public.world_commands') IS NOT NULL AND
        to_regclass('public.world_events') IS NOT NULL AND
        to_regclass('public.world_snapshots') IS NOT NULL),
    ('001.base_indexes',
        to_regclass('public.ix_content_definitions_published') IS NOT NULL AND
        to_regclass('public.ix_world_entities_zone') IS NOT NULL AND
        to_regclass('public.ux_world_facts_active_value') IS NOT NULL AND
        to_regclass('public.ix_world_rule_bindings_active') IS NOT NULL),
    ('002.content_packages',
        to_regclass('public.content_packages') IS NOT NULL AND
        to_regclass('public.content_package_members') IS NOT NULL AND
        to_regclass('public.ix_content_package_members_user') IS NOT NULL),
    ('003.player_runtime',
        to_regclass('public.world_player_avatars') IS NOT NULL AND
        to_regclass('public.ix_world_player_avatars_entity') IS NOT NULL),
    ('004.account_characters',
        to_regclass('public.player_characters') IS NOT NULL AND
        to_regclass('public.ix_player_characters_user') IS NOT NULL AND
        to_regclass('public.ux_player_characters_default') IS NOT NULL AND
        EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='world_player_avatars' AND column_name='character_id') AND
        EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='world_player_avatars' AND column_name='character_selected_at')),
    ('005.account_profile',
        EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='player_characters' AND column_name='profile_revision') AND
        EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='player_characters' AND column_name='profile_relations') AND
        to_regclass('public.player_character_profile_commands') IS NOT NULL),
    ('006.world_avatar_profile',
        to_regclass('public.world_avatar_profiles') IS NOT NULL),
    ('007.world_content_packages',
        to_regclass('public.world_content_packages') IS NOT NULL AND
        to_regclass('public.ix_world_content_packages_enabled') IS NOT NULL),
    ('008.action_definition_kind',
        EXISTS (
            SELECT 1 FROM pg_constraint
            WHERE conrelid='public.content_definitions'::regclass
              AND contype='c' AND pg_get_constraintdef(oid) LIKE '%action_effect%')),
    ('009.password_auth',
        to_regclass('public.account_credentials') IS NOT NULL AND
        to_regclass('public.account_sessions') IS NOT NULL AND
        to_regclass('public.ix_account_sessions_active_token') IS NOT NULL),
    ('010.avatar_checkpoints',
        to_regclass('public.world_avatar_checkpoints') IS NOT NULL AND
        to_regclass('public.ix_world_avatar_checkpoints_zone') IS NOT NULL),
    ('011.package_scoped_actions',
        to_regclass('public.ux_content_definitions_global_non_action') IS NOT NULL AND
        to_regclass('public.ux_content_action_definitions_package') IS NOT NULL AND
        NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='content_definitions_pkey' AND conrelid='public.content_definitions'::regclass)),
    ('012.meaning_package_applications',
        to_regclass('public.world_meaning_package_applications') IS NOT NULL AND
        to_regclass('public.ux_world_meaning_package_active_slot') IS NOT NULL AND
        to_regclass('public.ix_world_meaning_package_active_target') IS NOT NULL),
    ('013.rule_result_provenance',
        EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='world_facts' AND column_name='source_rule_binding_id') AND
        to_regclass('public.ix_world_facts_rule_result') IS NOT NULL AND
        EXISTS (SELECT 1 FROM pg_indexes WHERE schemaname='public' AND indexname='ux_world_facts_active_value' AND indexdef LIKE '%source_rule_binding_id%')),
    ('014_015.rule_result_lifetime',
        EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='world_facts' AND column_name='rule_result_lifetime') AND
        to_regclass('public.ix_world_facts_retractable_rule_result') IS NOT NULL AND
        EXISTS (SELECT 1 FROM pg_constraint WHERE conname='ck_world_facts_rule_result_lifetime' AND conrelid='public.world_facts'::regclass)),
    ('016.meaning_package_ownership_schema',
        to_regclass('public.world_meaning_package_fact_ownership') IS NOT NULL AND
        to_regclass('public.world_meaning_package_binding_ownership') IS NOT NULL AND
        to_regclass('public.ux_meaning_package_fact_active_owner') IS NOT NULL AND
        to_regclass('public.ux_meaning_package_binding_active_owner') IS NOT NULL),
    ('016.fact_ownership_backfill',
        NOT EXISTS (
            SELECT 1
            FROM world_meaning_package_applications p
            CROSS JOIN LATERAL jsonb_array_elements_text(p.inserted_fact_ids) owned(value)
            LEFT JOIN world_meaning_package_fact_ownership o
              ON o.application_id=p.application_id AND o.fact_id=owned.value::uuid
            WHERE o.fact_id IS NULL)),
    ('016.binding_ownership_backfill',
        NOT EXISTS (
            SELECT 1
            FROM world_meaning_package_applications p
            CROSS JOIN LATERAL jsonb_array_elements_text(p.inserted_binding_ids) owned(value)
            LEFT JOIN world_meaning_package_binding_ownership o
              ON o.application_id=p.application_id AND o.binding_id=owned.value::uuid
            WHERE o.binding_id IS NULL))
)
SELECT name FROM checks WHERE NOT ok ORDER BY name;
