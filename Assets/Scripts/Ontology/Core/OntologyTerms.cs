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
        public const string PickupBehavior = "pickup_behavior";
        public const string EquippedBy = "equipped_by";
        public const string CarriedBy = "carried_by";
        public const string EquippedItem = "equipped_item";
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
        public const string ImmersionDepth = "immersion_depth";

        public static bool IsRuntimeDerived(string predicate)
        {
            return predicate == ActivePhysicalEffect;
        }
    }

    public static class OntologyConcepts
    {
        public const string Actor = "Actor";
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
        public const string Floating = "Floating";
        public const string Buoyancy = "Buoyancy";
        public const string Water = "Water";
        public const string Unknown = "Unknown";
        public const string Dynamic = "Dynamic";
        public const string Anchored = "Anchored";
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
    }
}
