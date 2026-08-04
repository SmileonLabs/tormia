namespace Tormia.Ontology.Core
{
    public static class OntologyPredicates
    {
        public const string EquippedPart = "equipped_part";
        public const string UnequipPart = "unequip_part";
        public const string HasCapability = "has_capability";
        public const string HasConcept = "has_concept";
        public const string HasSlot = "has_slot";
        public const string Provides = "provides";
        public const string GrantsCapability = "grants_capability";
        public const string EquipAction = "equip_action";
        public const string UnequipAction = "unequip_action";
        public const string InteractionRange = "interaction_range";
        public const string AttackAction = "attack_action";
        public const string SwingAction = "swing_action";
        public const string AttackDamage = "attack_damage";
        public const string AttackRange = "attack_range";
        public const string AttackCooldown = "attack_cooldown";
        public const string AttackPlaybackSpeed = "attack_playback_speed";
        public const string AttackWindupSeconds =
            "attack_windup_seconds";
        public const string AttackRecoverySeconds =
            "attack_recovery_seconds";
        public const string DamageProfile = "damage_profile";
        public const string AttackContactMode = "attack_contact_mode";
        public const string AttackContactReach = "attack_contact_reach";
        public const string AttackContactOpenSeconds =
            "attack_contact_open_seconds";
        public const string AttackContactWindowSeconds =
            "attack_contact_window_seconds";
        public const string DetectionRange = "detection_range";
        public const string LeashRange = "leash_range";
        public const string TargetConcept = "target_concept";
        public const string TargetingProfile = "targeting_profile";
        public const string ChaseProfile = "chase_profile";
        public const string TargetAction = "target_action";
        public const string ChaseAction = "chase_action";
        public const string HostileToFaction = "hostile_to_faction";
        public const string BelongsToFaction = "belongs_to_faction";
        public const string Faction = "faction";
        public const string AutonomousAttackIntent =
            "autonomous_attack_intent";
        public const string AutonomousTargetIntent =
            "autonomous_target_intent";
        public const string AutonomousChaseIntent =
            "autonomous_chase_intent";
        public const string LocomotionAction = "locomotion_action";
        public const string LocomotionIntent = "locomotion_intent";
        public const string SprintSpeed = "sprint_speed";
        public const string JumpAction = "jump_action";
        public const string JumpIntent = "jump_intent";
        public const string JumpTakeoffSpeed = "jump_takeoff_speed";
        public const string GravityAcceleration = "gravity_acceleration";
        public const string GroundStickVelocity = "ground_stick_velocity";
        public const string MaximumStepHeight = "maximum_step_height";
        public const string GroundClearance = "ground_clearance";
        public const string ImpactResponseProfile =
            "impact_response_profile";
        public const string RespawnAction = "respawn_action";
        public const string RespawnIntent = "respawn_intent";
        public const string CurrentHealth = "current_health";
        public const string MaximumHealth = "maximum_health";
        public const string IdleAnimationIntent =
            "idle_animation_intent";
        public const string MoveAnimationIntent =
            "move_animation_intent";
        public const string CombatDisposition = "combat_disposition";
        public const string IsAlive = "is_alive";
        public const string PrimaryAttackIntent = "primary_attack_intent";
        public const string PrimarySwingIntent = "primary_swing_intent";
        public const string UnequipIntent = "unequip_intent";
        public const string DefeatResolvedIntent = "defeat_resolved_intent";
        public const string LootCollectionIntent = "loot_collection_intent";
        public const string LootAction = "loot_action";
        public const string LootTable = "loot_table";
        public const string LootItem = "loot_item";
        public const string LootStatus = "loot_status";
        public const string LootCollectedBy = "loot_collected_by";
        public const string HitAnimationIntent = "hit_animation_intent";
        public const string DeathAnimationIntent = "death_animation_intent";
        public const string HitVfxIntent = "hit_vfx_intent";
        public const string AttacksWith = "attacks_with";
        public const string GrantsSkill = "grants_skill";
        public const string SkillGrantRequiresRule = "skill_grant_requires_rule";
        public const string HasSkill = "has_skill";
        public const string HasTemporarySkill = "has_temporary_skill";
        public const string CanUseSkill = "can_use_skill";
        public const string ConflictsWithSlot = "conflicts_with_slot";
        public const string HasAnimation = "has_animation";
        public const string Near = "near";
        public const string InteractionIntent = "interaction_intent";
        public const string Occupies = "occupies";
        public const string PhysicalState = "physical_state";
        public const string PhysicalProfile = "physical_profile";
        public const string HasPhysicalEffect = "has_physical_effect";
        public const string ActivePhysicalEffect = "active_physical_effect";
        public const string CompatibleWith = "compatible_with";
        public const string AttachmentProfile = "attachment_profile";
        public const string AttachmentKind = "attachment_kind";
        public const string AttachmentRelationPredicate = "attachment_relation_predicate";
        public const string AttachmentRelationDirection = "attachment_relation_direction";
        public const string PickupBehavior = "pickup_behavior";
        public const string EquippedBy = "equipped_by";
        public const string CarriedBy = "carried_by";
        public const string EquippedItem = "equipped_item";
        public const string CanEquip = "can_equip";
        public const string SlotOwner = "slot_owner";
        public const string SlotId = "slot_id";
        public const string MountedIn = "mounted_in";
        public const string CarriedItem = "carried_item";
        public const string MountedActor = "mounted_actor";
        public const string SupportsBehavior = "supports_behavior";
        public const string MobilityMode = "mobility_mode";
        public const string HasMaterial = "has_material";
        public const string DensityClass = "density_class";
        public const string SupportedBy = "supported_by";
        public const string RestingOn = "resting_on";
        public const string CanBePlacedOn = "can_be_placed_on";
        public const string HasRuleBlock = "has_rule_block";
        public const string SemanticContractVersion =
            "semantic_contract_version";
        public const string SemanticContractChecksum =
            "semantic_contract_checksum";
        public const string PhysicalProfileContractVersion =
            "physical_profile_contract_version";
        public const string ImmersionDepth = "immersion_depth";
        public const string MovementSpeed = "movement_speed";
        public const string CollisionRole = "collision_role";
        public const string CollisionProxyShape = "collision_proxy_shape";
        public const string CollisionRadius = "collision_radius";
        public const string CollisionHeight = "collision_height";
        public const string CollisionSizeX = "collision_size_x";
        public const string CollisionSizeY = "collision_size_y";
        public const string CollisionSizeZ = "collision_size_z";
        public const string CollisionCenterOffsetX =
            "collision_center_offset_x";
        public const string CollisionCenterOffsetY =
            "collision_center_offset_y";
        public const string CollisionCenterOffsetZ =
            "collision_center_offset_z";
        public const string Location = "location";

        public static bool IsRuntimeDerived(string predicate)
        {
            return predicate == ActivePhysicalEffect;
        }
    }

    public static class OntologyConcepts
    {
        public const string Actor = "Actor";
        public const string AutonomousAgent = "AutonomousAgent";
        public const string PlayerControlled = "PlayerControlled";
        public const string Monster = "Monster";
        public const string Combatant = "Combatant";
        public const string CharacterPart = "CharacterPart";
        public const string FloatableObject = "FloatableObject";
        public const string Wearable = "Wearable";
        public const string Carryable = "Carryable";
        public const string Mountable = "Mountable";
        public const string FlotationDevice = "FlotationDevice";
        public const string WaterRegion = "WaterRegion";
        public const string PhysicalProfile = "PhysicalProfile";
        public const string PhysicalEffect = "PhysicalEffect";
        public const string EquipmentSlot = "EquipmentSlot";
        public const string AttachmentProfile = "AttachmentProfile";
        public const string Weapon = "Weapon";
        public const string Sword = "Sword";
        public const string Damageable = "Damageable";
    }

    public static class OntologyObjects
    {
        public const string SwampResistance = "SwampResistance";
        public const string ColdProtection = "ColdProtection";
        public const string WaterFloat = "WaterFloat";
        public const string Swimming = "Swimming";
        public const string Moving = "Moving";
        public const string Idle = "Idle";
        public const string Shallow = "Shallow";
        public const string Deep = "Deep";
        public const string SelectThenEquip = "SelectThenEquip";
        public const string SelectThenCarry = "SelectThenCarry";
        public const string SelectThenMount = "SelectThenMount";
        public const string ItemToActor = "ItemToActor";
        public const string ActorToItem = "ActorToItem";
        public const string Floating = "Floating";
        public const string Buoyancy = "Buoyancy";
        public const string Water = "Water";
        public const string Unknown = "Unknown";
        public const string Dynamic = "Dynamic";
        public const string Anchored = "Anchored";
        public const string AuthorityKinematic = "AuthorityKinematic";
        public const string StaticAnchored = "StaticAnchored";
        public const string LocalCharacterController =
            "LocalCharacterController";
        public const string ControllerImpulse = "ControllerImpulse";
        public const string TemporaryRigidbodyReaction =
            "TemporaryRigidbodyReaction";
        public const string DynamicProp = "DynamicProp";
        public const string WalkableSupport = "WalkableSupport";
        public const string ActorBody = "ActorBody";
        public const string InteractionTrigger = "InteractionTrigger";
        public const string WaterVolume = "WaterVolume";
        public const string Capsule = "Capsule";
        public const string Box = "Box";
        public const string NearestHostileWithinDetection =
            "NearestHostileWithinDetection";
        public const string ChaseWithinLeash = "ChaseWithinLeash";
        public const string WeaponContactWindow = "WeaponContactWindow";
        public const string Light = "Light";
        public const string Heavy = "Heavy";
        public const string Stone = "Stone";
        public const string Polymer = "Polymer";
        public const string Vegetation = "Vegetation";
        public const string Wood = "Wood";
        public const string Wearable = "Wearable";
        public const string Carryable = "Carryable";
        public const string Mountable = "Mountable";
    }

    public static class OntologyActions
    {
        public const string UnequipWearable = "unequip_wearable";
        public const string DropCarried = "drop_carried";
        public const string Dismount = "dismount";
        public const string EquipWeapon = "equip_weapon";
        public const string UnequipEquipment = "unequip_equipment";
        public const string PrimaryAttack = "attack";
        public const string SwingWeapon = "swing_weapon";
        public const string AutonomousMeleeAttack =
            "autonomous_melee_attack";
        public const string AcquireAutonomousTarget =
            "acquire_autonomous_target";
        public const string ChaseAutonomousTarget =
            "chase_autonomous_target";
        public const string CollectLoot = "collect_loot";
        public const string RespawnAvatar = "respawn_avatar";
        public const string MoveAvatar = "move_avatar";
        public const string JumpAvatar = "jump_avatar";
    }

    public static class OntologyRuleBlocks
    {
        public const string EquipItemOnInteractionIntent =
            "EquipItemOnInteractionIntent";
        public const string UnequipItemOnInteractionIntent =
            "UnequipItemOnInteractionIntent";
        public const string MeleeAttackOnPrimaryIntent =
            "MeleeAttackOnPrimaryIntent";
        public const string SwingWeaponOnPrimaryIntent =
            "SwingWeaponOnPrimaryIntent";
        public const string AutonomousMeleeCombat =
            "AutonomousMeleeCombat";
        public const string AcquireNearestHostileTarget =
            "AcquireNearestHostileTarget";
        public const string ChaseTargetWithinLeash =
            "ChaseTargetWithinLeash";
        public const string LootBecomesAvailableOnDefeat =
            "LootBecomesAvailableOnDefeat";
        public const string CollectAvailableLoot =
            "CollectAvailableLoot";
        public const string RespawnPlayerOnDeath =
            "RespawnPlayerOnDeath";
        public const string MovePlayerFromIntent =
            "MovePlayerFromIntent";
        public const string JumpPlayerFromIntent =
            "JumpPlayerFromIntent";
    }

    public static class OntologySemanticContracts
    {
        public const int PlayerAvatarVersion = 12;
        public const int WeaponVersion = 13;
        public const int AutonomousActorVersion = 12;
    }
}
