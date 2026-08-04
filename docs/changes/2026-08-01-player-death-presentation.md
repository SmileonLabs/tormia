# Authority-projected player death presentation

- Date: 2026-08-01
- Status: implemented and asset-pipeline validated

The player death animation is selected only after Authority projects the
evaluated vitality result as `current_health=0` or `is_alive=false`. The world
avatar authors `death_animation_intent=Death`, while the project-owned
`Standing Death Left 01.fbx` is registered through Manifest, Database, and the
PlayerProfile repertoire as `Anim_Player_Death_Left` with root motion disabled.

Existing avatars migrate through player semantic contract version 11. The
migration adds only the missing death-presentation Triple. Removing that
Triple or the validated Manifest/Profile entry removes the presentation route;
Unity does not infer death from a collision, object name, or local health rule.

The imported death clip bakes vertical and XZ root displacement into its pose
while manifest root motion stays disabled. This preserves CharacterController
as the only transform owner and prevents the persistent death pose from floating.
