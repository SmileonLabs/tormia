-- Published action effects are immutable content definitions.  A world only
-- executes one after explicitly enabling the matching package/version.
ALTER TABLE content_definitions
    DROP CONSTRAINT IF EXISTS content_definitions_definition_kind_check;

ALTER TABLE content_definitions
    ADD CONSTRAINT content_definitions_definition_kind_check CHECK (definition_kind IN
        ('object_template', 'rule', 'rule_block_preset',
         'physical_profile', 'attachment_profile', 'localization', 'action_effect'));
