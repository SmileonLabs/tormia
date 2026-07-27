using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;

namespace Tormia.Ontology.Core
{
    public sealed class OntologyCharacterPartAdapter : MonoBehaviour
    {
        [SerializeField] private OntologyWorldBootstrap bootstrap;
        [SerializeField] private OntologyCharacterPartDatabase partDatabase;
        [SerializeField] private Transform visualRoot;
        [SerializeField] private OntologyObject actorObject;
        [SerializeField, Tooltip("Legacy fallback only. Leave empty; OntologyObject.EntityId is authoritative.")]
        private string actorId;
        [SerializeField] private bool applyOnStart = true;
        [SerializeField] private bool injectFactsOnStart = true;
        [SerializeField] private bool syncFromWorldFacts = true;

        public OntologyCharacterPartDatabase PartDatabase => partDatabase;
        public Transform VisualRoot => visualRoot != null ? visualRoot : transform;
        public event Action EquippedPartsChanged;

        /// <summary>
        /// Finds the scene avatar adapter even while session gating keeps the
        /// player inactive before authenticated world entry. Account creation
        /// and preview UI must not require the gameplay avatar to be active.
        /// </summary>
        public static OntologyCharacterPartAdapter FindAvailable()
        {
            return FindAnyObjectByType<OntologyCharacterPartAdapter>(
                FindObjectsInactive.Include);
        }

        private string ActorId => actorObject != null && !string.IsNullOrWhiteSpace(actorObject.EntityId)
            ? actorObject.EntityId
            : actorId;

        public const string FailureDefinitionMissing = "definition_missing";
        public const string FailureRendererMissing = "renderer_missing";
        public const string FailureWorldMissing = "world_missing";
        public const string FailureAlreadyEquipped = "already_equipped";
        public const string FailureAlreadyUnequipped = "already_unequipped";
        public const string FailureRequiredPart = "required_part";

        private readonly Dictionary<Renderer, RendererVisualState> baseRendererStates = new();
        private bool appearanceInitialized;

        private sealed class RendererVisualState
        {
            public Mesh Mesh;
            public Material[] Materials;
            public Bounds LocalBounds;
        }

        private void OnEnable()
        {
            if (bootstrap == null)
            {
                bootstrap = FindAnyObjectByType<OntologyWorldBootstrap>();
            }

            if (bootstrap != null)
            {
                bootstrap.WorldChanged += HandleWorldChanged;
            }
        }

        private void OnDisable()
        {
            if (bootstrap != null)
            {
                bootstrap.WorldChanged -= HandleWorldChanged;
            }
        }

        private void Awake()
        {
            if (actorObject == null)
            {
                actorObject = GetComponentInParent<OntologyObject>();
            }

            if (bootstrap == null)
            {
                bootstrap = FindAnyObjectByType<OntologyWorldBootstrap>();
            }

            if (visualRoot == null)
            {
                // The visual root is a presentation binding, so it belongs in the
                // serialized scene/prefab configuration rather than in a dependency
                // on a third-party character hierarchy name. Falling back to this
                // component root keeps generic characters usable without guessing a
                // child such as "Visual_Base_Mesh".
                visualRoot = transform;
            }

            CacheBaseRendererStates();
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

        private void HandleWorldChanged()
        {
            if (syncFromWorldFacts)
            {
                SyncFromWorldFacts();
            }
        }

        public void ApplyDefaultPreset()
        {
            if (partDatabase == null || partDatabase.Definitions == null || visualRoot == null)
            {
                return;
            }

            CacheBaseRendererStates();
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

            appearanceInitialized = true;
        }

        public void EnsureAppearanceInitialized()
        {
            EnsureWorldReady();
            if (bootstrap == null || bootstrap.World == null || partDatabase == null)
            {
                return;
            }

            foreach (var definition in partDatabase.Definitions)
            {
                if (definition != null
                    && bootstrap.World.HasFact(
                        ActorId,
                        OntologyPredicates.EquippedPart,
                        definition.partId))
                {
                    if (!appearanceInitialized)
                    {
                        SyncRenderersFromWorldFacts();
                    }

                    appearanceInitialized = true;
                    return;
                }
            }

            ApplyDefaultPreset();
            InjectActivePartFacts();
        }

        /// <summary>
        /// Applies an account-owned character appearance profile when its avatar enters a world.
        /// The profile remains account data; this method only projects it into the local ontology
        /// actor presentation and the actor's current equipped_part facts.
        /// An empty profile deliberately resolves to the template's data-defined default preset.
        /// </summary>
        public bool ApplyAccountProfile(IReadOnlyList<string> equippedPartIds)
        {
            if (partDatabase == null || partDatabase.Definitions == null || visualRoot == null)
            {
                return false;
            }

            CacheBaseRendererStates();
            if (equippedPartIds == null || equippedPartIds.Count == 0)
            {
                ApplyDefaultPreset();
                InjectActivePartFacts();
                return true;
            }

            var requestedIds = new HashSet<string>(equippedPartIds, System.StringComparer.Ordinal);
            var clearedRendererPaths = new HashSet<string>();
            foreach (var definition in partDatabase.Definitions)
            {
                if (definition == null || string.IsNullOrWhiteSpace(definition.rendererPath)
                    || !clearedRendererPaths.Add(definition.rendererPath))
                {
                    continue;
                }

                var renderer = FindRenderer(definition.rendererPath);
                if (renderer != null)
                {
                    renderer.enabled = false;
                }
            }

            foreach (var definition in partDatabase.Definitions)
            {
                if (definition == null || !requestedIds.Contains(definition.partId))
                {
                    continue;
                }

                if (definition.visibleInCustomization || !IsLinkedByEquippedDefinition(definition.partId))
                {
                    DisableConflictingParts(definition);
                }

                SetPartEnabled(definition, true);
            }

            InjectActivePartFacts();
            appearanceInitialized = true;
            return true;
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
                ActorId,
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

                if (bootstrap.World.HasFact(ActorId, OntologyPredicates.UnequipPart, definition.partId))
                {
                    factsChanged |= SetDefinitionEquipped(definition, false);
                    continue;
                }

                if (bootstrap.World.HasFact(ActorId, OntologyPredicates.EquippedPart, definition.partId))
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

                if (!bootstrap.World.HasFact(ActorId, OntologyPredicates.EquippedPart, definition.partId))
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
            appearanceInitialized = true;
            EquippedPartsChanged?.Invoke();
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
            EquippedPartsChanged?.Invoke();
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

            if (definition.required)
            {
                reason = FailureRequiredPart;
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
                ? bootstrap.World.HasFact(ActorId, OntologyPredicates.EquippedPart, definition.partId)
                : IsDefinitionActive(definition);
        }

        public bool HasEquippedPartFact(string partId)
        {
            EnsureWorldReady();
            return bootstrap != null && bootstrap.World != null && bootstrap.World.HasFact(ActorId, OntologyPredicates.EquippedPart, partId);
        }

        /// <summary>
        /// Returns the visible account-appearance selection. Callers persist
        /// this only to the selected account character profile, never as an
        /// arbitrary world Fact.
        /// </summary>
        public string[] GetEquippedPartIds()
        {
            if (partDatabase == null || partDatabase.Definitions == null) return System.Array.Empty<string>();
            var result = new List<string>();
            foreach (var definition in partDatabase.Definitions)
            {
                if (definition != null && IsPartEquipped(definition.partId)) result.Add(definition.partId);
            }
            return result.ToArray();
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
                CacheBaseRendererState(renderer);
                renderer.enabled = enabled;
                if (!enabled)
                {
                    return;
                }

                var appliedVariant = definition.useBaseRendererMesh
                    ? RestoreBaseRendererState(renderer)
                    : OntologyCharacterAppearanceProjector.ApplyVariantMeshAndMaterials(
                        definition,
                        renderer);
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
                if (definition.useBaseRendererMesh
                    && renderer is SkinnedMeshRenderer skinnedRenderer
                    && baseRendererStates.TryGetValue(renderer, out var baseState))
                {
                    return skinnedRenderer.sharedMesh == baseState.Mesh;
                }

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
                    factsChanged |= bootstrap.World.AddFactContribution(
                        ActorId,
                        OntologyPredicates.EquippedPart,
                        definition.partId,
                        OntologyFactOrigin.CharacterAppearance);
                    // Legacy local actions wrote equipped_part as a durable world
                    // fact. Appearance is account-owned, so absorb that contribution
                    // into the character-appearance projection.
                    bootstrap.World.RemoveFactContribution(
                        ActorId,
                        OntologyPredicates.EquippedPart,
                        definition.partId,
                        OntologyFactOrigin.Durable);
                    factsChanged |= bootstrap.World.RemoveFact(ActorId, OntologyPredicates.UnequipPart, definition.partId);
                }
                else
                {
                    factsChanged |= bootstrap.World.RemoveFactContribution(
                        ActorId,
                        OntologyPredicates.EquippedPart,
                        definition.partId,
                        OntologyFactOrigin.CharacterAppearance);
                    factsChanged |= bootstrap.World.RemoveFact(ActorId, OntologyPredicates.UnequipPart, definition.partId);
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
                    || !bootstrap.World.HasFact(ActorId, OntologyPredicates.EquippedPart, definition.partId))
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

        private void CacheBaseRendererStates()
        {
            if (partDatabase == null || partDatabase.Definitions == null || visualRoot == null)
            {
                return;
            }

            foreach (var definition in partDatabase.Definitions)
            {
                if (definition != null)
                {
                    CacheBaseRendererState(FindRenderer(definition.rendererPath));
                }
            }
        }

        private void CacheBaseRendererState(Renderer renderer)
        {
            if (renderer == null || baseRendererStates.ContainsKey(renderer))
            {
                return;
            }

            var skinnedRenderer = renderer as SkinnedMeshRenderer;
            baseRendererStates.Add(renderer, new RendererVisualState
            {
                Mesh = skinnedRenderer != null ? skinnedRenderer.sharedMesh : null,
                Materials = renderer.sharedMaterials,
                LocalBounds = skinnedRenderer != null ? skinnedRenderer.localBounds : default
            });
        }

        private bool RestoreBaseRendererState(Renderer renderer)
        {
            if (renderer == null || !baseRendererStates.TryGetValue(renderer, out var state))
            {
                return false;
            }

            renderer.sharedMaterials = state.Materials;
            if (renderer is SkinnedMeshRenderer skinnedRenderer)
            {
                skinnedRenderer.sharedMesh = state.Mesh;
                skinnedRenderer.localBounds = state.LocalBounds;
            }

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

                    // Definitions in the same slot often share one renderer.
                    // Disabling an unequipped definition can therefore hide the
                    // actually equipped variant or one of its linked parts.
                    if (!IsPartEquipped(definition.partId))
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

            return OntologyCharacterAppearanceProjector.FindRenderer(
                visualRoot,
                rendererPath);
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
