using System;
using System.Collections.Generic;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    [CreateAssetMenu(
        fileName = "WeaponContentManifest",
        menuName = "Tormia/Ontology/Weapon Content Manifest")]
    public sealed class OntologyWeaponContentManifest : ScriptableObject
    {
        [SerializeField]
        private List<OntologyWeaponContentEntry> weapons = new();

        public IReadOnlyList<OntologyWeaponContentEntry> Weapons => weapons;
    }

    [Serializable]
    public sealed class OntologyWeaponContentEntry
    {
        public string weaponId;
        public GameObject sourcePrefab;
        public string weaponFamily = "Sword";
        public string grantedCapability = "MeleeAttack";
        public string attackActionId = "attack";
        public int actionDefinitionVersion = 9;
        public string equipAnimationIntent = "WeaponEquip";
        public string unequipAnimationIntent = "WeaponUnequip";
        public string idleAnimationIntent = "WeaponIdle";
        public string moveAnimationIntent = "WeaponWalk";
        public string recoveryAnimationIntent = "AttackRecovery";
        public string swingVfxIntent = "WeaponSwingSwordBasic";
        public string contactModeId = OntologyObjects.WeaponContactWindow;

        public bool IsValid =>
            !string.IsNullOrWhiteSpace(weaponId) &&
            sourcePrefab != null &&
            !string.IsNullOrWhiteSpace(weaponFamily) &&
            !string.IsNullOrWhiteSpace(grantedCapability) &&
            !string.IsNullOrWhiteSpace(attackActionId) &&
            !string.IsNullOrWhiteSpace(contactModeId);
    }
}
