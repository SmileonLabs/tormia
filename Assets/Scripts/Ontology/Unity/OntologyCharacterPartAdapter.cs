using UnityEngine;
using System.Collections;
using System.Collections.Generic;

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

            var clearedRendererPaths = new HashSet<string>();
            foreach (var definition in partDatabase.Definitions)
            {
                if (definition == null || string.IsNullOrWhiteSpace(definition.rendererPath))
                {
                    continue;
                }

                if (clearedRendererPaths.Add(definition.rendererPath))
                {
                    var renderer = FindRenderer(definition.rendererPath);
                    if (renderer != null)
                    {
                        renderer.enabled = false;
                    }
                }
            }

            foreach (var definition in partDatabase.Definitions)
            {
                if (definition != null && definition.enabledByDefault)
                {
                    SetPartEnabled(definition, true);
                }
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
                IsDefinitionActive);
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
                    factsChanged |= SetDefinitionEquipped(definition, false);
                    continue;
                }

                if (bootstrap.World.HasFact(actorId, OntologyPredicates.EquippedPart, definition.partId))
                {
                    if (!definition.visibleInCustomization && IsLinkedByEquippedDefinition(definition.partId))
                    {
                        SetPartEnabled(definition, true);
                        continue;
                    }

                    factsChanged |= DisableConflictingParts(definition);
                    factsChanged |= SetDefinitionEquipped(definition, true);
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

            var clearedRendererPaths = new HashSet<string>();
            foreach (var definition in partDatabase.Definitions)
            {
                if (definition != null && clearedRendererPaths.Add(definition.rendererPath))
                {
                    var renderer = FindRenderer(definition.rendererPath);
                    if (renderer != null)
                    {
                        renderer.enabled = false;
                    }
                }
            }

            foreach (var definition in partDatabase.Definitions)
            {
                if (definition == null || string.IsNullOrWhiteSpace(definition.partId))
                {
                    continue;
                }

                if (!bootstrap.World.HasFact(actorId, OntologyPredicates.EquippedPart, definition.partId))
                {
                    continue;
                }

                if (definition.visibleInCustomization || !IsLinkedByEquippedDefinition(definition.partId))
                {
                    DisableConflictingParts(definition);
                }

                SetPartEnabled(definition, true);
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
            EnsureWorldReady();
            SetDefinitionEquipped(definition, true);
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

            EnsureWorldReady();
            SetDefinitionEquipped(definition, false);
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

            EnsureWorldReady();
            return bootstrap != null && bootstrap.World != null
                ? bootstrap.World.HasFact(actorId, OntologyPredicates.EquippedPart, definition.partId)
                : IsDefinitionActive(definition);
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

        private bool IsDefinitionActive(OntologyCharacterPartDefinition definition)
        {
            var renderer = FindRenderer(definition.rendererPath);
            if (renderer == null || !renderer.enabled)
            {
                return false;
            }

            if (definition.variantPrefab == null)
            {
                return true;
            }

            var sourceRenderer = definition.variantPrefab.GetComponentInChildren<SkinnedMeshRenderer>(true);
            var targetRenderer = renderer as SkinnedMeshRenderer;
            return sourceRenderer != null
                && targetRenderer != null
                && sourceRenderer.sharedMesh == targetRenderer.sharedMesh;
        }

        private bool SetDefinitionEquipped(OntologyCharacterPartDefinition definition, bool equipped)
        {
            return SetDefinitionEquipped(definition, equipped, new HashSet<string>());
        }

        private bool SetDefinitionEquipped(
            OntologyCharacterPartDefinition definition,
            bool equipped,
            HashSet<string> visited)
        {
            if (definition == null || string.IsNullOrWhiteSpace(definition.partId) || !visited.Add(definition.partId))
            {
                return false;
            }

            SetPartEnabled(definition, equipped);
            var factsChanged = false;
            if (bootstrap != null && bootstrap.World != null)
            {
                if (equipped)
                {
                    InjectPartDefinitionFacts(definition);
                    factsChanged |= bootstrap.World.AddFact(actorId, OntologyPredicates.EquippedPart, definition.partId);
                    factsChanged |= bootstrap.World.RemoveFact(actorId, OntologyPredicates.UnequipPart, definition.partId);
                }
                else
                {
                    factsChanged |= bootstrap.World.RemoveFact(actorId, OntologyPredicates.EquippedPart, definition.partId);
                    factsChanged |= bootstrap.World.RemoveFact(actorId, OntologyPredicates.UnequipPart, definition.partId);
                }
            }

            foreach (var linkedPartId in definition.linkedPartIds ?? System.Array.Empty<string>())
            {
                factsChanged |= SetDefinitionEquipped(FindDefinition(linkedPartId), equipped, visited);
            }

            return factsChanged;
        }

        private bool IsLinkedByEquippedDefinition(string partId)
        {
            if (bootstrap == null || bootstrap.World == null || partDatabase == null)
            {
                return false;
            }

            foreach (var definition in partDatabase.Definitions)
            {
                if (definition == null
                    || !bootstrap.World.HasFact(actorId, OntologyPredicates.EquippedPart, definition.partId))
                {
                    continue;
                }

                foreach (var linkedPartId in definition.linkedPartIds ?? System.Array.Empty<string>())
                {
                    if (linkedPartId == partId)
                    {
                        return true;
                    }
                }
            }

            return false;
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
                    if (IsLinkedPart(equippedDefinition, definition.partId))
                    {
                        continue;
                    }

                    factsChanged |= SetDefinitionEquipped(definition, false);
                }
            }

            return factsChanged;
        }

        private static bool IsLinkedPart(OntologyCharacterPartDefinition definition, string partId)
        {
            foreach (var linkedPartId in definition.linkedPartIds ?? System.Array.Empty<string>())
            {
                if (linkedPartId == partId)
                {
                    return true;
                }
            }

            return false;
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
