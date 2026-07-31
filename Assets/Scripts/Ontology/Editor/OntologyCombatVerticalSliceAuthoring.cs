using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Builds project-owned wrappers and semantic presentation catalogs while
    /// preserving the imported third-party packages as immutable source assets.
    /// </summary>
    public static class OntologyCombatVerticalSliceAuthoring
    {
        private const string CombatDataFolder = "Assets/Data/Ontology/Combat";
        private const string CombatPrefabFolder = "Assets/Prefabs/Ontology/Combat";
        private const string CatalogPath = CombatDataFolder + "/CombatCatalog.asset";
        private const string WeaponManifestPath =
            CombatDataFolder + "/WeaponContentManifest.asset";
        private const string AttachmentPath =
            "Assets/Data/Ontology/Profiles/RightHandCarryAttachmentProfile.asset";
        private const string WeaponPhysicalProfilePath =
            "Assets/Data/Ontology/Profiles/HandheldWeaponPhysicalProfile.asset";
        private const string PlacementCatalogPath =
            "Assets/Data/Ontology/Placeables/PolyStylePlaceables.asset";
        private const string RuleDatabasePath =
            "Assets/Data/Ontology/RuleDatabase.asset";
        private const string RulePresetDatabasePath =
            "Assets/Data/Ontology/RuleBlockPresetDatabase.asset";
        private const string AuthoritySettingsPath =
            "Assets/Data/Ontology/Networking/WorldAuthoritySettings.asset";
        public const string EquipRuleId =
            OntologyRuleBlocks.EquipItemOnInteractionIntent;
        public const string UnequipRuleId =
            OntologyRuleBlocks.UnequipItemOnInteractionIntent;
        public const string AttackRuleId =
            OntologyRuleBlocks.MeleeAttackOnPrimaryIntent;
        public const string SwingRuleId =
            OntologyRuleBlocks.SwingWeaponOnPrimaryIntent;
        public const string AutonomousTargetRuleId =
            OntologyRuleBlocks.AcquireNearestHostileTarget;
        public const string AutonomousChaseRuleId =
            OntologyRuleBlocks.ChaseTargetWithinLeash;
        public const string AutonomousCombatRuleId =
            OntologyRuleBlocks.AutonomousMeleeCombat;
        public const string DefeatLootRuleId =
            OntologyRuleBlocks.LootBecomesAvailableOnDefeat;
        public const string CollectLootRuleId =
            OntologyRuleBlocks.CollectAvailableLoot;
        public const string PlayerRespawnRuleId =
            OntologyRuleBlocks.RespawnPlayerOnDeath;
        public const string PlayerLocomotionRuleId =
            OntologyRuleBlocks.MovePlayerFromIntent;
        public const string PlayerJumpRuleId =
            OntologyRuleBlocks.JumpPlayerFromIntent;
        public const string AutonomousAttackActionId =
            OntologyActions.AutonomousMeleeAttack;
        public const string AutonomousTargetActionId =
            OntologyActions.AcquireAutonomousTarget;
        public const string AutonomousChaseActionId =
            OntologyActions.ChaseAutonomousTarget;
        public const string EquipActionId =
            OntologyActions.EquipWeapon;
        public const string UnequipActionId =
            OntologyActions.UnequipEquipment;
        public const string CollectLootActionId =
            OntologyActions.CollectLoot;
        public const string PlayerRespawnActionId =
            OntologyActions.RespawnAvatar;
        public const string PlayerLocomotionActionId =
            OntologyActions.MoveAvatar;
        public const string PlayerJumpActionId =
            OntologyActions.JumpAvatar;
        public const int EquipActionVersion = 8;
        public const int UnequipActionVersion = 2;
        public const int AttackActionVersion = 10;
        public const int SwingActionVersion = 1;
        public const int AutonomousAttackActionVersion = 1;
        public const int AutonomousTargetActionVersion = 1;
        public const int AutonomousChaseActionVersion = 1;
        public const int CollectLootActionVersion = 1;
        public const int PlayerRespawnActionVersion = 1;
        public const int PlayerLocomotionActionVersion = 1;
        public const int PlayerLocomotionRuleVersion = 2;
        public const int PlayerJumpActionVersion = 2;
        public const int PlayerJumpRuleVersion = 3;
        public const int PlayerAvatarSemanticContractVersion =
            OntologySemanticContracts.PlayerAvatarVersion;
        public const int AutonomousMonsterSemanticContractVersion =
            OntologySemanticContracts.AutonomousActorVersion;
        public const int AttackSemanticContractVersion = 4;
        public const int SwingSemanticContractVersion = 5;
        public const int WeaponSemanticContractVersion =
            OntologySemanticContracts.WeaponVersion;

        [MenuItem("Tools/Ontology/Combat/Synchronize Ontology Contracts")]
        public static void SynchronizeOntologyContractsMenu()
        {
            SynchronizeOntologyContracts();
            AssetDatabase.SaveAssets();
            Debug.Log(
                "Combat ontology contracts synchronized from Rule/Action data.");
        }

        [MenuItem("Tools/Ontology/Combat/Build First Vertical Slice Assets")]
        public static void Build()
        {
            EnsureFolder(CombatDataFolder);
            EnsureFolder(CombatPrefabFolder);

            var attachment = AssetDatabase.LoadAssetAtPath<OntologyAttachmentProfile>(
                AttachmentPath);
            var weaponManifest =
                AssetDatabase.LoadAssetAtPath<OntologyWeaponContentManifest>(
                    WeaponManifestPath);
            if (weaponManifest == null ||
                weaponManifest.Weapons.Count == 0 ||
                weaponManifest.Weapons.Any(value =>
                    value == null || !value.IsValid))
            {
                throw new InvalidOperationException(
                    "WeaponContentManifest is missing or contains invalid entries.");
            }
            SynchronizeOntologyContracts();
            var weaponPrefabs = weaponManifest.Weapons
                .Select(value =>
                    BuildWeaponWrapper(
                        value.weaponId,
                        value.sourcePrefab,
                        value.contactModeId))
                .ToArray();
            var beholder = BuildMonsterWrapper();
            BuildCombatCatalog(
                weaponManifest,
                attachment,
                weaponPrefabs,
                beholder);
            var animationManifest =
                OntologyAnimationContentPipeline.CreateOrMigrateManifest();
            EnsureMonsterAnimationManifestEntries(animationManifest);
            if (!OntologyAnimationContentPipeline.ValidateAndSynchronize(
                    animationManifest,
                    true))
            {
                throw new InvalidOperationException(
                    "AnimationContentManifest validation failed.");
            }
            AddMonsterToPlacementCatalog(beholder, weaponManifest);
            SynchronizeOntologyContracts();
            InstallRuntimePresentation();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<OntologyCombatCatalog>(
                CatalogPath);
            Debug.Log(
                "TOV combat vertical-slice assets built: three swords, Beholder, " +
                "semantic animation intents, Slash VFX, Hit VFX, and Authority attack definition.");
        }

        private static GameObject BuildWeaponWrapper(
            string weaponId,
            GameObject source,
            string contactModeId)
        {
            if (source == null)
                throw new InvalidOperationException(
                    "Missing weapon source for " + weaponId);

            var output =
                CombatPrefabFolder + "/Ontology" + weaponId + ".prefab";
            var existing =
                AssetDatabase.LoadAssetAtPath<GameObject>(output);
            var existingGripPoint = existing == null
                ? null
                : existing.GetComponentInChildren<
                    OntologyAttachmentGripPoint>(true);
            var gripLocalPosition = existingGripPoint == null
                ? Vector3.zero
                : existingGripPoint.transform.localPosition;
            var gripLocalRotation = existingGripPoint == null
                ? Quaternion.identity
                : existingGripPoint.transform.localRotation;
            var gripLocalScale = existingGripPoint == null
                ? Vector3.one
                : existingGripPoint.transform.localScale;

            var root = new GameObject("Ontology" + weaponId);
            try
            {
                root.AddComponent<OntologyObject>();
                root.AddComponent<OntologyAuthorityEntityIdentity>();
                root.AddComponent<OntologyPhysicsPresentationCoordinator>();
                root.AddComponent<OntologyAttachmentAdapter>();
                var collider = root.AddComponent<BoxCollider>();
                collider.isTrigger = true;
                collider.size = new Vector3(0.18f, 1.1f, 0.18f);

                var visual = (GameObject)PrefabUtility.InstantiatePrefab(source);
                visual.name = "Visual";
                visual.transform.SetParent(root.transform, false);

                var gripPoint =
                    new GameObject("AttachmentGripPoint");
                gripPoint.transform.SetParent(root.transform, false);
                gripPoint.transform.localPosition = gripLocalPosition;
                gripPoint.transform.localRotation = gripLocalRotation;
                gripPoint.transform.localScale = gripLocalScale;
                gripPoint.AddComponent<OntologyAttachmentGripPoint>();

                var slashAnchor = new GameObject("SlashVfxAnchor").transform;
                slashAnchor.SetParent(root.transform, false);
                slashAnchor.localPosition = new Vector3(0f, 0.55f, 0f);

                var presenter = root.AddComponent<OntologyCombatWeaponPresenter>();
                presenter.Configure(
                    "WeaponSwingSwordBasic",
                    slashAnchor,
                    collider,
                    contactModeId);

                return PrefabUtility.SaveAsPrefabAsset(root, output);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static GameObject BuildMonsterWrapper()
        {
            const string sourcePath =
                "Assets/RPGMonsterPartnersPBRPolyart/Prefabs/Character/BeholderPolyartDefault.prefab";
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            if (source == null)
                throw new InvalidOperationException("Missing Beholder source prefab.");

            var root = new GameObject("OntologyBeholderBasic");
            try
            {
                var ontology = root.AddComponent<OntologyObject>();
                ontology.ConfigureOntologyData(
                    string.Empty,
                    new[] { "Actor", "Monster", "Damageable" },
                    Array.Empty<OntologyFactEntry>());
                var identity = root.AddComponent<OntologyAuthorityEntityIdentity>();
                var collider = root.AddComponent<SphereCollider>();
                collider.isTrigger = true;
                collider.center = new Vector3(0f, 1.1f, 0f);
                collider.radius = 0.75f;

                var visual = (GameObject)PrefabUtility.InstantiatePrefab(source);
                visual.name = "Visual";
                visual.transform.SetParent(root.transform, false);
                var visualAnimator = visual.GetComponentInChildren<Animator>(true);
                if (visualAnimator != null)
                {
                    var animationAdapter =
                        visualAnimator.GetComponent<OntologyAnimationAdapter>() ??
                        visualAnimator.gameObject
                            .AddComponent<OntologyAnimationAdapter>();
                    var serialized = new SerializedObject(animationAdapter);
                    serialized.FindProperty("animationDatabase").objectReferenceValue =
                        AssetDatabase.LoadAssetAtPath<OntologyAnimationDatabase>(
                            OntologyAnimationContentPipeline.DatabasePath);
                    serialized.FindProperty("actorProfile").objectReferenceValue =
                        AssetDatabase.LoadAssetAtPath<OntologyActorProfile>(
                            OntologyAnimationContentPipeline.MonsterProfilePath);
                    serialized.FindProperty("actorObject").objectReferenceValue =
                        ontology;
                    serialized.FindProperty("targetAnimator").objectReferenceValue =
                        visualAnimator;
                    serialized.FindProperty("playSelectedClip").boolValue = true;
                    serialized.FindProperty(
                        "driveBaseLocomotionFromManifest").boolValue = true;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }

                var hitAnchor = new GameObject("HitCenter").transform;
                hitAnchor.SetParent(root.transform, false);
                hitAnchor.localPosition = new Vector3(0f, 1.2f, 0f);

                var dropAnchor = new GameObject("DropAnchor").transform;
                dropAnchor.SetParent(root.transform, false);
                dropAnchor.localPosition = new Vector3(0f, 0.2f, 0f);
                var lootVisual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                lootVisual.name = "OntologyDataFragmentVisual";
                lootVisual.transform.SetParent(dropAnchor, false);
                lootVisual.transform.localScale = Vector3.one * 0.28f;
                UnityEngine.Object.DestroyImmediate(
                    lootVisual.GetComponent<Collider>());
                lootVisual.SetActive(false);

                var presenter = root.AddComponent<OntologyCombatTargetPresenter>();
                presenter.Configure(
                    identity,
                    hitAnchor,
                    "HitReaction",
                    "Death",
                    "HitPhysicalLight",
                    lootVisual,
                    collider);

                return PrefabUtility.SaveAsPrefabAsset(
                    root,
                    CombatPrefabFolder + "/OntologyBeholderBasic.prefab");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void BuildCombatCatalog(
            OntologyWeaponContentManifest weaponManifest,
            OntologyAttachmentProfile attachment,
            IReadOnlyList<GameObject> weaponPrefabs,
            GameObject beholder)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<OntologyCombatCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<OntologyCombatCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }

            var weapons = weaponManifest.Weapons.Select((entry, index) =>
                new OntologyWeaponPresentationDefinition
                {
                    weaponId = entry.weaponId,
                    visualPrefab = weaponPrefabs[index],
                    attachmentProfile = attachment,
                    weaponFamily = entry.weaponFamily,
                    attackActionId = entry.attackActionId,
                    actionDefinitionVersion =
                        entry.actionDefinitionVersion,
                    equipAnimationIntent =
                        entry.equipAnimationIntent,
                    unequipAnimationIntent =
                        entry.unequipAnimationIntent,
                    idleAnimationIntent =
                        entry.idleAnimationIntent,
                    moveAnimationIntent =
                        entry.moveAnimationIntent,
                    recoveryAnimationIntent =
                        entry.recoveryAnimationIntent,
                    swingVfxIntent = entry.swingVfxIntent,
                    contactModeId = entry.contactModeId
                }).ToArray();
            var monsters = new[]
            {
                new OntologyMonsterPresentationDefinition
                {
                    monsterId = "BeholderBasic",
                    visualPrefab = beholder
                }
            };
            var vfx = new[]
            {
                new OntologyCombatVfxDefinition
                {
                    intentId = "WeaponSwingSwordBasic",
                    prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                        "Assets/Matthew Guz/Slash Effects FREE/Prefab/Basic Slash Blue.prefab"),
                    localScale = Vector3.one,
                    lifetimeSeconds = 1.2f,
                    initialPoolSize = 3,
                    followAnchor = true
                },
                new OntologyCombatVfxDefinition
                {
                    intentId = "HitPhysicalLight",
                    prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                        "Assets/Lana Studio/Hyper Casual FX/Prefabs/Flash/Flash_round_ellow.prefab"),
                    localScale = Vector3.one * 0.55f,
                    lifetimeSeconds = 1f,
                    initialPoolSize = 4,
                    followAnchor = false
                }
            };
            catalog.ReplaceDefinitions(weapons, monsters, vfx);
            EditorUtility.SetDirty(catalog);
        }

        private static void AddMonsterToPlacementCatalog(
            GameObject beholder,
            OntologyWeaponContentManifest weaponManifest)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<OntologyPlaceableCatalog>(
                PlacementCatalogPath);
            if (catalog == null) return;

            var entries = new List<OntologyPlaceableDefinition>(catalog.Definitions);
            entries.RemoveAll(value =>
                value != null &&
                string.Equals(
                    value.definitionId,
                    "BeholderBasic",
                    StringComparison.Ordinal));
            var templatePath = CombatDataFolder + "/BeholderBasicTemplate.asset";
            var template = AssetDatabase.LoadAssetAtPath<OntologyMapObjectTemplate>(
                templatePath);
            if (template == null)
            {
                template = ScriptableObject.CreateInstance<OntologyMapObjectTemplate>();
                AssetDatabase.CreateAsset(template, templatePath);
            }
            template.description = "Basic ontology combat monster.";
            template.concepts = new[]
            {
                OntologyConcepts.Actor,
                OntologyConcepts.AutonomousAgent,
                OntologyConcepts.Monster,
                OntologyConcepts.Combatant,
                OntologyConcepts.Damageable
            };
            template.facts = new[]
                {
                    new OntologyFactEntry
                    {
                        predicate = "vitality_profile",
                        obj = "BeholderBasicVitality"
                    }
                }
                .Concat(CreateAutonomousMonsterFacts())
                .Append(new OntologyFactEntry
                {
                    predicate = OntologyPredicates.PhysicalProfile,
                    obj = OntologyObjects.AuthorityKinematic
                })
                .ToArray();
            EditorUtility.SetDirty(template);
            entries.Add(new OntologyPlaceableDefinition
            {
                definitionId = "BeholderBasic",
                placementKind = OntologyPlaceableKind.Monster,
                displayName = "Beholder",
                category = "Combat",
                description = "First Authority-driven combat monster.",
                prefab = beholder,
                previewPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/RPGMonsterPartnersPBRPolyart/Prefabs/Character/" +
                    "BeholderPolyartDefault.prefab"),
                ontologyTemplate = template,
                physicalProfile =
                    AssetDatabase.LoadAssetAtPath<OntologyPhysicalProfile>(
                        "Assets/Data/Ontology/Profiles/" +
                        "AuthorityKinematicPhysicalProfile.asset"),
                semanticContractVersion =
                    AutonomousMonsterSemanticContractVersion,
                ruleBlockMigrations =
                    new List<OntologyRuleBlockMigration>
                    {
                        new()
                        {
                            targetContractVersion =
                                AutonomousMonsterSemanticContractVersion,
                            fromRuleId = AutonomousCombatRuleId,
                            fromBindingVariable = "?actor",
                            replacement =
                                new OntologyRuleBlockBinding
                                {
                                    ruleId = AutonomousCombatRuleId,
                                    bindingVariable = "?actor"
                                }
                        }
                    },
                introducedFacts =
                    CreateAutonomousMonsterFactIntroductions(),
                introducedRuleBlocks =
                    new List<OntologyRuleBlockIntroduction>
                    {
                        new()
                        {
                            contractVersion =
                                AutonomousMonsterSemanticContractVersion,
                            binding = new OntologyRuleBlockBinding
                            {
                                ruleId = AutonomousCombatRuleId,
                                bindingVariable = "?actor"
                            }
                        },
                        new()
                        {
                            contractVersion =
                                AutonomousMonsterSemanticContractVersion,
                            binding = new OntologyRuleBlockBinding
                            {
                                ruleId = AutonomousTargetRuleId,
                                bindingVariable = "?actor"
                            }
                        },
                        new()
                        {
                            contractVersion =
                                AutonomousMonsterSemanticContractVersion,
                            binding = new OntologyRuleBlockBinding
                            {
                                ruleId = AutonomousChaseRuleId,
                                bindingVariable = "?actor"
                            }
                        },
                        new()
                        {
                            contractVersion =
                                AutonomousMonsterSemanticContractVersion,
                            binding = new OntologyRuleBlockBinding
                            {
                                ruleId = DefeatLootRuleId,
                                bindingVariable = "?target"
                            }
                        },
                        new()
                        {
                            contractVersion =
                                AutonomousMonsterSemanticContractVersion,
                            binding = new OntologyRuleBlockBinding
                            {
                                ruleId = CollectLootRuleId,
                                bindingVariable = "?target"
                            }
                        }
                    },
                retiredFacts =
                    new List<OntologyFactRetirement>
                    {
                        new()
                        {
                            contractVersion = 3,
                            fact = new OntologyFactEntry
                            {
                                predicate = "current_health",
                                obj = "30"
                            },
                            onlyWhenPredicateHasDifferentValue = true
                        },
                        new()
                        {
                            contractVersion = 3,
                            fact = new OntologyFactEntry
                            {
                                predicate = OntologyPredicates.IsAlive,
                                obj = bool.TrueString
                            },
                            onlyWhenPredicateHasDifferentValue = true
                        }
                    },
                defaultRuleBlocks =
                    new List<OntologyRuleBlockBinding>
                    {
                        new()
                        {
                            ruleId = AutonomousCombatRuleId,
                            bindingVariable = "?actor"
                        },
                        new()
                        {
                            ruleId = AutonomousTargetRuleId,
                            bindingVariable = "?actor"
                        },
                        new()
                        {
                            ruleId = AutonomousChaseRuleId,
                            bindingVariable = "?actor"
                        },
                        new()
                        {
                            ruleId = DefeatLootRuleId,
                            bindingVariable = "?target"
                        },
                        new()
                        {
                            ruleId = CollectLootRuleId,
                            bindingVariable = "?target"
                        }
                    },
                placementPolicy = new OntologyPlacementPolicy
                {
                    maximumDistanceFromActor = 8f,
                    minimumDistanceFromPlacedObject = 1f,
                    requiredSurface = OntologyPlacementSurfaceKind.Ground,
                    defaultLocalScale = Vector3.one
                }
            });
            catalog.ReplaceDefinitions(entries);
            EditorUtility.SetDirty(catalog);
            AddWeaponsToPlacementCatalog(catalog, weaponManifest);
        }

        private static void AddWeaponsToPlacementCatalog(
            OntologyPlaceableCatalog catalog,
            OntologyWeaponContentManifest weaponManifest)
        {
            var entries = new List<OntologyPlaceableDefinition>(catalog.Definitions);
            foreach (var weapon in weaponManifest.Weapons)
            {
                var weaponId = weapon.weaponId;
                entries.RemoveAll(value =>
                    value != null &&
                    string.Equals(
                        value.definitionId,
                        weaponId,
                        StringComparison.Ordinal));
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    CombatPrefabFolder + "/Ontology" + weaponId + ".prefab");
                var templatePath =
                    CombatDataFolder + "/" + weaponId + "Template.asset";
                var template =
                    AssetDatabase.LoadAssetAtPath<OntologyMapObjectTemplate>(
                        templatePath);
                if (template == null)
                {
                    template = ScriptableObject.CreateInstance<OntologyMapObjectTemplate>();
                    AssetDatabase.CreateAsset(template, templatePath);
                }
                template.description = "Equipable sword weapon.";
                template.concepts = new[]
                {
                    "Item",
                    "Weapon",
                    weapon.weaponFamily,
                    "Carryable"
                };
                template.facts = new[]
                {
                    new OntologyFactEntry
                    {
                        predicate = "attachment_profile",
                        obj = "RightHandCarry"
                    },
                    new OntologyFactEntry
                    {
                        predicate = "grants_capability",
                        obj = weapon.grantedCapability
                    },
                    new OntologyFactEntry
                    {
                        predicate = "damage_profile",
                        obj = "BasicSwordDamage"
                    },
                    new OntologyFactEntry
                    {
                        predicate = "attack_damage",
                        obj = "10"
                    },
                    new OntologyFactEntry
                    {
                        predicate = OntologyPredicates.AttackAction,
                        obj = "attack"
                    },
                    new OntologyFactEntry
                    {
                        predicate = OntologyPredicates.AttackRange,
                        obj = "3"
                    },
                    new OntologyFactEntry
                    {
                        predicate = OntologyPredicates.AttackCooldown,
                        obj = "1"
                    },
                    new OntologyFactEntry
                    {
                        predicate =
                            OntologyPredicates.AttackContactMode,
                        obj = weapon.contactModeId
                    },
                    new OntologyFactEntry
                    {
                        predicate = "can_equip",
                        obj = "True"
                    },
                    new OntologyFactEntry
                    {
                        predicate = "has_slot",
                        obj = "RightHand"
                    },
                    new OntologyFactEntry
                    {
                        predicate = "pickup_behavior",
                        obj = "SelectThenCarry"
                    },
                    new OntologyFactEntry
                    {
                        predicate = OntologyPredicates.EquipAction,
                        obj = EquipActionId
                    },
                    new OntologyFactEntry
                    {
                        predicate = OntologyPredicates.UnequipAction,
                        obj = UnequipActionId
                    },
                    new OntologyFactEntry
                    {
                        predicate =
                            OntologyPredicates.InteractionRange,
                        obj = "3"
                    },
                    new OntologyFactEntry
                    {
                        predicate = OntologyPredicates.PhysicalProfile,
                        obj = "HandheldWeapon"
                    }
                };
                EditorUtility.SetDirty(template);
                entries.Add(new OntologyPlaceableDefinition
                {
                    definitionId = weaponId,
                    placementKind = OntologyPlaceableKind.Object,
                    displayName = weaponId,
                    category = "Combat",
                    description = "Authority-driven sword weapon.",
                    prefab = prefab,
                    previewPrefab = weapon.sourcePrefab,
                    ontologyTemplate = template,
                    physicalProfile =
                        AssetDatabase.LoadAssetAtPath<OntologyPhysicalProfile>(
                            WeaponPhysicalProfilePath),
                    attachmentProfile =
                        AssetDatabase.LoadAssetAtPath<OntologyAttachmentProfile>(
                            AttachmentPath),
                    semanticContractVersion =
                        WeaponSemanticContractVersion,
                    ruleBlockMigrations =
                        new List<OntologyRuleBlockMigration>
                        {
                            new()
                            {
                                targetContractVersion = 2,
                                fromRuleId =
                                    "AutoCarryNearbyCarryable",
                                fromBindingVariable = "?object",
                                replacement =
                                    new OntologyRuleBlockBinding
                                    {
                                        ruleId =
                                            "EquipItemOnInteractionIntent",
                                        bindingVariable = "?target"
                                    }
                            },
                            new()
                            {
                                targetContractVersion =
                                    WeaponSemanticContractVersion,
                                fromRuleId = AttackRuleId,
                                fromBindingVariable = "?tool",
                                replacement =
                                    new OntologyRuleBlockBinding
                                    {
                                        ruleId = AttackRuleId,
                                        bindingVariable = "?tool"
                                    }
                            }
                        },
                    introducedFacts =
                        CreateAttackFactIntroductions(),
                    introducedRuleBlocks =
                        new List<OntologyRuleBlockIntroduction>
                        {
                            new()
                            {
                                contractVersion =
                                    AttackSemanticContractVersion,
                                binding =
                                    new OntologyRuleBlockBinding
                                    {
                                        ruleId = AttackRuleId,
                                        bindingVariable = "?tool"
                                    }
                            },
                            new()
                            {
                                contractVersion =
                                    SwingSemanticContractVersion,
                                binding =
                                    new OntologyRuleBlockBinding
                                    {
                                        ruleId = SwingRuleId,
                                        bindingVariable = "?tool"
                                    }
                            }
                            ,
                            new()
                            {
                                contractVersion =
                                    WeaponSemanticContractVersion,
                                binding =
                                    new OntologyRuleBlockBinding
                                    {
                                        ruleId = UnequipRuleId,
                                        bindingVariable = "?target"
                                    }
                            }
                        },
                    retiredRuleBlocks =
                        new List<OntologyRuleBlockRetirement>
                        {
                            new()
                            {
                                contractVersion = 3,
                                ruleId =
                                    "AutoCarryNearbyCarryable",
                                bindingVariable = "?object"
                            }
                        },
                    defaultRuleBlocks =
                        new List<OntologyRuleBlockBinding>
                        {
                            new()
                            {
                                ruleId =
                                    EquipRuleId,
                                bindingVariable = "?target"
                            },
                            new()
                            {
                                ruleId = UnequipRuleId,
                                bindingVariable = "?target"
                            },
                            new()
                            {
                                ruleId = AttackRuleId,
                                bindingVariable = "?tool"
                            },
                            new()
                            {
                                ruleId = SwingRuleId,
                                bindingVariable = "?tool"
                            }
                        },
                    placementPolicy = new OntologyPlacementPolicy
                    {
                        maximumDistanceFromActor = 8f,
                        minimumDistanceFromPlacedObject = 0.4f,
                        requiredSurface = OntologyPlacementSurfaceKind.Ground,
                        defaultLocalScale = Vector3.one
                    }
                });
            }
            catalog.ReplaceDefinitions(entries);
            EditorUtility.SetDirty(catalog);
        }

        private static void InstallRuntimePresentation()
        {
            var player =
                UnityEngine.Object.FindObjectsByType<
                        OntologyInputSystemPlayerInput>(
                        FindObjectsInactive.Include)
                    .FirstOrDefault();
            var client =
                UnityEngine.Object.FindObjectsByType<OntologyWorldAuthorityClient>(
                        FindObjectsInactive.Include)
                    .FirstOrDefault();
            if (player == null)
            {
                Debug.LogWarning(
                    "Combat assets were built, but the open scene has no ontology player. " +
                    "Runtime components were not installed.");
                return;
            }

            var playerObject = player.gameObject;
            var identity =
                playerObject.GetComponent<OntologyAuthorityEntityIdentity>() ??
                playerObject.AddComponent<OntologyAuthorityEntityIdentity>();
            var controller =
                playerObject.GetComponent<OntologyCombatController>() ??
                playerObject.AddComponent<OntologyCombatController>();
            controller.Configure(
                client,
                identity,
                null,
                Camera.main);
            var animationAdapter =
                playerObject.GetComponent<OntologyAnimationAdapter>();
            animationAdapter?.ConfigureEquipmentPresentation(
                AssetDatabase.LoadAssetAtPath<OntologyCombatCatalog>(
                    CatalogPath));

            var vfxAdapter =
                UnityEngine.Object.FindObjectsByType<OntologyCombatVfxAdapter>(
                        FindObjectsInactive.Include)
                    .FirstOrDefault();
            if (vfxAdapter == null)
            {
                var root = new GameObject("OntologyCombatPresentation");
                vfxAdapter = root.AddComponent<OntologyCombatVfxAdapter>();
            }
            vfxAdapter.Configure(
                AssetDatabase.LoadAssetAtPath<OntologyCombatCatalog>(CatalogPath));
            EditorSceneManager.MarkSceneDirty(playerObject.scene);
            EditorSceneManager.SaveScene(playerObject.scene);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = path.Substring(0, path.LastIndexOf('/'));
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(path.LastIndexOf('/') + 1));
        }

        public static void SynchronizeOntologyContracts()
        {
            var ruleDatabase =
                AssetDatabase.LoadAssetAtPath<OntologyRuleDatabase>(
                    RuleDatabasePath);
            var settings =
                AssetDatabase.LoadAssetAtPath<OntologyWorldAuthoritySettings>(
                    AuthoritySettingsPath);
            var weaponManifest =
                AssetDatabase.LoadAssetAtPath<OntologyWeaponContentManifest>(
                    WeaponManifestPath);
            var placementCatalog =
                AssetDatabase.LoadAssetAtPath<OntologyPlaceableCatalog>(
                    PlacementCatalogPath);
            var combatCatalog =
                AssetDatabase.LoadAssetAtPath<OntologyCombatCatalog>(
                    CatalogPath);
            var rulePresetDatabase =
                AssetDatabase.LoadAssetAtPath<
                    OntologyRuleBlockPresetDatabase>(
                    RulePresetDatabasePath);
            if (ruleDatabase == null || settings == null ||
                weaponManifest == null || placementCatalog == null ||
                rulePresetDatabase == null)
            {
                throw new InvalidOperationException(
                    "Combat ontology contract assets are incomplete.");
            }

            var rule = ruleDatabase.Definitions.FirstOrDefault(value =>
                value != null &&
                string.Equals(
                    value.id,
                    AttackRuleId,
                    StringComparison.Ordinal)) ??
                ruleDatabase.CreateDefinition(AttackRuleId);
            ConfigureAttackRule(rule);
            var swingRule = ruleDatabase.Definitions.FirstOrDefault(value =>
                value != null &&
                string.Equals(
                    value.id,
                    SwingRuleId,
                    StringComparison.Ordinal)) ??
                ruleDatabase.CreateDefinition(SwingRuleId);
            ConfigureSwingRule(swingRule);
            var unequipRule =
                GetOrCreateRule(ruleDatabase, UnequipRuleId);
            ConfigureUnequipRule(unequipRule);
            var autonomousTargetRule =
                GetOrCreateRule(ruleDatabase, AutonomousTargetRuleId);
            ConfigureAutonomousTargetRule(autonomousTargetRule);
            var autonomousChaseRule =
                GetOrCreateRule(ruleDatabase, AutonomousChaseRuleId);
            ConfigureAutonomousChaseRule(autonomousChaseRule);
            var autonomousRule =
                ruleDatabase.Definitions.FirstOrDefault(value =>
                    value != null &&
                    string.Equals(
                        value.id,
                        AutonomousCombatRuleId,
                        StringComparison.Ordinal)) ??
                ruleDatabase.CreateDefinition(AutonomousCombatRuleId);
            ConfigureAutonomousCombatRule(autonomousRule);
            var defeatLootRule =
                GetOrCreateRule(ruleDatabase, DefeatLootRuleId);
            ConfigureDefeatLootRule(defeatLootRule);
            var collectLootRule =
                GetOrCreateRule(ruleDatabase, CollectLootRuleId);
            ConfigureCollectLootRule(collectLootRule);
            var playerRespawnRule =
                ruleDatabase.Definitions.FirstOrDefault(value =>
                    value != null &&
                    string.Equals(
                        value.id,
                        PlayerRespawnRuleId,
                        StringComparison.Ordinal)) ??
                ruleDatabase.CreateDefinition(PlayerRespawnRuleId);
            ConfigurePlayerRespawnRule(playerRespawnRule);
            var playerLocomotionRule =
                ruleDatabase.Definitions.FirstOrDefault(value =>
                    value != null &&
                    string.Equals(
                        value.id,
                        PlayerLocomotionRuleId,
                        StringComparison.Ordinal)) ??
                ruleDatabase.CreateDefinition(PlayerLocomotionRuleId);
            ConfigurePlayerLocomotionRule(playerLocomotionRule);
            var playerJumpRule =
                ruleDatabase.Definitions.FirstOrDefault(value =>
                    value != null &&
                    string.Equals(
                        value.id,
                        PlayerJumpRuleId,
                        StringComparison.Ordinal)) ??
                ruleDatabase.CreateDefinition(PlayerJumpRuleId);
            ConfigurePlayerJumpRule(playerJumpRule);
            settings.developmentPackageVersion = "3.9.0";
            var developmentRules =
                (settings.developmentRules ??
                 Array.Empty<OntologyAuthorityDevelopmentRule>())
                .Where(value =>
                    value != null &&
                    !string.Equals(
                        value.ruleId,
                        AttackRuleId,
                        StringComparison.Ordinal) &&
                    !string.Equals(
                        value.ruleId,
                        SwingRuleId,
                        StringComparison.Ordinal) &&
                    !string.Equals(
                        value.ruleId,
                        UnequipRuleId,
                        StringComparison.Ordinal) &&
                    !string.Equals(
                        value.ruleId,
                        AutonomousTargetRuleId,
                        StringComparison.Ordinal) &&
                    !string.Equals(
                        value.ruleId,
                        AutonomousChaseRuleId,
                        StringComparison.Ordinal) &&
                    !string.Equals(
                        value.ruleId,
                        AutonomousCombatRuleId,
                        StringComparison.Ordinal) &&
                    !string.Equals(
                        value.ruleId,
                        DefeatLootRuleId,
                        StringComparison.Ordinal) &&
                    !string.Equals(
                        value.ruleId,
                        CollectLootRuleId,
                        StringComparison.Ordinal) &&
                    !string.Equals(
                        value.ruleId,
                        PlayerRespawnRuleId,
                        StringComparison.Ordinal) &&
                    !string.Equals(
                        value.ruleId,
                        PlayerLocomotionRuleId,
                        StringComparison.Ordinal) &&
                    !string.Equals(
                        value.ruleId,
                        OntologyRuleBlocks.JumpPlayerFromIntent,
                        StringComparison.Ordinal))
                .ToList();
            developmentRules.Add(new OntologyAuthorityDevelopmentRule
            {
                ruleId = AttackRuleId,
                definitionVersion = rule.catalogVersion
            });
            developmentRules.Add(new OntologyAuthorityDevelopmentRule
            {
                ruleId = SwingRuleId,
                definitionVersion = swingRule.catalogVersion
            });
            developmentRules.Add(new OntologyAuthorityDevelopmentRule
            {
                ruleId = UnequipRuleId,
                definitionVersion = unequipRule.catalogVersion
            });
            developmentRules.Add(new OntologyAuthorityDevelopmentRule
            {
                ruleId = AutonomousTargetRuleId,
                definitionVersion = autonomousTargetRule.catalogVersion
            });
            developmentRules.Add(new OntologyAuthorityDevelopmentRule
            {
                ruleId = AutonomousChaseRuleId,
                definitionVersion = autonomousChaseRule.catalogVersion
            });
            developmentRules.Add(new OntologyAuthorityDevelopmentRule
            {
                ruleId = AutonomousCombatRuleId,
                definitionVersion = autonomousRule.catalogVersion
            });
            developmentRules.Add(new OntologyAuthorityDevelopmentRule
            {
                ruleId = DefeatLootRuleId,
                definitionVersion = defeatLootRule.catalogVersion
            });
            developmentRules.Add(new OntologyAuthorityDevelopmentRule
            {
                ruleId = CollectLootRuleId,
                definitionVersion = collectLootRule.catalogVersion
            });
            developmentRules.Add(new OntologyAuthorityDevelopmentRule
            {
                ruleId = PlayerRespawnRuleId,
                definitionVersion = playerRespawnRule.catalogVersion
            });
            developmentRules.Add(new OntologyAuthorityDevelopmentRule
            {
                ruleId = PlayerLocomotionRuleId,
                definitionVersion = playerLocomotionRule.catalogVersion
            });
            developmentRules.Add(new OntologyAuthorityDevelopmentRule
            {
                ruleId = PlayerJumpRuleId,
                definitionVersion = playerJumpRule.catalogVersion
            });
            settings.developmentRules = developmentRules.ToArray();

            var actions =
                (settings.developmentActions ??
                 Array.Empty<OntologyAuthorityDevelopmentAction>())
                .Where(value => value != null)
                .ToList();
            var equip = GetOrCreateAction(actions, EquipActionId);
            equip.definitionVersion = EquipActionVersion;
            equip.predicateId = OntologyPredicates.InteractionIntent;
            equip.requiresTool = false;
            equip.objectPattern = "?target";
            equip.structuredDefinitionJson =
                JsonUtility.ToJson(CreateEquipTransportDefinition());
            var unequip = GetOrCreateAction(actions, UnequipActionId);
            unequip.definitionVersion = UnequipActionVersion;
            unequip.predicateId = OntologyPredicates.UnequipIntent;
            unequip.requiresTool = false;
            unequip.objectPattern = "?target";
            unequip.structuredDefinitionJson =
                JsonUtility.ToJson(CreateUnequipTransportDefinition());
            var attack = actions.FirstOrDefault(value =>
                value != null &&
                string.Equals(
                    value.actionId,
                    "attack",
                    StringComparison.Ordinal));
            if (attack == null)
            {
                attack = new OntologyAuthorityDevelopmentAction
                {
                    actionId = "attack"
                };
                actions.Add(attack);
            }
            attack.definitionVersion = AttackActionVersion;
            attack.predicateId = OntologyPredicates.PrimaryAttackIntent;
            attack.requiresTool = true;
            attack.objectPattern = "?target";
            attack.structuredDefinitionJson =
                JsonUtility.ToJson(CreateAttackTransportDefinition());
            var swing = actions.FirstOrDefault(value =>
                value != null &&
                string.Equals(
                    value.actionId,
                    "swing_weapon",
                    StringComparison.Ordinal));
            if (swing == null)
            {
                swing = new OntologyAuthorityDevelopmentAction
                {
                    actionId = "swing_weapon"
                };
                actions.Add(swing);
            }
            swing.definitionVersion = SwingActionVersion;
            swing.predicateId = OntologyPredicates.PrimarySwingIntent;
            swing.requiresTool = true;
            swing.objectPattern = "?actor";
            swing.structuredDefinitionJson =
                JsonUtility.ToJson(CreateSwingTransportDefinition());
            var autonomousAttack = actions.FirstOrDefault(value =>
                value != null &&
                string.Equals(
                    value.actionId,
                    AutonomousAttackActionId,
                    StringComparison.Ordinal));
            if (autonomousAttack == null)
            {
                autonomousAttack =
                    new OntologyAuthorityDevelopmentAction
                    {
                        actionId = AutonomousAttackActionId
                    };
                actions.Add(autonomousAttack);
            }
            autonomousAttack.definitionVersion =
                AutonomousAttackActionVersion;
            autonomousAttack.predicateId =
                OntologyPredicates.AutonomousAttackIntent;
            autonomousAttack.requiresTool = false;
            autonomousAttack.objectPattern = "?target";
            autonomousAttack.structuredDefinitionJson =
                JsonUtility.ToJson(
                    CreateAutonomousAttackTransportDefinition());
            var autonomousTarget =
                GetOrCreateAction(actions, AutonomousTargetActionId);
            autonomousTarget.definitionVersion =
                AutonomousTargetActionVersion;
            autonomousTarget.predicateId =
                OntologyPredicates.AutonomousTargetIntent;
            autonomousTarget.requiresTool = false;
            autonomousTarget.objectPattern = "?target";
            autonomousTarget.structuredDefinitionJson =
                JsonUtility.ToJson(
                    CreateAutonomousTargetTransportDefinition());
            var autonomousChase =
                GetOrCreateAction(actions, AutonomousChaseActionId);
            autonomousChase.definitionVersion =
                AutonomousChaseActionVersion;
            autonomousChase.predicateId =
                OntologyPredicates.AutonomousChaseIntent;
            autonomousChase.requiresTool = false;
            autonomousChase.objectPattern = "?target";
            autonomousChase.structuredDefinitionJson =
                JsonUtility.ToJson(
                    CreateAutonomousChaseTransportDefinition());
            var collectLoot =
                GetOrCreateAction(actions, CollectLootActionId);
            collectLoot.definitionVersion = CollectLootActionVersion;
            collectLoot.predicateId =
                OntologyPredicates.LootCollectionIntent;
            collectLoot.requiresTool = false;
            collectLoot.objectPattern = "?target";
            collectLoot.structuredDefinitionJson =
                JsonUtility.ToJson(
                    CreateCollectLootTransportDefinition());
            var playerRespawn = actions.FirstOrDefault(value =>
                value != null &&
                string.Equals(
                    value.actionId,
                    PlayerRespawnActionId,
                    StringComparison.Ordinal));
            if (playerRespawn == null)
            {
                playerRespawn =
                    new OntologyAuthorityDevelopmentAction
                    {
                        actionId = PlayerRespawnActionId
                    };
                actions.Add(playerRespawn);
            }
            playerRespawn.definitionVersion =
                PlayerRespawnActionVersion;
            playerRespawn.predicateId =
                OntologyPredicates.RespawnIntent;
            playerRespawn.requiresTool = false;
            playerRespawn.objectPattern = "?actor";
            playerRespawn.structuredDefinitionJson =
                JsonUtility.ToJson(
                    CreatePlayerRespawnTransportDefinition());
            var playerLocomotion = actions.FirstOrDefault(value =>
                value != null &&
                string.Equals(
                    value.actionId,
                    PlayerLocomotionActionId,
                    StringComparison.Ordinal));
            if (playerLocomotion == null)
            {
                playerLocomotion =
                    new OntologyAuthorityDevelopmentAction
                    {
                        actionId = PlayerLocomotionActionId
                    };
                actions.Add(playerLocomotion);
            }
            playerLocomotion.definitionVersion =
                PlayerLocomotionActionVersion;
            playerLocomotion.predicateId =
                OntologyPredicates.LocomotionIntent;
            playerLocomotion.requiresTool = false;
            playerLocomotion.objectPattern = "?actor";
            playerLocomotion.structuredDefinitionJson =
                JsonUtility.ToJson(
                    CreatePlayerLocomotionTransportDefinition());
            var playerJump =
                GetOrCreateAction(actions, PlayerJumpActionId);
            playerJump.definitionVersion =
                PlayerJumpActionVersion;
            playerJump.predicateId =
                OntologyPredicates.JumpIntent;
            playerJump.requiresTool = false;
            playerJump.objectPattern = "?actor";
            playerJump.structuredDefinitionJson =
                JsonUtility.ToJson(
                    CreatePlayerJumpTransportDefinition());
            settings.developmentActions = actions.ToArray();

            rulePresetDatabase.Upsert(
                CreateAutonomousMonsterPreset());

            foreach (var definition in placementCatalog.Definitions)
            {
                if (definition == null ||
                    definition.ontologyTemplate == null ||
                    !(definition.ontologyTemplate.concepts ??
                      Array.Empty<string>())
                    .Contains(OntologyConcepts.Monster))
                {
                    continue;
                }

                foreach (var fact in CreateAutonomousMonsterFacts())
                {
                    EnsureFact(
                        definition.ontologyTemplate,
                        fact.predicate,
                        fact.obj);
                }
                definition.introducedFacts ??=
                    new List<OntologyFactIntroduction>();
                foreach (var introduction in
                         CreateAutonomousMonsterFactIntroductions())
                {
                    EnsureFactIntroduction(
                        definition.introducedFacts,
                        introduction);
                }
                definition.defaultRuleBlocks ??=
                    new List<OntologyRuleBlockBinding>();
                EnsureBinding(
                    definition.defaultRuleBlocks,
                    AutonomousCombatRuleId,
                    "?actor");
                EnsureBinding(
                    definition.defaultRuleBlocks,
                    AutonomousTargetRuleId,
                    "?actor");
                EnsureBinding(
                    definition.defaultRuleBlocks,
                    AutonomousChaseRuleId,
                    "?actor");
                EnsureBinding(
                    definition.defaultRuleBlocks,
                    DefeatLootRuleId,
                    "?target");
                EnsureBinding(
                    definition.defaultRuleBlocks,
                    CollectLootRuleId,
                    "?target");
                definition.introducedRuleBlocks ??=
                    new List<OntologyRuleBlockIntroduction>();
                EnsureRuleIntroduction(
                    definition.introducedRuleBlocks,
                    AutonomousMonsterSemanticContractVersion,
                    AutonomousCombatRuleId,
                    "?actor");
                EnsureRuleIntroduction(
                    definition.introducedRuleBlocks,
                    AutonomousMonsterSemanticContractVersion,
                    AutonomousTargetRuleId,
                    "?actor");
                EnsureRuleIntroduction(
                    definition.introducedRuleBlocks,
                    AutonomousMonsterSemanticContractVersion,
                    AutonomousChaseRuleId,
                    "?actor");
                EnsureRuleIntroduction(
                    definition.introducedRuleBlocks,
                    AutonomousMonsterSemanticContractVersion,
                    DefeatLootRuleId,
                    "?target");
                EnsureRuleIntroduction(
                    definition.introducedRuleBlocks,
                    AutonomousMonsterSemanticContractVersion,
                    CollectLootRuleId,
                    "?target");
                definition.ruleBlockMigrations ??=
                    new List<OntologyRuleBlockMigration>();
                EnsureRuleMigration(
                    definition.ruleBlockMigrations,
                    AutonomousMonsterSemanticContractVersion,
                    AutonomousCombatRuleId,
                    "?actor",
                    AutonomousCombatRuleId,
                    "?actor");
                definition.semanticContractVersion = Math.Max(
                    definition.semanticContractVersion,
                    AutonomousMonsterSemanticContractVersion);
                EditorUtility.SetDirty(definition.ontologyTemplate);
            }

            foreach (var weapon in weaponManifest.Weapons)
            {
                if (weapon == null) continue;
                weapon.attackActionId = "attack";
                weapon.actionDefinitionVersion = AttackActionVersion;
            }

            foreach (var definition in placementCatalog.Definitions)
            {
                if (definition == null ||
                    definition.ontologyTemplate == null ||
                    !definition.ontologyTemplate.concepts.Contains(
                        OntologyConcepts.Weapon))
                {
                    continue;
                }

                EnsureFact(
                    definition.ontologyTemplate,
                    OntologyPredicates.AttackAction,
                    OntologyActions.PrimaryAttack);
                EnsureFact(
                    definition.ontologyTemplate,
                    OntologyPredicates.EquipAction,
                    EquipActionId);
                EnsureFact(
                    definition.ontologyTemplate,
                    OntologyPredicates.UnequipAction,
                    UnequipActionId);
                EnsureFact(
                    definition.ontologyTemplate,
                    OntologyPredicates.InteractionRange,
                    "3");
                EnsureFact(
                    definition.ontologyTemplate,
                    OntologyPredicates.SwingAction,
                    "swing_weapon");
                EnsureFact(
                    definition.ontologyTemplate,
                    OntologyPredicates.AttackRange,
                    "3");
                EnsureFact(
                    definition.ontologyTemplate,
                    OntologyPredicates.AttackCooldown,
                    "1");
                var authoredWeapon = weaponManifest.Weapons.FirstOrDefault(
                    value =>
                        value != null &&
                        string.Equals(
                            value.weaponId,
                            definition.definitionId,
                            StringComparison.Ordinal));
                if (authoredWeapon == null ||
                    string.IsNullOrWhiteSpace(
                        authoredWeapon.contactModeId))
                {
                    continue;
                }
                EnsureFact(
                    definition.ontologyTemplate,
                    OntologyPredicates.AttackContactMode,
                    authoredWeapon.contactModeId);
                definition.introducedFacts ??=
                    new List<OntologyFactIntroduction>();
                foreach (var introduction in
                         CreateAttackFactIntroductions())
                {
                    if (definition.introducedFacts.Any(value =>
                            value?.fact != null &&
                            value.contractVersion ==
                            introduction.contractVersion &&
                            string.Equals(
                                value.fact.predicate,
                                introduction.fact.predicate,
                                StringComparison.Ordinal)))
                    {
                        continue;
                    }
                    definition.introducedFacts.Add(introduction);
                }
                EnsureBinding(
                    definition.defaultRuleBlocks,
                    EquipRuleId,
                    "?target");
                EnsureBinding(
                    definition.defaultRuleBlocks,
                    UnequipRuleId,
                    "?target");
                EnsureBinding(
                    definition.defaultRuleBlocks,
                    AttackRuleId,
                    "?tool");
                EnsureBinding(
                    definition.defaultRuleBlocks,
                    SwingRuleId,
                    "?tool");
                definition.semanticContractVersion = Math.Max(
                    definition.semanticContractVersion,
                    WeaponSemanticContractVersion);
                definition.ruleBlockMigrations ??=
                    new List<OntologyRuleBlockMigration>();
                EnsureRuleMigration(
                    definition.ruleBlockMigrations,
                    WeaponSemanticContractVersion,
                    AttackRuleId,
                    "?tool",
                    AttackRuleId,
                    "?tool");
                definition.introducedRuleBlocks ??=
                    new List<OntologyRuleBlockIntroduction>();
                if (!definition.introducedRuleBlocks.Any(value =>
                        value?.binding != null &&
                        value.contractVersion ==
                        AttackSemanticContractVersion &&
                        string.Equals(
                            value.binding.ruleId,
                            AttackRuleId,
                            StringComparison.Ordinal)))
                {
                    definition.introducedRuleBlocks.Add(
                        new OntologyRuleBlockIntroduction
                        {
                            contractVersion =
                                AttackSemanticContractVersion,
                            binding = new OntologyRuleBlockBinding
                            {
                                ruleId = AttackRuleId,
                                bindingVariable = "?tool"
                            }
                        });
                }
                if (!definition.introducedRuleBlocks.Any(value =>
                        value?.binding != null &&
                        value.contractVersion ==
                        WeaponSemanticContractVersion &&
                        string.Equals(
                            value.binding.ruleId,
                            UnequipRuleId,
                            StringComparison.Ordinal)))
                {
                    definition.introducedRuleBlocks.Add(
                        new OntologyRuleBlockIntroduction
                        {
                            contractVersion =
                                WeaponSemanticContractVersion,
                            binding = new OntologyRuleBlockBinding
                            {
                                ruleId = UnequipRuleId,
                                bindingVariable = "?target"
                            }
                        });
                }
                if (!definition.introducedRuleBlocks.Any(value =>
                        value?.binding != null &&
                        value.contractVersion ==
                        SwingSemanticContractVersion &&
                        string.Equals(
                            value.binding.ruleId,
                            SwingRuleId,
                            StringComparison.Ordinal)))
                {
                    definition.introducedRuleBlocks.Add(
                        new OntologyRuleBlockIntroduction
                        {
                            contractVersion =
                                SwingSemanticContractVersion,
                            binding = new OntologyRuleBlockBinding
                            {
                                ruleId = SwingRuleId,
                                bindingVariable = "?tool"
                            }
                        });
                }
                EditorUtility.SetDirty(definition.ontologyTemplate);
            }

            if (combatCatalog != null)
            {
                foreach (var weapon in combatCatalog.Weapons)
                {
                    if (weapon == null) continue;
                    weapon.attackActionId = "attack";
                    weapon.actionDefinitionVersion =
                        AttackActionVersion;
                    var authoredWeapon =
                        weaponManifest.Weapons.FirstOrDefault(
                            value =>
                                value != null &&
                                string.Equals(
                                    value.weaponId,
                                    weapon.weaponId,
                                    StringComparison.Ordinal));
                    if (authoredWeapon != null)
                    {
                        weapon.contactModeId =
                            authoredWeapon.contactModeId;
                    }
                }
                EditorUtility.SetDirty(combatCatalog);
            }

            EditorUtility.SetDirty(ruleDatabase);
            EditorUtility.SetDirty(settings);
            EditorUtility.SetDirty(weaponManifest);
            EditorUtility.SetDirty(placementCatalog);
            EditorUtility.SetDirty(rulePresetDatabase);
        }

        private static OntologyRuleDefinition GetOrCreateRule(
            OntologyRuleDatabase database,
            string ruleId)
        {
            return database.Definitions.FirstOrDefault(value =>
                       value != null &&
                       string.Equals(
                           value.id,
                           ruleId,
                           StringComparison.Ordinal)) ??
                   database.CreateDefinition(ruleId);
        }

        private static OntologyAuthorityDevelopmentAction GetOrCreateAction(
            ICollection<OntologyAuthorityDevelopmentAction> actions,
            string actionId)
        {
            var action = actions.FirstOrDefault(value =>
                value != null &&
                string.Equals(
                    value.actionId,
                    actionId,
                    StringComparison.Ordinal));
            if (action != null) return action;
            action = new OntologyAuthorityDevelopmentAction
            {
                actionId = actionId
            };
            actions.Add(action);
            return action;
        }

        private static OntologyActionEffectDefinition
            CreateEquipTransportDefinition()
        {
            return new OntologyActionEffectDefinition
            {
                actionVerb = EquipActionId,
                requiresTool = false,
                presentation =
                    new OntologyActionPresentationDefinition
                    {
                        actorAnimationIntent =
                            OntologyAnimationIntentIds.WeaponEquip
                    },
                ruleInvocation =
                    new OntologyActionRuleInvocationDefinition
                    {
                        ruleId = EquipRuleId,
                        bindingVariable = "?target",
                        bindingEntityPattern = "?target",
                        intentSubjectPattern = "?actor",
                        intentPredicate =
                            OntologyPredicates.InteractionIntent,
                        intentObjectPattern = "?target"
                    },
                runtimeConstraints =
                    new OntologyActionRuntimeConstraints
                    {
                        maxActorTargetDistanceFrom =
                            new OntologyNumericFactSource
                            {
                                subject = "?target",
                                predicate =
                                    OntologyPredicates.InteractionRange
                            }
                    }
            };
        }

        private static void ConfigureUnequipRule(
            OntologyRuleDefinition rule)
        {
            rule.id = UnequipRuleId;
            rule.catalogVersion = PlayerLocomotionRuleVersion;
            rule.description =
                "An unequip intent removes both sides of an authored " +
                "equipment relation only while this Rule Block remains " +
                "assigned to the item.";
            rule.conditions = new List<OntologyCondition>
            {
                OntologyCondition.Fact(
                    "?actor",
                    OntologyPredicates.UnequipIntent,
                    "?target"),
                OntologyCondition.HasConcept(
                    "?actor",
                    OntologyConcepts.Actor),
                OntologyCondition.HasConcept("?target", "Item"),
                OntologyCondition.Fact(
                    "?target",
                    OntologyPredicates.HasRuleBlock,
                    UnequipRuleId),
                OntologyCondition.Fact(
                    "?target",
                    OntologyPredicates.EquippedBy,
                    "?actor")
            };
            rule.effects = new List<OntologyEffect>
            {
                OntologyEffect.RemoveFact(
                    "?target",
                    OntologyPredicates.EquippedBy,
                    "?actor"),
                OntologyEffect.RemoveFact(
                    "?actor",
                    OntologyPredicates.EquippedItem,
                    "?target")
            };
            rule.runtimePresentation =
                new OntologyActionPresentationDefinition
                {
                    actorAnimationIntent =
                        OntologyAnimationIntentIds.WeaponUnequip
                };
        }

        private static OntologyActionEffectDefinition
            CreateUnequipTransportDefinition()
        {
            return new OntologyActionEffectDefinition
            {
                actionVerb = UnequipActionId,
                requiresTool = false,
                ruleInvocation =
                    new OntologyActionRuleInvocationDefinition
                    {
                        ruleId = UnequipRuleId,
                        bindingVariable = "?target",
                        bindingEntityPattern = "?target",
                        intentSubjectPattern = "?actor",
                        intentPredicate =
                            OntologyPredicates.UnequipIntent,
                        intentObjectPattern = "?target"
                    },
                presentation =
                    new OntologyActionPresentationDefinition(),
                runtimeConstraints =
                    new OntologyActionRuntimeConstraints()
            };
        }

        private static void ConfigureAutonomousTargetRule(
            OntologyRuleDefinition rule)
        {
            rule.id = AutonomousTargetRuleId;
            rule.catalogVersion = 1;
            rule.description =
                "An autonomous actor may acquire the nearest living hostile " +
                "entity matching its authored target_concept and targeting " +
                "profile. The Authority scheduler supplies ephemeral distance.";
            rule.conditions = new List<OntologyCondition>
            {
                OntologyCondition.Fact(
                    "?actor",
                    OntologyPredicates.AutonomousTargetIntent,
                    "?target"),
                OntologyCondition.HasConcept(
                    "?actor",
                    OntologyConcepts.AutonomousAgent),
                OntologyCondition.Fact(
                    "?actor",
                    OntologyPredicates.HasRuleBlock,
                    AutonomousTargetRuleId),
                OntologyCondition.Fact(
                    "?actor",
                    OntologyPredicates.TargetConcept,
                    "?targetConcept"),
                OntologyCondition.Fact(
                    "?actor",
                    OntologyPredicates.TargetingProfile,
                    OntologyObjects.NearestHostileWithinDetection),
                OntologyCondition.HasConcept(
                    "?target",
                    "?targetConcept"),
                OntologyCondition.HasConcept(
                    "?target",
                    OntologyConcepts.Damageable),
                OntologyCondition.Fact(
                    "?target",
                    OntologyPredicates.IsAlive,
                    bool.TrueString),
                OntologyCondition.Fact(
                    "?target",
                    OntologyPredicates.BelongsToFaction,
                    "?targetFaction"),
                OntologyCondition.Fact(
                    "?actor",
                    OntologyPredicates.HostileToFaction,
                    "?targetFaction"),
                OntologyCondition.NotEqual("?actor", "?target")
            };
            rule.effects = new List<OntologyEffect>();
            rule.runtimePresentation =
                new OntologyActionPresentationDefinition
                {
                    actorAnimationIntent =
                        OntologyAnimationIntentIds.MonsterIdle
                };
        }

        private static OntologyActionEffectDefinition
            CreateAutonomousTargetTransportDefinition()
        {
            return new OntologyActionEffectDefinition
            {
                actionVerb = AutonomousTargetActionId,
                requiresTool = false,
                evaluationOnly = true,
                ruleInvocation =
                    new OntologyActionRuleInvocationDefinition
                    {
                        ruleId = AutonomousTargetRuleId,
                        bindingVariable = "?actor",
                        bindingEntityPattern = "?actor",
                        intentSubjectPattern = "?actor",
                        intentPredicate =
                            OntologyPredicates.AutonomousTargetIntent,
                        intentObjectPattern = "?target"
                    },
                presentation =
                    new OntologyActionPresentationDefinition()
            };
        }

        private static void ConfigureAutonomousChaseRule(
            OntologyRuleDefinition rule)
        {
            rule.id = AutonomousChaseRuleId;
            rule.catalogVersion = 1;
            rule.description =
                "AuthorityKinematic moves an autonomous actor toward its " +
                "acquired target while detection_range, leash_range, and the " +
                "authored ChaseWithinLeash profile permit it.";
            rule.conditions = new List<OntologyCondition>
            {
                OntologyCondition.Fact(
                    "?actor",
                    OntologyPredicates.AutonomousChaseIntent,
                    "?target"),
                OntologyCondition.HasConcept(
                    "?actor",
                    OntologyConcepts.AutonomousAgent),
                OntologyCondition.Fact(
                    "?actor",
                    OntologyPredicates.HasRuleBlock,
                    AutonomousChaseRuleId),
                OntologyCondition.Fact(
                    "?actor",
                    OntologyPredicates.ChaseProfile,
                    OntologyObjects.ChaseWithinLeash),
                OntologyCondition.Fact(
                    "?actor",
                    OntologyPredicates.PhysicalProfile,
                    OntologyObjects.AuthorityKinematic),
                OntologyCondition.Fact(
                    "?actor",
                    OntologyPredicates.MovementSpeed,
                    "?speed"),
                OntologyCondition.Fact(
                    "?actor",
                    OntologyPredicates.DetectionRange,
                    "?detection"),
                OntologyCondition.Fact(
                    "?actor",
                    OntologyPredicates.LeashRange,
                    "?leash")
            };
            rule.effects = new List<OntologyEffect>();
            rule.runtimePresentation =
                new OntologyActionPresentationDefinition
                {
                    actorAnimationIntent =
                        OntologyAnimationIntentIds.MonsterWalk
                };
        }

        private static OntologyActionEffectDefinition
            CreateAutonomousChaseTransportDefinition()
        {
            return new OntologyActionEffectDefinition
            {
                actionVerb = AutonomousChaseActionId,
                requiresTool = false,
                evaluationOnly = true,
                ruleInvocation =
                    new OntologyActionRuleInvocationDefinition
                    {
                        ruleId = AutonomousChaseRuleId,
                        bindingVariable = "?actor",
                        bindingEntityPattern = "?actor",
                        intentSubjectPattern = "?actor",
                        intentPredicate =
                            OntologyPredicates.AutonomousChaseIntent,
                        intentObjectPattern = "?target"
                    },
                presentation =
                    new OntologyActionPresentationDefinition()
            };
        }

        private static void ConfigureDefeatLootRule(
            OntologyRuleDefinition rule)
        {
            rule.id = DefeatLootRuleId;
            rule.catalogVersion = 1;
            rule.description =
                "A defeated Damageable with authored loot data exposes loot " +
                "only while this independently removable Rule Block is bound.";
            rule.conditions = new List<OntologyCondition>
            {
                OntologyCondition.Fact(
                    "?target",
                    OntologyPredicates.DefeatResolvedIntent,
                    "?target"),
                OntologyCondition.HasConcept(
                    "?target",
                    OntologyConcepts.Damageable),
                OntologyCondition.Fact(
                    "?target",
                    OntologyPredicates.HasRuleBlock,
                    DefeatLootRuleId),
                OntologyCondition.Fact(
                    "?target",
                    OntologyPredicates.IsAlive,
                    bool.FalseString),
                OntologyCondition.Fact(
                    "?target",
                    OntologyPredicates.CurrentHealth,
                    "0"),
                OntologyCondition.Fact(
                    "?target",
                    OntologyPredicates.LootItem,
                    "?lootItem"),
                OntologyCondition.NotFact(
                    "?target",
                    OntologyPredicates.LootStatus,
                    "Available")
            };
            rule.effects = new List<OntologyEffect>
            {
                OntologyEffect.SetFact(
                    "?target",
                    OntologyPredicates.LootStatus,
                    "Available")
            };
            rule.runtimePresentation =
                new OntologyActionPresentationDefinition();
        }

        private static OntologyActionRuleInvocationDefinition
            CreateDefeatLootPostInvocation()
        {
            return new OntologyActionRuleInvocationDefinition
            {
                ruleId = DefeatLootRuleId,
                bindingVariable = "?target",
                bindingEntityPattern = "?target",
                intentSubjectPattern = "?target",
                intentPredicate =
                    OntologyPredicates.DefeatResolvedIntent,
                intentObjectPattern = "?target",
                required = false
            };
        }

        private static void ConfigureCollectLootRule(
            OntologyRuleDefinition rule)
        {
            rule.id = CollectLootRuleId;
            rule.catalogVersion = 2;
            rule.description =
                "A living Actor may collect one available authored loot item. " +
                "The defeated entity keeps the durable loot item and collector " +
                "provenance, so repeated collections do not overwrite a " +
                "single actor-owned inventory slot.";
            rule.conditions = new List<OntologyCondition>
            {
                OntologyCondition.Fact(
                    "?actor",
                    OntologyPredicates.LootCollectionIntent,
                    "?target"),
                OntologyCondition.HasConcept(
                    "?actor",
                    OntologyConcepts.Actor),
                OntologyCondition.Fact(
                    "?actor",
                    OntologyPredicates.IsAlive,
                    bool.TrueString),
                OntologyCondition.Fact(
                    "?target",
                    OntologyPredicates.HasRuleBlock,
                    CollectLootRuleId),
                OntologyCondition.Fact(
                    "?target",
                    OntologyPredicates.LootStatus,
                    "Available"),
                OntologyCondition.Fact(
                    "?target",
                    OntologyPredicates.LootItem,
                    "?lootItem"),
                OntologyCondition.NotFact(
                    "?target",
                    OntologyPredicates.LootCollectedBy,
                    "?collector")
            };
            rule.effects = new List<OntologyEffect>
            {
                OntologyEffect.SetFact(
                    "?target",
                    OntologyPredicates.LootStatus,
                    "Collected",
                    resultLifetime:
                        OntologyRuleResultLifetime.DurableState),
                OntologyEffect.SetFact(
                    "?target",
                    OntologyPredicates.LootCollectedBy,
                    "?actor",
                    resultLifetime:
                        OntologyRuleResultLifetime.DurableState)
            };
            rule.runtimePresentation =
                new OntologyActionPresentationDefinition();
        }

        private static OntologyActionEffectDefinition
            CreateCollectLootTransportDefinition()
        {
            return new OntologyActionEffectDefinition
            {
                actionVerb = CollectLootActionId,
                requiresTool = false,
                ruleInvocation =
                    new OntologyActionRuleInvocationDefinition
                    {
                        ruleId = CollectLootRuleId,
                        bindingVariable = "?target",
                        bindingEntityPattern = "?target",
                        intentSubjectPattern = "?actor",
                        intentPredicate =
                            OntologyPredicates.LootCollectionIntent,
                        intentObjectPattern = "?target"
                    },
                presentation =
                    new OntologyActionPresentationDefinition(),
                runtimeConstraints =
                    new OntologyActionRuntimeConstraints
                    {
                        maxActorTargetDistanceFrom =
                            new OntologyNumericFactSource
                            {
                                subject = "?target",
                                predicate =
                                    OntologyPredicates.InteractionRange
                            }
                    }
            };
        }

        private static void ConfigureAttackRule(
            OntologyRuleDefinition rule)
        {
            rule.id = AttackRuleId;
            rule.catalogVersion = 4;
            rule.description =
                "A primary attack intent lets an equipped melee weapon with " +
                "an authored contact-delivery contract damage one living " +
                "hostile Damageable target.";
            rule.conditions = new List<OntologyCondition>
            {
                OntologyCondition.Fact(
                    "?actor",
                    OntologyPredicates.PrimaryAttackIntent,
                    "?target"),
                OntologyCondition.HasConcept("?actor", OntologyConcepts.Actor),
                OntologyCondition.HasConcept("?tool", OntologyConcepts.Weapon),
                OntologyCondition.Fact(
                    "?tool",
                    OntologyPredicates.HasRuleBlock,
                    AttackRuleId),
                OntologyCondition.Fact(
                    "?tool",
                    OntologyPredicates.EquippedBy,
                    "?actor"),
                OntologyCondition.Fact(
                    "?tool",
                    OntologyPredicates.GrantsCapability,
                    "MeleeAttack"),
                OntologyCondition.Fact(
                    "?tool",
                    OntologyPredicates.AttackContactMode,
                    OntologyObjects.WeaponContactWindow),
                OntologyCondition.HasConcept(
                    "?target",
                    OntologyConcepts.Damageable),
                OntologyCondition.Fact(
                    "?target",
                    "combat_disposition",
                    "Hostile"),
                OntologyCondition.Fact(
                    "?target",
                    "is_alive",
                    "True")
            };
            var damage = OntologyEffect.AdjustNumberFact(
                "?target",
                "current_health",
                "0",
                "0",
                null,
                OntologyRuleResultLifetime.DurableState);
            damage.valueFrom = new OntologyNumericFactSource
            {
                subject = "?tool",
                predicate = OntologyPredicates.AttackDamage,
                multiplier = -1
            };
            var defeat = OntologyEffect.SetFact(
                "?target",
                "is_alive",
                "False",
                resultLifetime:
                    OntologyRuleResultLifetime.DurableState);
            defeat.when = new OntologyNumericFactGuard
            {
                subject = "?target",
                predicate = "current_health",
                comparison = OntologyNumericComparison.Equal,
                value = "0"
            };
            rule.effects = new List<OntologyEffect>
            {
                damage,
                defeat
            };
            rule.runtimePresentation =
                new OntologyActionPresentationDefinition();
        }

        private static void ConfigureSwingRule(
            OntologyRuleDefinition rule)
        {
            rule.id = SwingRuleId;
            rule.catalogVersion = 1;
            rule.description =
                "A primary swing intent authorizes one ephemeral equipped-" +
                "weapon swing presentation without changing world Facts.";
            rule.conditions = new List<OntologyCondition>
            {
                OntologyCondition.Fact(
                    "?actor",
                    OntologyPredicates.PrimarySwingIntent,
                    "?actor"),
                OntologyCondition.HasConcept(
                    "?actor",
                    OntologyConcepts.Actor),
                OntologyCondition.HasConcept(
                    "?tool",
                    OntologyConcepts.Weapon),
                OntologyCondition.Fact(
                    "?tool",
                    OntologyPredicates.HasRuleBlock,
                    SwingRuleId),
                OntologyCondition.Fact(
                    "?tool",
                    OntologyPredicates.EquippedBy,
                    "?actor"),
                OntologyCondition.Fact(
                    "?tool",
                    OntologyPredicates.GrantsCapability,
                    "MeleeAttack")
            };
            rule.effects = new List<OntologyEffect>();
            rule.runtimePresentation =
                new OntologyActionPresentationDefinition
                {
                    actorAnimationIntent =
                        OntologyAnimationIntentIds.AttackLight
                };
        }

        private static OntologyActionEffectDefinition
            CreateAttackTransportDefinition()
        {
            var definition = new OntologyActionEffectDefinition
            {
                actionVerb = OntologyActions.PrimaryAttack,
                requiresTool = true,
                presentation =
                    new OntologyActionPresentationDefinition(),
                ruleInvocation =
                    new OntologyActionRuleInvocationDefinition
                    {
                        ruleId = AttackRuleId,
                        bindingVariable = "?tool",
                        bindingEntityPattern = "?tool",
                        intentSubjectPattern = "?actor",
                        intentPredicate =
                            OntologyPredicates.PrimaryAttackIntent,
                        intentObjectPattern = "?target"
                    },
                runtimeConstraints =
                    new OntologyActionRuntimeConstraints
                    {
                        maxActorTargetDistanceFrom =
                            new OntologyNumericFactSource
                            {
                                subject = "?tool",
                                predicate =
                                    OntologyPredicates.AttackRange
                            },
                        cooldownSecondsFrom =
                            new OntologyNumericFactSource
                            {
                                subject = "?tool",
                                predicate =
                                    OntologyPredicates.AttackCooldown
                            }
                    }
            };
            definition.postRuleInvocations.Add(
                CreateDefeatLootPostInvocation());
            return definition;
        }

        private static OntologyActionEffectDefinition
            CreateSwingTransportDefinition()
        {
            return new OntologyActionEffectDefinition
            {
                actionVerb = "swing_weapon",
                requiresTool = true,
                presentation =
                    new OntologyActionPresentationDefinition(),
                ruleInvocation =
                    new OntologyActionRuleInvocationDefinition
                    {
                        ruleId = SwingRuleId,
                        bindingVariable = "?tool",
                        bindingEntityPattern = "?tool",
                        intentSubjectPattern = "?actor",
                        intentPredicate =
                            OntologyPredicates.PrimarySwingIntent,
                        intentObjectPattern = "?actor"
                    },
                runtimeConstraints =
                    new OntologyActionRuntimeConstraints
                    {
                        cooldownSecondsFrom =
                            new OntologyNumericFactSource
                            {
                                subject = "?tool",
                                predicate =
                                    OntologyPredicates.AttackCooldown
                            }
                    }
            };
        }

        private static void ConfigureAutonomousCombatRule(
            OntologyRuleDefinition rule)
        {
            rule.id = AutonomousCombatRuleId;
            rule.catalogVersion = 3;
            rule.description =
                "An autonomous living combat actor may damage one living " +
                "hostile target matching its authored target_concept. " +
                "Removing this Rule Block removes " +
                "the behavior.";
            rule.conditions = new List<OntologyCondition>
            {
                OntologyCondition.Fact(
                    "?actor",
                    OntologyPredicates.AutonomousAttackIntent,
                    "?target"),
                OntologyCondition.HasConcept(
                    "?actor",
                    OntologyConcepts.Actor),
                OntologyCondition.HasConcept(
                    "?actor",
                    OntologyConcepts.AutonomousAgent),
                OntologyCondition.HasConcept(
                    "?actor",
                    OntologyConcepts.Damageable),
                OntologyCondition.Fact(
                    "?actor",
                    OntologyPredicates.HasRuleBlock,
                    AutonomousCombatRuleId),
                OntologyCondition.Fact(
                    "?actor",
                    OntologyPredicates.GrantsCapability,
                    "NaturalMeleeAttack"),
                OntologyCondition.Fact(
                    "?actor",
                    OntologyPredicates.IsAlive,
                    bool.TrueString),
                OntologyCondition.HasConcept(
                    "?target",
                    OntologyConcepts.Actor),
                OntologyCondition.Fact(
                    "?actor",
                    OntologyPredicates.TargetConcept,
                    "?targetConcept"),
                OntologyCondition.HasConcept(
                    "?target",
                    "?targetConcept"),
                OntologyCondition.HasConcept(
                    "?target",
                    OntologyConcepts.Damageable),
                OntologyCondition.Fact(
                    "?target",
                    OntologyPredicates.IsAlive,
                    bool.TrueString),
                OntologyCondition.Fact(
                    "?target",
                    OntologyPredicates.BelongsToFaction,
                    "?targetFaction"),
                OntologyCondition.Fact(
                    "?actor",
                    OntologyPredicates.HostileToFaction,
                    "?targetFaction"),
                OntologyCondition.NotEqual("?actor", "?target")
            };
            var damage = OntologyEffect.AdjustNumberFact(
                "?target",
                "current_health",
                "0",
                "0",
                null,
                OntologyRuleResultLifetime.DurableState);
            damage.valueFrom = new OntologyNumericFactSource
            {
                subject = "?actor",
                predicate = OntologyPredicates.AttackDamage,
                multiplier = -1
            };
            var defeat = OntologyEffect.SetFact(
                "?target",
                OntologyPredicates.IsAlive,
                bool.FalseString,
                resultLifetime:
                    OntologyRuleResultLifetime.DurableState);
            defeat.when = new OntologyNumericFactGuard
            {
                subject = "?target",
                predicate = "current_health",
                comparison = OntologyNumericComparison.Equal,
                value = "0"
            };
            rule.effects = new List<OntologyEffect>
            {
                damage,
                defeat
            };
            rule.runtimePresentation =
                new OntologyActionPresentationDefinition
                {
                    actorAnimationIntent =
                        OntologyAnimationIntentIds.MonsterAttack
                };
        }

        private static OntologyActionEffectDefinition
            CreateAutonomousAttackTransportDefinition()
        {
            var definition = new OntologyActionEffectDefinition
            {
                actionVerb = AutonomousAttackActionId,
                requiresTool = false,
                presentation =
                    new OntologyActionPresentationDefinition(),
                ruleInvocation =
                    new OntologyActionRuleInvocationDefinition
                    {
                        ruleId = AutonomousCombatRuleId,
                        bindingVariable = "?actor",
                        bindingEntityPattern = "?actor",
                        intentSubjectPattern = "?actor",
                        intentPredicate =
                            OntologyPredicates.AutonomousAttackIntent,
                        intentObjectPattern = "?target"
                    },
                runtimeConstraints =
                    new OntologyActionRuntimeConstraints
                    {
                        maxActorTargetDistanceFrom =
                            new OntologyNumericFactSource
                            {
                                subject = "?actor",
                                predicate =
                                    OntologyPredicates.AttackRange
                            },
                        cooldownSecondsFrom =
                            new OntologyNumericFactSource
                            {
                                subject = "?actor",
                                predicate =
                                    OntologyPredicates.AttackCooldown
                            }
                    }
            };
            definition.postRuleInvocations.Add(
                CreateDefeatLootPostInvocation());
            return definition;
        }

        private static void ConfigurePlayerRespawnRule(
            OntologyRuleDefinition rule)
        {
            rule.id = PlayerRespawnRuleId;
            rule.catalogVersion = 2;
            rule.description =
                "A dead player-controlled actor with this Rule Block may " +
                "restore its authored maximum health through a canonical " +
                "respawn intent.";
            rule.conditions = new List<OntologyCondition>
            {
                OntologyCondition.Fact(
                    "?actor",
                    OntologyPredicates.RespawnIntent,
                    "?actor"),
                OntologyCondition.HasConcept(
                    "?actor",
                    OntologyConcepts.Actor),
                OntologyCondition.HasConcept(
                    "?actor",
                    OntologyConcepts.PlayerControlled),
                OntologyCondition.HasConcept(
                    "?actor",
                    OntologyConcepts.Damageable),
                OntologyCondition.Fact(
                    "?actor",
                    OntologyPredicates.HasRuleBlock,
                    PlayerRespawnRuleId),
                OntologyCondition.Fact(
                    "?actor",
                    OntologyPredicates.IsAlive,
                    bool.FalseString),
                OntologyCondition.Fact(
                    "?actor",
                    OntologyPredicates.CurrentHealth,
                    "0"),
                OntologyCondition.Fact(
                    "?actor",
                    OntologyPredicates.MaximumHealth,
                    "?maximumHealth")
            };
            var restoreHealth = OntologyEffect.AdjustNumberFact(
                "?actor",
                OntologyPredicates.CurrentHealth,
                "0",
                "0",
                null,
                OntologyRuleResultLifetime.DurableState);
            restoreHealth.valueFrom = new OntologyNumericFactSource
            {
                subject = "?actor",
                predicate = OntologyPredicates.MaximumHealth,
                multiplier = 1
            };
            var revive = OntologyEffect.SetFact(
                "?actor",
                OntologyPredicates.IsAlive,
                bool.TrueString,
                resultLifetime:
                    OntologyRuleResultLifetime.DurableState);
            revive.when = new OntologyNumericFactGuard
            {
                subject = "?actor",
                predicate = OntologyPredicates.CurrentHealth,
                comparison =
                    OntologyNumericComparison.GreaterThan,
                value = "0"
            };
            rule.effects = new List<OntologyEffect>
            {
                restoreHealth,
                revive
            };
            rule.runtimePresentation =
                new OntologyActionPresentationDefinition();
        }

        private static OntologyActionEffectDefinition
            CreatePlayerRespawnTransportDefinition()
        {
            return new OntologyActionEffectDefinition
            {
                actionVerb = PlayerRespawnActionId,
                objectPattern = "?actor",
                requiresTool = false,
                presentation =
                    new OntologyActionPresentationDefinition(),
                ruleInvocation =
                    new OntologyActionRuleInvocationDefinition
                    {
                        ruleId = PlayerRespawnRuleId,
                        bindingVariable = "?actor",
                        bindingEntityPattern = "?actor",
                        intentSubjectPattern = "?actor",
                        intentPredicate =
                            OntologyPredicates.RespawnIntent,
                        intentObjectPattern = "?actor"
                    },
                runtimeConstraints =
                    new OntologyActionRuntimeConstraints()
            };
        }

        private static void ConfigurePlayerLocomotionRule(
            OntologyRuleDefinition rule)
        {
            rule.id = PlayerLocomotionRuleId;
            rule.catalogVersion = 1;
            rule.description =
                "A living player-controlled actor may submit ephemeral ground " +
                "locomotion only while this Rule Block and its complete " +
                "semantic contract are assigned.";
            rule.conditions = new List<OntologyCondition>
            {
                OntologyCondition.Fact(
                    "?actor",
                    OntologyPredicates.LocomotionIntent,
                    "?actor"),
                OntologyCondition.HasConcept(
                    "?actor",
                    OntologyConcepts.Actor),
                OntologyCondition.HasConcept(
                    "?actor",
                    OntologyConcepts.PlayerControlled),
                OntologyCondition.Fact(
                    "?actor",
                    OntologyPredicates.HasRuleBlock,
                    PlayerLocomotionRuleId),
                OntologyCondition.Fact(
                    "?actor",
                    OntologyPredicates.GrantsCapability,
                    "Locomotion"),
                OntologyCondition.Fact(
                    "?actor",
                    OntologyPredicates.PhysicalProfile,
                    OntologyObjects.LocalCharacterController),
                OntologyCondition.Fact(
                    "?actor",
                    OntologyPredicates.IsAlive,
                    bool.TrueString),
                OntologyCondition.Fact(
                    "?actor",
                    OntologyPredicates.MovementSpeed,
                    "?speed")
            };
            rule.effects = new List<OntologyEffect>();
            rule.runtimePresentation =
                new OntologyActionPresentationDefinition
                {
                    actorAnimationIntent =
                        OntologyAnimationIntentIds.Locomotion
                };
        }

        private static OntologyActionEffectDefinition
            CreatePlayerLocomotionTransportDefinition()
        {
            return new OntologyActionEffectDefinition
            {
                actionVerb = PlayerLocomotionActionId,
                objectPattern = "?actor",
                requiresTool = false,
                presentation =
                    new OntologyActionPresentationDefinition(),
                ruleInvocation =
                    new OntologyActionRuleInvocationDefinition
                    {
                        ruleId = PlayerLocomotionRuleId,
                        bindingVariable = "?actor",
                        bindingEntityPattern = "?actor",
                        intentSubjectPattern = "?actor",
                        intentPredicate =
                            OntologyPredicates.LocomotionIntent,
                        intentObjectPattern = "?actor"
                    },
                runtimeConstraints =
                    new OntologyActionRuntimeConstraints()
            };
        }

        private static void ConfigurePlayerJumpRule(
            OntologyRuleDefinition rule)
        {
            rule.id = PlayerJumpRuleId;
            rule.catalogVersion = PlayerJumpRuleVersion;
            rule.description =
                "A supported living player-controlled actor may receive one " +
                "ephemeral jump impulse only while this Rule Block and the " +
                "complete authored jump contract are assigned.";
            rule.conditions = new List<OntologyCondition>
            {
                OntologyCondition.Fact(
                    "?actor",
                    OntologyPredicates.JumpIntent,
                    "?actor"),
                OntologyCondition.HasConcept(
                    "?actor",
                    OntologyConcepts.Actor),
                OntologyCondition.HasConcept(
                    "?actor",
                    OntologyConcepts.PlayerControlled),
                OntologyCondition.Fact(
                    "?actor",
                    OntologyPredicates.HasRuleBlock,
                    PlayerJumpRuleId),
                OntologyCondition.Fact(
                    "?actor",
                    OntologyPredicates.GrantsCapability,
                    "Jump"),
                OntologyCondition.Fact(
                    "?actor",
                    OntologyPredicates.PhysicalProfile,
                    OntologyObjects.LocalCharacterController),
                OntologyCondition.Fact(
                    "?actor",
                    OntologyPredicates.IsAlive,
                    bool.TrueString),
                OntologyCondition.Fact(
                    "?actor",
                    OntologyPredicates.JumpTakeoffSpeed,
                    "?takeoffSpeed"),
                OntologyCondition.Fact(
                    "?actor",
                    OntologyPredicates.GravityAcceleration,
                    "?gravity"),
                OntologyCondition.Fact(
                    "?actor",
                    OntologyPredicates.GroundStickVelocity,
                    "?groundStickVelocity")
            };
            rule.effects = new List<OntologyEffect>();
            rule.runtimePresentation =
                new OntologyActionPresentationDefinition
                {
                    actorAnimationIntent =
                        OntologyAnimationIntentIds.JumpStart
                };
        }

        private static OntologyActionEffectDefinition
            CreatePlayerJumpTransportDefinition()
        {
            return new OntologyActionEffectDefinition
            {
                actionVerb = PlayerJumpActionId,
                objectPattern = "?actor",
                requiresTool = false,
                evaluationOnly = true,
                presentation =
                    new OntologyActionPresentationDefinition(),
                ruleInvocation =
                    new OntologyActionRuleInvocationDefinition
                    {
                        ruleId = PlayerJumpRuleId,
                        bindingVariable = "?actor",
                        bindingEntityPattern = "?actor",
                        intentSubjectPattern = "?actor",
                        intentPredicate =
                            OntologyPredicates.JumpIntent,
                        intentObjectPattern = "?actor"
                    },
                runtimeConstraints =
                    new OntologyActionRuntimeConstraints
                    {
                        requiresGroundedObservation = true
                    }
            };
        }

        private static OntologyRuleBlockPreset
            CreateAutonomousMonsterPreset()
        {
            return new OntologyRuleBlockPreset
            {
                presetId = "autonomous_melee_monster",
                displayNameKey =
                    "rule_preset.autonomous_melee_monster",
                descriptionKey =
                    "rule_preset.autonomous_melee_monster.description",
                primaryRuleId = AutonomousCombatRuleId,
                bindingVariable = "?actor",
                physicalProfileId =
                    OntologyObjects.AuthorityKinematic,
                additionalRuleBlocks =
                    new List<OntologyRuleBlockBinding>
                    {
                        new()
                        {
                            ruleId = AutonomousTargetRuleId,
                            bindingVariable = "?actor"
                        },
                        new()
                        {
                            ruleId = AutonomousChaseRuleId,
                            bindingVariable = "?actor"
                        },
                        new()
                        {
                            ruleId = DefeatLootRuleId,
                            bindingVariable = "?target"
                        },
                        new()
                        {
                            ruleId = CollectLootRuleId,
                            bindingVariable = "?target"
                        }
                    },
                requiredConcepts = new List<string>
                {
                    OntologyConcepts.Actor,
                    OntologyConcepts.AutonomousAgent,
                    OntologyConcepts.Monster,
                    OntologyConcepts.Combatant,
                    OntologyConcepts.Damageable
                },
                requiredFacts = CreateAutonomousMonsterFacts()
                    .ToList()
            };
        }

        private static OntologyFactEntry[]
            CreateAutonomousMonsterFacts()
        {
            return new[]
            {
                new OntologyFactEntry
                {
                    predicate = "current_health",
                    obj = "30"
                },
                new OntologyFactEntry
                {
                    predicate = "maximum_health",
                    obj = "30"
                },
                new OntologyFactEntry
                {
                    predicate = OntologyPredicates.IsAlive,
                    obj = bool.TrueString
                },
                new OntologyFactEntry
                {
                    predicate = OntologyPredicates.BelongsToFaction,
                    obj = "WildMonster"
                },
                new OntologyFactEntry
                {
                    predicate = OntologyPredicates.HostileToFaction,
                    obj = "PlayerFaction"
                },
                new OntologyFactEntry
                {
                    predicate = OntologyPredicates.TargetConcept,
                    obj = OntologyConcepts.PlayerControlled
                },
                new OntologyFactEntry
                {
                    predicate = OntologyPredicates.TargetingProfile,
                    obj =
                        OntologyObjects.NearestHostileWithinDetection
                },
                new OntologyFactEntry
                {
                    predicate = OntologyPredicates.ChaseProfile,
                    obj = OntologyObjects.ChaseWithinLeash
                },
                new OntologyFactEntry
                {
                    predicate = OntologyPredicates.TargetAction,
                    obj = AutonomousTargetActionId
                },
                new OntologyFactEntry
                {
                    predicate = OntologyPredicates.ChaseAction,
                    obj = AutonomousChaseActionId
                },
                new OntologyFactEntry
                {
                    predicate = OntologyPredicates.CombatDisposition,
                    obj = "Hostile"
                },
                new OntologyFactEntry
                {
                    predicate = OntologyPredicates.GrantsCapability,
                    obj = "NaturalMeleeAttack"
                },
                new OntologyFactEntry
                {
                    predicate = OntologyPredicates.AttackAction,
                    obj = AutonomousAttackActionId
                },
                new OntologyFactEntry
                {
                    predicate = OntologyPredicates.AttackDamage,
                    obj = "5"
                },
                new OntologyFactEntry
                {
                    predicate = OntologyPredicates.AttackRange,
                    obj = "2"
                },
                new OntologyFactEntry
                {
                    predicate = OntologyPredicates.AttackCooldown,
                    obj = "1"
                },
                new OntologyFactEntry
                {
                    predicate = OntologyPredicates.DetectionRange,
                    obj = "12"
                },
                new OntologyFactEntry
                {
                    predicate = OntologyPredicates.LeashRange,
                    obj = "18"
                },
                new OntologyFactEntry
                {
                    predicate = OntologyPredicates.MovementSpeed,
                    obj = "2"
                },
                new OntologyFactEntry
                {
                    predicate =
                        OntologyPredicates.IdleAnimationIntent,
                    obj = OntologyAnimationIntentIds.MonsterIdle
                },
                new OntologyFactEntry
                {
                    predicate =
                        OntologyPredicates.MoveAnimationIntent,
                    obj = OntologyAnimationIntentIds.MonsterWalk
                },
                new OntologyFactEntry
                {
                    predicate = OntologyPredicates.HitAnimationIntent,
                    obj = OntologyAnimationIntentIds.HitReaction
                },
                new OntologyFactEntry
                {
                    predicate =
                        OntologyPredicates.DeathAnimationIntent,
                    obj = OntologyAnimationIntentIds.Death
                },
                new OntologyFactEntry
                {
                    predicate = OntologyPredicates.HitVfxIntent,
                    obj = "HitPhysicalLight"
                },
                new OntologyFactEntry
                {
                    predicate = OntologyPredicates.LootTable,
                    obj = "BeholderBasicLoot"
                },
                new OntologyFactEntry
                {
                    predicate = OntologyPredicates.LootItem,
                    obj = "OntologyDataFragment"
                },
                new OntologyFactEntry
                {
                    predicate = OntologyPredicates.LootAction,
                    obj = CollectLootActionId
                },
                new OntologyFactEntry
                {
                    predicate =
                        OntologyPredicates.InteractionRange,
                    obj = "2.5"
                }
            };
        }

        private static List<OntologyFactIntroduction>
            CreateAutonomousMonsterFactIntroductions()
        {
            var facts = new List<OntologyFactEntry>
            {
                new()
                {
                    predicate = OntologyPredicates.HasConcept,
                    obj = OntologyConcepts.AutonomousAgent
                },
                new()
                {
                    predicate = OntologyPredicates.HasConcept,
                    obj = OntologyConcepts.Combatant
                }
            };
            facts.AddRange(CreateAutonomousMonsterFacts());
            facts.Add(new OntologyFactEntry
            {
                predicate = OntologyPredicates.PhysicalProfile,
                obj = OntologyObjects.AuthorityKinematic
            });
            return facts
                .GroupBy(
                    value => value.predicate + "\n" + value.obj,
                    StringComparer.Ordinal)
                .Select(group => new OntologyFactIntroduction
                {
                    contractVersion =
                        AutonomousMonsterSemanticContractVersion,
                    fact = group.First()
                })
                .ToList();
        }

        private static void EnsureMonsterAnimationManifestEntries(
            OntologyAnimationContentManifest manifest)
        {
            if (manifest == null) return;
            var profile =
                AssetDatabase.LoadAssetAtPath<OntologyActorProfile>(
                    OntologyAnimationContentPipeline.MonsterProfilePath);
            if (profile == null) return;
            var entries = manifest.Entries
                .Where(value => value != null)
                .ToList();
            UpsertMonsterAnimation(
                entries,
                profile,
                "Anim_Beholder_Attack",
                "Assets/RPGMonsterPartnersPBRPolyart/Animations/" +
                "Beholder/Attack01.fbx",
                OntologyAnimationIntentIds.MonsterAttack,
                false,
                110,
                false);
            UpsertMonsterAnimation(
                entries,
                profile,
                "Anim_Beholder_Walk",
                "Assets/RPGMonsterPartnersPBRPolyart/Animations/" +
                "Beholder/WalkFWD.fbx",
                OntologyAnimationIntentIds.MonsterWalk,
                true,
                30,
                true);
            UpsertMonsterAnimation(
                entries,
                profile,
                "Anim_Beholder_Run",
                "Assets/RPGMonsterPartnersPBRPolyart/Animations/" +
                "Beholder/Run.fbx",
                OntologyAnimationIntentIds.MonsterRun,
                true,
                40,
                true);
            manifest.ReplaceEntries(entries);
            EditorUtility.SetDirty(manifest);
        }

        private static void UpsertMonsterAnimation(
            IList<OntologyAnimationContentEntry> entries,
            OntologyActorProfile profile,
            string animationId,
            string path,
            string intent,
            bool loop,
            int priority,
            bool interruptible)
        {
            var clip = AssetDatabase.LoadAllAssetsAtPath(path)
                .OfType<AnimationClip>()
                .FirstOrDefault(value =>
                    value != null &&
                    !value.name.StartsWith(
                        "__preview__",
                        StringComparison.Ordinal));
            if (clip == null)
            {
                throw new InvalidOperationException(
                    "Monster animation clip is missing: " + path);
            }
            for (var index = entries.Count - 1; index >= 0; index--)
            {
                if (string.Equals(
                        entries[index]?.animationId,
                        animationId,
                        StringComparison.Ordinal))
                {
                    entries.RemoveAt(index);
                }
            }
            entries.Add(new OntologyAnimationContentEntry
            {
                animationId = animationId,
                clip = clip,
                sourceAssetPath = path,
                deliveryKey = "animations/" + animationId,
                intents = new[] { intent },
                actorTypes = new[] { "Monster" },
                rigTypes = new[] { "Generic" },
                profiles = new[] { profile },
                layer = OntologyAnimationLayer.FullBody,
                loop = loop,
                loopPose = loop,
                rootMotionMode =
                    OntologyAnimationRootMotionMode.Disabled,
                playbackStartNormalized = 0f,
                playbackEndNormalized = 1f,
                interruptible = interruptible,
                priority = priority,
                canBlend = true,
                transitionDuration = 0.2f,
                properties = new[]
                {
                    "Combat",
                    loop ? "Locomotion" : "Attack"
                },
                contentVersion = "1.0.0"
            });
        }

        private static void EnsureFact(
            OntologyMapObjectTemplate template,
            string predicate,
            string value)
        {
            var facts = (template.facts ??
                         Array.Empty<OntologyFactEntry>())
                .Where(fact =>
                    fact != null &&
                    !string.Equals(
                        fact.predicate,
                        predicate,
                        StringComparison.Ordinal))
                .ToList();
            facts.Add(new OntologyFactEntry
            {
                predicate = predicate,
                obj = value
            });
            template.facts = facts.ToArray();
        }

        private static List<OntologyFactIntroduction>
            CreateAttackFactIntroductions()
        {
            return new List<OntologyFactIntroduction>
            {
                new()
                {
                    contractVersion = AttackSemanticContractVersion,
                    fact = new OntologyFactEntry
                    {
                        predicate = OntologyPredicates.AttackAction,
                        obj = "attack"
                    }
                },
                new()
                {
                    contractVersion = AttackSemanticContractVersion,
                    fact = new OntologyFactEntry
                    {
                        predicate = OntologyPredicates.AttackRange,
                        obj = "3"
                    }
                },
                new()
                {
                    contractVersion = AttackSemanticContractVersion,
                    fact = new OntologyFactEntry
                    {
                        predicate = OntologyPredicates.AttackCooldown,
                        obj = "1"
                    }
                },
                new()
                {
                    contractVersion = SwingSemanticContractVersion,
                    fact = new OntologyFactEntry
                    {
                        predicate = OntologyPredicates.SwingAction,
                        obj = "swing_weapon"
                    }
                },
                new()
                {
                    contractVersion = WeaponSemanticContractVersion,
                    fact = new OntologyFactEntry
                    {
                        predicate =
                            OntologyPredicates.AttackContactMode,
                        obj = OntologyObjects.WeaponContactWindow
                    }
                },
                new()
                {
                    contractVersion = WeaponSemanticContractVersion,
                    fact = new OntologyFactEntry
                    {
                        predicate = OntologyPredicates.EquipAction,
                        obj = EquipActionId
                    }
                },
                new()
                {
                    contractVersion = WeaponSemanticContractVersion,
                    fact = new OntologyFactEntry
                    {
                        predicate = OntologyPredicates.UnequipAction,
                        obj = UnequipActionId
                    }
                },
                new()
                {
                    contractVersion = WeaponSemanticContractVersion,
                    fact = new OntologyFactEntry
                    {
                        predicate =
                            OntologyPredicates.InteractionRange,
                        obj = "3"
                    }
                }
            };
        }

        private static void EnsureBinding(
            ICollection<OntologyRuleBlockBinding> bindings,
            string ruleId,
            string variable)
        {
            if (bindings.Any(value =>
                    value != null &&
                    string.Equals(
                        value.ruleId,
                        ruleId,
                        StringComparison.Ordinal) &&
                    string.Equals(
                        value.bindingVariable,
                        variable,
                        StringComparison.Ordinal)))
            {
                return;
            }
            bindings.Add(new OntologyRuleBlockBinding
            {
                ruleId = ruleId,
                bindingVariable = variable
            });
        }

        private static void EnsureFactIntroduction(
            ICollection<OntologyFactIntroduction> introductions,
            OntologyFactIntroduction introduction)
        {
            if (introduction?.fact == null ||
                introductions.Any(value =>
                    value?.fact != null &&
                    value.contractVersion == introduction.contractVersion &&
                    string.Equals(
                        value.fact.predicate,
                        introduction.fact.predicate,
                        StringComparison.Ordinal) &&
                    string.Equals(
                        value.fact.obj,
                        introduction.fact.obj,
                        StringComparison.Ordinal)))
            {
                return;
            }
            introductions.Add(introduction);
        }

        private static void EnsureRuleIntroduction(
            ICollection<OntologyRuleBlockIntroduction> introductions,
            int contractVersion,
            string ruleId,
            string variable)
        {
            if (introductions.Any(value =>
                    value?.binding != null &&
                    value.contractVersion == contractVersion &&
                    string.Equals(
                        value.binding.ruleId,
                        ruleId,
                        StringComparison.Ordinal) &&
                    string.Equals(
                        value.binding.bindingVariable,
                        variable,
                        StringComparison.Ordinal)))
            {
                return;
            }
            introductions.Add(new OntologyRuleBlockIntroduction
            {
                contractVersion = contractVersion,
                binding = new OntologyRuleBlockBinding
                {
                    ruleId = ruleId,
                    bindingVariable = variable
                }
            });
        }

        private static void EnsureRuleMigration(
            ICollection<OntologyRuleBlockMigration> migrations,
            int targetContractVersion,
            string fromRuleId,
            string fromVariable,
            string replacementRuleId,
            string replacementVariable)
        {
            if (migrations.Any(value =>
                    value?.replacement != null &&
                    value.targetContractVersion == targetContractVersion &&
                    string.Equals(
                        value.fromRuleId,
                        fromRuleId,
                        StringComparison.Ordinal) &&
                    string.Equals(
                        value.fromBindingVariable,
                        fromVariable,
                        StringComparison.Ordinal) &&
                    string.Equals(
                        value.replacement.ruleId,
                        replacementRuleId,
                        StringComparison.Ordinal) &&
                    string.Equals(
                        value.replacement.bindingVariable,
                        replacementVariable,
                        StringComparison.Ordinal)))
            {
                return;
            }
            migrations.Add(new OntologyRuleBlockMigration
            {
                targetContractVersion = targetContractVersion,
                fromRuleId = fromRuleId,
                fromBindingVariable = fromVariable,
                replacement = new OntologyRuleBlockBinding
                {
                    ruleId = replacementRuleId,
                    bindingVariable = replacementVariable
                }
            });
        }
    }
}
