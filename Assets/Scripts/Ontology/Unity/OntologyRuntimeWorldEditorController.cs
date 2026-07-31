using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Tormia.Ontology.Core
{
    /// <summary>Runtime world editing service for placed catalog instances. It edits transforms, not ontology rules.</summary>
    [DefaultExecutionOrder(-200)]
    public sealed class OntologyRuntimeWorldEditorController : MonoBehaviour
    {
        [SerializeField] private Camera editCamera;
        [SerializeField] private OntologyRuntimeObjectPlacementController placementController;
        [SerializeField] private OntologyWorldBootstrap bootstrap;
        [Header("Input")]
        [SerializeField] private KeyCode toggleKey = KeyCode.Tab;
        [SerializeField] private KeyCode moveKey = KeyCode.G;
        [SerializeField] private KeyCode rotateLeftKey = KeyCode.LeftBracket;
        [SerializeField] private KeyCode rotateRightKey = KeyCode.RightBracket;
        [SerializeField] private KeyCode scaleDownKey = KeyCode.Minus;
        [SerializeField] private KeyCode scaleUpKey = KeyCode.Equals;
        [SerializeField, Min(1f)] private float rotateStepDegrees = 15f;
        [SerializeField, Min(0.01f)] private float scaleStep = 0.1f;
        [SerializeField, Min(0.1f)] private float raycastDistance = 500f;
        [SerializeField, Min(0.1f)] private float nearbySelectionRadius = 8f;
        [SerializeField] private Transform nearbySelectionOrigin;
        [Header("Hierarchy UI Bindings")]
        [SerializeField] private OntologyRuntimeWorldEditHandle editHandle;

        private OntologyPlaceableInstance selected;
        private OntologyRuntimeSelectionMarker marker;
        private bool moving;
        private bool movingNewCopy;
        private Vector3 moveStartPosition;
        private OntologyPhysicsPresentationCoordinator movingPhysicsCoordinator;

        public static bool IsEditInputCaptured { get; private set; }
        public bool IsEditing { get; private set; }
        public bool IsOntologyOpen { get; private set; }
        public OntologyPlaceableInstance Selected => selected;
        public bool IsMoving => moving;
        public Camera EditCamera => editCamera != null ? editCamera : Camera.main;
        public OntologyPlaceableCatalog PlaceableCatalog =>
            placementController != null ? placementController.Catalog : null;
        public string SelectedDisplayName => GetDisplayName(selected);
        public IReadOnlyList<OntologyRuleDefinition> AvailableRuleDefinitions =>
            bootstrap != null && bootstrap.RuleDatabase != null
                ? bootstrap.RuleDatabase.Definitions
                : Array.Empty<OntologyRuleDefinition>();
        public IReadOnlyList<OntologyRuleBlockBinding> SelectedRuleBlocks
        {
            get
            {
                if (selected == null) return Array.Empty<OntologyRuleBlockBinding>();
                var assignment = selected.GetComponent<OntologyRuleBlockAssignment>();
                return assignment != null
                    ? assignment.Bindings
                    : Array.Empty<OntologyRuleBlockBinding>();
            }
        }
        public IReadOnlyList<OntologyPhysicalProfile> AvailablePhysicalProfiles =>
            bootstrap != null && bootstrap.PhysicalProfileDatabase != null
                ? bootstrap.PhysicalProfileDatabase.Profiles
                : Array.Empty<OntologyPhysicalProfile>();
        public IReadOnlyList<OntologyPhysicalEffectProfile> AvailablePhysicalEffects =>
            bootstrap != null && bootstrap.PhysicalEffectDatabase != null
                ? bootstrap.PhysicalEffectDatabase.Effects
                : Array.Empty<OntologyPhysicalEffectProfile>();
        public IReadOnlyList<OntologyAttachmentProfile> AvailableAttachmentProfiles =>
            bootstrap != null && bootstrap.AttachmentProfileDatabase != null
                ? bootstrap.AttachmentProfileDatabase.Profiles
                : Array.Empty<OntologyAttachmentProfile>();
        public event Action StateChanged;
        /// <summary>Raised only after a move, rotation, or scale is committed by the editor.</summary>
        public event Action<OntologyPlaceableInstance> TransformCommitted;
        /// <summary>Raised after the user commits placement of a duplicated instance.</summary>
        public event Action<OntologyPlaceableInstance> DuplicateCommitted;
        /// <summary>
        /// Requests durable retirement of an Authority-projected entity. A handler
        /// returns true only when it accepted ownership of the request.
        /// </summary>
        public event Func<OntologyPlaceableInstance, bool>
            EntityRetirementRequested;
        /// <summary>Raised after an authored triple is added or removed in the runtime editor.</summary>
        public event Action<OntologyPlaceableInstance, string, string, bool> AuthoredFactChanged;
        /// <summary>Raised after an instance rule-block binding is added or removed.</summary>
        public event Action<OntologyPlaceableInstance, string, string, bool> RuleBlockChanged;
        /// <summary>
        /// Raised for a complete Triple + Rule Block + Physical Meaning change.
        /// The Authority bridge commits it atomically before Unity projects it.
        /// </summary>
        public event Action<OntologyPlaceableInstance, OntologyMeaningPackageChange>
            MeaningPackageChangeRequested;

        public string GetDisplayNameForEntity(string entityId)
        {
            if (string.IsNullOrWhiteSpace(entityId))
                return entityId;
            foreach (var candidate in OntologyPlaceableInstance.ActiveInstances)
            {
                if (candidate == null) continue;
                var ontology = candidate.GetComponent<OntologyObject>();
                if (ontology != null &&
                    string.Equals(
                        ontology.EntityId,
                        entityId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return GetDisplayName(candidate);
                }
            }
            return OntologyLanguagePackService.Term(entityId);
        }

        private string GetDisplayName(OntologyPlaceableInstance instance)
        {
            if (instance == null)
                return string.Empty;
            var definition = PlaceableCatalog?.Find(instance.DefinitionId);
            return definition == null
                ? instance.name
                : OntologyLanguagePackService.FormatInstanceName(
                    definition,
                    instance.name);
        }

        public string Status
        {
            get
            {
                if (!IsEditing)
                    return L("world_edit.status.inactive", "Press Tab near an object to select it.");
                if (placementController != null && placementController.IsPlacing)
                    return L("world_edit.status.finish_placement", "Finish or cancel object placement first.");
                if (selected == null)
                    return L("world_edit.status.no_nearby_object", "No nearby editable object was found.");
                return moving
                    ? L("world_edit.status.moving", "Move the mouse to position the object. Left-click confirms; right-click or Esc cancels.")
                    : string.Format(
                        L("world_edit.status.selected", "Selected: {0}"),
                        SelectedDisplayName);
            }
        }

        public string OntologySummary
        {
            get
            {
                if (selected == null)
                    return L("world_edit.summary.no_selection", "No object selected.");
                var ontology = selected.GetComponent<OntologyObject>();
                if (ontology == null)
                    return SelectedDisplayName + "\n" +
                           L("world_edit.summary.no_ontology", "No ontology data is attached.");
                var concepts = ontology.Concepts.Count == 0
                    ? L("common.none", "none")
                    : string.Join(
                        ", ",
                        ontology.Concepts.Select(OntologyLanguagePackService.Term));
                var facts = ontology.Facts
                    .Where(f => f != null)
                    .Select(f =>
                        OntologyLanguagePackService.Term(f.predicate) +
                        " " +
                        GetDisplayNameForEntity(f.obj))
                    .ToArray();
                return SelectedDisplayName +
                       "\n" + L("world_edit.summary.id", "Id") + ": " +
                       ontology.EntityId +
                       "\n" + L("world_edit.summary.concepts", "Concepts") + ": " +
                       concepts +
                       "\n" + L("world_edit.summary.facts", "Facts") + ": " +
                       (facts.Length == 0
                           ? L("common.none", "none")
                           : string.Join(" | ", facts));
            }
        }

        private static string L(string key, string fallback) =>
            OntologyLanguagePackService.Text(key, fallback);

        public void Configure(Camera camera, OntologyRuntimeObjectPlacementController placement, OntologyWorldBootstrap nextBootstrap)
        {
            editCamera = camera;
            placementController = placement;
            bootstrap = nextBootstrap;
        }

        private void Awake()
        {
            if (editCamera == null) editCamera = Camera.main;
            if (placementController == null) placementController = FindAnyObjectByType<OntologyRuntimeObjectPlacementController>();
            if (bootstrap == null) bootstrap = FindAnyObjectByType<OntologyWorldBootstrap>();
            if (nearbySelectionOrigin == null)
            {
                var playerInput = FindAnyObjectByType<OntologyInputSystemPlayerInput>();
                if (playerInput != null) nearbySelectionOrigin = playerInput.transform;
            }
            if (editHandle == null)
                editHandle = FindAnyObjectByType<OntologyRuntimeWorldEditHandle>();
            editHandle?.Configure(this);
        }

        private void OnEnable()
        {
            OntologyLanguagePackService.LanguageChanged += NotifyLanguageChanged;
        }

        private void OnDisable()
        {
            OntologyLanguagePackService.LanguageChanged -= NotifyLanguageChanged;
            if (moving) CancelMove();
            IsEditInputCaptured = false;
            if (marker != null) Destroy(marker.gameObject);
            if (editHandle != null) editHandle.SetTarget(null);
        }

        private void NotifyLanguageChanged() => StateChanged?.Invoke();

        private void Update()
        {
            if (ReleaseStaleSelectionCapture())
            {
                return;
            }

#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current == null) return;
            if (WasPressed(toggleKey))
            {
                SelectNextNearbyPlaceable();
                return;
            }
            if (!IsEditing) return;
            if (selected != null && !IsWorldEditableCandidate(selected))
            {
                SetEditing(false);
                return;
            }
            IsEditInputCaptured = true;
            if (placementController != null && placementController.IsPlacing) return;
            if (Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                if (moving) CancelMove();
                else if (IsOntologyOpen) CloseOntologyEditor();
                else SetEditing(false);
                return;
            }
            if (selected != null)
            {
                if (WasPressed(moveKey)) BeginMove();
                if (WasPressed(rotateLeftKey)) RotateSelection(-rotateStepDegrees);
                if (WasPressed(rotateRightKey)) RotateSelection(rotateStepDegrees);
                if (WasPressed(scaleDownKey)) ScaleSelection(-scaleStep);
                if (WasPressed(scaleUpKey)) ScaleSelection(scaleStep);
                if (Keyboard.current.deleteKey.wasPressedThisFrame) DeleteSelection();
                if ((Keyboard.current.leftCtrlKey.isPressed || Keyboard.current.rightCtrlKey.isPressed) && Keyboard.current.dKey.wasPressedThisFrame) DuplicateSelection();
            }
            if (Mouse.current != null && !OntologyUIPointerUtility.IsPointerOverUi(Mouse.current.position.ReadValue()))
            {
                if (moving) UpdateMoveFromPointer(Mouse.current.position.ReadValue());
                if (moving && Mouse.current.rightButton.wasPressedThisFrame) { CancelMove(); return; }
                if (Mouse.current.leftButton.wasPressedThisFrame) HandleWorldClick(Mouse.current.position.ReadValue());
            }
#endif
        }

        private bool ReleaseStaleSelectionCapture()
        {
            if (!IsEditing || selected != null)
            {
                return false;
            }

            // A selected placeable can be retired or removed by an Authority
            // projection between frames. Unity then reports the destroyed
            // reference as null, so edit mode must release its ephemeral input
            // ownership instead of blocking player clicks indefinitely.
            SetEditing(false);
            return true;
        }

        public void SetEditing(bool value)
        {
            IsEditing = value; IsEditInputCaptured = value;
            if (moving) CancelMove();
            if (!value)
            {
                IsOntologyOpen = false;
                Select(null);
            }
            StateChanged?.Invoke();
        }

        public void OpenOntologyEditor()
        {
            if (!IsEditing || selected == null) return;
            IsOntologyOpen = true;
            IsEditInputCaptured = true;
            StateChanged?.Invoke();
        }

        /// <summary>
        /// Opens the ontology editor from the authored context handle. The handle
        /// carries the selected transform, so this also repairs selection state if
        /// a UI lifecycle change cleared the controller's cached reference.
        /// </summary>
        public void OpenOntologyEditorFor(Transform targetTransform)
        {
            if (targetTransform == null) return;
            var candidate = targetTransform.GetComponentInParent<OntologyPlaceableInstance>();
            if (candidate != null && IsWorldEditableCandidate(candidate))
            {
                Select(candidate);
                IsEditing = true;
                IsEditInputCaptured = true;
            }
            OpenOntologyEditor();
        }

        public void CloseOntologyEditor()
        {
            if (!IsOntologyOpen) return;
            IsOntologyOpen = false;
            StateChanged?.Invoke();
        }

        /// <summary>
        /// Selects nearby editable objects in distance order. The candidate list is
        /// rebuilt on every call so moved, added, removed, attached, or disabled
        /// objects are reflected without maintaining stale selection state.
        /// </summary>
        public void SelectNextNearbyPlaceable()
        {
            if (placementController != null && placementController.IsPlacing) return;
            if (moving) return;
            if (nearbySelectionOrigin == null)
            {
                var playerInput = FindAnyObjectByType<OntologyInputSystemPlayerInput>();
                if (playerInput != null) nearbySelectionOrigin = playerInput.transform;
            }

            if (nearbySelectionOrigin == null)
            {
                SetEditing(false);
                return;
            }

            var origin = nearbySelectionOrigin.position;
            var maximumDistance = Mathf.Max(0.1f, nearbySelectionRadius);
            var candidates = FindObjectsByType<OntologyPlaceableInstance>(
                    FindObjectsInactive.Exclude)
                .Where(IsWorldEditableCandidate)
                .Select(candidate => new NearbySelectionCandidate(
                    candidate,
                    DistanceToPlaceable(origin, candidate)))
                .Where(candidate => candidate.Distance <= maximumDistance)
                .OrderBy(candidate => candidate.Distance)
                .ThenBy(candidate => candidate.Placeable.GetInstanceID())
                .Select(candidate => candidate.Placeable)
                .ToList();

            OntologyPlaceableInstance next = null;
            if (candidates.Count > 0)
            {
                var currentIndex = selected == null
                    ? -1
                    : candidates.IndexOf(selected);
                next = candidates[(currentIndex + 1) % candidates.Count];
            }

            IsOntologyOpen = false;
            IsEditing = next != null;
            IsEditInputCaptured = IsEditing;
            Select(next);
        }

        // Retained for scene events and older callers; selection now cycles.
        public void SelectNearestPlaceable() => SelectNextNearbyPlaceable();

        private readonly struct NearbySelectionCandidate
        {
            public NearbySelectionCandidate(
                OntologyPlaceableInstance placeable,
                float distance)
            {
                Placeable = placeable;
                Distance = distance;
            }

            public OntologyPlaceableInstance Placeable { get; }
            public float Distance { get; }
        }

        private static float DistanceToPlaceable(Vector3 origin, OntologyPlaceableInstance candidate)
        {
            var colliders = candidate.GetComponentsInChildren<Collider>(true);
            if (colliders.Length > 0)
            {
                var best = float.PositiveInfinity;
                foreach (var value in colliders)
                {
                    if (value == null || !value.enabled) continue;
                    best = Mathf.Min(best, Vector3.Distance(origin, value.ClosestPoint(origin)));
                }
                if (!float.IsPositiveInfinity(best)) return best;
            }

            var renderers = candidate.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length > 0)
            {
                var bounds = renderers[0].bounds;
                for (var index = 1; index < renderers.Length; index++)
                    bounds.Encapsulate(renderers[index].bounds);
                return Vector3.Distance(origin, bounds.ClosestPoint(origin));
            }

            return Vector3.Distance(origin, candidate.transform.position);
        }

        private void HandleWorldClick(Vector2 pointer)
        {
            if (editCamera == null) editCamera = Camera.main;
            if (editCamera == null) return;
            var ray = editCamera.ScreenPointToRay(pointer);
            if (moving && selected != null)
            {
                var committed = selected;
                var committedNewCopy = movingNewCopy;
                EndMovePhysicsOverride();
                moving = false;
                movingNewCopy = false;
                RefreshWorld();
                TransformCommitted?.Invoke(committed);
                if (committedNewCopy) DuplicateCommitted?.Invoke(committed);
                StateChanged?.Invoke();
                return;
            }
            if (!Physics.Raycast(ray, out var hit, raycastDistance, ~0, QueryTriggerInteraction.Ignore)) return;
            var candidate = hit.collider.GetComponentInParent<OntologyPlaceableInstance>();
            Select(IsWorldEditableCandidate(candidate) ? candidate : null);
        }

        public void BeginMove() => BeginMove(false);

        private void BeginMove(bool isNewCopy)
        {
            if (selected == null) return;
            moveStartPosition = selected.transform.position;
            movingNewCopy = isNewCopy;
            movingPhysicsCoordinator =
                selected.GetComponent<OntologyPhysicsPresentationCoordinator>();
            movingPhysicsCoordinator?.SetWorldEditOverride(true);
            moving = true;
            StateChanged?.Invoke();
        }

        private void CancelMove()
        {
            if (moving && selected != null)
            {
                if (movingNewCopy)
                {
                    var temporaryCopy = selected;
                    Select(null);
                    Destroy(temporaryCopy.gameObject);
                }
                else selected.transform.position = moveStartPosition;
            }
            EndMovePhysicsOverride();
            moving = false;
            movingNewCopy = false;
            StateChanged?.Invoke();
        }

        private void UpdateMoveFromPointer(Vector2 pointer)
        {
            if (selected == null || editCamera == null) return;
            if (placementController != null &&
                placementController.TryResolveMoveSurface(
                    selected,
                    editCamera.ScreenPointToRay(pointer),
                    out var semanticPoint))
            {
                selected.transform.position = semanticPoint;
                return;
            }

            var hits = Physics.RaycastAll(editCamera.ScreenPointToRay(pointer), raycastDistance, ~0, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
            foreach (var candidate in hits)
            {
                if (candidate.collider.GetComponentInParent<OntologyPlaceableInstance>() == selected) continue;
                selected.transform.position = candidate.point;
                return;
            }
        }

        private void Select(OntologyPlaceableInstance value)
        {
            if (value != null && !IsWorldEditableCandidate(value))
            {
                value = null;
            }
            if (selected != value) IsOntologyOpen = false;
            selected = value;
            if (selected == null)
            {
                if (marker != null) marker.SetTarget(null);
                if (editHandle != null) editHandle.SetTarget(null);
            }
            else
            {
                if (marker == null) marker = OntologyRuntimeSelectionMarker.Create();
                marker.SetTarget(selected.transform);
                if (editHandle == null)
                    editHandle = FindAnyObjectByType<OntologyRuntimeWorldEditHandle>();
                if (editHandle != null)
                {
                    editHandle.Configure(this);
                    editHandle.SetTarget(selected.transform);
                }
            }
            StateChanged?.Invoke();
        }

        public bool IsWorldEditableCandidate(
            OntologyPlaceableInstance candidate)
        {
            if (candidate == null || !candidate.isActiveAndEnabled)
            {
                return false;
            }

            var attachment =
                candidate.GetComponent<OntologyAttachmentAdapter>();
            if (attachment != null && attachment.IsAttached)
            {
                return false;
            }

            var ontology = candidate.GetComponent<OntologyObject>();
            if (ontology == null)
            {
                return true;
            }

            if (bootstrap == null)
            {
                bootstrap = FindAnyObjectByType<OntologyWorldBootstrap>();
            }

            if (bootstrap == null || bootstrap.World == null)
            {
                return true;
            }

            foreach (var fact in bootstrap.World.Facts)
            {
                if (fact.Subject.Value == ontology.EntityId &&
                    fact.Predicate.Value == OntologyPredicates.EquippedBy)
                {
                    return false;
                }
            }

            return true;
        }

        private void EndMovePhysicsOverride()
        {
            movingPhysicsCoordinator?.SetWorldEditOverride(false);
            movingPhysicsCoordinator = null;
        }

        public void RotateSelection(float degrees)
        {
            if (selected == null) return;
            selected.transform.Rotate(Vector3.up, degrees, Space.World); RefreshWorld(); TransformCommitted?.Invoke(selected); StateChanged?.Invoke();
        }
        public void ScaleSelection(float amount)
        {
            if (selected == null) return;
            var next = selected.transform.localScale + Vector3.one * amount;
            if (next.x < 0.05f || next.y < 0.05f || next.z < 0.05f) return;
            selected.transform.localScale = next; RefreshWorld(); TransformCommitted?.Invoke(selected); StateChanged?.Invoke();
        }

        public void DuplicateSelection()
        {
            if (selected == null) return;
            var copy = Instantiate(selected.gameObject, selected.transform.parent);
            copy.name = BuildUniqueName(selected.name + "_Copy", copy.transform.parent);
            copy.transform.position += Vector3.right * 0.75f;
            var copyInstance = copy.GetComponent<OntologyPlaceableInstance>();
            var sourceOntology = selected.GetComponent<OntologyObject>();
            var copyOntology = copy.GetComponent<OntologyObject>();
            var authorityIdentity = copy.GetComponent<OntologyAuthorityEntityIdentity>() ??
                                    copy.AddComponent<OntologyAuthorityEntityIdentity>();
            var copyEntityId = authorityIdentity.RegenerateGuid().ToString("D");
            if (sourceOntology != null && copyOntology != null)
                copyOntology.ConfigureOntologyData(copyEntityId, sourceOntology.Concepts.ToArray(), sourceOntology.Facts.Where(f => f != null).Select(f => new OntologyFactEntry { predicate = f.predicate, obj = f.obj }).ToArray());
            Select(copyInstance);
            // A copy is normally created to be placed somewhere else, so immediately
            // enter the same live mouse-placement mode as the MOVE command.
            BeginMove(true);
        }

        public void DeleteSelection()
        {
            if (selected == null) return;
            var target = selected;
            var request = EntityRetirementRequested;
            if (request != null)
            {
                foreach (Func<OntologyPlaceableInstance, bool> handler in
                         request.GetInvocationList())
                {
                    if (!handler(target)) continue;
                    Select(null);
                    StateChanged?.Invoke();
                    return;
                }
            }

            Select(null);
            Destroy(target.gameObject);
            RefreshWorld();
        }

        public bool AddSelectedConcept(string concept) => ChangeSelectedOntology(concept, null, null, true);
        public bool RemoveSelectedConcept(string concept) => ChangeSelectedOntology(concept, null, null, false);
        public bool AddSelectedFact(string predicate, string obj) => ChangeSelectedOntology(null, predicate, obj, true);
        public bool RemoveSelectedFact(string predicate, string obj) => ChangeSelectedOntology(null, predicate, obj, false);

        public IReadOnlyList<string> GetObjectCandidatesForRelation(
            string relation)
        {
            relation = relation?.Trim();
            if (string.IsNullOrWhiteSpace(relation))
                return Array.Empty<string>();
            if (bootstrap == null)
                bootstrap = FindAnyObjectByType<OntologyWorldBootstrap>();

            var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Add(string value)
            {
                value = value?.Trim();
                if (!string.IsNullOrWhiteSpace(value))
                    candidates.Add(value);
            }

            foreach (var registered in
                     OntologyLanguagePackService.GetRegisteredObjectCandidates(
                         relation))
            {
                Add(registered);
            }

            if (bootstrap != null && bootstrap.World != null)
            {
                foreach (var fact in bootstrap.World.Facts)
                {
                    if (string.Equals(
                            fact.Predicate.Value,
                            relation,
                            StringComparison.OrdinalIgnoreCase))
                        Add(fact.Object.Value);
                }
            }

            if (relation == OntologyPredicates.HasConcept)
            {
                var policy = bootstrap != null
                    ? bootstrap.ConceptPolicyDatabase
                    : null;
                if (policy != null)
                {
                    foreach (var definition in policy.Concepts)
                    {
                        if (definition != null && definition.selectable)
                            Add(definition.id);
                    }
                }
            }
            else if (relation == OntologyPredicates.PhysicalProfile)
            {
                foreach (var profile in AvailablePhysicalProfiles)
                    if (profile != null) Add(profile.profileId);
            }
            else if (relation == OntologyPredicates.AttachmentProfile)
            {
                foreach (var profile in AvailableAttachmentProfiles)
                    if (profile != null) Add(profile.profileId);
            }
            else if (relation == OntologyPredicates.HasSlot)
            {
                foreach (var profile in AvailableAttachmentProfiles)
                    if (profile != null) Add(profile.slotId);
            }
            else if (relation == OntologyPredicates.PickupBehavior)
            {
                foreach (var profile in AvailableAttachmentProfiles)
                {
                    if (profile == null) continue;
                    Add(profile.kind switch
                    {
                        OntologyAttachmentKind.Carryable =>
                            OntologyObjects.SelectThenCarry,
                        OntologyAttachmentKind.Mountable =>
                            OntologyObjects.SelectThenMount,
                        _ => OntologyObjects.SelectThenEquip
                    });
                }
            }

            if (IsEntityReferenceRelation(relation) &&
                bootstrap != null &&
                bootstrap.World != null)
            {
                foreach (var fact in bootstrap.World.Facts)
                {
                    Add(fact.Subject.Value);
                    Add(fact.Object.Value);
                }
            }

            return candidates.OrderBy(value => value).ToArray();
        }

        /// <summary>
        /// Returns skill identifiers that are already meaningful to the current ontology
        /// catalog or world. This deliberately derives choices from rules and authored
        /// facts instead of keeping a second, hard-coded UI list of skills.
        /// </summary>
        public IReadOnlyList<string> GetAvailableTemporaryGrantSkills()
        {
            if (bootstrap == null)
                bootstrap = FindAnyObjectByType<OntologyWorldBootstrap>();

            var skills = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Add(string value)
            {
                value = value?.Trim();
                if (!string.IsNullOrWhiteSpace(value) && !value.StartsWith("?"))
                    skills.Add(value);
            }

            if (bootstrap?.World != null)
            {
                foreach (var fact in bootstrap.World.Facts)
                {
                    if (fact.Predicate.Value == OntologyPredicates.GrantsSkill ||
                        fact.Predicate.Value == OntologyPredicates.HasSkill ||
                        fact.Predicate.Value == OntologyPredicates.HasTemporarySkill ||
                        fact.Predicate.Value == OntologyPredicates.CanUseSkill)
                    {
                        Add(fact.Object.Value);
                    }
                }
            }

            // A skill can also be meaningful before any world object has authored it.
            // For example, the swimming movement rule declares the canonical Swimming
            // skill as a fixed condition. Reading the rule catalog keeps the picker
            // extensible as new skills are published.
            foreach (var rule in AvailableRuleDefinitions)
            {
                foreach (var condition in rule?.conditions ??
                             new List<OntologyCondition>())
                {
                    if (condition.kind == OntologyConditionKind.Fact &&
                        condition.predicate == OntologyPredicates.CanUseSkill)
                    {
                        Add(condition.obj);
                    }
                }
            }

            return skills
                .OrderBy(OntologyLanguagePackService.Term)
                .ToArray();
        }

        /// <summary>
        /// Authors the two explicit facts required by the temporary-skill rule, then
        /// applies that rule block to the selected item. The creator selects both the
        /// granted skill and the already-present rule block that keeps the grant valid;
        /// no semantic value is inferred from the object's visual prefab or its name.
        /// </summary>
        public bool ConfigureSelectedTemporarySkillGrant(
            string skillId,
            string requiredRuleId)
        {
            skillId = skillId?.Trim();
            requiredRuleId = requiredRuleId?.Trim();
            if (selected == null || string.IsNullOrWhiteSpace(skillId) ||
                string.IsNullOrWhiteSpace(requiredRuleId) || skillId.StartsWith("?"))
            {
                return false;
            }

            if (bootstrap == null)
                bootstrap = FindAnyObjectByType<OntologyWorldBootstrap>();
            var preset = bootstrap?.RuleBlockPresetDatabase?.Find("temporary_skill");
            if (preset == null || string.IsNullOrWhiteSpace(preset.primaryRuleId))
                return false;

            var assignment = selected.GetComponent<OntologyRuleBlockAssignment>();
            if (assignment == null || !assignment.Bindings.Any(binding =>
                    binding != null && binding.ruleId == requiredRuleId))
            {
                return false;
            }

            if (MeaningPackageChangeRequested != null)
            {
                var change = BuildMeaningPackageChange(preset);
                change.slotId = "temporary_skill_grant";
                change.packageId = "temporary_skill_" + skillId;
                change.replacePredicateIds.Add(
                    OntologyPredicates.GrantsSkill);
                change.replacePredicateIds.Add(
                    OntologyPredicates.SkillGrantRequiresRule);
                change.authoredFacts.Add(new OntologyFactEntry
                {
                    predicate = OntologyPredicates.GrantsSkill,
                    obj = skillId
                });
                change.authoredFacts.Add(new OntologyFactEntry
                {
                    predicate =
                        OntologyPredicates.SkillGrantRequiresRule,
                    obj = requiredRuleId
                });
                MeaningPackageChangeRequested.Invoke(selected, change);
                return true;
            }

            var ontology = selected.GetComponent<OntologyObject>() ??
                           selected.gameObject.AddComponent<OntologyObject>();
            var conceptsBefore = new HashSet<string>(ontology.Concepts.Where(value =>
                !string.IsNullOrWhiteSpace(value)));
            var factsBefore = new HashSet<string>(ontology.Facts.Where(value =>
                    value != null && !string.IsNullOrWhiteSpace(value.predicate) &&
                    !string.IsNullOrWhiteSpace(value.obj))
                .Select(value => FactKey(value.predicate, value.obj)));
            var ruleBlocksBefore = new HashSet<string>(assignment.Bindings.Where(value =>
                    value != null && !string.IsNullOrWhiteSpace(value.ruleId) &&
                    !string.IsNullOrWhiteSpace(value.bindingVariable))
                .Select(value => RuleBlockKey(value.ruleId, value.bindingVariable)));

            var facts = ontology.Facts
                .Where(value => value != null &&
                                !string.IsNullOrWhiteSpace(value.predicate) &&
                                !string.IsNullOrWhiteSpace(value.obj))
                .Select(value => new OntologyFactEntry
                {
                    predicate = value.predicate,
                    obj = value.obj
                })
                .ToList();
            if (!facts.Any(value => value.predicate == OntologyPredicates.GrantsSkill &&
                                    value.obj == skillId))
            {
                facts.Add(new OntologyFactEntry
                {
                    predicate = OntologyPredicates.GrantsSkill,
                    obj = skillId
                });
            }
            if (!facts.Any(value => value.predicate == OntologyPredicates.SkillGrantRequiresRule &&
                                    value.obj == requiredRuleId))
            {
                facts.Add(new OntologyFactEntry
                {
                    predicate = OntologyPredicates.SkillGrantRequiresRule,
                    obj = requiredRuleId
                });
            }
            ontology.ConfigureOntologyData(
                ontology.EntityId,
                ontology.Concepts.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray(),
                facts.ToArray());

            var applied = AddSelectedRuleBlock(
                preset.primaryRuleId,
                preset.bindingVariable);
            foreach (var binding in preset.additionalRuleBlocks.Where(value =>
                         value != null && !string.IsNullOrWhiteSpace(value.ruleId)))
            {
                applied |= AddSelectedRuleBlock(binding.ruleId, binding.bindingVariable);
            }

            assignment = selected.GetComponent<OntologyRuleBlockAssignment>();
            var allPresent = assignment != null && assignment.Bindings.Any(value =>
                value != null && value.ruleId == preset.primaryRuleId);
            if (!allPresent)
                return false;

            RecordPresetContribution(
                preset,
                ontology,
                assignment,
                conceptsBefore,
                factsBefore,
                ruleBlocksBefore);
            OntologySemanticAdapterSynchronizer.SynchronizeAll(selected.gameObject, bootstrap);
            RefreshWorld(runSimulation: true);
            StateChanged?.Invoke();
            return applied || allPresent;
        }

        private static bool IsEntityReferenceRelation(string relation)
        {
            return relation == "located_in" ||
                   relation == "related_to" ||
                   relation == OntologyPredicates.Occupies ||
                   relation == OntologyPredicates.SupportedBy ||
                   relation == OntologyPredicates.RestingOn;
        }

        public OntologyConceptValidationResult ValidateSelectedConcept(
            string concept,
            string ignoredExistingConcept = null)
        {
            if (selected == null)
                return new OntologyConceptValidationResult(
                    true,
                    true,
                    L(
                        "world_edit.summary.no_selection",
                        "No object is selected."));
            if (bootstrap == null) bootstrap = FindAnyObjectByType<OntologyWorldBootstrap>();
            var policy = bootstrap != null ? bootstrap.ConceptPolicyDatabase : null;
            if (policy == null) return OntologyConceptValidationResult.Valid;

            var ontology = selected.GetComponent<OntologyObject>();
            return policy.ValidateAddition(
                ontology != null ? ontology.Concepts : Array.Empty<string>(),
                concept,
                ignoredExistingConcept);
        }

        public List<string> GetRuleVariables(string ruleId)
        {
            var definition = AvailableRuleDefinitions.FirstOrDefault(value =>
                value != null && value.id == ruleId);
            return OntologyRuleBlockResolver.GetVariables(definition);
        }

        public IReadOnlyList<OntologyRuleBlockPreset> AvailableRuleBlockPresets
        {
            get
            {
                if (bootstrap == null)
                    bootstrap = FindAnyObjectByType<OntologyWorldBootstrap>();
                return bootstrap?.RuleBlockPresetDatabase?.Presets ??
                       Array.Empty<OntologyRuleBlockPreset>();
            }
        }

        /// <summary>
        /// Applies a semantic shortcut to the selected placed object. Templates remain
        /// untouched: a preset only authors facts and rule bindings on this instance.
        /// </summary>
        public bool ApplySelectedRuleBlockPreset(string presetId)
        {
            if (selected == null) return false;
            if (bootstrap == null)
                bootstrap = FindAnyObjectByType<OntologyWorldBootstrap>();
            var preset = bootstrap?.RuleBlockPresetDatabase?.Find(presetId);
            if (preset == null || string.IsNullOrWhiteSpace(preset.primaryRuleId))
                return false;
            if (!string.IsNullOrWhiteSpace(
                    GetSelectedRuleBlockPresetValidationMessage(presetId)))
            {
                return false;
            }

            if (MeaningPackageChangeRequested != null)
            {
                MeaningPackageChangeRequested.Invoke(
                    selected,
                    BuildMeaningPackageChange(preset));
                return true;
            }

            var ontology = selected.GetComponent<OntologyObject>() ??
                           selected.gameObject.AddComponent<OntologyObject>();
            var conceptsBefore = new HashSet<string>(ontology.Concepts.Where(value =>
                !string.IsNullOrWhiteSpace(value)));
            var factsBefore = new HashSet<string>(ontology.Facts.Where(value =>
                    value != null &&
                    !string.IsNullOrWhiteSpace(value.predicate) &&
                    !string.IsNullOrWhiteSpace(value.obj))
                .Select(value => FactKey(value.predicate, value.obj)));
            var assignmentBefore = selected.GetComponent<OntologyRuleBlockAssignment>();
            var ruleBlocksBefore = new HashSet<string>(assignmentBefore?.Bindings.Where(value =>
                    value != null &&
                    !string.IsNullOrWhiteSpace(value.ruleId) &&
                    !string.IsNullOrWhiteSpace(value.bindingVariable))
                .Select(value => RuleBlockKey(value.ruleId, value.bindingVariable)) ??
                Enumerable.Empty<string>());

            if (!string.IsNullOrWhiteSpace(preset.physicalProfileId) &&
                !SetSelectedPhysicalBehavior(preset.physicalProfileId))
            {
                return false;
            }

            var concepts = ontology.Concepts
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToList();
            var facts = ontology.Facts
                .Where(value => value != null &&
                                !string.IsNullOrWhiteSpace(value.predicate) &&
                                !string.IsNullOrWhiteSpace(value.obj))
                .Select(value => new OntologyFactEntry
                {
                    predicate = value.predicate,
                    obj = value.obj
                })
                .ToList();

            foreach (var concept in preset.requiredConcepts.Where(value =>
                         !string.IsNullOrWhiteSpace(value)))
            {
                if (!concepts.Contains(concept)) concepts.Add(concept);
            }
            foreach (var fact in preset.requiredFacts.Where(value =>
                         value != null &&
                         !string.IsNullOrWhiteSpace(value.predicate) &&
                         !string.IsNullOrWhiteSpace(value.obj)))
            {
                if (!facts.Any(value => value.predicate == fact.predicate &&
                                        value.obj == fact.obj))
                {
                    facts.Add(new OntologyFactEntry
                    {
                        predicate = fact.predicate,
                        obj = fact.obj
                    });
                }
            }

            ontology.ConfigureOntologyData(ontology.EntityId, concepts.ToArray(), facts.ToArray());
            var applied = AddSelectedRuleBlock(
                preset.primaryRuleId,
                preset.bindingVariable);
            foreach (var binding in preset.additionalRuleBlocks.Where(value =>
                         value != null && !string.IsNullOrWhiteSpace(value.ruleId)))
            {
                applied |= AddSelectedRuleBlock(binding.ruleId, binding.bindingVariable);
            }

            // AddSelectedRuleBlock intentionally reports false for an existing binding.
            // A preset is still successful when all of its desired bindings already exist.
            var assignment = selected.GetComponent<OntologyRuleBlockAssignment>();
            var allPresent = assignment != null &&
                             assignment.Bindings.Any(value => value != null &&
                                 value.ruleId == preset.primaryRuleId);
            RecordPresetContribution(
                preset,
                ontology,
                assignment,
                conceptsBefore,
                factsBefore,
                ruleBlocksBefore);
            OntologySemanticAdapterSynchronizer.SynchronizeAll(selected.gameObject, bootstrap);
            RefreshWorld(runSimulation: true);
            StateChanged?.Invoke();
            return applied || allPresent;
        }

        /// <summary>
        /// Checks only authored prerequisites. It never guesses missing semantic meaning
        /// from a prefab's visual shape or name.
        /// </summary>
        public string GetSelectedRuleBlockPresetValidationMessage(string presetId)
        {
            if (selected == null)
                return OntologyLanguagePackService.Text(
                    "rule_preset.validation.no_object",
                    "먼저 월드 오브젝트를 선택하세요.");
            if (bootstrap == null)
                bootstrap = FindAnyObjectByType<OntologyWorldBootstrap>();
            var preset = bootstrap?.RuleBlockPresetDatabase?.Find(presetId);
            var ontology = selected.GetComponent<OntologyObject>();
            if (preset == null || ontology == null)
            {
                return OntologyLanguagePackService.Text(
                    "rule_preset.validation.unavailable",
                    "규칙 설정 데이터를 찾을 수 없습니다.");
            }

            var missing = new List<string>();
            foreach (var concept in preset.requiredExistingConcepts.Where(value =>
                         !string.IsNullOrWhiteSpace(value)))
            {
                if (!ontology.Concepts.Contains(concept))
                    missing.Add(OntologyLanguagePackService.Term(concept));
            }
            foreach (var predicate in preset.requiredExistingPredicates.Where(value =>
                         !string.IsNullOrWhiteSpace(value)))
            {
                if (!ontology.Facts.Any(value =>
                        value != null && value.predicate == predicate &&
                        !string.IsNullOrWhiteSpace(value.obj)))
                {
                    missing.Add(OntologyLanguagePackService.Term(predicate));
                }
            }

            return missing.Count == 0
                ? string.Empty
                : string.Format(
                    OntologyLanguagePackService.Text(
                        "rule_preset.validation.requires",
                        "이 규칙에 필요한 정보: {0}"),
                    string.Join(", ", missing));
        }

        public bool AddSelectedRuleBlock(string ruleId, string bindingVariable)
        {
            if (selected == null || string.IsNullOrWhiteSpace(ruleId)) return false;
            if (bootstrap == null) bootstrap = FindAnyObjectByType<OntologyWorldBootstrap>();
            var definition = AvailableRuleDefinitions.FirstOrDefault(value =>
                value != null && value.id == ruleId);
            if (definition == null) return false;

            var variables = OntologyRuleBlockResolver.GetVariables(definition);
            if (variables.Count == 0) return false;
            if (string.IsNullOrWhiteSpace(bindingVariable) || !variables.Contains(bindingVariable))
                bindingVariable = variables.Contains("?target") ? "?target" : variables[0];

            var assignment = selected.GetComponent<OntologyRuleBlockAssignment>() ??
                             selected.gameObject.AddComponent<OntologyRuleBlockAssignment>();
            if (!assignment.Add(ruleId, bindingVariable)) return false;

            bootstrap.RuleBlockRegistry.SetControlled(ruleId, true);
            OntologySemanticAdapterSynchronizer.SynchronizeAll(
                selected.gameObject,
                bootstrap);
            RefreshWorld(runSimulation: true);
            RuleBlockChanged?.Invoke(selected, ruleId, bindingVariable, true);
            StateChanged?.Invoke();
            return true;
        }

        public bool RemoveSelectedRuleBlock(string ruleId, string bindingVariable)
        {
            if (selected == null) return false;
            var assignment = selected.GetComponent<OntologyRuleBlockAssignment>();
            if (assignment == null || !assignment.Remove(ruleId, bindingVariable)) return false;

            var ledger = selected.GetComponent<OntologySemanticContributionLedger>();
            var completedContributions = ledger == null
                ? Array.Empty<OntologySemanticContribution>()
                : ledger.RemoveRuleBinding(ruleId, bindingVariable).ToArray();
            if (completedContributions.Length > 0)
            {
                RemoveUnclaimedContributionData(completedContributions, ledger);
            }
            OntologySemanticAdapterSynchronizer.SynchronizeAll(
                selected.gameObject,
                bootstrap);
            RefreshWorld(runSimulation: true);
            RuleBlockChanged?.Invoke(selected, ruleId, bindingVariable, false);
            StateChanged?.Invoke();
            return true;
        }

        private void RecordPresetContribution(
            OntologyRuleBlockPreset preset,
            OntologyObject ontology,
            OntologyRuleBlockAssignment assignment,
            ISet<string> conceptsBefore,
            ISet<string> factsBefore,
            ISet<string> ruleBlocksBefore)
        {
            if (preset == null || ontology == null ||
                string.IsNullOrWhiteSpace(preset.presetId))
            {
                return;
            }

            var addedConcepts = ontology.Concepts
                .Where(value => !string.IsNullOrWhiteSpace(value) &&
                                !conceptsBefore.Contains(value))
                .ToArray();
            var addedFacts = ontology.Facts
                .Where(value => value != null &&
                                !string.IsNullOrWhiteSpace(value.predicate) &&
                                !string.IsNullOrWhiteSpace(value.obj) &&
                                !factsBefore.Contains(FactKey(value.predicate, value.obj)))
                .Select(value => new OntologyFactEntry
                {
                    predicate = value.predicate,
                    obj = value.obj
                })
                .ToArray();
            var addedRuleBlocks = assignment?.Bindings
                .Where(value => value != null &&
                                !string.IsNullOrWhiteSpace(value.ruleId) &&
                                !string.IsNullOrWhiteSpace(value.bindingVariable) &&
                                !ruleBlocksBefore.Contains(
                                    RuleBlockKey(value.ruleId, value.bindingVariable)))
                .Select(value => new OntologyRuleBlockBinding
                {
                    ruleId = value.ruleId,
                    bindingVariable = value.bindingVariable
                })
                .ToArray() ?? Array.Empty<OntologyRuleBlockBinding>();
            if (addedConcepts.Length == 0 && addedFacts.Length == 0 &&
                addedRuleBlocks.Length == 0)
            {
                return;
            }

            var ledger = selected.GetComponent<OntologySemanticContributionLedger>() ??
                         selected.gameObject.AddComponent<OntologySemanticContributionLedger>();
            ledger.AddContribution(
                "rule_preset:" + preset.presetId,
                addedConcepts,
                addedFacts,
                addedRuleBlocks);
        }

        private void RemoveUnclaimedContributionData(
            IEnumerable<OntologySemanticContribution> removedContributions,
            OntologySemanticContributionLedger ledger)
        {
            var ontology = selected?.GetComponent<OntologyObject>();
            if (ontology == null || ledger == null) return;

            var concepts = ontology.Concepts
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToList();
            var facts = ontology.Facts
                .Where(value => value != null &&
                                !string.IsNullOrWhiteSpace(value.predicate) &&
                                !string.IsNullOrWhiteSpace(value.obj))
                .Select(value => new OntologyFactEntry
                {
                    predicate = value.predicate,
                    obj = value.obj
                })
                .ToList();

            foreach (var contribution in removedContributions.Where(value => value != null))
            {
                foreach (var concept in contribution.concepts.Where(value =>
                             !string.IsNullOrWhiteSpace(value) &&
                             !ledger.IsConceptClaimed(value)))
                {
                    concepts.RemoveAll(value => value == concept);
                }

                foreach (var fact in contribution.facts.Where(value =>
                             value != null &&
                             !string.IsNullOrWhiteSpace(value.predicate) &&
                             !string.IsNullOrWhiteSpace(value.obj) &&
                             !ledger.IsFactClaimed(value.predicate, value.obj)))
                {
                    facts.RemoveAll(value => value.predicate == fact.predicate &&
                                             value.obj == fact.obj);
                }
            }

            ontology.ConfigureOntologyData(
                ontology.EntityId,
                concepts.ToArray(),
                facts.ToArray());
        }

        private static string FactKey(string predicate, string obj) =>
            predicate + "\n" + obj;

        private static string RuleBlockKey(string ruleId, string bindingVariable) =>
            ruleId + "\n" + bindingVariable;

        public bool SetRuleBlockControlled(string ruleId, bool controlled)
        {
            if (bootstrap == null) bootstrap = FindAnyObjectByType<OntologyWorldBootstrap>();
            if (bootstrap == null || !bootstrap.RuleBlockRegistry.SetControlled(ruleId, controlled))
                return false;
            RefreshWorld(runSimulation: true);
            StateChanged?.Invoke();
            return true;
        }

        private bool ChangeSelectedOntology(string concept, string predicate, string obj, bool add)
        {
            if (selected == null) return false;
            var ontology = selected.GetComponent<OntologyObject>() ?? selected.gameObject.AddComponent<OntologyObject>();
            var concepts = ontology.Concepts.Where(value => !string.IsNullOrWhiteSpace(value)).ToList();
            var facts = ontology.Facts.Where(value => value != null && !string.IsNullOrWhiteSpace(value.predicate) && !string.IsNullOrWhiteSpace(value.obj)).Select(value => new OntologyFactEntry { predicate = value.predicate, obj = value.obj }).ToList();
            if (!string.IsNullOrWhiteSpace(concept))
            {
                concept = OntologyLanguagePackService.CanonicalTerm(concept.Trim());
                if (add) { if (concepts.Contains(concept)) return false; concepts.Add(concept); }
                else if (!concepts.Remove(concept)) return false;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(predicate) || string.IsNullOrWhiteSpace(obj)) return false;
                predicate = OntologyLanguagePackService.CanonicalTerm(predicate.Trim());
                obj = OntologyLanguagePackService.CanonicalObjectForRelation(predicate, obj.Trim());
                if (string.IsNullOrWhiteSpace(predicate) || string.IsNullOrWhiteSpace(obj)) return false;
                var index = facts.FindIndex(value => value.predicate == predicate && value.obj == obj);
                if (add) { if (index >= 0) return false; facts.Add(new OntologyFactEntry { predicate = predicate, obj = obj }); }
                else { if (index < 0) return false; facts.RemoveAt(index); }
            }
            ontology.ConfigureOntologyData(ontology.EntityId, concepts.ToArray(), facts.ToArray());
            if (bootstrap == null) bootstrap = FindAnyObjectByType<OntologyWorldBootstrap>();
            OntologySemanticAdapterSynchronizer.SynchronizeAll(
                selected.gameObject,
                bootstrap);
            RefreshWorld(); StateChanged?.Invoke();
            AuthoredFactChanged?.Invoke(
                selected,
                string.IsNullOrWhiteSpace(concept)
                    ? predicate
                    : OntologyPredicates.HasConcept,
                string.IsNullOrWhiteSpace(concept)
                    ? obj
                    : concept,
                add);
            return true;
        }

        public OntologyPhysicalProfile GetSelectedPhysicalProfile()
        {
            if (selected == null) return null;
            if (bootstrap == null) bootstrap = FindAnyObjectByType<OntologyWorldBootstrap>();
            return OntologySemanticAdapterSynchronizer.ResolvePhysicalProfile(
                selected.GetComponent<OntologyObject>(),
                bootstrap == null ? null : bootstrap.PhysicalProfileDatabase);
        }

        public IReadOnlyList<OntologyPhysicalEffectProfile> GetSelectedPhysicalEffects()
        {
            if (selected == null)
                return Array.Empty<OntologyPhysicalEffectProfile>();
            if (bootstrap == null)
                bootstrap = FindAnyObjectByType<OntologyWorldBootstrap>();
            var database = bootstrap == null
                ? null
                : bootstrap.PhysicalEffectDatabase;
            var ontology = selected.GetComponent<OntologyObject>();
            if (database == null || ontology == null)
                return Array.Empty<OntologyPhysicalEffectProfile>();

            return ontology.Facts
                .Where(value =>
                    value != null &&
                    value.predicate == OntologyPredicates.HasPhysicalEffect)
                .Select(value => database.Find(value.obj))
                .Where(value => value != null)
                .Distinct()
                .ToArray();
        }

        public IReadOnlyList<OntologyPhysicalEffectProfile>
            GetCompatiblePhysicalEffects(string profileId)
        {
            if (bootstrap == null)
                bootstrap = FindAnyObjectByType<OntologyWorldBootstrap>();
            var database = bootstrap == null
                ? null
                : bootstrap.PhysicalEffectDatabase;
            return database == null
                ? Array.Empty<OntologyPhysicalEffectProfile>()
                : database.Effects
                    .Where(value =>
                        value != null &&
                        database.IsCompatible(value.effectId, profileId))
                    .ToArray();
        }

        /// <summary>
        /// Authors the semantic prerequisites for a selected attachment profile.
        /// The profile, slot, interaction behaviour, and object concept remain
        /// explicit ontology data; this helper merely prevents the UI from asking
        /// creators to enter four coupled triples by hand.
        /// </summary>
        public bool ConfigureSelectedAttachmentBehavior(string profileId)
        {
            if (selected == null || string.IsNullOrWhiteSpace(profileId)) return false;
            if (bootstrap == null)
                bootstrap = FindAnyObjectByType<OntologyWorldBootstrap>();
            var profile = bootstrap?.AttachmentProfileDatabase?.Find(profileId);
            if (profile == null || string.IsNullOrWhiteSpace(profile.slotId)) return false;

            var concept = profile.kind switch
            {
                OntologyAttachmentKind.Carryable => OntologyConcepts.Carryable,
                OntologyAttachmentKind.Mountable => OntologyConcepts.Mountable,
                _ => OntologyConcepts.Wearable
            };
            var pickupBehavior = profile.kind switch
            {
                OntologyAttachmentKind.Carryable => OntologyObjects.SelectThenCarry,
                OntologyAttachmentKind.Mountable => OntologyObjects.SelectThenMount,
                _ => OntologyObjects.SelectThenEquip
            };
            if (MeaningPackageChangeRequested != null)
            {
                MeaningPackageChangeRequested.Invoke(
                    selected,
                    new OntologyMeaningPackageChange
                    {
                        operation = "apply",
                        applicationId = Guid.NewGuid().ToString("D"),
                        slotId = "attachment_meaning",
                        packageId = "attachment_profile_" + profile.profileId,
                        replacePredicateIds = new List<string>
                        {
                            OntologyPredicates.AttachmentProfile,
                            OntologyPredicates.HasSlot,
                            OntologyPredicates.PickupBehavior
                        },
                        requiredConceptIds = new List<string> { concept },
                        authoredFacts = new List<OntologyFactEntry>
                        {
                            new()
                            {
                                predicate =
                                    OntologyPredicates.AttachmentProfile,
                                obj = profile.profileId
                            },
                            new()
                            {
                                predicate = OntologyPredicates.HasSlot,
                                obj = profile.slotId
                            },
                            new()
                            {
                                predicate =
                                    OntologyPredicates.PickupBehavior,
                                obj = pickupBehavior
                            }
                        }
                    });
                return true;
            }
            var ontology = selected.GetComponent<OntologyObject>() ??
                           selected.gameObject.AddComponent<OntologyObject>();
            var concepts = ontology.Concepts
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToList();
            if (!concepts.Contains(concept)) concepts.Add(concept);
            var facts = ontology.Facts
                .Where(value => value != null &&
                                !string.IsNullOrWhiteSpace(value.predicate) &&
                                !string.IsNullOrWhiteSpace(value.obj))
                .Where(value => value.predicate != OntologyPredicates.AttachmentProfile &&
                                value.predicate != OntologyPredicates.HasSlot &&
                                value.predicate != OntologyPredicates.PickupBehavior)
                .Select(value => new OntologyFactEntry
                {
                    predicate = value.predicate,
                    obj = value.obj
                })
                .ToList();
            facts.Add(new OntologyFactEntry
            {
                predicate = OntologyPredicates.AttachmentProfile,
                obj = profile.profileId
            });
            facts.Add(new OntologyFactEntry
            {
                predicate = OntologyPredicates.HasSlot,
                obj = profile.slotId
            });
            facts.Add(new OntologyFactEntry
            {
                predicate = OntologyPredicates.PickupBehavior,
                obj = pickupBehavior
            });
            ontology.ConfigureOntologyData(ontology.EntityId, concepts.ToArray(), facts.ToArray());
            OntologySemanticAdapterSynchronizer.SynchronizeAll(selected.gameObject, bootstrap);
            RefreshWorld(runSimulation: true);
            StateChanged?.Invoke();
            return true;
        }

        public bool AddSelectedPhysicalEffect(string effectId)
        {
            if (selected == null) return false;
            if (bootstrap == null)
                bootstrap = FindAnyObjectByType<OntologyWorldBootstrap>();
            var database = bootstrap == null
                ? null
                : bootstrap.PhysicalEffectDatabase;
            var profile = GetSelectedPhysicalProfile();
            var effect = database == null ? null : database.Find(effectId);
            if (profile == null ||
                effect == null ||
                !database.IsCompatible(effect.effectId, profile.profileId))
            {
                return false;
            }

            var rule = AvailableRuleDefinitions.FirstOrDefault(value =>
                value != null && value.id == effect.activationRuleId);
            var bindingVariable = ResolveActivationBindingVariable(rule, effect.effectId);
            if (rule == null || string.IsNullOrWhiteSpace(bindingVariable))
                return false;

            if (MeaningPackageChangeRequested != null)
            {
                var change = new OntologyMeaningPackageChange
                {
                    operation = "apply",
                    applicationId = Guid.NewGuid().ToString("D"),
                    slotId = "physical_effect_" + effect.effectId,
                    packageId = "physical_effect_" + effect.effectId,
                    authoredFacts = new List<OntologyFactEntry>
                    {
                        new()
                        {
                            predicate =
                                OntologyPredicates.HasPhysicalEffect,
                            obj = effect.effectId
                        }
                    }
                };
                AddMeaningPackageRule(
                    change, effect.activationRuleId, bindingVariable);
                MeaningPackageChangeRequested.Invoke(selected, change);
                return true;
            }

            var ontology = selected.GetComponent<OntologyObject>() ??
                           selected.gameObject.AddComponent<OntologyObject>();
            if (ontology.Facts.Any(value =>
                    value != null &&
                    value.predicate == OntologyPredicates.HasPhysicalEffect &&
                    value.obj == effect.effectId))
            {
                return false;
            }

            var concepts = ontology.Concepts
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToArray();
            var facts = ontology.Facts
                .Where(value =>
                    value != null &&
                    !string.IsNullOrWhiteSpace(value.predicate) &&
                    !string.IsNullOrWhiteSpace(value.obj))
                .Select(value => new OntologyFactEntry
                {
                    predicate = value.predicate,
                    obj = value.obj
                })
                .ToList();
            facts.Add(new OntologyFactEntry
            {
                predicate = OntologyPredicates.HasPhysicalEffect,
                obj = effect.effectId
            });

            var assignment = selected.GetComponent<OntologyRuleBlockAssignment>() ??
                             selected.gameObject.AddComponent<OntologyRuleBlockAssignment>();
            assignment.Add(effect.activationRuleId, bindingVariable);
            bootstrap.RuleBlockRegistry.SetControlled(
                effect.activationRuleId,
                true);
            ontology.ConfigureOntologyData(
                ontology.EntityId,
                concepts,
                facts.ToArray());
            OntologySemanticAdapterSynchronizer.SynchronizeAll(
                selected.gameObject,
                bootstrap);
            RefreshWorld(runSimulation: true);
            StateChanged?.Invoke();
            return true;
        }

        public bool RemoveSelectedPhysicalEffect(string effectId)
        {
            if (selected == null) return false;
            if (bootstrap == null)
                bootstrap = FindAnyObjectByType<OntologyWorldBootstrap>();
            var database = bootstrap == null
                ? null
                : bootstrap.PhysicalEffectDatabase;
            var effect = database == null ? null : database.Find(effectId);
            var ontology = selected.GetComponent<OntologyObject>();
            if (effect == null || ontology == null) return false;

            if (MeaningPackageChangeRequested != null)
            {
                MeaningPackageChangeRequested.Invoke(
                    selected,
                    new OntologyMeaningPackageChange
                    {
                        operation = "remove",
                        slotId = "physical_effect_" + effect.effectId,
                        packageId = string.Empty
                    });
                return true;
            }

            var facts = ontology.Facts
                .Where(value =>
                    value != null &&
                    !string.IsNullOrWhiteSpace(value.predicate) &&
                    !string.IsNullOrWhiteSpace(value.obj))
                .Select(value => new OntologyFactEntry
                {
                    predicate = value.predicate,
                    obj = value.obj
                })
                .ToList();
            var removed = facts.RemoveAll(value =>
                value.predicate == OntologyPredicates.HasPhysicalEffect &&
                value.obj == effect.effectId) > 0;
            if (!removed) return false;

            RemovePhysicalEffectRuleBinding(
                selected.GetComponent<OntologyRuleBlockAssignment>(),
                effect,
                facts);
            ontology.ConfigureOntologyData(
                ontology.EntityId,
                ontology.Concepts.ToArray(),
                facts.ToArray());
            OntologySemanticAdapterSynchronizer.SynchronizeAll(
                selected.gameObject,
                bootstrap);
            RefreshWorld(runSimulation: true);
            StateChanged?.Invoke();
            return true;
        }

        public bool SetSelectedBuoyancy(string profileId, bool enabled)
        {
            var fallback = placementController?.Catalog?.DefaultPhysicalProfile;
            return SetSelectedPhysicalBehavior(
                enabled
                    ? profileId
                    : fallback != null
                        ? fallback.profileId
                        : null);
        }

        /// <summary>
        /// Applies one complete physical meaning to the selected object.
        /// The profile is the single authoring choice; dependent concepts, inferred-state
        /// residue, and rule-block bindings are synchronized atomically.
        /// </summary>
        public bool SetSelectedPhysicalBehavior(string profileId)
        {
            if (selected == null) return false;
            if (bootstrap == null) bootstrap = FindAnyObjectByType<OntologyWorldBootstrap>();
            if (bootstrap == null || bootstrap.PhysicalProfileDatabase == null) return false;

            var profile = string.IsNullOrWhiteSpace(profileId)
                ? null
                : bootstrap.PhysicalProfileDatabase.Find(profileId);
            if (!string.IsNullOrWhiteSpace(profileId) && profile == null)
                return false;

            if (MeaningPackageChangeRequested != null)
            {
                MeaningPackageChangeRequested.Invoke(
                    selected,
                    BuildPhysicalMeaningPackageChange(profile));
                return true;
            }

            var ontology = selected.GetComponent<OntologyObject>() ??
                           selected.gameObject.AddComponent<OntologyObject>();
            var concepts = ontology.Concepts
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToList();
            var facts = ontology.Facts
                .Where(value =>
                    value != null &&
                    !string.IsNullOrWhiteSpace(value.predicate) &&
                    !string.IsNullOrWhiteSpace(value.obj))
                .Select(value => new OntologyFactEntry
                {
                    predicate = value.predicate,
                    obj = value.obj
                })
                .ToList();
            facts.RemoveAll(value =>
                value.predicate == OntologyPredicates.PhysicalProfile ||
                value.predicate == OntologyPredicates.PhysicalState);
            concepts.RemoveAll(value =>
                value == OntologyConcepts.FloatableObject);

            var assignment = selected.GetComponent<OntologyRuleBlockAssignment>() ??
                             selected.gameObject.AddComponent<OntologyRuleBlockAssignment>();
            var buoyancyRuleIds = bootstrap.PhysicalProfileDatabase.Profiles
                .Where(value =>
                    value != null &&
                    value.supportsBuoyancy &&
                    !string.IsNullOrWhiteSpace(value.buoyancyRuleId))
                .Select(value => value.buoyancyRuleId)
                .Distinct()
                .ToArray();
            var buoyancyRule = profile != null && profile.supportsBuoyancy
                ? AvailableRuleDefinitions.FirstOrDefault(value =>
                    value != null && value.id == profile.buoyancyRuleId)
                : null;
            var buoyancyBindingVariable = buoyancyRule == null
                ? null
                : FindEffectSubject(
                    buoyancyRule,
                    OntologyPredicates.PhysicalState,
                    OntologyObjects.Floating);
            if (profile != null &&
                profile.supportsBuoyancy &&
                (buoyancyRule == null ||
                 string.IsNullOrWhiteSpace(buoyancyBindingVariable)))
            {
                return false;
            }

            foreach (var ruleId in buoyancyRuleIds)
            {
                foreach (var binding in assignment.Bindings
                             .Where(value => value != null && value.ruleId == ruleId)
                             .ToArray())
                {
                    assignment.Remove(binding.ruleId, binding.bindingVariable);
                }
            }

            var effectDatabase = bootstrap.PhysicalEffectDatabase;
            if (effectDatabase != null)
            {
                var removedEffects = facts
                    .Where(value =>
                        value.predicate ==
                        OntologyPredicates.HasPhysicalEffect &&
                        (profile == null ||
                         !effectDatabase.IsCompatible(
                             value.obj,
                             profile.profileId)))
                    .Select(value => effectDatabase.Find(value.obj))
                    .Where(value => value != null)
                    .ToArray();
                facts.RemoveAll(value =>
                    value.predicate ==
                    OntologyPredicates.HasPhysicalEffect &&
                    (profile == null ||
                     !effectDatabase.IsCompatible(
                         value.obj,
                         profile.profileId)));
                foreach (var removedEffect in removedEffects)
                {
                    RemovePhysicalEffectRuleBinding(
                        assignment,
                        removedEffect,
                        facts);
                }
            }

            if (profile != null)
            {
                facts.Add(new OntologyFactEntry
                {
                    predicate = OntologyPredicates.PhysicalProfile,
                    obj = profile.profileId
                });

                if (profile.supportsBuoyancy)
                {
                    concepts.Add(OntologyConcepts.FloatableObject);
                    assignment.Add(
                        buoyancyRule.id,
                        buoyancyBindingVariable);
                    bootstrap.RuleBlockRegistry.SetControlled(
                        buoyancyRule.id,
                        true);
                }
            }

            ontology.ConfigureOntologyData(
                ontology.EntityId,
                concepts.ToArray(),
                facts.ToArray());
            OntologySemanticAdapterSynchronizer.SynchronizeAll(
                selected.gameObject,
                bootstrap);
            RefreshWorld(runSimulation: true);
            StateChanged?.Invoke();
            return true;
        }

        private OntologyMeaningPackageChange BuildMeaningPackageChange(
            OntologyRuleBlockPreset preset)
        {
            var change = new OntologyMeaningPackageChange
            {
                operation = "apply",
                applicationId = Guid.NewGuid().ToString("D"),
                slotId = string.IsNullOrWhiteSpace(preset.physicalProfileId)
                    ? "rule_preset_" + preset.presetId
                    : "primary_physical_meaning",
                packageId = "rule_preset_" + preset.presetId,
                requiredConceptIds = preset.requiredConcepts
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct()
                    .ToList(),
                authoredFacts = preset.requiredFacts
                    .Where(value => value != null &&
                                    !string.IsNullOrWhiteSpace(value.predicate) &&
                                    !string.IsNullOrWhiteSpace(value.obj))
                    .Select(value => new OntologyFactEntry
                    {
                        predicate = value.predicate,
                        obj = value.obj
                    })
                    .ToList()
            };
            if (!string.IsNullOrWhiteSpace(preset.physicalProfileId))
            {
                change.replacePredicateIds.Add(
                    OntologyPredicates.PhysicalProfile);
                change.replacePredicateIds.Add(
                    OntologyPredicates.PhysicalState);
                change.authoredFacts.Add(new OntologyFactEntry
                {
                    predicate = OntologyPredicates.PhysicalProfile,
                    obj = preset.physicalProfileId
                });
            }
            AddMeaningPackageRule(
                change, preset.primaryRuleId, preset.bindingVariable);
            foreach (var binding in preset.additionalRuleBlocks.Where(
                         value => value != null))
            {
                AddMeaningPackageRule(
                    change, binding.ruleId, binding.bindingVariable);
            }
            return change;
        }

        private OntologyMeaningPackageChange BuildPhysicalMeaningPackageChange(
            OntologyPhysicalProfile profile)
        {
            var change = new OntologyMeaningPackageChange
            {
                operation = profile == null ? "remove" : "apply",
                applicationId = profile == null
                    ? string.Empty
                    : Guid.NewGuid().ToString("D"),
                slotId = "primary_physical_meaning",
                packageId = profile == null
                    ? string.Empty
                    : "physical_profile_" + profile.profileId
            };
            if (profile == null) return change;
            change.replacePredicateIds.Add(OntologyPredicates.PhysicalProfile);
            change.replacePredicateIds.Add(OntologyPredicates.PhysicalState);
            change.authoredFacts.Add(new OntologyFactEntry
            {
                predicate = OntologyPredicates.PhysicalProfile,
                obj = profile.profileId
            });
            if (profile.supportsBuoyancy)
            {
                change.requiredConceptIds.Add(
                    OntologyConcepts.FloatableObject);
                var rule = AvailableRuleDefinitions.FirstOrDefault(value =>
                    value != null && value.id == profile.buoyancyRuleId);
                var variable = FindEffectSubject(
                    rule,
                    OntologyPredicates.PhysicalState,
                    OntologyObjects.Floating);
                AddMeaningPackageRule(
                    change, profile.buoyancyRuleId, variable);
            }
            return change;
        }

        private void AddMeaningPackageRule(
            OntologyMeaningPackageChange change,
            string ruleId,
            string bindingVariable)
        {
            if (change == null || string.IsNullOrWhiteSpace(ruleId) ||
                change.ruleBlocks.Any(value =>
                    value != null && value.ruleId == ruleId))
            {
                return;
            }
            change.ruleBlocks.Add(new OntologyMeaningPackageRuleBlock
            {
                bindingId = Guid.NewGuid().ToString("D"),
                ruleId = ruleId,
                ruleVersion = ResolveRuleVersion(ruleId),
                bindingVariable = string.IsNullOrWhiteSpace(bindingVariable)
                    ? "?target"
                    : bindingVariable
            });
        }

        private int ResolveRuleVersion(string ruleId)
        {
            var definition = AvailableRuleDefinitions.FirstOrDefault(value =>
                value != null && value.id == ruleId);
            return definition == null
                ? 1
                : Math.Max(1, definition.catalogVersion);
        }

        private static string FindEffectSubject(
            OntologyRuleDefinition rule,
            string predicate,
            string obj)
        {
            return rule?.effects
                .FirstOrDefault(effect =>
                    effect != null &&
                    effect.predicate == predicate &&
                    effect.obj == obj)
                ?.subject;
        }

        private static string ResolveActivationBindingVariable(
            OntologyRuleDefinition rule,
            string effectId)
        {
            return rule?.effects
                .FirstOrDefault(value =>
                    value != null &&
                    value.kind == OntologyEffectKind.AddFact &&
                    value.predicate ==
                    OntologyPredicates.ActivePhysicalEffect &&
                    value.obj == effectId)
                ?.subject;
        }

        private void RemovePhysicalEffectRuleBinding(
            OntologyRuleBlockAssignment assignment,
            OntologyPhysicalEffectProfile effect,
            IReadOnlyList<OntologyFactEntry> remainingFacts)
        {
            if (assignment == null || effect == null) return;
            var database = bootstrap == null
                ? null
                : bootstrap.PhysicalEffectDatabase;
            var ruleStillNeeded = remainingFacts.Any(value =>
            {
                if (value == null ||
                    value.predicate != OntologyPredicates.HasPhysicalEffect)
                    return false;
                var remainingEffect = database?.Find(value.obj);
                return remainingEffect != null &&
                       remainingEffect.activationRuleId ==
                       effect.activationRuleId;
            });
            if (ruleStillNeeded) return;

            foreach (var binding in assignment.Bindings
                         .Where(value =>
                             value != null &&
                             value.ruleId == effect.activationRuleId)
                         .ToArray())
            {
                assignment.Remove(binding.ruleId, binding.bindingVariable);
            }
        }

        private void RefreshWorld(bool runSimulation = false)
        {
            if (bootstrap == null) bootstrap = FindAnyObjectByType<OntologyWorldBootstrap>();
            if (bootstrap == null) return;
            bootstrap.SynchronizeSceneObjects(runSimulation);
        }

        private static string BuildUniqueName(string baseName, Transform parent)
        {
            var index = 1; var candidate = baseName;
            while (parent != null && parent.Find(candidate) != null) candidate = baseName + "_" + (index++).ToString("000");
            return candidate;
        }

#if ENABLE_INPUT_SYSTEM
        private static bool WasPressed(KeyCode value) => System.Enum.TryParse<Key>(value.ToString(), out var key) && Keyboard.current[key].wasPressedThisFrame;
#endif
    }
}
