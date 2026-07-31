using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Data-driven runtime placement service. It knows no individual prefab or ontology fact;
    /// those are supplied by the selected catalog definition.
    /// </summary>
    public sealed class OntologyRuntimeObjectPlacementController : MonoBehaviour
    {
        [SerializeField] private OntologyPlaceableCatalog catalog;
        [SerializeField] private Transform actor;
        [SerializeField] private Camera placementCamera;
        [SerializeField] private OntologyWorldBootstrap bootstrap;
        [SerializeField] private Transform placedObjectsRoot;
        [SerializeField] private string placedObjectsRootName = "PlacedObjects";
        [SerializeField] private float raycastDistance = 500f;

        private OntologyPlaceableDefinition activeDefinition;
        private GameObject previewObject;
        private bool hasValidPreview;
        private Vector3 previewPosition;
        private Quaternion previewRotation;
        private Vector3 previewSurfacePoint;
        private Vector3 previewSurfaceNormal = Vector3.up;
        private OntologyPlacementSurfaceKind? previewSurfaceKind;
        private float yawOffset;
        private bool waitForPlacementClickRelease;

        public static bool IsPlacementInputCaptured { get; private set; }
        public OntologyPlaceableCatalog Catalog => catalog;
        public bool IsPlacing => activeDefinition != null;
        public bool HasValidPreview => hasValidPreview;
        public Vector3 PreviewPosition => previewPosition;
        public string Status { get; private set; }
        public event Action StateChanged;
        /// <summary>Raised after a local placement has completed and ontology template data has been injected.</summary>
        public event Action<OntologyPlaceableInstance> ObjectPlaced;
        private string statusKey;
        private string statusFallback;
        private object[] statusArguments = Array.Empty<object>();

        public void Configure(OntologyPlaceableCatalog nextCatalog, Transform nextActor, Camera nextCamera, OntologyWorldBootstrap nextBootstrap, Transform nextRoot)
        {
            catalog = nextCatalog;
            actor = nextActor;
            placementCamera = nextCamera;
            bootstrap = nextBootstrap;
            placedObjectsRoot = nextRoot;
        }

        private void Awake()
        {
            if (actor == null) actor = transform;
            if (placementCamera == null) placementCamera = Camera.main;
            if (bootstrap == null) bootstrap = FindAnyObjectByType<OntologyWorldBootstrap>();
            SetStatusKey("placement.status.choose", "Choose an object.");
        }

        private void OnEnable()
        {
            OntologyLanguagePackService.LanguageChanged += RefreshLocalizedStatus;
        }

        private void OnDisable()
        {
            OntologyLanguagePackService.LanguageChanged -= RefreshLocalizedStatus;
            CancelPlacement();
        }

        private void Update()
        {
            if (ReleaseStalePlacementCapture())
            {
                return;
            }

            if (!IsPlacing) return;
            IsPlacementInputCaptured = true;
            UpdatePreviewFromPointer();
            HandlePlacementInput();
        }

        private bool ReleaseStalePlacementCapture()
        {
            if (activeDefinition == null)
            {
                if (!IsPlacementInputCaptured)
                {
                    return false;
                }

                IsPlacementInputCaptured = false;
                StateChanged?.Invoke();
                return true;
            }

            if (activeDefinition.IsValid && previewObject != null)
            {
                return false;
            }

            // A play-mode/domain transition can invalidate the transient
            // definition or preview while leaving the static input owner set.
            // Placement without both authored data and its preview is no longer
            // a valid operation, so release player input through the normal
            // cancellation path.
            CancelPlacement();
            return true;
        }

        public bool BeginPlacement(OntologyPlaceableDefinition definition)
        {
            if (definition == null || !definition.IsValid)
            {
                SetStatusKey(
                    "placement.status.invalid_prefab",
                    "This catalog entry has no valid prefab.");
                return false;
            }

            CancelPlacement();
            activeDefinition = definition;
            IsPlacementInputCaptured = true;
            var policy = GetPolicy(definition);
            previewObject = Instantiate(definition.prefab);
            previewObject.name = "Preview_" + definition.definitionId;
            previewObject.transform.localScale = policy.defaultLocalScale;
            PreparePreview(previewObject);
            yawOffset = 0f;
            // The catalog Select button and placement confirmation both use the left
            // pointer button. Require a release so one click cannot perform both actions.
            waitForPlacementClickRelease = true;
            SetStatusKey(
                "placement.status.instructions",
                "Move the mouse over a valid surface, then left-click to place. Q/E rotates; Esc cancels.");
            UpdatePreviewFromActor();
            StateChanged?.Invoke();
            return true;
        }

        public List<OntologyPlacedObjectRecord> CapturePlacedObjects()
        {
            var records = new List<OntologyPlacedObjectRecord>();
            var root = GetPlacedObjectsRoot();
            foreach (var placed in OntologyPlaceableInstance.ActiveInstances
                         .Where(value => value != null && value.transform.IsChildOf(root))
                         .OrderBy(value => value.name))
            {
                var ontology = placed.GetComponent<OntologyObject>();
                var record = new OntologyPlacedObjectRecord
                {
                    instanceName = placed.name,
                    entityId = ontology == null ? string.Empty : ontology.EntityId,
                    definitionId = placed.DefinitionId,
                    transform = CaptureTransform(placed.transform)
                };

                if (ontology != null)
                {
                    record.concepts.AddRange(ontology.Concepts.Where(value => !string.IsNullOrWhiteSpace(value)));
                    foreach (var fact in ontology.Facts.Where(value =>
                                 value != null &&
                                 !string.IsNullOrWhiteSpace(value.predicate) &&
                                 !string.IsNullOrWhiteSpace(value.obj)))
                    {
                        record.facts.Add(new OntologyFactRecord
                        {
                            subject = ontology.EntityId,
                            predicate = fact.predicate,
                            obj = fact.obj
                        });
                    }
                }

                var ruleBlocks = placed.GetComponent<OntologyRuleBlockAssignment>();
                if (ruleBlocks != null)
                {
                    foreach (var binding in ruleBlocks.Bindings.Where(value =>
                                 value != null &&
                                 !string.IsNullOrWhiteSpace(value.ruleId) &&
                                 !string.IsNullOrWhiteSpace(value.bindingVariable)))
                    {
                        record.ruleBlocks.Add(new OntologyRuleBlockRecord
                        {
                            ruleId = binding.ruleId,
                            bindingVariable = binding.bindingVariable
                        });
                    }
                }

                var ledger = placed.GetComponent<OntologySemanticContributionLedger>();
                if (ledger != null)
                {
                    foreach (var contribution in ledger.Contributions.Where(value =>
                                 value != null &&
                                 !string.IsNullOrWhiteSpace(value.ownerId)))
                    {
                        var contributionRecord = new OntologySemanticContributionRecord
                        {
                            ownerId = contribution.ownerId
                        };
                        contributionRecord.concepts.AddRange(
                            contribution.concepts.Where(value =>
                                !string.IsNullOrWhiteSpace(value)));
                        contributionRecord.facts.AddRange(contribution.facts.Where(value =>
                                value != null &&
                                !string.IsNullOrWhiteSpace(value.predicate) &&
                                !string.IsNullOrWhiteSpace(value.obj))
                            .Select(value => new OntologyFactRecord
                            {
                                subject = placed.name,
                                predicate = value.predicate,
                                obj = value.obj
                            }));
                        contributionRecord.ruleBlocks.AddRange(contribution.ruleBlocks.Where(value =>
                                value != null &&
                                !string.IsNullOrWhiteSpace(value.ruleId) &&
                                !string.IsNullOrWhiteSpace(value.bindingVariable))
                            .Select(value => new OntologyRuleBlockRecord
                            {
                                ruleId = value.ruleId,
                                bindingVariable = value.bindingVariable
                            }));
                        record.semanticContributions.Add(contributionRecord);
                    }
                }

                records.Add(record);
            }

            return records;
        }

        /// <summary>
        /// Restores missing authored baseline data for records created before version 5,
        /// then applies data-driven template retirements for records created before
        /// version 6. Neither step contains prefab-specific conditions: templates own
        /// their semantic evolution, while newer saves preserve later user edits.
        /// </summary>
        public int MigrateLegacyPlacementRecords(OntologySaveData saveData)
        {
            if (saveData == null || saveData.version >= 6 ||
                saveData.placedObjects == null || catalog == null)
            {
                return 0;
            }

            saveData.facts ??= new List<OntologyFactRecord>();
            var restoreMissingBaseline = saveData.version < 5;
            var applyTemplateRetirements = saveData.version < 6;
            var migrated = 0;
            foreach (var record in saveData.placedObjects)
            {
                if (record == null || string.IsNullOrWhiteSpace(record.definitionId) ||
                    string.IsNullOrWhiteSpace(record.instanceName))
                {
                    continue;
                }

                var definition = catalog.Find(record.definitionId);
                var template = definition?.ontologyTemplate;
                if (definition == null || template == null)
                {
                    continue;
                }

                record.concepts ??= new List<string>();
                record.facts ??= new List<OntologyFactRecord>();
                record.ruleBlocks ??= new List<OntologyRuleBlockRecord>();
                var changed = false;

                if (restoreMissingBaseline)
                {
                    foreach (var concept in template.concepts ?? Array.Empty<string>())
                    {
                        if (string.IsNullOrWhiteSpace(concept) || record.concepts.Contains(concept)) continue;
                        record.concepts.Add(concept);
                        changed = true;
                    }

                    foreach (var templateFact in template.facts ?? Array.Empty<OntologyFactEntry>())
                    {
                        if (templateFact == null ||
                            string.IsNullOrWhiteSpace(templateFact.predicate) ||
                            string.IsNullOrWhiteSpace(templateFact.obj) ||
                            record.facts.Any(value => value != null &&
                                                      value.predicate == templateFact.predicate &&
                                                      value.obj == templateFact.obj))
                        {
                            continue;
                        }

                        record.facts.Add(new OntologyFactRecord
                        {
                            subject =
                                OntologyPlacedObjectFactProjection.ResolveSubject(
                                    record),
                            predicate = templateFact.predicate,
                            obj = templateFact.obj
                        });
                        changed = true;
                    }

                    foreach (var binding in definition.defaultRuleBlocks ??
                             Enumerable.Empty<OntologyRuleBlockBinding>())
                    {
                        if (binding == null || string.IsNullOrWhiteSpace(binding.ruleId) ||
                            string.IsNullOrWhiteSpace(binding.bindingVariable) ||
                            record.ruleBlocks.Any(value => value != null &&
                                                          value.ruleId == binding.ruleId &&
                                                          value.bindingVariable == binding.bindingVariable))
                        {
                            continue;
                        }

                        record.ruleBlocks.Add(new OntologyRuleBlockRecord
                        {
                            ruleId = binding.ruleId,
                            bindingVariable = binding.bindingVariable
                        });
                        changed = true;
                    }
                }

                if (applyTemplateRetirements &&
                    OntologyPlacedObjectFactProjection.RemoveRetiredTemplateData(
                        saveData.facts,
                        record,
                        template))
                {
                    changed = true;
                }

                if (!changed) continue;
                migrated++;
                OntologyPlacedObjectFactProjection.Synchronize(
                    saveData.facts,
                    record);
            }

            saveData.version = 6;

            return migrated;
        }

        public int RestorePlacedObjects(IReadOnlyList<OntologyPlacedObjectRecord> records)
        {
            ClearPlacedObjects();
            if (records == null || catalog == null) return 0;

            var root = GetPlacedObjectsRoot();
            var restored = 0;
            foreach (var record in records)
            {
                if (record == null || string.IsNullOrWhiteSpace(record.definitionId)) continue;
                var definition = catalog.Find(record.definitionId);
                if (definition == null || !definition.IsValid) continue;

                var instance = Instantiate(definition.prefab, root);
                instance.name = string.IsNullOrWhiteSpace(record.instanceName)
                    ? GenerateUniqueInstanceName(definition.definitionId)
                    : record.instanceName;
                ApplyTransform(instance.transform, record.transform, GetPolicy(definition).defaultLocalScale);

                var placed = instance.GetComponent<OntologyPlaceableInstance>() ??
                             instance.AddComponent<OntologyPlaceableInstance>();
                placed.Configure(definition.definitionId);

                var ontology = instance.GetComponent<OntologyObject>() ?? instance.AddComponent<OntologyObject>();
                var concepts = record.concepts?.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray() ??
                               Array.Empty<string>();
                var facts = record.facts?
                    .Where(value =>
                        value != null &&
                        !string.IsNullOrWhiteSpace(value.predicate) &&
                        !string.IsNullOrWhiteSpace(value.obj))
                    .Select(value => new OntologyFactEntry
                    {
                        predicate = value.predicate,
                        obj = value.obj
                    })
                    .ToArray() ?? Array.Empty<OntologyFactEntry>();
                facts = EnsurePhysicalProfileFact(facts, definition);
                var restoredEntityId = string.IsNullOrWhiteSpace(record.entityId)
                    ? instance.name
                    : record.entityId;
                ontology.ConfigureOntologyData(restoredEntityId, concepts, facts);
                if (Guid.TryParse(restoredEntityId, out var restoredGuid))
                {
                    var identity = instance.GetComponent<OntologyAuthorityEntityIdentity>() ??
                                   instance.AddComponent<OntologyAuthorityEntityIdentity>();
                    identity.SetGuid(restoredGuid);
                }
                EnsureSemanticAdapters(instance, definition);
                ResetPhysicalMotion(instance);

                if (record.ruleBlocks != null && record.ruleBlocks.Count > 0)
                {
                    var assignment = instance.GetComponent<OntologyRuleBlockAssignment>() ??
                                     instance.AddComponent<OntologyRuleBlockAssignment>();
                    assignment.Replace(record.ruleBlocks.Select(value => new OntologyRuleBlockBinding
                    {
                        ruleId = value.ruleId,
                        bindingVariable = value.bindingVariable
                    }));
                    if (bootstrap == null)
                    {
                        bootstrap = FindAnyObjectByType<OntologyWorldBootstrap>();
                    }

                    if (bootstrap != null)
                    {
                        foreach (var ruleBlock in record.ruleBlocks)
                        {
                            if (ruleBlock != null && !string.IsNullOrWhiteSpace(ruleBlock.ruleId))
                            {
                                bootstrap.RuleBlockRegistry.SetControlled(ruleBlock.ruleId, true);
                            }
                        }
                    }
                }

                if (record.semanticContributions != null &&
                    record.semanticContributions.Count > 0)
                {
                    var ledger = instance.GetComponent<OntologySemanticContributionLedger>() ??
                                 instance.AddComponent<OntologySemanticContributionLedger>();
                    ledger.ReplaceFromRecords(record.semanticContributions);
                    // Older saves may contain preset contribution records but
                    // no catalog-baseline ownership. Merge the data-authored
                    // baseline for only the default bindings that are still
                    // active; intentionally removed defaults stay removed.
                    RecordDefaultSemanticContributions(instance, definition);
                }
                else
                {
                    if (bootstrap == null)
                        bootstrap = FindAnyObjectByType<OntologyWorldBootstrap>();
                    RecordDefaultSemanticContributions(instance, definition);
                }
                restored++;
            }

            return restored;
        }

        public void ClearPlacedObjects()
        {
            var root = GetPlacedObjectsRoot();
            for (var index = root.childCount - 1; index >= 0; index--)
            {
                var child = root.GetChild(index);
                if (child.GetComponent<OntologyPlaceableInstance>() == null) continue;
                child.gameObject.SetActive(false);
                Destroy(child.gameObject);
            }
        }

        public void CancelPlacement()
        {
            if (previewObject != null) Destroy(previewObject);
            previewObject = null;
            activeDefinition = null;
            hasValidPreview = false;
            waitForPlacementClickRelease = false;
            IsPlacementInputCaptured = false;
            if (string.IsNullOrWhiteSpace(Status) ||
                statusKey == "placement.status.instructions" ||
                statusKey == "placement.status.ready")
            {
                SetStatusKey("placement.status.choose", "Choose an object.");
            }
            StateChanged?.Invoke();
        }

        private void HandlePlacementInput()
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null)
            {
                if (Keyboard.current.escapeKey.wasPressedThisFrame) { CancelPlacement(); return; }
                if (Keyboard.current.qKey.wasPressedThisFrame) yawOffset -= 15f;
                if (Keyboard.current.eKey.wasPressedThisFrame) yawOffset += 15f;
            }

            if (Mouse.current != null && waitForPlacementClickRelease)
            {
                if (!Mouse.current.leftButton.isPressed)
                {
                    waitForPlacementClickRelease = false;
                }
                return;
            }

            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame && !OntologyUIPointerUtility.IsPointerOverUi(Mouse.current.position.ReadValue()))
            {
                ConfirmPlacement();
            }
#endif
        }

        private void UpdatePreviewFromPointer()
        {
#if ENABLE_INPUT_SYSTEM
            if (placementCamera == null) placementCamera = Camera.main;
            if (placementCamera == null || Mouse.current == null) return;
            var pointer = Mouse.current.position.ReadValue();
            if (OntologyUIPointerUtility.IsPointerOverUi(pointer)) return;
            var ray = placementCamera.ScreenPointToRay(pointer);
            ApplyBestPreviewSurface(ray, Mathf.Max(1f, raycastDistance));
#endif
        }

        private void UpdatePreviewFromActor()
        {
            if (actor == null || previewObject == null) return;
            var origin = actor.position + Vector3.up * 20f + actor.forward * 2f;
            ApplyBestPreviewSurface(new Ray(origin, Vector3.down), 100f);
        }

        private void ApplyBestPreviewSurface(Ray ray, float maximumDistance)
        {
            var policy = activeDefinition?.placementPolicy ?? new OntologyPlacementPolicy();
            if (!TryResolveSurface(
                    ray,
                    maximumDistance,
                    CanTargetWaterSurface(policy),
                    previewObject == null ? null : previewObject.transform,
                    actor,
                    out var point,
                    out var normal,
                    out var collider,
                    out var surfaceKind))
            {
                return;
            }

            ApplyPreviewSurface(point, normal, collider, surfaceKind);
        }

        public bool TryResolveMoveSurface(
            OntologyPlaceableInstance instance,
            Ray ray,
            out Vector3 point)
        {
            point = default;
            if (instance == null) return false;
            var definition = catalog == null
                ? null
                : catalog.Find(instance.DefinitionId);
            var allowWater = definition == null ||
                             CanTargetWaterSurface(
                                 definition.placementPolicy ??
                                 new OntologyPlacementPolicy());
            var resolved = TryResolveSurface(
                ray,
                Mathf.Max(1f, raycastDistance),
                allowWater,
                instance.transform,
                actor,
                out point,
                out var normal,
                out _,
                out var surfaceKind);
            if (resolved)
            {
                point = CalculateSurfaceSupportedPosition(
                    instance.gameObject,
                    point,
                    normal);
            }

            return resolved;
        }

        private static bool TryResolveSurface(
            Ray ray,
            float maximumDistance,
            bool allowWater,
            Transform ignoredRoot,
            Transform actorRoot,
            out Vector3 point,
            out Vector3 normal,
            out Collider collider,
            out OntologyPlacementSurfaceKind? surfaceKind)
        {
            point = default;
            normal = Vector3.up;
            collider = null;
            surfaceKind = null;

            var solidHits = Physics.RaycastAll(
                ray,
                maximumDistance,
                ~0,
                QueryTriggerInteraction.Ignore);
            Array.Sort(solidHits, (left, right) => left.distance.CompareTo(right.distance));
            var hasSolid = false;
            var solidHit = default(RaycastHit);
            foreach (var candidate in solidHits)
            {
                if (candidate.collider == null ||
                    IsIgnoredPlacementSurface(
                        candidate.collider,
                        ignoredRoot,
                        actorRoot))
                {
                    continue;
                }

                solidHit = candidate;
                hasSolid = true;
                break;
            }

            var hasWater = TryGetWaterPlacementSurface(
                ray,
                maximumDistance,
                allowWater,
                ignoredRoot,
                actorRoot,
                out var waterPoint,
                out var waterCollider,
                out var waterDistance);
            if (hasWater && (!hasSolid || waterDistance <= solidHit.distance))
            {
                point = waterPoint;
                normal = Vector3.up;
                collider = waterCollider;
                surfaceKind = OntologyPlacementSurfaceKind.Water;
                return true;
            }

            if (!hasSolid) return false;
            point = solidHit.point;
            normal = solidHit.normal;
            collider = solidHit.collider;
            var label = collider.GetComponentInParent<OntologyPlacementSurface>();
            surfaceKind = label == null
                ? (OntologyPlacementSurfaceKind?)null
                : label.SurfaceKind;
            return true;
        }

        private static bool TryGetWaterPlacementSurface(
            Ray ray,
            float maximumDistance,
            bool allowWater,
            Transform ignoredRoot,
            Transform actorRoot,
            out Vector3 point,
            out Collider waterCollider,
            out float distance)
        {
            point = default;
            waterCollider = null;
            distance = 0f;
            if (!allowWater || Mathf.Abs(ray.direction.y) <= Mathf.Epsilon)
            {
                return false;
            }

            var hits = Physics.RaycastAll(
                ray,
                maximumDistance,
                ~0,
                QueryTriggerInteraction.Collide);
            Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));

            foreach (var hit in hits)
            {
                if (hit.collider == null ||
                    IsIgnoredPlacementSurface(
                        hit.collider,
                        ignoredRoot,
                        actorRoot))
                {
                    continue;
                }

                var volume = hit.collider == null
                    ? null
                    : hit.collider.GetComponentInParent<OntologyWaterRegionVolume>();
                if (volume == null || !volume.TryGetSurfaceHeight(out var surfaceHeight))
                {
                    continue;
                }

                var candidateDistance = (surfaceHeight - ray.origin.y) / ray.direction.y;
                if (candidateDistance < 0f || candidateDistance > maximumDistance)
                {
                    continue;
                }

                var candidatePoint = ray.GetPoint(candidateDistance);
                var bounds = hit.collider.bounds;
                if (candidatePoint.x < bounds.min.x ||
                    candidatePoint.x > bounds.max.x ||
                    candidatePoint.z < bounds.min.z ||
                    candidatePoint.z > bounds.max.z)
                {
                    continue;
                }

                if (!volume.TryClassifyDepthAt(
                        candidatePoint,
                        ignoredRoot,
                        out var depth) ||
                    depth == OntologyWaterDepth.DryLand)
                {
                    continue;
                }

                point = candidatePoint;
                waterCollider = hit.collider;
                distance = candidateDistance;
                return true;
            }

            return false;
        }

        private static bool IsIgnoredPlacementSurface(
            Collider candidate,
            Transform ignoredRoot,
            Transform actorRoot)
        {
            if (candidate == null)
            {
                return true;
            }

            var candidateTransform = candidate.transform;
            if (ignoredRoot != null &&
                candidateTransform.IsChildOf(ignoredRoot))
            {
                return true;
            }

            if (actorRoot != null &&
                candidateTransform.IsChildOf(actorRoot))
            {
                return true;
            }

            var attachment =
                candidate.GetComponentInParent<OntologyAttachmentAdapter>();
            return attachment != null && attachment.IsAttached;
        }

        private void ApplyPreviewHit(RaycastHit hit)
        {
            ApplyPreviewSurface(hit.point, hit.normal, hit.collider, null);
        }

        private void ApplyPreviewSurface(
            Vector3 point,
            Vector3 normal,
            Collider collider,
            OntologyPlacementSurfaceKind? observedSurfaceKind)
        {
            if (previewObject == null || activeDefinition == null) return;
            var policy = activeDefinition.placementPolicy ?? new OntologyPlacementPolicy();
            previewSurfaceNormal = normal.sqrMagnitude > Mathf.Epsilon
                ? normal.normalized
                : Vector3.up;
            previewSurfaceKind = observedSurfaceKind;
            previewSurfacePoint = point;
            previewPosition = point;
            previewRotation = policy.alignToSurfaceNormal
                ? Quaternion.FromToRotation(Vector3.up, normal) * Quaternion.Euler(0f, yawOffset, 0f)
                : Quaternion.Euler(0f, yawOffset, 0f);
            previewObject.transform.SetPositionAndRotation(previewPosition, previewRotation);
            previewPosition = CalculateSurfaceSupportedPosition(
                previewObject,
                point,
                normal);
            previewObject.transform.position = previewPosition;
            hasValidPreview = Validate(
                point,
                normal,
                collider,
                observedSurfaceKind,
                policy,
                out var reason);
            SetPreviewTint(previewObject, hasValidPreview ? new Color(0.25f, 1f, 0.45f, 1f) : new Color(1f, 0.25f, 0.25f, 1f));
            if (hasValidPreview)
            {
                SetStatusKey(
                    "placement.status.ready",
                    "Ready: left-click to place. Q/E rotates; Esc cancels.");
            }
            else
            {
                SetStatus(reason);
            }
        }

        public static Vector3 CalculateSurfaceSupportedPosition(
            GameObject target,
            Vector3 surfacePoint,
            Vector3 surfaceNormal)
        {
            if (target == null)
            {
                return surfacePoint;
            }

            var normal = surfaceNormal.sqrMagnitude > Mathf.Epsilon
                ? surfaceNormal.normalized
                : Vector3.up;
            var rootPosition = target.transform.position;
            var minimumProjection = float.PositiveInfinity;
            var hasBounds = false;

            foreach (var collider in target.GetComponentsInChildren<Collider>(true))
            {
                if (collider == null || !collider.enabled || collider.isTrigger)
                {
                    continue;
                }

                IncludeBoundsMinimumProjection(
                    collider.bounds,
                    rootPosition,
                    normal,
                    ref minimumProjection);
                hasBounds = true;
            }

            if (!hasBounds)
            {
                foreach (var renderer in target.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer == null)
                    {
                        continue;
                    }

                    IncludeBoundsMinimumProjection(
                        renderer.bounds,
                        rootPosition,
                        normal,
                        ref minimumProjection);
                    hasBounds = true;
                }
            }

            return hasBounds
                ? surfacePoint - normal * minimumProjection
                : surfacePoint;
        }

        private static void IncludeBoundsMinimumProjection(
            Bounds bounds,
            Vector3 rootPosition,
            Vector3 surfaceNormal,
            ref float minimumProjection)
        {
            var min = bounds.min;
            var max = bounds.max;
            for (var x = 0; x < 2; x++)
            for (var y = 0; y < 2; y++)
            for (var z = 0; z < 2; z++)
            {
                var corner = new Vector3(
                    x == 0 ? min.x : max.x,
                    y == 0 ? min.y : max.y,
                    z == 0 ? min.z : max.z);
                minimumProjection = Mathf.Min(
                    minimumProjection,
                    Vector3.Dot(corner - rootPosition, surfaceNormal));
            }
        }

        private bool Validate(
            Vector3 point,
            Vector3 normal,
            Collider collider,
            OntologyPlacementSurfaceKind? observedSurfaceKind,
            OntologyPlacementPolicy policy,
            out string reason)
        {
            if (actor != null)
            {
                var delta = point - actor.position; delta.y = 0f;
                if (delta.magnitude > Mathf.Max(0.1f, policy.maximumDistanceFromActor))
                {
                    reason = OntologyLanguagePackService.Text(
                        "placement.validation.too_far",
                        "Too far away. Move your character closer.");
                    return false;
                }
            }
            if (Vector3.Angle(normal, Vector3.up) > policy.maximumSlopeAngle)
            {
                reason = OntologyLanguagePackService.Text(
                    "placement.validation.too_steep",
                    "This surface is too steep.");
                return false;
            }
            var labelledSurface = collider == null
                ? null
                : collider.GetComponentInParent<OntologyPlacementSurface>();
            var surfaceKind = observedSurfaceKind ??
                              (labelledSurface == null
                                  ? (OntologyPlacementSurfaceKind?)null
                                  : labelledSurface.SurfaceKind);
            if (policy.requiredSurface != OntologyPlacementSurfaceKind.AnyCollider)
            {
                if (!surfaceKind.HasValue && !policy.allowUnclassifiedSurface)
                {
                    reason = OntologyLanguagePackService.Text(
                        "placement.validation.unmarked_surface",
                        "This surface has not been marked for placement yet.");
                    return false;
                }
                if (surfaceKind.HasValue && surfaceKind.Value != policy.requiredSurface)
                {
                    reason = OntologyLanguagePackService.Text(
                        "placement.validation.wrong_surface",
                        "This object cannot be placed on this type of surface.");
                    return false;
                }
            }
            foreach (var placed in OntologyPlaceableInstance.ActiveInstances)
            {
                if (placed == null) continue;
                var delta = placed.transform.position - point; delta.y = 0f;
                if (delta.magnitude < policy.minimumDistanceFromPlacedObject)
                {
                    reason = OntologyLanguagePackService.Text(
                        "placement.validation.too_close",
                        "Too close to another placed object.");
                    return false;
                }
            }
            reason = string.Empty;
            return true;
        }

        private static bool CanTargetWaterSurface(
            OntologyPlacementPolicy policy)
        {
            return policy == null ||
                   policy.requiredSurface != OntologyPlacementSurfaceKind.Ground;
        }

        private void ConfirmPlacement()
        {
            if (!hasValidPreview || activeDefinition == null) return;
            var definition = activeDefinition;
            var policy = GetPolicy(definition);
            var placementPosition =
                previewSurfaceKind == OntologyPlacementSurfaceKind.Water
                    ? previewPosition
                    : CalculateFinalPlacementPosition(
                        definition,
                        previewSurfacePoint,
                        previewSurfaceNormal,
                        previewRotation,
                        policy.defaultLocalScale);
            var authorityBridge = FindAnyObjectByType<OntologyWorldAuthorityBridge>();
            if (authorityBridge != null &&
                authorityBridge.TryRequestAuthorityPlacement(
                    definition,
                    placementPosition,
                    previewRotation,
                    policy.defaultLocalScale))
            {
                SetStatusKey(
                    "placement.status.awaiting_authority",
                    "{0} placement is awaiting World Authority confirmation.",
                    definition.LocalizedDisplayName);
                CancelPlacement();
                return;
            }

            var root = GetPlacedObjectsRoot();
            var instance = Instantiate(
                definition.prefab,
                placementPosition,
                previewRotation,
                root);
            instance.name = GenerateUniqueInstanceName(definition.definitionId);
            instance.transform.localScale = policy.defaultLocalScale;
            var record = instance.GetComponent<OntologyPlaceableInstance>() ?? instance.AddComponent<OntologyPlaceableInstance>();
            record.Configure(definition.definitionId);
            var identity = instance.GetComponent<OntologyAuthorityEntityIdentity>() ??
                           instance.AddComponent<OntologyAuthorityEntityIdentity>();
            identity.EnsureGuid();
            ApplyOntologyTemplate(instance, definition);
            ApplyDefaultRuleBlocks(instance, definition);
            EnsureSemanticAdapters(instance, definition);
            Physics.SyncTransforms();
            if (previewSurfaceKind != OntologyPlacementSurfaceKind.Water)
            {
                instance.transform.position = CalculateSurfaceSupportedPosition(
                    instance,
                    previewSurfacePoint,
                    previewSurfaceNormal);
                Physics.SyncTransforms();
            }
            ResetPhysicalMotion(instance);
            if (bootstrap == null) bootstrap = FindAnyObjectByType<OntologyWorldBootstrap>();
            var ontologyObject = instance.GetComponent<OntologyObject>();
            if (bootstrap != null && ontologyObject != null)
            {
                bootstrap.RegisterSceneObject(
                    ontologyObject,
                    runSimulation: true);
            }
            ObjectPlaced?.Invoke(record);
            SetStatusKey(
                "placement.status.placed",
                "{0} was placed and its ontology data was injected.",
                definition.LocalizedDisplayName);
            CancelPlacement();
        }

        /// <summary>
        /// Creates a presentation from a server-confirmed entity projection. Template
        /// data supplies local adapters; the server identity remains the durable key.
        /// It intentionally does not emit ObjectPlaced, avoiding a network echo.
        /// </summary>
        public OntologyPlaceableInstance CreateAuthorityPresentation(
            string entityGuid,
            string definitionId,
            string displayName,
            Vector3 position,
            Vector3 rotationEuler,
            Vector3 localScale)
        {
            var definition = catalog == null ? null : catalog.Find(definitionId);
            if (definition == null || definition.prefab == null ||
                !Guid.TryParse(entityGuid, out var guid))
            {
                return null;
            }

            var instance = Instantiate(
                definition.prefab,
                position,
                Quaternion.Euler(rotationEuler),
                GetPlacedObjectsRoot());
            instance.name = string.IsNullOrWhiteSpace(displayName)
                ? GenerateUniqueInstanceName(definitionId)
                : displayName;
            instance.transform.localScale = localScale;
            var record = instance.GetComponent<OntologyPlaceableInstance>() ??
                         instance.AddComponent<OntologyPlaceableInstance>();
            record.Configure(definition.definitionId);
            var identity = instance.GetComponent<OntologyAuthorityEntityIdentity>() ??
                           instance.AddComponent<OntologyAuthorityEntityIdentity>();
            identity.SetGuid(guid);
            ApplyOntologyTemplate(instance, definition);
            ApplyDefaultRuleBlocks(instance, definition);
            EnsureSemanticAdapters(instance, definition);
            ResetPhysicalMotion(instance);
            if (bootstrap == null) bootstrap = FindAnyObjectByType<OntologyWorldBootstrap>();
            var ontologyObject = instance.GetComponent<OntologyObject>();
            if (bootstrap != null && ontologyObject != null)
            {
                bootstrap.RegisterSceneObject(ontologyObject, runSimulation: true);
            }
            return record;
        }

        private Vector3 CalculateFinalPlacementPosition(
            OntologyPlaceableDefinition definition,
            Vector3 surfacePoint,
            Vector3 surfaceNormal,
            Quaternion rotation,
            Vector3 localScale)
        {
            if (definition?.prefab == null)
            {
                return surfacePoint;
            }

            var measurement = Instantiate(
                definition.prefab,
                surfacePoint,
                rotation);
            measurement.name = "__PlacementMeasurement";
            measurement.hideFlags = HideFlags.HideAndDontSave;
            measurement.transform.localScale = localScale;
            ApplyOntologyTemplate(measurement, definition);
            EnsureSemanticAdapters(measurement, definition);
            Physics.SyncTransforms();
            var resolved = CalculateSurfaceSupportedPosition(
                measurement,
                surfacePoint,
                surfaceNormal);
            measurement.SetActive(false);
            if (Application.isPlaying)
            {
                Destroy(measurement);
            }
            else
            {
                DestroyImmediate(measurement);
            }
            return resolved;
        }

        private static void ResetPhysicalMotion(GameObject target)
        {
            var body = target == null
                ? null
                : target.GetComponent<OntologyPhysicalBodyAdapter>()?.TargetBody;
            if (body == null)
            {
                return;
            }

            body.position = target.transform.position;
            body.rotation = target.transform.rotation;
            if (!body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
        }

        private Transform GetPlacedObjectsRoot()
        {
            if (placedObjectsRoot != null) return placedObjectsRoot;
            var worldRoot = GameObject.Find("WorldRoot");
            var root = new GameObject(placedObjectsRootName);
            root.transform.SetParent(worldRoot == null ? null : worldRoot.transform, false);
            placedObjectsRoot = root.transform;
            return placedObjectsRoot;
        }

        private void ApplyOntologyTemplate(GameObject target, OntologyPlaceableDefinition definition)
        {
            var template = definition.ontologyTemplate;
            var physicalProfile = catalog == null
                ? definition.physicalProfile
                : catalog.ResolvePhysicalProfile(definition);
            if (template == null && physicalProfile == null) return;
            var ontology = target.GetComponent<OntologyObject>() ?? target.AddComponent<OntologyObject>();
            var concepts = template?.concepts == null
                ? Array.Empty<string>()
                : (string[])template.concepts.Clone();
            var facts = CloneFacts(template?.facts).ToList();
            var profileId = physicalProfile == null
                ? string.Empty
                : physicalProfile.profileId;
            if (!string.IsNullOrWhiteSpace(profileId) &&
                !facts.Any(value =>
                    value != null &&
                    value.predicate == OntologyPredicates.PhysicalProfile))
            {
                facts.Add(new OntologyFactEntry
                {
                    predicate = OntologyPredicates.PhysicalProfile,
                    obj = profileId
                });
            }

            var identity = target.GetComponent<OntologyAuthorityEntityIdentity>() ??
                           target.AddComponent<OntologyAuthorityEntityIdentity>();
            ontology.ConfigureOntologyData(
                identity.EnsureGuid().ToString("D"),
                concepts,
                facts.ToArray());
        }

        private OntologyFactEntry[] EnsurePhysicalProfileFact(
            OntologyFactEntry[] source,
            OntologyPlaceableDefinition definition)
        {
            var facts = CloneFacts(source).ToList();
            var physicalProfile = catalog == null
                ? definition?.physicalProfile
                : catalog.ResolvePhysicalProfile(definition);
            if (physicalProfile == null ||
                string.IsNullOrWhiteSpace(physicalProfile.profileId) ||
                facts.Any(value =>
                    value != null &&
                    value.predicate == OntologyPredicates.PhysicalProfile))
            {
                return facts.ToArray();
            }

            facts.Add(new OntologyFactEntry
            {
                predicate = OntologyPredicates.PhysicalProfile,
                obj = physicalProfile.profileId
            });
            return facts.ToArray();
        }

        private void EnsureSemanticAdapters(
            GameObject target,
            OntologyPlaceableDefinition definition)
        {
            if (target == null) return;
            if (bootstrap == null) bootstrap = FindAnyObjectByType<OntologyWorldBootstrap>();
            var actorOntology = actor == null
                ? null
                : actor.GetComponentInParent<OntologyObject>();
            OntologySemanticAdapterSynchronizer.SynchronizeAll(
                target,
                bootstrap,
                actorOntology);
        }

        private void ApplyDefaultRuleBlocks(
            GameObject target,
            OntologyPlaceableDefinition definition)
        {
            if (target == null || definition?.defaultRuleBlocks == null ||
                definition.defaultRuleBlocks.Count == 0)
            {
                return;
            }

            var assignment = target.GetComponent<OntologyRuleBlockAssignment>() ??
                             target.AddComponent<OntologyRuleBlockAssignment>();
            assignment.Replace(definition.defaultRuleBlocks);

            if (bootstrap == null)
            {
                bootstrap = FindAnyObjectByType<OntologyWorldBootstrap>();
            }

            if (bootstrap == null)
            {
                return;
            }

            foreach (var binding in definition.defaultRuleBlocks)
            {
                if (binding != null && !string.IsNullOrWhiteSpace(binding.ruleId))
                {
                    bootstrap.RuleBlockRegistry.SetControlled(binding.ruleId, true);
                }
            }

            RecordDefaultSemanticContributions(target, definition);
        }

        private void RecordDefaultSemanticContributions(
            GameObject target,
            OntologyPlaceableDefinition definition)
        {
            if (target == null || definition?.defaultRuleBlocks == null ||
                definition.defaultRuleBlocks.Count == 0)
            {
                return;
            }

            var ontology = target.GetComponent<OntologyObject>();
            var assignment = target.GetComponent<OntologyRuleBlockAssignment>();
            if (ontology == null || assignment == null) return;
            var ledger = target.GetComponent<OntologySemanticContributionLedger>() ??
                         target.AddComponent<OntologySemanticContributionLedger>();
            var activeDefaultBindings = definition.defaultRuleBlocks
                .Where(binding =>
                    binding != null &&
                    !string.IsNullOrWhiteSpace(binding.ruleId) &&
                    assignment.Bindings.Any(value =>
                        value != null &&
                        value.ruleId == binding.ruleId &&
                        NormalizeBindingVariable(value.bindingVariable) ==
                        NormalizeBindingVariable(binding.bindingVariable)))
                .Select(binding => new OntologyRuleBlockBinding
                {
                    ruleId = binding.ruleId,
                    bindingVariable = NormalizeBindingVariable(
                        binding.bindingVariable)
                })
                .ToArray();
            if (activeDefaultBindings.Length == 0) return;

            var template = definition.ontologyTemplate;
            var concepts = (template?.concepts ?? Array.Empty<string>())
                .Where(value =>
                    !string.IsNullOrWhiteSpace(value) &&
                    ontology.Concepts.Contains(value))
                .Distinct()
                .ToList();
            var facts = (template?.facts ??
                         Array.Empty<OntologyFactEntry>())
                .Where(value =>
                    value != null &&
                    ontology.Facts.Any(existing =>
                        existing != null &&
                        existing.predicate == value.predicate &&
                        existing.obj == value.obj))
                .Select(value => new OntologyFactEntry
                {
                    predicate = value.predicate,
                    obj = value.obj
                })
                .ToList();
            AddOwnedProfileFact(
                facts,
                ontology,
                OntologyPredicates.PhysicalProfile,
                definition.physicalProfile?.profileId);
            AddOwnedProfileFact(
                facts,
                ontology,
                OntologyPredicates.AttachmentProfile,
                definition.attachmentProfile?.profileId);

            ledger.AddContribution(
                "template_semantic_baseline:" + definition.definitionId +
                ":v" + Mathf.Max(1, definition.semanticContractVersion),
                concepts,
                facts,
                activeDefaultBindings);
        }

        private static void AddOwnedProfileFact(
            ICollection<OntologyFactEntry> facts,
            OntologyObject ontology,
            string predicate,
            string profileId)
        {
            if (facts == null || ontology == null ||
                string.IsNullOrWhiteSpace(predicate) ||
                string.IsNullOrWhiteSpace(profileId) ||
                facts.Any(value =>
                    value != null &&
                    value.predicate == predicate &&
                    value.obj == profileId) ||
                !ontology.Facts.Any(value =>
                    value != null &&
                    value.predicate == predicate &&
                    value.obj == profileId))
            {
                return;
            }

            facts.Add(new OntologyFactEntry
            {
                predicate = predicate,
                obj = profileId
            });
        }

        private static string NormalizeBindingVariable(string value) =>
            string.IsNullOrWhiteSpace(value) ? "?target" : value;

        private static OntologyFactEntry[] CloneFacts(OntologyFactEntry[] source)
        {
            if (source == null || source.Length == 0) return Array.Empty<OntologyFactEntry>();
            var copy = new List<OntologyFactEntry>();
            foreach (var fact in source) if (fact != null) copy.Add(new OntologyFactEntry { predicate = fact.predicate, obj = fact.obj });
            return copy.ToArray();
        }

        private static OntologyTransformRecord CaptureTransform(Transform value)
        {
            var position = value.position;
            var rotation = value.rotation;
            var scale = value.localScale;
            return new OntologyTransformRecord
            {
                positionX = position.x,
                positionY = position.y,
                positionZ = position.z,
                rotationX = rotation.x,
                rotationY = rotation.y,
                rotationZ = rotation.z,
                rotationW = rotation.w,
                scaleX = scale.x,
                scaleY = scale.y,
                scaleZ = scale.z
            };
        }

        private static void ApplyTransform(
            Transform target,
            OntologyTransformRecord record,
            Vector3 fallbackScale)
        {
            if (record == null)
            {
                target.localScale = fallbackScale;
                return;
            }

            target.SetPositionAndRotation(
                new Vector3(record.positionX, record.positionY, record.positionZ),
                new Quaternion(record.rotationX, record.rotationY, record.rotationZ, record.rotationW));
            target.localScale = new Vector3(record.scaleX, record.scaleY, record.scaleZ);
        }

        private static OntologyPlacementPolicy GetPolicy(OntologyPlaceableDefinition definition)
        {
            if (definition.placementPolicy == null) definition.placementPolicy = new OntologyPlacementPolicy();
            return definition.placementPolicy;
        }

        public string GenerateUniqueInstanceName(string baseName)
        {
            var safe = string.IsNullOrWhiteSpace(baseName) ? "PlacedObject" : baseName.Replace(" ", "_");
            var index = 1;
            while (IsEntityIdInUse(
                       safe + "_" + index.ToString("000")))
            {
                index++;
            }

            return safe + "_" + index.ToString("000");
        }

        private bool IsEntityIdInUse(string candidate)
        {
            if (bootstrap == null)
            {
                bootstrap = FindAnyObjectByType<OntologyWorldBootstrap>();
            }

            if (bootstrap != null &&
                bootstrap.EntityRegistry.Contains(candidate))
            {
                return true;
            }

            foreach (var ontologyObject in FindObjectsByType<OntologyObject>(
                         FindObjectsInactive.Include))
            {
                if (ontologyObject != null &&
                    ontologyObject.EntityId == candidate)
                {
                    return true;
                }
            }

            return false;
        }

        private static void PreparePreview(GameObject value)
        {
            foreach (var collider in value.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            foreach (var rigidbody in value.GetComponentsInChildren<Rigidbody>(true)) { rigidbody.isKinematic = true; rigidbody.detectCollisions = false; }
            foreach (var renderer in value.GetComponentsInChildren<Renderer>(true)) renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private static void SetPreviewTint(GameObject value, Color color)
        {
            foreach (var renderer in value.GetComponentsInChildren<Renderer>(true))
            {
                var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block);
                block.SetColor("_BaseColor", color); block.SetColor("_Color", color);
                renderer.SetPropertyBlock(block);
            }
        }

        private void SetStatus(string value)
        {
            statusKey = null;
            statusFallback = null;
            statusArguments = Array.Empty<object>();
            if (Status == value) return;
            Status = value;
            StateChanged?.Invoke();
        }

        private void SetStatusKey(
            string key,
            string fallback,
            params object[] arguments)
        {
            statusKey = key;
            statusFallback = fallback;
            statusArguments = arguments ?? Array.Empty<object>();
            RefreshLocalizedStatus();
        }

        private void RefreshLocalizedStatus()
        {
            if (string.IsNullOrWhiteSpace(statusKey))
            {
                StateChanged?.Invoke();
                return;
            }

            var template = OntologyLanguagePackService.Text(
                statusKey,
                statusFallback);
            var value = statusArguments.Length == 0
                ? template
                : string.Format(template, statusArguments);
            if (Status == value) return;
            Status = value;
            StateChanged?.Invoke();
        }
    }
}
