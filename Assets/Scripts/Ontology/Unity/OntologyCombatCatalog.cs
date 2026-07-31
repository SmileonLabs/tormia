using System;
using System.Collections.Generic;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    [CreateAssetMenu(
        fileName = "CombatCatalog",
        menuName = "Tormia/Ontology/Combat Catalog")]
    public sealed class OntologyCombatCatalog : ScriptableObject
    {
        [SerializeField] private List<OntologyWeaponPresentationDefinition> weapons = new();
        [SerializeField] private List<OntologyMonsterPresentationDefinition> monsters = new();
        [SerializeField] private List<OntologyCombatVfxDefinition> vfx = new();

        public IReadOnlyList<OntologyWeaponPresentationDefinition> Weapons => weapons;
        public IReadOnlyList<OntologyMonsterPresentationDefinition> Monsters => monsters;
        public IReadOnlyList<OntologyCombatVfxDefinition> Vfx => vfx;

        public OntologyWeaponPresentationDefinition FindWeapon(string weaponId)
        {
            if (string.IsNullOrWhiteSpace(weaponId)) return null;
            return weapons.Find(value =>
                value != null &&
                string.Equals(
                    value.weaponId,
                    weaponId,
                    StringComparison.Ordinal));
        }

        public OntologyCombatVfxDefinition FindVfx(string intentId)
        {
            if (string.IsNullOrWhiteSpace(intentId)) return null;
            return vfx.Find(value =>
                value != null &&
                string.Equals(value.intentId, intentId, StringComparison.Ordinal));
        }

        public void ReplaceDefinitions(
            IEnumerable<OntologyWeaponPresentationDefinition> weaponValues,
            IEnumerable<OntologyMonsterPresentationDefinition> monsterValues,
            IEnumerable<OntologyCombatVfxDefinition> vfxValues)
        {
            weapons = weaponValues == null
                ? new List<OntologyWeaponPresentationDefinition>()
                : new List<OntologyWeaponPresentationDefinition>(weaponValues);
            monsters = monsterValues == null
                ? new List<OntologyMonsterPresentationDefinition>()
                : new List<OntologyMonsterPresentationDefinition>(monsterValues);
            vfx = vfxValues == null
                ? new List<OntologyCombatVfxDefinition>()
                : new List<OntologyCombatVfxDefinition>(vfxValues);
        }
    }

    [Serializable]
    public sealed class OntologyWeaponPresentationDefinition
    {
        public string weaponId;
        public GameObject visualPrefab;
        public OntologyAttachmentProfile attachmentProfile;
        public string weaponFamily = string.Empty;
        public string attackActionId = string.Empty;
        public int actionDefinitionVersion;
        public string equipAnimationIntent = string.Empty;
        public string unequipAnimationIntent = string.Empty;
        public string idleAnimationIntent = string.Empty;
        public string moveAnimationIntent = string.Empty;
        public string recoveryAnimationIntent = string.Empty;
        public string swingVfxIntent = string.Empty;
        public string contactModeId = OntologyObjects.WeaponContactWindow;
    }

    [Serializable]
    public sealed class OntologyMonsterPresentationDefinition
    {
        public string monsterId;
        public GameObject visualPrefab;
        public string actorType = "Monster";
        public string idleAnimationIntent = "MonsterIdle";
        public string hitAnimationIntent = "HitReaction";
        public string deathAnimationIntent = "Death";
        public string hitVfxIntent = "HitPhysicalLight";
    }

    [Serializable]
    public sealed class OntologyCombatVfxDefinition
    {
        public string intentId;
        public GameObject prefab;
        public Vector3 localPosition;
        public Vector3 localEulerAngles;
        public Vector3 localScale = Vector3.one;
        [Min(0.05f)] public float lifetimeSeconds = 1.5f;
        [Min(1)] public int initialPoolSize = 2;
        public bool followAnchor;
    }
}
