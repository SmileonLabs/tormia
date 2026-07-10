using UnityEngine;
using System.Collections;

namespace Tormia.Ontology.Core
{
    public sealed class OntologyCharacterPartAdapter : MonoBehaviour
    {
        [SerializeField] private OntologyWorldBootstrap bootstrap;
        [SerializeField] private OntologyCharacterPartDatabase partDatabase;
        [SerializeField] private Transform visualRoot;
        [SerializeField] private string actorId = "Player";
        [SerializeField] private bool applyOnStart = true;
        [SerializeField] private bool injectFactsOnStart = true;
        [SerializeField] private bool syncFromWorldFacts = true;

        public OntologyCharacterPartDatabase PartDatabase => partDatabase;

        public const string FailureDefinitionMissing = "definition_missing";
        public const string FailureRendererMissing = "renderer_missing";
        public const string FailureWorldMissing = "world_missing";
        public const string FailureAlreadyEquipped = "already_equipped";
        public const string FailureAlreadyUnequipped = "already_unequipped";

        private void Update()
        {
            if (syncFromWorldFacts)
            {
                SyncFromWorldFacts();
            }
        }

        private void Awake()
        {
            if (bootstrap == null)
            {
                bootstrap = FindAnyObjectByType<OntologyWorldBootstrap>();
            }

            if (visualRoot == null)
            {
                var child = transform.Find("Visual_Base_Mesh");
                visualRoot = child != null ? child : transform;
            }
        }

        private void Start()
        {
            if (applyOnStart)
            {
                ApplyDefaultPreset();
            }

            if (injectFactsOnStart)
            {
                StartCoroutine(InjectFactsAfterBootstrapReady());
            }
        }

        private IEnumerator InjectFactsAfterBootstrapReady()
        {
            yield return null;
            InjectActivePartFacts();
        }

        public void ApplyDefaultPreset()
        {
            if (partDatabase == null || partDatabase.Definitions == null || visualRoot == null)
            {
                return;
            }

            foreach (var definition in partDatabase.Definitions)
            {
                if (definition == null || string.IsNullOrWhiteSpace(definition.rendererPath))
                {
                    continue;
                }

                var renderer = FindRenderer(definition.rendererPath);
                if (renderer == null)
                {
                    continue;
                }

                SetPartEnabled(definition, definition.enabledByDefault);
            }
        }

        public void InjectActivePartFacts()
        {
            EnsureWorldReady();
            if (bootstrap == null || bootstrap.World == null || partDatabase == null)
            {
                return;
            }

            OntologyCharacterPartFactSynchronizer.RebuildActorFacts(
                bootstrap.World,
                actorId,
                partDatabase.Definitions,
                definition =>
                {
                    var renderer = FindRenderer(definition.rendererPath);
                    return renderer != null && renderer.enabled;
                });
        }

        private void InjectPartDefinitionFacts(OntologyCharacterPartDefinition definition)
        {
            OntologyCharacterPartFactSynchronizer.AddDefinitionFacts(bootstrap.World, definition);
        }

        public void SyncFromWorldFacts()
        {
            if (partDatabase == null || partDatabase.Definitions == null)
            {
                return;
            }

            EnsureWorldReady();
            if (bootstrap == null || bootstrap.World == null)
            {
                return;
            }

            var factsChanged = false;
            foreach (var definition in partDatabase.Definitions)
            {
                if (definition == null || string.IsNullOrWhiteSpace(definition.partId))
                {
                    continue;
                }

                if (bootstrap.World.HasFact(actorId, OntologyPredicates.UnequipPart, definition.partId))
                {
                    SetPartEnabled(definition, false);
                    factsChanged |= bootstrap.World.RemoveFact(actorId, OntologyPredicates.EquippedPart, definition.partId);
                    factsChanged |= bootstrap.World.RemoveFact(actorId, OntologyPredicates.UnequipPart, definition.partId);
                    continue;
                }

                if (bootstrap.World.HasFact(actorId, OntologyPredicates.EquippedPart, definition.partId))
                {
                    factsChanged |= DisableConflictingParts(definition);
                    SetPartEnabled(definition, true);
                }
            }

            if (factsChanged)
            {
                InjectActivePartFacts();
            }
        }

        public void SyncRenderersFromWorldFacts()
        {
            if (partDatabase == null || partDatabase.Definitions == null)
            {
                return;
            }

            EnsureWorldReady();
            if (bootstrap == null || bootstrap.World == null)
            {
                return;
            }

            foreach (var definition in partDatabase.Definitions)
            {
                if (definition == null || string.IsNullOrWhiteSpace(definition.partId))
                {
                    continue;
                }

                var shouldEnable = bootstrap.World.HasFact(actorId, OntologyPredicates.EquippedPart, definition.partId);
                if (shouldEnable)
                {
                    DisableConflictingParts(definition);
                }

                SetPartEnabled(definition, shouldEnable);
            }
        }

        public bool EquipPart(string partId)
        {
            var definition = FindDefinition(partId);
            if (!CanEquipPart(partId, out _))
            {
                return false;
            }

            DisableConflictingParts(definition);
            SetPartEnabled(definition, true);
            EnsureWorldReady();
            if (bootstrap != null && bootstrap.World != null)
            {
                InjectPartDefinitionFacts(definition);
                bootstrap.World.AddFact(actorId, OntologyPredicates.EquippedPart, definition.partId);
            }
            InjectActivePartFacts();
            return true;
        }

        public bool UnequipPart(string partId)
        {
            var definition = FindDefinition(partId);
            if (!CanUnequipPart(partId, out _))
            {
                return false;
            }

            SetPartEnabled(definition, false);
            EnsureWorldReady();
            if (bootstrap != null && bootstrap.World != null)
            {
                bootstrap.World.RemoveFact(actorId, OntologyPredicates.EquippedPart, definition.partId);
                bootstrap.World.RemoveFact(actorId, OntologyPredicates.UnequipPart, definition.partId);
            }
            InjectActivePartFacts();
            return true;
        }

        public bool CanEquipPart(string partId, out string reason)
        {
            reason = string.Empty;
            var definition = FindDefinition(partId);
            if (definition == null)
            {
                reason = FailureDefinitionMissing;
                return false;
            }

            if (FindRenderer(definition.rendererPath) == null)
            {
                reason = FailureRendererMissing;
                return false;
            }

            EnsureWorldReady();
            if (bootstrap == null || bootstrap.World == null)
            {
                reason = FailureWorldMissing;
                return false;
            }

            if (IsPartEquipped(partId))
            {
                reason = FailureAlreadyEquipped;
                return false;
            }

            return true;
        }

        public bool CanUnequipPart(string partId, out string reason)
        {
            reason = string.Empty;
            var definition = FindDefinition(partId);
            if (definition == null)
            {
                reason = FailureDefinitionMissing;
                return false;
            }

            if (FindRenderer(definition.rendererPath) == null)
            {
                reason = FailureRendererMissing;
                return false;
            }

            EnsureWorldReady();
            if (bootstrap == null || bootstrap.World == null)
            {
                reason = FailureWorldMissing;
                return false;
            }

            if (!IsPartEquipped(partId))
            {
                reason = FailureAlreadyUnequipped;
                return false;
            }

            return true;
        }

        public bool IsPartEquipped(string partId)
        {
            var definition = FindDefinition(partId);
            if (definition == null)
            {
                return false;
            }

            var renderer = FindRenderer(definition.rendererPath);
            return renderer != null && renderer.enabled;
        }

        public bool HasEquippedPartFact(string partId)
        {
            EnsureWorldReady();
            return bootstrap != null && bootstrap.World != null && bootstrap.World.HasFact(actorId, OntologyPredicates.EquippedPart, partId);
        }

        private OntologyCharacterPartDefinition FindDefinition(string partId)
        {
            if (partDatabase == null || partDatabase.Definitions == null || string.IsNullOrWhiteSpace(partId))
            {
                return null;
            }

            foreach (var definition in partDatabase.Definitions)
            {
                if (definition != null && definition.partId == partId)
                {
                    return definition;
                }
            }

            return null;
        }

        private void SetPartEnabled(OntologyCharacterPartDefinition definition, bool enabled)
        {
            var renderer = FindRenderer(definition.rendererPath);
            if (renderer != null)
            {
                renderer.enabled = enabled;
                if (!enabled)
                {
                    return;
                }

                var appliedVariant = ApplyVariantMeshAndMaterials(definition, renderer);
                if (!renderer.gameObject.activeSelf)
                {
                    renderer.gameObject.SetActive(true);
                }

                if (!appliedVariant && definition.material != null)
                {
                    renderer.sharedMaterial = definition.material;
                }
            }

        }

        private static bool ApplyVariantMeshAndMaterials(OntologyCharacterPartDefinition definition, Renderer targetRenderer)
        {
            if (definition.variantPrefab == null || targetRenderer == null)
            {
                return false;
            }

            var sourceRenderer = definition.variantPrefab.GetComponentInChildren<SkinnedMeshRenderer>(true);
            var targetSkinnedRenderer = targetRenderer as SkinnedMeshRenderer;
            if (sourceRenderer == null || sourceRenderer.sharedMesh == null || targetSkinnedRenderer == null)
            {
                return false;
            }

            targetSkinnedRenderer.sharedMesh = sourceRenderer.sharedMesh;
            targetSkinnedRenderer.sharedMaterials = sourceRenderer.sharedMaterials;
            targetSkinnedRenderer.localBounds = sourceRenderer.sharedMesh.bounds;
            return true;
        }

        private bool DisableConflictingParts(OntologyCharacterPartDefinition equippedDefinition)
        {
            if (partDatabase == null || partDatabase.Definitions == null || string.IsNullOrWhiteSpace(equippedDefinition.slot))
            {
                return false;
            }

            EnsureWorldReady();
            var factsChanged = false;

            foreach (var definition in partDatabase.Definitions)
            {
                if (definition == null || definition == equippedDefinition)
                {
                    continue;
                }

                if (definition.slot == equippedDefinition.slot
                    || OntologyCharacterPartFactSynchronizer.SlotsConflict(equippedDefinition, definition))
                {
                    SetPartEnabled(definition, false);
                    if (bootstrap != null && bootstrap.World != null)
                    {
                        factsChanged |= bootstrap.World.RemoveFact(actorId, OntologyPredicates.EquippedPart, definition.partId);
                        factsChanged |= bootstrap.World.RemoveFact(actorId, OntologyPredicates.UnequipPart, definition.partId);
                    }
                }
            }

            return factsChanged;
        }


        private Renderer FindRenderer(string rendererPath)
        {
            if (visualRoot == null || string.IsNullOrWhiteSpace(rendererPath))
            {
                return null;
            }

            var normalized = rendererPath;
            if (normalized.StartsWith(visualRoot.name + "/"))
            {
                normalized = normalized.Substring(visualRoot.name.Length + 1);
            }

            var target = visualRoot.Find(normalized);
            return target == null ? null : target.GetComponent<Renderer>();
        }

        private void EnsureWorldReady()
        {
            if (bootstrap == null)
            {
                bootstrap = FindAnyObjectByType<OntologyWorldBootstrap>();
            }

            if (bootstrap != null && (bootstrap.World == null || bootstrap.Session == null))
            {
                bootstrap.ResetWorld(logReport: false);
            }
        }
    }
}
