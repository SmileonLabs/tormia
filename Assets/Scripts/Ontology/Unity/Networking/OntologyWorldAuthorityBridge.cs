using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// One-way development publisher from existing local authoring data to the
    /// authority. It is intentionally explicit: current local UI remains usable
    /// until command-first editor operations replace it in the following slice.
    /// </summary>
    public sealed class OntologyWorldAuthorityBridge : MonoBehaviour
    {
        [SerializeField] private OntologyWorldAuthorityClient authorityClient;
        [SerializeField] private OntologyRuntimeObjectPlacementController placementController;
        [SerializeField] private OntologyRuntimeWorldEditorController worldEditorController;
        [SerializeField, Tooltip("The locally controlled avatar. Live Transform ownership belongs to input/motion and checkpoint restore, not durable world projection refreshes.")]
        private OntologyAuthorityEntityIdentity localAvatarIdentity;
        [SerializeField, TextArea] private string lastPublishStatus;
        private readonly HashSet<string> publishedEntityIds =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Guid> remoteRuleBindingIds =
            new(StringComparer.Ordinal);
        private readonly HashSet<string> semanticProjectionInitialized =
            new(StringComparer.OrdinalIgnoreCase);
        private bool suppressOutgoingChanges;

        public string LastPublishStatus => lastPublishStatus;
        public bool HasSelectedAuthorityWorld => authorityClient != null && authorityClient.IsWorldRuntimeReady;
        public bool CanEditAuthorityWorld => authorityClient != null && authorityClient.CanEditCurrentWorld;
        public event Action StatusChanged;

        public void Configure(OntologyWorldAuthorityClient client)
        {
            authorityClient = client;
        }

        private void Awake()
        {
            ResolveDependencies();
        }

        private void OnEnable()
        {
            ResolveDependencies();
            SubscribeDependencies();
        }

        private void OnDisable()
        {
            UnsubscribeDependencies();
        }

        /// <summary>
        /// Rebinds presentation/editor dependencies after the World and UI
        /// scenes have been loaded additively around the persistent services.
        /// </summary>
        public void RefreshSceneBindings()
        {
            if (isActiveAndEnabled)
            {
                UnsubscribeDependencies();
            }

            placementController = null;
            worldEditorController = null;
            ResolveDependencies();

            if (isActiveAndEnabled)
            {
                SubscribeDependencies();
            }
        }

        private void SubscribeDependencies()
        {
            if (authorityClient != null)
            {
                authorityClient.ProjectionReceived += ApplyProjection;
                authorityClient.ProjectionZoneScopeChanged += HandleProjectionZoneScopeChanged;
            }
            if (placementController != null)
            {
                placementController.ObjectPlaced += HandleLocalPlacement;
            }
            if (worldEditorController != null)
            {
                worldEditorController.TransformCommitted += HandleTransformCommitted;
                worldEditorController.DuplicateCommitted += HandleDuplicateCommitted;
                worldEditorController.AuthoredFactChanged += HandleAuthoredFactChanged;
                worldEditorController.RuleBlockChanged += HandleRuleBlockChanged;
            }
        }

        private void UnsubscribeDependencies()
        {
            if (authorityClient != null)
            {
                authorityClient.ProjectionReceived -= ApplyProjection;
                authorityClient.ProjectionZoneScopeChanged -= HandleProjectionZoneScopeChanged;
            }
            if (placementController != null)
            {
                placementController.ObjectPlaced -= HandleLocalPlacement;
            }
            if (worldEditorController != null)
            {
                worldEditorController.TransformCommitted -= HandleTransformCommitted;
                worldEditorController.DuplicateCommitted -= HandleDuplicateCommitted;
                worldEditorController.AuthoredFactChanged -= HandleAuthoredFactChanged;
                worldEditorController.RuleBlockChanged -= HandleRuleBlockChanged;
            }
        }

        [ContextMenu("Publish Existing Placed Objects to Authority")]
        public void PublishExistingPlacedObjects()
        {
            if (authorityClient == null)
            {
                authorityClient = GetComponent<OntologyWorldAuthorityClient>() ??
                                  FindAnyObjectByType<OntologyWorldAuthorityClient>();
            }
            if (authorityClient == null)
            {
                SetStatus("No authority client is available.");
                return;
            }

            StartCoroutine(PublishRoutine());
        }

        public void PublishMove(OntologyPlaceableInstance instance)
        {
            if (instance == null || authorityClient == null || !authorityClient.IsWorldRuntimeReady)
            {
                return;
            }

            var identity = EnsureIdentity(instance.gameObject);
            var command = OntologyWorldAuthorityClient.CreateCommand(
                OntologyWorldCommandKinds.MoveEntity,
                OntologyWorldAuthorityClient.CreateMoveEntityPayload(
                    identity.EnsureGuid(), instance.transform));
            StartCoroutine(authorityClient.SendCommandRoutine(command, result =>
            {
                SetStatus(result.accepted
                    ? "Published move at revision " + result.revision + "."
                    : "Move was not published: " + result.rejectionCode);
            }));
        }

        private void HandleLocalPlacement(OntologyPlaceableInstance instance)
        {
            if (instance != null && IsAutomaticAuthorityEnabled())
            {
                StartCoroutine(PublishNewInstanceRoutine(instance));
            }
        }

        private void HandleDuplicateCommitted(OntologyPlaceableInstance instance)
        {
            if (instance != null && IsAutomaticAuthorityEnabled())
            {
                StartCoroutine(PublishNewInstanceRoutine(instance));
            }
        }

        private void HandleTransformCommitted(OntologyPlaceableInstance instance)
        {
            if (instance == null) return;
            var identity = instance.GetComponent<OntologyAuthorityEntityIdentity>();
            if (identity != null && identity.TryGetGuid(out var entityId) &&
                publishedEntityIds.Contains(entityId.ToString("D")))
            {
                PublishMove(instance);
            }
        }

        private void HandleAuthoredFactChanged(
            OntologyPlaceableInstance instance,
            string predicate,
            string obj,
            bool added)
        {
            if (suppressOutgoingChanges || instance == null ||
                !IsAutomaticAuthorityEnabled())
            {
                return;
            }

            var identity = EnsureIdentity(instance.gameObject);
            var entityKey = identity.EnsureGuid().ToString("D");
            if (!publishedEntityIds.Contains(entityKey))
            {
                // The first accepted placement publishes its complete current
                // semantic graph, including this newly edited triple.
                StartCoroutine(PublishNewInstanceRoutine(instance));
                return;
            }

            StartCoroutine(PublishFactChangeRoutine(
                instance, identity.EnsureGuid(), predicate, obj, added));
        }

        private void HandleRuleBlockChanged(
            OntologyPlaceableInstance instance,
            string ruleId,
            string bindingVariable,
            bool added)
        {
            if (suppressOutgoingChanges || instance == null ||
                !IsAutomaticAuthorityEnabled())
            {
                return;
            }

            var identity = EnsureIdentity(instance.gameObject);
            var entityKey = identity.EnsureGuid().ToString("D");
            if (!publishedEntityIds.Contains(entityKey))
            {
                StartCoroutine(PublishNewInstanceRoutine(instance));
                return;
            }

            StartCoroutine(PublishRuleBlockChangeRoutine(
                instance, identity.EnsureGuid(), ruleId, bindingVariable, added));
        }

        private IEnumerator PublishRoutine()
        {
            if (!authorityClient.IsWorldRuntimeReady)
            {
                SetStatus("Enter the selected world before publishing durable scene edits.");
                yield break;
            }

            OntologyAuthorityWorldProjection projection = null;
            authorityClient.ProjectionReceived += CaptureProjection;
            yield return authorityClient.LoadWorldRoutine();
            authorityClient.ProjectionReceived -= CaptureProjection;

            var existing = new HashSet<string>(
                projection?.entities == null
                    ? Array.Empty<string>()
                    : projection.entities.Where(value => value != null)
                        .Select(value => value.entityId),
                StringComparer.OrdinalIgnoreCase);

            var instances = OntologyPlaceableInstance.ActiveInstances
                .Where(value => value != null && value.isActiveAndEnabled)
                .OrderBy(value => value.name)
                .ToArray();
            var published = 0;
            foreach (var instance in instances)
            {
                var identity = EnsureIdentity(instance.gameObject);
                var entityGuid = identity.EnsureGuid();
                if (existing.Contains(entityGuid.ToString("D")))
                {
                    publishedEntityIds.Add(entityGuid.ToString("D"));
                    continue;
                }

                var definitionId = instance.DefinitionId;
                if (string.IsNullOrWhiteSpace(definitionId))
                {
                    SetStatus("Skipped '" + instance.name + "': it has no catalog definition id.");
                    continue;
                }

                var placed = false;
                var placeCommand = OntologyWorldAuthorityClient.CreateCommand(
                    OntologyWorldCommandKinds.PlaceEntity,
                    OntologyWorldAuthorityClient.CreatePlaceEntityPayload(
                        entityGuid, definitionId, instance.name, instance.transform,
                        authorityClient.CurrentProjectionZoneKey));
                yield return authorityClient.SendCommandRoutine(
                    placeCommand,
                    result => placed = result != null && result.accepted);
                if (!placed)
                {
                    SetStatus("Stopped while publishing '" + instance.name + "': " + authorityClient.LastStatus);
                    yield break;
                }

                yield return PublishSemanticData(instance.gameObject, entityGuid);
                publishedEntityIds.Add(entityGuid.ToString("D"));
                published++;
            }

            yield return authorityClient.LoadWorldRoutine();
            SetStatus("Published " + published + " new placed object(s) to authority world at revision " + authorityClient.CurrentRevision + ".");

            void CaptureProjection(OntologyAuthorityWorldProjection value)
            {
                projection = value;
            }
        }

        private IEnumerator PublishNewInstanceRoutine(OntologyPlaceableInstance instance)
        {
            if (instance == null) yield break;
            ResolveDependencies();
            if (authorityClient == null)
            {
                yield break;
            }

            if (!authorityClient.IsWorldRuntimeReady)
            {
                SetStatus("Enter the selected world before publishing a placement.");
                yield break;
            }

            var identity = EnsureIdentity(instance.gameObject);
            var entityId = identity.EnsureGuid();
            var entityKey = entityId.ToString("D");
            if (publishedEntityIds.Contains(entityKey)) yield break;
            if (string.IsNullOrWhiteSpace(instance.DefinitionId))
            {
                SetStatus("Cannot publish '" + instance.name + "': it has no catalog definition id.");
                yield break;
            }

            var accepted = false;
            var command = OntologyWorldAuthorityClient.CreateCommand(
                    OntologyWorldCommandKinds.PlaceEntity,
                    OntologyWorldAuthorityClient.CreatePlaceEntityPayload(
                        entityId, instance.DefinitionId, instance.name, instance.transform,
                        authorityClient.CurrentProjectionZoneKey));
            yield return authorityClient.SendCommandRoutine(
                command,
                result => accepted = result != null && result.accepted);
            if (!accepted)
            {
                SetStatus("Placement was not confirmed by authority: " +
                          authorityClient.LastStatus);
                // In authority mode local placement is optimistic presentation only.
                // A rejected command must not leave an unsaved world entity behind.
                Destroy(instance.gameObject);
                yield return null;
                var bootstrap = FindAnyObjectByType<OntologyWorldBootstrap>();
                bootstrap?.SynchronizeSceneObjects(runSimulation: true);
                yield break;
            }

            publishedEntityIds.Add(entityKey);
            yield return PublishSemanticData(instance.gameObject, entityId);
            SetStatus("Authority confirmed placement of '" + instance.name + "'.");
        }

        private IEnumerator PublishFactChangeRoutine(
            OntologyPlaceableInstance instance,
            Guid subjectEntityId,
            string predicate,
            string obj,
            bool added)
        {
            var canonicalPredicate = OntologyLanguagePackService.CanonicalTerm(predicate);
            var canonicalObject = OntologyLanguagePackService.CanonicalObjectForRelation(
                canonicalPredicate, obj);
            OntologyAuthorityCommandResult result = null;
            if (added)
            {
                var referenced = FindIdentityForOntologyEntity(canonicalObject);
                var payload = referenced != null
                    ? OntologyWorldAuthorityClient.CreateEntityFactPayload(
                        subjectEntityId, canonicalPredicate, referenced.EnsureGuid())
                    : OntologyWorldAuthorityClient.CreateCanonicalFactPayload(
                        subjectEntityId, canonicalPredicate, canonicalObject);
                yield return authorityClient.SendCommandRoutine(
                    OntologyWorldAuthorityClient.CreateCommand(
                        OntologyWorldCommandKinds.SetAuthoredFact, payload),
                    value => result = value);
            }
            else
            {
                OntologyAuthorityWorldProjection projection = null;
                yield return LoadProjectionCapture(value => projection = value);
                var fact = FindRemoteFact(
                    projection, subjectEntityId, canonicalPredicate, canonicalObject);
                if (fact == null || !Guid.TryParse(fact.factId, out var factId))
                {
                    SetStatus("Triple removal was already reflected by authority.");
                    yield break;
                }
                yield return authorityClient.SendCommandRoutine(
                    OntologyWorldAuthorityClient.CreateCommand(
                        OntologyWorldCommandKinds.RetractAuthoredFact,
                        OntologyWorldAuthorityClient.CreateRetractFactPayload(factId)),
                    value => result = value);
            }

            if (result != null && result.accepted)
            {
                SetStatus("Authority confirmed triple change at revision " + result.revision + ".");
                yield break;
            }

            SetStatus("Authority rejected triple change: " +
                      (result?.rejectionCode ?? "unknown_error"));
            RevertLocalFact(instance, predicate, obj, added);
            if (result?.rejectionCode == "stale_revision")
            {
                yield return authorityClient.LoadWorldRoutine();
            }
        }

        private IEnumerator PublishRuleBlockChangeRoutine(
            OntologyPlaceableInstance instance,
            Guid subjectEntityId,
            string ruleId,
            string bindingVariable,
            bool added)
        {
            bindingVariable = string.IsNullOrWhiteSpace(bindingVariable)
                ? "?target"
                : bindingVariable;
            var key = RuleBindingKey(subjectEntityId, ruleId, bindingVariable);
            OntologyAuthorityCommandResult result = null;
            if (added)
            {
                var bindingId = Guid.NewGuid();
                yield return authorityClient.SendCommandRoutine(
                    OntologyWorldAuthorityClient.CreateCommand(
                        OntologyWorldCommandKinds.AddRuleBlock,
                        OntologyWorldAuthorityClient.CreateRuleBlockPayload(
                            bindingId, subjectEntityId, ruleId, bindingVariable,
                            ResolveRuleVersion(ruleId))),
                    value => result = value);
                if (result != null && result.accepted)
                {
                    remoteRuleBindingIds[key] = bindingId;
                }
            }
            else
            {
                if (!remoteRuleBindingIds.TryGetValue(key, out var bindingId))
                {
                    OntologyAuthorityWorldProjection projection = null;
                    yield return LoadProjectionCapture(value => projection = value);
                    var remote = FindRemoteRuleBinding(
                        projection, subjectEntityId, ruleId, bindingVariable);
                    if (remote == null || !Guid.TryParse(remote.bindingId, out bindingId))
                    {
                        SetStatus("Rule-block removal was already reflected by authority.");
                        yield break;
                    }
                }

                yield return authorityClient.SendCommandRoutine(
                    OntologyWorldAuthorityClient.CreateCommand(
                        OntologyWorldCommandKinds.RemoveRuleBlock,
                        OntologyWorldAuthorityClient.CreateRemoveRuleBlockPayload(bindingId)),
                    value => result = value);
                if (result != null && result.accepted)
                {
                    remoteRuleBindingIds.Remove(key);
                }
            }

            if (result != null && result.accepted)
            {
                SetStatus("Authority confirmed rule-block change at revision " + result.revision + ".");
                yield break;
            }

            SetStatus("Authority rejected rule-block change: " +
                      (result?.rejectionCode ?? "unknown_error"));
            RevertLocalRuleBlock(instance, ruleId, bindingVariable, added);
            if (result?.rejectionCode == "stale_revision")
            {
                yield return authorityClient.LoadWorldRoutine();
            }
        }

        private IEnumerator LoadProjectionCapture(
            Action<OntologyAuthorityWorldProjection> capture)
        {
            authorityClient.ProjectionReceived += capture;
            yield return authorityClient.LoadWorldRoutine();
            authorityClient.ProjectionReceived -= capture;
        }

        private IEnumerator PublishSemanticData(GameObject target, Guid subjectEntityId)
        {
            var ontology = target.GetComponent<OntologyObject>();
            if (ontology != null)
            {
                foreach (var concept in ontology.Concepts.Where(value => !string.IsNullOrWhiteSpace(value)))
                {
                    yield return PublishCanonicalFact(
                        subjectEntityId,
                        OntologyPredicates.HasConcept,
                        OntologyLanguagePackService.CanonicalTerm(concept));
                }

                foreach (var fact in ontology.Facts.Where(value =>
                             value != null &&
                             !string.IsNullOrWhiteSpace(value.predicate) &&
                             !string.IsNullOrWhiteSpace(value.obj)))
                {
                    var predicate = OntologyLanguagePackService.CanonicalTerm(fact.predicate);
                    var objectValue = OntologyLanguagePackService.CanonicalObjectForRelation(predicate, fact.obj);
                    var referenced = FindIdentityForOntologyEntity(objectValue);
                    if (referenced != null)
                    {
                        yield return PublishEntityFact(
                            subjectEntityId,
                            predicate,
                            referenced.EnsureGuid());
                    }
                    else
                    {
                        yield return PublishCanonicalFact(subjectEntityId, predicate, objectValue);
                    }
                }
            }

            var assignment = target.GetComponent<OntologyRuleBlockAssignment>();
            if (assignment == null)
            {
                yield break;
            }

            foreach (var binding in assignment.Bindings.Where(value =>
                         value != null && !string.IsNullOrWhiteSpace(value.ruleId)))
            {
                var command = OntologyWorldAuthorityClient.CreateCommand(
                    OntologyWorldCommandKinds.AddRuleBlock,
                    OntologyWorldAuthorityClient.CreateRuleBlockPayload(
                        Guid.NewGuid(), subjectEntityId, binding.ruleId,
                        string.IsNullOrWhiteSpace(binding.bindingVariable)
                            ? "?target"
                            : binding.bindingVariable,
                        ResolveRuleVersion(binding.ruleId)));
                yield return authorityClient.SendCommandRoutine(command);
            }
        }

        private IEnumerator PublishCanonicalFact(Guid subjectEntityId, string predicate, string objectValue)
        {
            if (string.IsNullOrWhiteSpace(predicate) || string.IsNullOrWhiteSpace(objectValue))
            {
                yield break;
            }

            var command = OntologyWorldAuthorityClient.CreateCommand(
                OntologyWorldCommandKinds.SetAuthoredFact,
                OntologyWorldAuthorityClient.CreateCanonicalFactPayload(
                    subjectEntityId, predicate, objectValue));
            yield return authorityClient.SendCommandRoutine(command);
        }

        private IEnumerator PublishEntityFact(Guid subjectEntityId, string predicate, Guid objectEntityId)
        {
            var command = OntologyWorldAuthorityClient.CreateCommand(
                OntologyWorldCommandKinds.SetAuthoredFact,
                OntologyWorldAuthorityClient.CreateEntityFactPayload(
                    subjectEntityId, predicate, objectEntityId));
            yield return authorityClient.SendCommandRoutine(command);
        }

        private static OntologyAuthorityEntityIdentity FindIdentityForOntologyEntity(string entityId)
        {
            if (string.IsNullOrWhiteSpace(entityId))
            {
                return null;
            }

            return FindObjectsByType<OntologyObject>(FindObjectsInactive.Exclude)
                .FirstOrDefault(value => value != null && value.EntityId == entityId)
                ?.GetComponent<OntologyAuthorityEntityIdentity>();
        }

        private static int ResolveRuleVersion(string ruleId)
        {
            var definition = FindAnyObjectByType<OntologyWorldBootstrap>()
                ?.RuleDatabase
                ?.Definitions
                .FirstOrDefault(value => value != null &&
                    string.Equals(value.id, ruleId, StringComparison.Ordinal));
            return Mathf.Max(1, definition?.catalogVersion ?? 1);
        }

        private void ApplyProjection(OntologyAuthorityWorldProjection projection)
        {
            if (projection?.entities == null) return;
            ResolveDependencies();
            var changed = false;
            foreach (var remote in projection.entities.Where(value =>
                         value != null && Guid.TryParse(value.entityId, out _)))
            {
                var identity = FindIdentity(remote.entityId);
                if (identity == null)
                {
                    var created = placementController == null || remote.transform == null
                        ? null
                        : placementController.CreateAuthorityPresentation(
                            remote.entityId,
                            remote.templateId,
                            remote.displayName,
                            ToPosition(remote.transform),
                            ToRotation(remote.transform),
                            ToScale(remote.transform));
                    if (created == null) continue;
                    identity = created.GetComponent<OntologyAuthorityEntityIdentity>();
                    if (!string.IsNullOrWhiteSpace(projection.scopeZoneKey))
                    {
                        var presentation = created.GetComponent<OntologyAuthorityRemotePresentation>() ??
                                           created.gameObject.AddComponent<OntologyAuthorityRemotePresentation>();
                        presentation.SetScopeZoneKey(projection.scopeZoneKey);
                    }
                    changed = true;
                }

                if (identity == null || remote.transform == null) continue;
                publishedEntityIds.Add(remote.entityId);
                if (!ShouldApplyProjectedTransform(identity, localAvatarIdentity))
                {
                    // The durable projection may contain the avatar entity so its
                    // profile/semantic facts remain addressable. Its live Transform,
                    // however, is ephemeral movement state and is restored explicitly
                    // through the checkpoint controller on world entry.
                    continue;
                }

                var instance = identity.GetComponent<OntologyPlaceableInstance>();
                if (worldEditorController != null && worldEditorController.IsMoving &&
                    worldEditorController.Selected == instance)
                {
                    continue;
                }

                var target = identity.transform;
                var position = ToPosition(remote.transform);
                var rotation = Quaternion.Euler(ToRotation(remote.transform));
                var scale = ToScale(remote.transform);
                if ((target.position - position).sqrMagnitude > 0.000001f ||
                    Quaternion.Angle(target.rotation, rotation) > 0.01f ||
                    (target.localScale - scale).sqrMagnitude > 0.000001f)
                {
                    target.SetPositionAndRotation(position, rotation);
                    target.localScale = scale;
                    changed = true;
                }
            }

            if (changed) Physics.SyncTransforms();
            if (ApplyAuthoritySemantics(projection))
            {
                SetStatus("Authority world data was updated from the latest server revision.");
            }
        }

        private void HandleProjectionZoneScopeChanged(string previousZoneKey, string nextZoneKey)
        {
            if (string.IsNullOrWhiteSpace(previousZoneKey) ||
                string.Equals(previousZoneKey, nextZoneKey, StringComparison.Ordinal))
            {
                return;
            }

            // Only presentations that the bridge created from the departing zone are
            // removed. Scene-authored objects remain intact and the new projection
            // recreates its own remote presentations after server confirmation.
            foreach (var presentation in FindObjectsByType<OntologyAuthorityRemotePresentation>(
                         FindObjectsInactive.Exclude))
            {
                if (presentation != null && string.Equals(
                        presentation.ScopeZoneKey,
                        previousZoneKey,
                        StringComparison.Ordinal))
                {
                    Destroy(presentation.gameObject);
                }
            }
        }

        private bool ApplyAuthoritySemantics(OntologyAuthorityWorldProjection projection)
        {
            if (projection?.entities == null) return false;
            var changed = false;
            foreach (var remoteEntity in projection.entities.Where(value =>
                         value != null && Guid.TryParse(value.entityId, out _)))
            {
                var identity = FindIdentity(remoteEntity.entityId);
                if (identity == null) continue;
                var factRows = projection.facts == null
                    ? Array.Empty<OntologyAuthorityFactProjection>()
                    : projection.facts.Where(value => value != null &&
                        string.Equals(value.subjectEntityId, remoteEntity.entityId,
                            StringComparison.OrdinalIgnoreCase)).ToArray();
                var ruleRows = projection.ruleBindings == null
                    ? Array.Empty<OntologyAuthorityRuleBindingProjection>()
                    : projection.ruleBindings.Where(value => value != null && value.enabled &&
                        string.Equals(value.targetEntityId, remoteEntity.entityId,
                            StringComparison.OrdinalIgnoreCase)).ToArray();
                var hasSemanticRows = factRows.Length > 0 || ruleRows.Length > 0;
                if (!hasSemanticRows &&
                    !semanticProjectionInitialized.Contains(remoteEntity.entityId))
                {
                    // A newly placed remote presentation receives its catalog defaults
                    // first. Do not erase those while the server is still receiving its
                    // follow-up authored triples and rule bindings.
                    continue;
                }

                semanticProjectionInitialized.Add(remoteEntity.entityId);
                var ontology = identity.GetComponent<OntologyObject>() ??
                               identity.gameObject.AddComponent<OntologyObject>();
                var concepts = new List<string>();
                var facts = new List<OntologyFactEntry>();
                foreach (var remoteFact in factRows)
                {
                    if (remoteFact.objectKind == "canonical" &&
                        remoteFact.predicateId == OntologyPredicates.HasConcept)
                    {
                        if (!string.IsNullOrWhiteSpace(remoteFact.objectCanonicalId))
                            concepts.Add(remoteFact.objectCanonicalId);
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(remoteFact.predicateId)) continue;
                    var value = ResolveRemoteFactObject(remoteFact);
                    if (string.IsNullOrWhiteSpace(value)) continue;
                    facts.Add(new OntologyFactEntry
                    {
                        predicate = remoteFact.predicateId,
                        obj = value
                    });
                }

                var assignment = identity.GetComponent<OntologyRuleBlockAssignment>();
                var bindings = ruleRows.Select(value => new OntologyRuleBlockBinding
                {
                    ruleId = value.ruleId,
                    bindingVariable = ResolveBindingVariable(value.parameterValuesJson)
                }).Where(value => !string.IsNullOrWhiteSpace(value.ruleId)).ToArray();

                if (!SameOntologyData(ontology, concepts, facts) ||
                    !SameBindings(assignment, bindings))
                {
                    ontology.ReplaceFactsAndConcepts(concepts, facts);
                    if (assignment == null && bindings.Length > 0)
                        assignment = identity.gameObject.AddComponent<OntologyRuleBlockAssignment>();
                    assignment?.Replace(bindings);
                    changed = true;
                }
            }

            if (!changed) return false;
            var bootstrap = FindAnyObjectByType<OntologyWorldBootstrap>();
            bootstrap?.SynchronizeSceneObjects(runSimulation: true);
            return true;
        }

        private static string ResolveRemoteFactObject(
            OntologyAuthorityFactProjection remoteFact)
        {
            if (remoteFact.objectKind == "canonical")
                return remoteFact.objectCanonicalId;
            if (remoteFact.objectKind == "entity")
            {
                var identity = FindIdentity(remoteFact.objectEntityId);
                return identity == null
                    ? string.Empty
                    : identity.GetComponent<OntologyObject>()?.EntityId;
            }
            return string.Empty;
        }

        private static string ResolveBindingVariable(string parametersJson)
        {
            var parameters = JsonUtility.FromJson<RuleBindingParameters>(
                parametersJson ?? "{}");
            return string.IsNullOrWhiteSpace(parameters?.bindingVariable)
                ? "?target"
                : parameters.bindingVariable;
        }

        private static bool SameOntologyData(
            OntologyObject ontology,
            IReadOnlyList<string> concepts,
            IReadOnlyList<OntologyFactEntry> facts)
        {
            if (ontology == null) return concepts.Count == 0 && facts.Count == 0;
            var existingConcepts = new HashSet<string>(ontology.Concepts,
                StringComparer.OrdinalIgnoreCase);
            var nextConcepts = new HashSet<string>(concepts,
                StringComparer.OrdinalIgnoreCase);
            if (!existingConcepts.SetEquals(nextConcepts)) return false;
            var existingFacts = new HashSet<string>(ontology.Facts.Where(value => value != null)
                .Select(value => (value.predicate ?? string.Empty) + "\u001f" +
                                 (value.obj ?? string.Empty)), StringComparer.Ordinal);
            var nextFacts = new HashSet<string>(facts.Where(value => value != null)
                .Select(value => (value.predicate ?? string.Empty) + "\u001f" +
                                 (value.obj ?? string.Empty)), StringComparer.Ordinal);
            return existingFacts.SetEquals(nextFacts);
        }

        private static bool SameBindings(
            OntologyRuleBlockAssignment assignment,
            IReadOnlyList<OntologyRuleBlockBinding> bindings)
        {
            var existing = assignment == null
                ? new HashSet<string>(StringComparer.Ordinal)
                : new HashSet<string>(assignment.Bindings.Where(value => value != null)
                    .Select(value => (value.ruleId ?? string.Empty) + "\u001f" +
                                     (value.bindingVariable ?? string.Empty)),
                    StringComparer.Ordinal);
            var next = new HashSet<string>(bindings.Where(value => value != null)
                .Select(value => (value.ruleId ?? string.Empty) + "\u001f" +
                                 (value.bindingVariable ?? string.Empty)),
                StringComparer.Ordinal);
            return existing.SetEquals(next);
        }

        private static OntologyAuthorityFactProjection FindRemoteFact(
            OntologyAuthorityWorldProjection projection,
            Guid subjectEntityId,
            string predicate,
            string objectValue)
        {
            if (projection?.facts == null) return null;
            var subject = subjectEntityId.ToString("D");
            var referenced = FindIdentityForOntologyEntity(objectValue);
            var expectedEntity = referenced == null
                ? string.Empty
                : referenced.EnsureGuid().ToString("D");
            return projection.facts.FirstOrDefault(value =>
                value != null &&
                string.Equals(value.subjectEntityId, subject,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(value.predicateId, predicate,
                    StringComparison.OrdinalIgnoreCase) &&
                ((referenced != null && value.objectKind == "entity" &&
                  string.Equals(value.objectEntityId, expectedEntity,
                      StringComparison.OrdinalIgnoreCase)) ||
                 (referenced == null && value.objectKind == "canonical" &&
                  string.Equals(value.objectCanonicalId, objectValue,
                      StringComparison.OrdinalIgnoreCase))));
        }

        private static OntologyAuthorityRuleBindingProjection FindRemoteRuleBinding(
            OntologyAuthorityWorldProjection projection,
            Guid subjectEntityId,
            string ruleId,
            string bindingVariable)
        {
            if (projection?.ruleBindings == null) return null;
            var subject = subjectEntityId.ToString("D");
            return projection.ruleBindings.FirstOrDefault(value =>
            {
                if (value == null || !value.enabled ||
                    !string.Equals(value.targetEntityId, subject,
                        StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(value.ruleId, ruleId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                var parameters = JsonUtility.FromJson<RuleBindingParameters>(
                    value.parameterValuesJson ?? "{}");
                return parameters != null &&
                       string.Equals(parameters.bindingVariable, bindingVariable,
                           StringComparison.Ordinal);
            });
        }

        private void RevertLocalFact(
            OntologyPlaceableInstance instance,
            string predicate,
            string obj,
            bool originalOperationWasAdd)
        {
            if (instance == null || worldEditorController == null ||
                worldEditorController.Selected != instance)
            {
                return;
            }

            suppressOutgoingChanges = true;
            try
            {
                if (predicate == OntologyPredicates.HasConcept)
                {
                    if (originalOperationWasAdd)
                        worldEditorController.RemoveSelectedConcept(obj);
                    else
                        worldEditorController.AddSelectedConcept(obj);
                }
                else if (originalOperationWasAdd)
                {
                    worldEditorController.RemoveSelectedFact(predicate, obj);
                }
                else
                {
                    worldEditorController.AddSelectedFact(predicate, obj);
                }
            }
            finally
            {
                suppressOutgoingChanges = false;
            }
        }

        private void RevertLocalRuleBlock(
            OntologyPlaceableInstance instance,
            string ruleId,
            string bindingVariable,
            bool originalOperationWasAdd)
        {
            if (instance == null || worldEditorController == null ||
                worldEditorController.Selected != instance)
            {
                return;
            }

            suppressOutgoingChanges = true;
            try
            {
                if (originalOperationWasAdd)
                    worldEditorController.RemoveSelectedRuleBlock(ruleId, bindingVariable);
                else
                    worldEditorController.AddSelectedRuleBlock(ruleId, bindingVariable);
            }
            finally
            {
                suppressOutgoingChanges = false;
            }
        }

        private static string RuleBindingKey(
            Guid subjectEntityId,
            string ruleId,
            string bindingVariable)
        {
            return subjectEntityId.ToString("D") + "|" +
                   (ruleId ?? string.Empty) + "|" +
                   (bindingVariable ?? string.Empty);
        }

        private void ResolveDependencies()
        {
            if (authorityClient == null)
            {
                authorityClient = GetComponent<OntologyWorldAuthorityClient>() ??
                                  FindAnyObjectByType<OntologyWorldAuthorityClient>();
            }
            if (placementController == null)
            {
                placementController = FindAnyObjectByType<OntologyRuntimeObjectPlacementController>();
            }
            if (worldEditorController == null)
            {
                worldEditorController = FindAnyObjectByType<OntologyRuntimeWorldEditorController>();
            }
            if (localAvatarIdentity == null)
            {
                var entryFlow = FindAnyObjectByType<OntologyWorldAuthorityAccountEntryFlow>(
                    FindObjectsInactive.Include);
                localAvatarIdentity = entryFlow == null
                    ? null
                    : entryFlow.AvatarIdentity;
            }
            if (localAvatarIdentity == null)
            {
                var localInput = FindAnyObjectByType<OntologyInputSystemPlayerInput>(
                    FindObjectsInactive.Include);
                localAvatarIdentity = localInput == null
                    ? null
                    : localInput.GetComponent<OntologyAuthorityEntityIdentity>();
            }
        }

        /// <summary>
        /// Durable world projections own placed-object transforms. A locally
        /// controlled avatar is instead owned by ephemeral motion and explicit
        /// checkpoint restoration, even when the projection contains the same
        /// authority entity ID.
        /// </summary>
        public static bool ShouldApplyProjectedTransform(
            OntologyAuthorityEntityIdentity identity,
            OntologyAuthorityEntityIdentity localIdentity)
        {
            if (identity == null)
            {
                return false;
            }

            var isLocallyControlled =
                identity == localIdentity ||
                identity.GetComponent<OntologyInputSystemPlayerInput>() != null;
            if (isLocallyControlled)
            {
                return false;
            }

            return localIdentity == null ||
                   !identity.TryGetGuid(out var identityId) ||
                   !localIdentity.TryGetGuid(out var localId) ||
                   identityId != localId;
        }

        private bool IsAutomaticAuthorityEnabled()
        {
            ResolveDependencies();
            return authorityClient != null && authorityClient.Settings != null &&
                   authorityClient.IsWorldRuntimeReady &&
                   authorityClient.Settings.connectOnStart && authorityClient.CanEditCurrentWorld;
        }

        private static OntologyAuthorityEntityIdentity FindIdentity(string entityGuid)
        {
            return FindObjectsByType<OntologyAuthorityEntityIdentity>(
                    FindObjectsInactive.Exclude)
                .FirstOrDefault(value => value != null &&
                    string.Equals(value.EntityGuid, entityGuid,
                        StringComparison.OrdinalIgnoreCase));
        }

        private static Vector3 ToPosition(OntologyAuthorityTransform value) =>
            new(value.positionX, value.positionY, value.positionZ);
        private static Vector3 ToRotation(OntologyAuthorityTransform value) =>
            new(value.rotationX, value.rotationY, value.rotationZ);
        private static Vector3 ToScale(OntologyAuthorityTransform value) =>
            new(value.scaleX, value.scaleY, value.scaleZ);

        [Serializable]
        private sealed class RuleBindingParameters
        {
            public string bindingVariable;
        }

        private static OntologyAuthorityEntityIdentity EnsureIdentity(GameObject target)
        {
            var identity = target.GetComponent<OntologyAuthorityEntityIdentity>();
            if (identity == null)
            {
                identity = target.AddComponent<OntologyAuthorityEntityIdentity>();
            }
            identity.EnsureGuid();
            return identity;
        }

        private void SetStatus(string value)
        {
            lastPublishStatus = value ?? string.Empty;
            Debug.Log("[WorldAuthorityBridge] " + lastPublishStatus, this);
            StatusChanged?.Invoke();
        }
    }
}
