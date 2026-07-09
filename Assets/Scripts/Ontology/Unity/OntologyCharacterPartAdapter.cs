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
                bootstrap = FindFirstObjectByType<OntologyWorldBootstrap>();
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

            bootstrap.World.RemoveFacts(actorId, OntologyPredicates.EquippedPart);
            bootstrap.World.RemoveFacts(actorId, OntologyPredicates.HasCapability);

            foreach (var definition in partDatabase.Definitions)
            {
                if (definition == null || string.IsNullOrWhiteSpace(definition.partId))
                {
                    continue;
                }

                InjectPartDefinitionFacts(definition);

                var renderer = FindRenderer(definition.rendererPath);
                if (renderer == null || !renderer.enabled)
                {
                    continue;
                }

                bootstrap.World.AddFact(actorId, OntologyPredicates.EquippedPart, definition.partId);
                bootstrap.World.AddFact(definition.partId, OntologyPredicates.HasConcept, OntologyConcepts.CharacterPart);
                if (!string.IsNullOrWhiteSpace(definition.slot))
                {
                    bootstrap.World.AddFact(definition.partId, OntologyPredicates.HasSlot, definition.slot);
                }

                if (definition.facts == null)
                {
                    continue;
                }

                foreach (var fact in definition.facts)
                {
                    if (fact == null || string.IsNullOrWhiteSpace(fact.predicate) || string.IsNullOrWhiteSpace(fact.obj))
                    {
                        continue;
                    }

                    bootstrap.World.AddFact(definition.partId, fact.predicate, fact.obj);
                    if (fact.predicate == OntologyPredicates.GrantsCapability)
                    {
                        bootstrap.World.AddFact(actorId, OntologyPredicates.HasCapability, fact.obj);
                    }
                }
            }
        }

        private void InjectPartDefinitionFacts(OntologyCharacterPartDefinition definition)
        {
            bootstrap.World.AddFact(definition.partId, OntologyPredicates.HasConcept, OntologyConcepts.CharacterPart);
            if (!string.IsNullOrWhiteSpace(definition.slot))
            {
                bootstrap.World.AddFact(definition.partId, OntologyPredicates.HasSlot, definition.slot);
            }

            if (definition.facts == null)
            {
                return;
            }

            foreach (var fact in definition.facts)
            {
                if (fact == null || string.IsNullOrWhiteSpace(fact.predicate) || string.IsNullOrWhiteSpace(fact.obj))
                {
                    continue;
                }

                bootstrap.World.AddFact(definition.partId, fact.predicate, fact.obj);
            }
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

            foreach (var definition in partDatabase.Definitions)
            {
                if (definition == null || string.IsNullOrWhiteSpace(definition.partId))
                {
                    continue;
                }

                if (bootstrap.World.HasFact(actorId, OntologyPredicates.UnequipPart, definition.partId))
                {
                    SetPartEnabled(definition, false);
                    bootstrap.World.RemoveFact(actorId, OntologyPredicates.EquippedPart, definition.partId);
                    bootstrap.World.RemoveFact(actorId, OntologyPredicates.UnequipPart, definition.partId);
                    continue;
                }

                if (bootstrap.World.HasFact(actorId, OntologyPredicates.EquippedPart, definition.partId))
                {
                    DisableConflictingParts(definition);
                    SetPartEnabled(definition, true);
                }
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

        private void DisableConflictingParts(OntologyCharacterPartDefinition equippedDefinition)
        {
            if (partDatabase == null || partDatabase.Definitions == null || string.IsNullOrWhiteSpace(equippedDefinition.slot))
            {
                return;
            }

            EnsureWorldReady();

            foreach (var definition in partDatabase.Definitions)
            {
                if (definition == null || definition == equippedDefinition)
                {
                    continue;
                }

                if (definition.slot == equippedDefinition.slot || SlotsConflict(equippedDefinition, definition))
                {
                    SetPartEnabled(definition, false);
                    if (bootstrap != null && bootstrap.World != null)
                    {
                        bootstrap.World.RemoveFact(actorId, OntologyPredicates.EquippedPart, definition.partId);
                        bootstrap.World.RemoveFact(actorId, OntologyPredicates.UnequipPart, definition.partId);
                    }
                }
            }
        }

        private static bool SlotsConflict(OntologyCharacterPartDefinition equippedDefinition, OntologyCharacterPartDefinition existingDefinition)
        {
            if (equippedDefinition == null || existingDefinition == null)
            {
                return false;
            }

            return HasFact(equippedDefinition, OntologyPredicates.ConflictsWithSlot, existingDefinition.slot)
                || HasFact(existingDefinition, OntologyPredicates.ConflictsWithSlot, equippedDefinition.slot);
        }

        private static bool HasFact(OntologyCharacterPartDefinition definition, string predicate, string obj)
        {
            if (definition.facts == null || string.IsNullOrWhiteSpace(predicate) || string.IsNullOrWhiteSpace(obj))
            {
                return false;
            }

            foreach (var fact in definition.facts)
            {
                if (fact != null && fact.predicate == predicate && fact.obj == obj)
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
                bootstrap = FindFirstObjectByType<OntologyWorldBootstrap>();
            }

            if (bootstrap != null && (bootstrap.World == null || bootstrap.Session == null))
            {
                bootstrap.ResetWorld(logReport: false);
            }
        }
    }
}
