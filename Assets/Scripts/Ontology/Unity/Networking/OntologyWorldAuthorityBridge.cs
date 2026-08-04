using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
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
        private readonly HashSet<string> publishingEntityIds =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> retiringEntityIds =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Guid> remoteRuleBindingIds =
            new(StringComparer.Ordinal);
        private readonly HashSet<string> semanticProjectionInitialized =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> dynamicProjectionSeeded =
            new(StringComparer.OrdinalIgnoreCase);
        private OntologyWorldAuthorityClient subscribedAuthorityClient;
        private OntologyRuntimeObjectPlacementController subscribedPlacementController;
        private OntologyRuntimeWorldEditorController subscribedWorldEditorController;
        private bool suppressOutgoingChanges;
        // Projection rows and presentation identities are indexed only for the
        // currently received Authority projection. This is a lookup cache, not
        // an ontology evaluator: rebuilding for a newer revision discards every
        // prior row before Unity adapters consume the approved result.
        private readonly OntologyAuthorityProjectionIndex projectionIndex = new();
        [SerializeField, Tooltip(
            "Ephemeral observability for the most recently applied Authority projection. " +
            "This is never persisted as a world Fact.")]
        private OntologyAuthorityProjectionApplyStatistics
            lastProjectionApplyStatistics;

        public string LastPublishStatus => lastPublishStatus;
        public OntologyAuthorityProjectionApplyStatistics
            LastProjectionApplyStatistics => lastProjectionApplyStatistics;
        public bool HasSelectedAuthorityWorld => authorityClient != null && authorityClient.IsWorldRuntimeReady;
        public bool CanEditAuthorityWorld => authorityClient != null && authorityClient.CanEditCurrentWorld;
        public event Action StatusChanged;

        public static bool ShouldPublishAutomatically(
            bool isWorldRuntimeReady,
            bool canEditCurrentWorld)
        {
            return isWorldRuntimeReady && canEditCurrentWorld;
        }

        public static bool ShouldRemoveUnprojectedPlaceablePresentation(
            bool isWorldRuntimeReady,
            bool placementCommandInFlight,
            OntologyAuthorityWorldProjection projection,
            OntologyAuthorityEntityIdentity identity)
        {
            if (!isWorldRuntimeReady ||
                placementCommandInFlight ||
                projection?.entities == null ||
                identity == null ||
                !identity.TryGetGuid(out var entityId))
            {
                return false;
            }

            var entityKey = entityId.ToString("D");
            return !projection.entities.Any(value =>
                value != null &&
                string.Equals(
                    value.entityId,
                    entityKey,
                    StringComparison.OrdinalIgnoreCase));
        }

        public bool IsEntityPublished(OntologyAuthorityEntityIdentity identity)
        {
            return identity != null &&
                   identity.TryGetGuid(out var entityId) &&
                   publishedEntityIds.Contains(entityId.ToString("D"));
        }

        /// <summary>
        /// Routes a placement intent through World Authority before creating its
        /// Unity presentation. Returning false preserves local-only authoring.
        /// </summary>
        public bool TryRequestAuthorityPlacement(
            OntologyPlaceableDefinition definition,
            Vector3 position,
            Quaternion rotation,
            Vector3 localScale)
        {
            ResolveDependencies();
            if (definition == null ||
                string.IsNullOrWhiteSpace(definition.definitionId) ||
                !IsAutomaticAuthorityEnabled())
            {
                return false;
            }

            StartCoroutine(PlaceAuthorityFirstRoutine(
                definition,
                position,
                rotation.eulerAngles,
                localScale));
            return true;
        }

        public IEnumerator EnsureEntityPublishedRoutine(
            OntologyPlaceableInstance instance,
            Action<bool> completed)
        {
            if (instance == null)
            {
                completed?.Invoke(false);
                yield break;
            }

            ResolveDependencies();
            var identity = EnsureIdentity(instance.gameObject);
            var entityKey = identity.EnsureGuid().ToString("D");
            if (publishedEntityIds.Contains(entityKey))
            {
                completed?.Invoke(true);
                yield break;
            }

            while (publishingEntityIds.Contains(entityKey))
                yield return null;

            if (!publishedEntityIds.Contains(entityKey))
            {
                yield return PublishInstanceRoutine(
                    instance,
                    destroyRejectedLocalInstance: false);
            }

            completed?.Invoke(publishedEntityIds.Contains(entityKey));
        }

        /// <summary>
        /// One-time, data-driven repair for entities created before Authority
        /// content published their default Rule Blocks. The durable contract
        /// marker prevents a later intentional Rule-Block removal from being
        /// silently restored on the next world entry.
        /// </summary>
        public IEnumerator RepairLegacySemanticContractsRoutine(
            Action<bool> completed)
        {
            ResolveDependencies();
            var projection = authorityClient?.CurrentProjection;
            var catalog = placementController?.Catalog;
            if (projection?.entities == null || catalog == null ||
                !authorityClient.CanAuthorSelectedWorld)
            {
                completed?.Invoke(true);
                yield break;
            }

            var repairedAny = false;
            foreach (var entity in projection.entities)
            {
                if (entity == null ||
                    !Guid.TryParse(entity.entityId, out var entityId))
                {
                    continue;
                }

                var definition = catalog.Find(entity.templateId);
                var physicalMigrationSucceeded = false;
                var physicalProfileMigrated = false;
                yield return RepairLegacyImplicitPhysicalProfile(
                    projection,
                    entity,
                    entityId,
                    definition,
                    (succeeded, migrated) =>
                    {
                        physicalMigrationSucceeded = succeeded;
                        physicalProfileMigrated = migrated;
                    });
                if (!physicalMigrationSucceeded)
                {
                    completed?.Invoke(false);
                    yield break;
                }
                repairedAny |= physicalProfileMigrated;

                if (definition == null)
                {
                    continue;
                }

                var currentContractVersion =
                    ResolveSemanticContractVersion(
                        projection,
                        entity.entityId);
                var targetContractVersion = Mathf.Max(
                    1,
                    definition.semanticContractVersion);
                if (currentContractVersion >= targetContractVersion)
                {
                    continue;
                }

                if (currentContractVersion > 0)
                {
                    var migrationsApplied = false;
                    yield return MigrateRuleBlockBindings(
                        projection,
                        entityId,
                        currentContractVersion,
                        targetContractVersion,
                        definition.ruleBlockMigrations,
                        value => migrationsApplied = value);
                    if (!migrationsApplied)
                    {
                        SetStatus(
                            "Semantic Rule Block migration was rejected for '" +
                            entity.displayName + "': " +
                            authorityClient.LastStatus);
                        completed?.Invoke(false);
                        yield break;
                    }

                    var introducedFactsAdded = false;
                    yield return IntroduceAuthoredFacts(
                        projection,
                        entityId,
                        currentContractVersion,
                        targetContractVersion,
                        definition.introducedFacts,
                        value => introducedFactsAdded = value);
                    if (!introducedFactsAdded)
                    {
                        SetStatus(
                            "Semantic Fact introduction was rejected for '" +
                            entity.displayName + "': " +
                            authorityClient.LastStatus);
                        completed?.Invoke(false);
                        yield break;
                    }

                    var retiredFactsRemoved = false;
                    yield return RetireAuthoredFacts(
                        projection,
                        entityId,
                        currentContractVersion,
                        targetContractVersion,
                        definition.retiredFacts,
                        value => retiredFactsRemoved = value);
                    if (!retiredFactsRemoved)
                    {
                        SetStatus(
                            "Semantic Fact retirement was rejected for '" +
                            entity.displayName + "': " +
                            authorityClient.LastStatus);
                        completed?.Invoke(false);
                        yield break;
                    }

                    var introducedBindingsAdded = false;
                    yield return IntroduceRuleBlockBindings(
                        projection,
                        entityId,
                        currentContractVersion,
                        targetContractVersion,
                        definition.introducedRuleBlocks,
                        value => introducedBindingsAdded = value);
                    if (!introducedBindingsAdded)
                    {
                        SetStatus(
                            "Semantic Rule Block introduction was rejected for '" +
                            entity.displayName + "': " +
                            authorityClient.LastStatus);
                        completed?.Invoke(false);
                        yield break;
                    }

                    var retiredBindingsRemoved = false;
                    yield return RetireRuleBlockBindings(
                        projection,
                        entityId,
                        currentContractVersion,
                        targetContractVersion,
                        definition.retiredRuleBlocks,
                        value => retiredBindingsRemoved = value);
                    if (!retiredBindingsRemoved)
                    {
                        SetStatus(
                            "Semantic Rule Block retirement was rejected for '" +
                            entity.displayName + "': " +
                            authorityClient.LastStatus);
                        completed?.Invoke(false);
                        yield break;
                    }

                    var activeDefaultBindings =
                        ResolveProjectedDefaultBindings(
                            projection,
                            entityId,
                            definition.defaultRuleBlocks);
                    var lifecycleSnapshot =
                        CaptureMutableLifecycleFacts(
                            projection,
                            entity.entityId);
                    var migratedBaselineOwned = false;
                    yield return PublishDefaultMeaningPackage(
                        definition,
                        entityId,
                        activeDefaultBindings,
                        value => migratedBaselineOwned = value);
                    if (!migratedBaselineOwned)
                    {
                        SetStatus(
                            "Semantic baseline ownership was rejected for '" +
                            entity.displayName + "': " +
                            authorityClient.LastStatus);
                        completed?.Invoke(false);
                        yield break;
                    }

                    OntologyAuthorityWorldProjection postBaselineProjection = null;
                    yield return LoadProjectionCapture(
                        value => postBaselineProjection = value);
                    var missingLifecycleFacts = lifecycleSnapshot
                        .Where(value => !HasProjectedInitialFact(
                            postBaselineProjection,
                            entity.entityId,
                            value))
                        .ToArray();
                    var lifecycleRestored = false;
                    yield return PublishInitialFacts(
                        missingLifecycleFacts,
                        entityId,
                        value => lifecycleRestored = value);
                    if (!lifecycleRestored)
                    {
                        SetStatus(
                            "Lifecycle preservation was rejected for '" +
                            entity.displayName + "': " +
                            authorityClient.LastStatus);
                        completed?.Invoke(false);
                        yield break;
                    }

                    var lifecycleNormalized = false;
                    yield return NormalizeMutableLifecycleFacts(
                        entityId,
                        value => lifecycleNormalized = value);
                    if (!lifecycleNormalized)
                    {
                        SetStatus(
                            "Lifecycle normalization was rejected for '" +
                            entity.displayName + "': " +
                            authorityClient.LastStatus);
                        completed?.Invoke(false);
                        yield break;
                    }

                    var migratedContractMarked = false;
                    yield return PublishSemanticContractMarker(
                        entityId,
                        targetContractVersion,
                        projection,
                        value => migratedContractMarked = value);
                    if (!migratedContractMarked)
                    {
                        SetStatus(
                            "Semantic contract version update was rejected for '" +
                            entity.displayName + "': " +
                            authorityClient.LastStatus);
                        completed?.Invoke(false);
                        yield break;
                    }

                    repairedAny = true;
                    continue;
                }

                if (definition.defaultRuleBlocks == null ||
                    definition.defaultRuleBlocks.Count == 0)
                {
                    continue;
                }

                var requiredFacts = BuildInitialFacts(definition);
                var missingFacts = requiredFacts
                    .Where(value =>
                        value != null &&
                        !HasProjectedInitialFactConflict(
                            projection,
                            entity.entityId,
                            value))
                    .ToArray();
                var factsPublished = false;
                yield return PublishInitialFacts(
                    missingFacts,
                    entityId,
                    value => factsPublished = value);
                if (!factsPublished)
                {
                    SetStatus(
                        "Legacy semantic fact repair was rejected for '" +
                        entity.displayName + "': " +
                        authorityClient.LastStatus);
                    completed?.Invoke(false);
                    yield break;
                }

                var legacyRetiredFactsRemoved = false;
                yield return RetireAuthoredFacts(
                    projection,
                    entityId,
                    0,
                    targetContractVersion,
                    definition.retiredFacts,
                    value => legacyRetiredFactsRemoved = value);
                if (!legacyRetiredFactsRemoved)
                {
                    SetStatus(
                        "Legacy semantic Fact retirement was rejected for '" +
                        entity.displayName + "': " +
                        authorityClient.LastStatus);
                    completed?.Invoke(false);
                    yield break;
                }

                var missingBindings =
                    definition.defaultRuleBlocks
                        .Where(value =>
                            value != null &&
                            !string.IsNullOrWhiteSpace(value.ruleId) &&
                            FindRemoteRuleBinding(
                                projection,
                                entityId,
                                value.ruleId,
                                string.IsNullOrWhiteSpace(
                                    value.bindingVariable)
                                    ? "?target"
                                    : value.bindingVariable) == null)
                        .ToArray();
                var bindingsPublished = false;
                yield return PublishRuleBindings(
                    missingBindings,
                    entityId,
                    value => bindingsPublished = value);
                if (!bindingsPublished)
                {
                    SetStatus(
                        "Legacy semantic contract repair was rejected for '" +
                        entity.displayName + "': " +
                        authorityClient.LastStatus);
                    completed?.Invoke(false);
                    yield break;
                }

                var legacyRetiredBindingsRemoved = false;
                yield return RetireRuleBlockBindings(
                    projection,
                    entityId,
                    0,
                    targetContractVersion,
                    definition.retiredRuleBlocks,
                    value => legacyRetiredBindingsRemoved = value);
                if (!legacyRetiredBindingsRemoved)
                {
                    SetStatus(
                        "Legacy Rule Block retirement was rejected for '" +
                        entity.displayName + "': " +
                        authorityClient.LastStatus);
                    completed?.Invoke(false);
                    yield break;
                }

                var legacyBaselineOwned = false;
                yield return PublishDefaultMeaningPackage(
                    definition,
                    entityId,
                    definition.defaultRuleBlocks,
                    value => legacyBaselineOwned = value);
                if (!legacyBaselineOwned)
                {
                    SetStatus(
                        "Legacy semantic baseline ownership was rejected for '" +
                        entity.displayName + "': " +
                        authorityClient.LastStatus);
                    completed?.Invoke(false);
                    yield break;
                }

                var contractMarked = false;
                yield return PublishSemanticContractMarker(
                    entityId,
                    targetContractVersion,
                    projection,
                    value => contractMarked = value);
                if (!contractMarked)
                {
                    SetStatus(
                        "Legacy semantic contract marker was rejected for '" +
                        entity.displayName + "': " +
                        authorityClient.LastStatus);
                    completed?.Invoke(false);
                    yield break;
                }

                repairedAny = true;
            }

            if (repairedAny)
            {
                var reloaded = false;
                yield return authorityClient.LoadWorldRoutine(
                    value => reloaded = value);
                completed?.Invoke(reloaded);
                yield break;
            }

            completed?.Invoke(true);
        }

        private IEnumerator MigrateRuleBlockBindings(
            OntologyAuthorityWorldProjection projection,
            Guid entityId,
            int currentContractVersion,
            int targetContractVersion,
            IReadOnlyList<OntologyRuleBlockMigration> migrations,
            Action<bool> completed)
        {
            foreach (var migration in migrations ??
                     Array.Empty<OntologyRuleBlockMigration>())
            {
                if (migration == null ||
                    currentContractVersion >=
                    Mathf.Max(1, migration.targetContractVersion) ||
                    Mathf.Max(1, migration.targetContractVersion) >
                    targetContractVersion ||
                    string.IsNullOrWhiteSpace(migration.fromRuleId) ||
                    migration.replacement == null ||
                    string.IsNullOrWhiteSpace(
                        migration.replacement.ruleId))
                {
                    continue;
                }

                var fromVariable = string.IsNullOrWhiteSpace(
                    migration.fromBindingVariable)
                    ? "?target"
                    : migration.fromBindingVariable;
                var replacementVariable = string.IsNullOrWhiteSpace(
                    migration.replacement.bindingVariable)
                    ? "?target"
                    : migration.replacement.bindingVariable;
                var source = FindRemoteRuleBinding(
                    projection,
                    entityId,
                    migration.fromRuleId,
                    fromVariable);
                var replacement = FindRemoteRuleBinding(
                    projection,
                    entityId,
                    migration.replacement.ruleId,
                    replacementVariable);
                var replacesSameBinding =
                    string.Equals(
                        migration.fromRuleId,
                        migration.replacement.ruleId,
                        StringComparison.Ordinal) &&
                    string.Equals(
                        fromVariable,
                        replacementVariable,
                        StringComparison.Ordinal);

                // Absence of both bindings means the user had removed the old
                // behavior intentionally. Advancing the contract version must
                // preserve that decision rather than restoring a default.
                if (source == null) continue;

                if (replacement == null && !replacesSameBinding)
                {
                    var replacementPublished = false;
                    yield return PublishRuleBindings(
                        new[] { migration.replacement },
                        entityId,
                        value => replacementPublished = value);
                    if (!replacementPublished)
                    {
                        completed?.Invoke(false);
                        yield break;
                    }
                }

                if (!Guid.TryParse(source.bindingId, out var sourceBindingId))
                {
                    completed?.Invoke(false);
                    yield break;
                }
                OntologyAuthorityCommandResult removeResult = null;
                yield return authorityClient
                    .SendCommandWithRevisionRetryRoutine(
                    () => OntologyWorldAuthorityClient.CreateCommand(
                        OntologyWorldCommandKinds.RemoveRuleBlock,
                        OntologyWorldAuthorityClient
                            .CreateRemoveRuleBlockPayload(
                                sourceBindingId)),
                    value => removeResult = value);
                if (removeResult == null ||
                    (!removeResult.accepted &&
                     !string.Equals(
                         removeResult.rejectionCode,
                         "rule_binding_not_found",
                         StringComparison.Ordinal)))
                {
                    completed?.Invoke(false);
                    yield break;
                }

                // A catalog-version upgrade keeps the same semantic Rule ID
                // but must mint a binding to the newly published immutable
                // definition. Remove the old binding first, then republish it;
                // treating it as an already-existing replacement would leave
                // the behavior disabled.
                if (replacesSameBinding)
                {
                    var replacementPublished = false;
                    yield return PublishRuleBindings(
                        new[] { migration.replacement },
                        entityId,
                        value => replacementPublished = value);
                    if (!replacementPublished)
                    {
                        completed?.Invoke(false);
                        yield break;
                    }
                }
            }

            completed?.Invoke(true);
        }

        private IEnumerator RetireRuleBlockBindings(
            OntologyAuthorityWorldProjection projection,
            Guid entityId,
            int currentContractVersion,
            int targetContractVersion,
            IReadOnlyList<OntologyRuleBlockRetirement> retirements,
            Action<bool> completed)
        {
            foreach (var retirement in retirements ??
                     Array.Empty<OntologyRuleBlockRetirement>())
            {
                if (retirement == null ||
                    string.IsNullOrWhiteSpace(retirement.ruleId))
                {
                    continue;
                }

                var retirementVersion = Mathf.Max(
                    1,
                    retirement.contractVersion);
                if (currentContractVersion >= retirementVersion ||
                    retirementVersion > targetContractVersion)
                {
                    continue;
                }

                var bindingVariable = string.IsNullOrWhiteSpace(
                    retirement.bindingVariable)
                    ? "?target"
                    : retirement.bindingVariable;
                var source = FindRemoteRuleBinding(
                    projection,
                    entityId,
                    retirement.ruleId,
                    bindingVariable);
                if (source == null) continue;
                if (!Guid.TryParse(source.bindingId, out var sourceBindingId))
                {
                    completed?.Invoke(false);
                    yield break;
                }

                OntologyAuthorityCommandResult removeResult = null;
                yield return authorityClient
                    .SendCommandWithRevisionRetryRoutine(
                    () => OntologyWorldAuthorityClient.CreateCommand(
                        OntologyWorldCommandKinds.RemoveRuleBlock,
                        OntologyWorldAuthorityClient
                            .CreateRemoveRuleBlockPayload(sourceBindingId)),
                    value => removeResult = value);
                if (removeResult == null ||
                    (!removeResult.accepted &&
                     !string.Equals(
                         removeResult.rejectionCode,
                         "rule_binding_not_found",
                         StringComparison.Ordinal)))
                {
                    completed?.Invoke(false);
                    yield break;
                }
            }

            completed?.Invoke(true);
        }

        private IEnumerator IntroduceRuleBlockBindings(
            OntologyAuthorityWorldProjection projection,
            Guid entityId,
            int currentContractVersion,
            int targetContractVersion,
            IReadOnlyList<OntologyRuleBlockIntroduction> introductions,
            Action<bool> completed)
        {
            foreach (var introduction in introductions ??
                     Array.Empty<OntologyRuleBlockIntroduction>())
            {
                if (introduction?.binding == null ||
                    string.IsNullOrWhiteSpace(
                        introduction.binding.ruleId))
                {
                    continue;
                }

                var introductionVersion = Mathf.Max(
                    1,
                    introduction.contractVersion);
                if (currentContractVersion >= introductionVersion ||
                    introductionVersion > targetContractVersion)
                {
                    continue;
                }

                var variable = string.IsNullOrWhiteSpace(
                    introduction.binding.bindingVariable)
                    ? "?target"
                    : introduction.binding.bindingVariable;
                if (FindRemoteRuleBinding(
                        projection,
                        entityId,
                        introduction.binding.ruleId,
                        variable) != null)
                {
                    continue;
                }

                var published = false;
                yield return PublishRuleBindings(
                    new[] { introduction.binding },
                    entityId,
                    value => published = value);
                if (!published)
                {
                    completed?.Invoke(false);
                    yield break;
                }
            }

            completed?.Invoke(true);
        }

        private IEnumerator IntroduceAuthoredFacts(
            OntologyAuthorityWorldProjection projection,
            Guid entityId,
            int currentContractVersion,
            int targetContractVersion,
            IReadOnlyList<OntologyFactIntroduction> introductions,
            Action<bool> completed)
        {
            var pending = new List<OntologyAuthorityInitialFact>();
            foreach (var introduction in introductions ??
                     Array.Empty<OntologyFactIntroduction>())
            {
                if (introduction?.fact == null ||
                    string.IsNullOrWhiteSpace(
                        introduction.fact.predicate))
                {
                    continue;
                }

                var introductionVersion = Mathf.Max(
                    1,
                    introduction.contractVersion);
                if (!ShouldIntroduceAuthoredFact(
                        currentContractVersion,
                        targetContractVersion,
                        introductionVersion,
                        HasProjectedIntroductionConflict(
                            projection,
                            entityId,
                            introduction.fact)))
                {
                    continue;
                }

                pending.Add(
                    OntologyWorldAuthorityClient.CreateInitialFact(
                        introduction.fact.predicate,
                        introduction.fact.obj));
            }

            if (pending.Count == 0)
            {
                completed?.Invoke(true);
                yield break;
            }

            var published = false;
            yield return PublishInitialFacts(
                pending,
                entityId,
                value => published = value);
            completed?.Invoke(published);
        }

        public static bool ShouldIntroduceAuthoredFact(
            int currentContractVersion,
            int targetContractVersion,
            int introductionVersion,
            bool predicateAlreadyExists)
        {
            var version = Mathf.Max(1, introductionVersion);
            return !predicateAlreadyExists &&
                   currentContractVersion < version &&
                   version <= targetContractVersion;
        }

        public static bool ShouldPublishLegacyInitialFact(
            OntologyCardinalityKind cardinality,
            bool predicateAlreadyExists,
            bool exactFactAlreadyExists)
        {
            return cardinality == OntologyCardinalityKind.Set
                ? !exactFactAlreadyExists
                : !predicateAlreadyExists;
        }

        public static bool ShouldRetireAuthoredFact(
            int currentContractVersion,
            int targetContractVersion,
            int retirementVersion,
            bool onlyWhenPredicateHasDifferentValue,
            bool predicateHasDifferentValue)
        {
            var version = Mathf.Max(1, retirementVersion);
            return currentContractVersion < version &&
                   version <= targetContractVersion &&
                   (!onlyWhenPredicateHasDifferentValue ||
                    predicateHasDifferentValue);
        }

        private IEnumerator RetireAuthoredFacts(
            OntologyAuthorityWorldProjection projection,
            Guid entityId,
            int currentContractVersion,
            int targetContractVersion,
            IReadOnlyList<OntologyFactRetirement> retirements,
            Action<bool> completed)
        {
            foreach (var retirement in retirements ??
                     Array.Empty<OntologyFactRetirement>())
            {
                if (retirement?.fact == null ||
                    string.IsNullOrWhiteSpace(
                        retirement.fact.predicate) ||
                    string.IsNullOrWhiteSpace(
                        retirement.fact.obj))
                {
                    continue;
                }

                var expected =
                    OntologyWorldAuthorityClient.CreateInitialFact(
                        retirement.fact.predicate,
                        retirement.fact.obj);
                var matching = projection?.facts?
                    .Where(value => MatchesProjectedInitialFact(
                        value,
                        entityId.ToString("D"),
                        expected))
                    .ToArray() ??
                    Array.Empty<OntologyAuthorityFactProjection>();
                var hasDifferentValue = projection?.facts?.Any(value =>
                    value != null &&
                    string.Equals(
                        value.subjectEntityId,
                        entityId.ToString("D"),
                        StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(
                        value.predicateId,
                        expected.predicateId,
                        StringComparison.Ordinal) &&
                    !MatchesProjectedInitialFact(
                        value,
                        entityId.ToString("D"),
                        expected)) == true;
                if (matching.Length == 0 ||
                    !ShouldRetireAuthoredFact(
                        currentContractVersion,
                        targetContractVersion,
                        retirement.contractVersion,
                        retirement.onlyWhenPredicateHasDifferentValue,
                        hasDifferentValue))
                {
                    continue;
                }

                foreach (var candidate in matching)
                {
                    if (!Guid.TryParse(
                            candidate.factId,
                            out var factId))
                    {
                        completed?.Invoke(false);
                        yield break;
                    }

                    OntologyAuthorityCommandResult result = null;
                    yield return authorityClient
                        .SendCommandWithRevisionRetryRoutine(
                        () => OntologyWorldAuthorityClient.CreateCommand(
                            OntologyWorldCommandKinds.RetractAuthoredFact,
                            OntologyWorldAuthorityClient
                                .CreateRetractFactPayload(factId)),
                        value => result = value);
                    if (result == null ||
                        (!result.accepted &&
                         !string.Equals(
                             result.rejectionCode,
                             "fact_not_found",
                             StringComparison.Ordinal)))
                    {
                        completed?.Invoke(false);
                        yield break;
                    }
                }
            }

            completed?.Invoke(true);
        }

        private static bool HasProjectedIntroductionConflict(
            OntologyAuthorityWorldProjection projection,
            Guid entityId,
            OntologyFactEntry introduction)
        {
            if (introduction == null ||
                string.IsNullOrWhiteSpace(introduction.predicate))
            {
                return false;
            }

            var term = OntologyLanguagePackService.Registry.Find(
                introduction.predicate);
            if (term?.Cardinality != OntologyCardinalityKind.Set)
            {
                // Single/unspecified predicates preserve an existing authored
                // value instead of replacing a user's tuning during migration.
                return HasProjectedPredicate(
                    projection,
                    entityId,
                    introduction.predicate);
            }

            // Set-valued relations such as has_concept only block an exact
            // duplicate. Another value of the same predicate must not suppress
            // a newly declared contract member.
            return HasProjectedInitialFact(
                projection,
                entityId.ToString("D"),
                OntologyWorldAuthorityClient.CreateInitialFact(
                    introduction.predicate,
                    introduction.obj));
        }

        private static bool HasProjectedPredicate(
            OntologyAuthorityWorldProjection projection,
            Guid entityId,
            string predicate)
        {
            if (projection?.facts == null ||
                entityId == Guid.Empty ||
                string.IsNullOrWhiteSpace(predicate))
            {
                return false;
            }

            var subjectId = entityId.ToString("D");
            return projection.facts.Any(value =>
                value != null &&
                string.Equals(
                    value.subjectEntityId,
                    subjectId,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    value.predicateId,
                    predicate,
                    StringComparison.Ordinal));
        }

        private IEnumerator RepairLegacyImplicitPhysicalProfile(
            OntologyAuthorityWorldProjection projection,
            OntologyAuthorityEntityProjection entity,
            Guid entityId,
            OntologyPlaceableDefinition definition,
            Action<bool, bool> completed)
        {
            var desiredProfileId = definition?.physicalProfile == null
                ? string.Empty
                : definition.physicalProfile.profileId;
            var profileFacts = projection?.facts == null
                ? Array.Empty<OntologyAuthorityFactProjection>()
                : projection.facts.Where(value =>
                    value != null &&
                    string.Equals(
                        value.subjectEntityId,
                        entity.entityId,
                        StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(
                        value.predicateId,
                        OntologyPredicates.PhysicalProfile,
                        StringComparison.Ordinal)).ToArray();
            var current = profileFacts.FirstOrDefault(value =>
                !string.IsNullOrWhiteSpace(value.objectCanonicalId));
            var markerPresent = projection?.facts != null &&
                                projection.facts.Any(value =>
                                    value != null &&
                                    string.Equals(
                                        value.subjectEntityId,
                                        entity.entityId,
                                        StringComparison.OrdinalIgnoreCase) &&
                                    string.Equals(
                                        value.predicateId,
                                        OntologyPredicates
                                            .PhysicalProfileContractVersion,
                                        StringComparison.Ordinal));
            if (!ShouldMigrateLegacyImplicitPhysicalProfile(
                    current?.objectCanonicalId,
                    desiredProfileId,
                    markerPresent) ||
                current == null ||
                !Guid.TryParse(current.factId, out var factId))
            {
                completed?.Invoke(true, false);
                yield break;
            }

            OntologyAuthorityCommandResult result = null;
            yield return authorityClient
                .SendCommandWithRevisionRetryRoutine(
                () => OntologyWorldAuthorityClient.CreateCommand(
                    OntologyWorldCommandKinds.SetAuthoredFact,
                    OntologyWorldAuthorityClient.CreateFactPayload(
                        entityId,
                        OntologyWorldAuthorityClient.CreateInitialFact(
                            OntologyPredicates.PhysicalProfile,
                            desiredProfileId))),
                value => result = value);
            if (result == null ||
                (!result.accepted &&
                 !string.Equals(
                     result.rejectionCode,
                     "fact_already_exists",
                     StringComparison.Ordinal)))
            {
                SetStatus(
                    "Explicit physical profile migration was rejected for '" +
                    entity.displayName + "': " +
                    (result?.rejectionCode ?? authorityClient.LastStatus));
                completed?.Invoke(false, false);
                yield break;
            }

            result = null;
            yield return authorityClient
                .SendCommandWithRevisionRetryRoutine(
                () => OntologyWorldAuthorityClient.CreateCommand(
                    OntologyWorldCommandKinds.RetractAuthoredFact,
                    OntologyWorldAuthorityClient.CreateRetractFactPayload(factId)),
                value => result = value);
            if (result == null || !result.accepted)
            {
                SetStatus(
                    "Legacy physical profile removal was rejected for '" +
                    entity.displayName + "': " +
                    (result?.rejectionCode ?? authorityClient.LastStatus));
                completed?.Invoke(false, false);
                yield break;
            }

            result = null;
            yield return authorityClient
                .SendCommandWithRevisionRetryRoutine(
                () => OntologyWorldAuthorityClient.CreateCommand(
                    OntologyWorldCommandKinds.SetAuthoredFact,
                    OntologyWorldAuthorityClient.CreateFactPayload(
                        entityId,
                        OntologyWorldAuthorityClient.CreateInitialFact(
                            OntologyPredicates.PhysicalProfileContractVersion,
                            "1"))),
                value => result = value);
            var marked = result != null &&
                         (result.accepted ||
                          string.Equals(
                              result.rejectionCode,
                              "fact_already_exists",
                              StringComparison.Ordinal));
            if (!marked)
            {
                SetStatus(
                    "Physical profile migration marker was rejected for '" +
                    entity.displayName + "': " +
                    (result?.rejectionCode ?? authorityClient.LastStatus));
            }
            completed?.Invoke(marked, marked);
        }

        public static bool ShouldMigrateLegacyImplicitPhysicalProfile(
            string currentProfileId,
            string desiredProfileId,
            bool markerPresent) =>
            !markerPresent &&
            string.Equals(
                currentProfileId,
                "HeavySinking",
                StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(desiredProfileId) &&
            !string.Equals(
                currentProfileId,
                desiredProfileId,
                StringComparison.Ordinal);

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
            if (subscribedAuthorityClient != authorityClient)
            {
                if (subscribedAuthorityClient != null)
                {
                    subscribedAuthorityClient.ProjectionReceived -= ApplyProjection;
                    subscribedAuthorityClient.ProjectionZoneScopeChanged -=
                        HandleProjectionZoneScopeChanged;
                }

                subscribedAuthorityClient = authorityClient;
                if (subscribedAuthorityClient != null)
                {
                    subscribedAuthorityClient.ProjectionReceived += ApplyProjection;
                    subscribedAuthorityClient.ProjectionZoneScopeChanged +=
                        HandleProjectionZoneScopeChanged;
                }
            }

            if (subscribedPlacementController != placementController)
            {
                if (subscribedPlacementController != null)
                {
                    subscribedPlacementController.ObjectPlaced -=
                        HandleLocalPlacement;
                }

                subscribedPlacementController = placementController;
                if (subscribedPlacementController != null)
                {
                    subscribedPlacementController.ObjectPlaced +=
                        HandleLocalPlacement;
                }
            }

            if (subscribedWorldEditorController != worldEditorController)
            {
                if (subscribedWorldEditorController != null)
                {
                    subscribedWorldEditorController.TransformCommitted -=
                        HandleTransformCommitted;
                    subscribedWorldEditorController.DuplicateCommitted -=
                        HandleDuplicateCommitted;
                    subscribedWorldEditorController.EntityRetirementRequested -=
                        HandleEntityRetirementRequested;
                    subscribedWorldEditorController.AuthoredFactChanged -=
                        HandleAuthoredFactChanged;
                    subscribedWorldEditorController.RuleBlockChanged -=
                        HandleRuleBlockChanged;
                    subscribedWorldEditorController.RuleBlockRemovalRequested -=
                        HandleRuleBlockRemovalRequested;
                    subscribedWorldEditorController.MeaningPackageChangeRequested -=
                        HandleMeaningPackageChangeRequested;
                }

                subscribedWorldEditorController = worldEditorController;
                if (subscribedWorldEditorController != null)
                {
                    subscribedWorldEditorController.TransformCommitted +=
                        HandleTransformCommitted;
                    subscribedWorldEditorController.DuplicateCommitted +=
                        HandleDuplicateCommitted;
                    subscribedWorldEditorController.EntityRetirementRequested +=
                        HandleEntityRetirementRequested;
                    subscribedWorldEditorController.AuthoredFactChanged +=
                        HandleAuthoredFactChanged;
                    subscribedWorldEditorController.RuleBlockChanged +=
                        HandleRuleBlockChanged;
                    subscribedWorldEditorController.RuleBlockRemovalRequested +=
                        HandleRuleBlockRemovalRequested;
                    subscribedWorldEditorController.MeaningPackageChangeRequested +=
                        HandleMeaningPackageChangeRequested;
                }
            }
        }

        private void UnsubscribeDependencies()
        {
            if (subscribedAuthorityClient != null)
            {
                subscribedAuthorityClient.ProjectionReceived -= ApplyProjection;
                subscribedAuthorityClient.ProjectionZoneScopeChanged -=
                    HandleProjectionZoneScopeChanged;
            }
            subscribedAuthorityClient = null;

            if (subscribedPlacementController != null)
            {
                subscribedPlacementController.ObjectPlaced -=
                    HandleLocalPlacement;
            }
            subscribedPlacementController = null;

            if (subscribedWorldEditorController != null)
            {
                subscribedWorldEditorController.TransformCommitted -=
                    HandleTransformCommitted;
                subscribedWorldEditorController.DuplicateCommitted -=
                    HandleDuplicateCommitted;
                subscribedWorldEditorController.EntityRetirementRequested -=
                    HandleEntityRetirementRequested;
                subscribedWorldEditorController.AuthoredFactChanged -=
                    HandleAuthoredFactChanged;
                subscribedWorldEditorController.RuleBlockChanged -=
                    HandleRuleBlockChanged;
                subscribedWorldEditorController.RuleBlockRemovalRequested -=
                    HandleRuleBlockRemovalRequested;
                subscribedWorldEditorController.MeaningPackageChangeRequested -=
                    HandleMeaningPackageChangeRequested;
            }
            subscribedWorldEditorController = null;
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

            StartCoroutine(PublishMoveRoutine(instance, null));
        }

        /// <summary>
        /// Persists one coarse resting pose for an ontology-selected Dynamic body.
        /// Per-frame physics remains local and never enters the durable command log.
        /// </summary>
        public bool TryPublishDynamicTransformCheckpoint(
            OntologyPlaceableInstance instance,
            Action<bool> completed)
        {
            ResolveDependencies();
            if (instance == null || !IsAutomaticAuthorityEnabled())
            {
                return false;
            }

            var identity = instance.GetComponent<OntologyAuthorityEntityIdentity>();
            if (identity == null ||
                !identity.TryGetGuid(out var entityId) ||
                !publishedEntityIds.Contains(entityId.ToString("D")))
            {
                return false;
            }

            StartCoroutine(PublishMoveRoutine(instance, completed));
            return true;
        }

        private IEnumerator PublishMoveRoutine(
            OntologyPlaceableInstance instance,
            Action<bool> completed)
        {
            if (instance == null || authorityClient == null ||
                !authorityClient.IsWorldRuntimeReady)
            {
                completed?.Invoke(false);
                yield break;
            }

            var identity = EnsureIdentity(instance.gameObject);
            var command = OntologyWorldAuthorityClient.CreateCommand(
                OntologyWorldCommandKinds.MoveEntity,
                OntologyWorldAuthorityClient.CreateMoveEntityPayload(
                    identity.EnsureGuid(), instance.transform));
            OntologyAuthorityCommandResult result = null;
            yield return authorityClient.SendCommandRoutine(
                command,
                value => result = value);
            var accepted = result != null && result.accepted;
            SetStatus(accepted
                ? "Published move at revision " + result.revision + "."
                : "Move was not published: " +
                  (result == null ? authorityClient.LastStatus : result.rejectionCode));
            completed?.Invoke(accepted);
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

        /// <summary>
        /// Claims deletion only for an entity already owned by the Authority
        /// projection. Local previews continue to use the editor's local fallback.
        /// </summary>
        private bool HandleEntityRetirementRequested(
            OntologyPlaceableInstance instance)
        {
            if (instance == null)
                return false;

            ResolveDependencies();
            var identity =
                instance.GetComponent<OntologyAuthorityEntityIdentity>();
            if (!IsAutomaticAuthorityEnabled() ||
                identity == null ||
                !identity.TryGetGuid(out var entityId))
            {
                return false;
            }

            var entityKey = entityId.ToString("D");
            if (!publishedEntityIds.Contains(entityKey))
                return false;

            if (!retiringEntityIds.Add(entityKey))
                return true;

            StartCoroutine(RetireEntityRoutine(entityId, entityKey));
            return true;
        }

        private IEnumerator RetireEntityRoutine(Guid entityId, string entityKey)
        {
            OntologyAuthorityCommandResult result = null;
            yield return authorityClient.SendCommandWithRevisionRetryRoutine(
                () => OntologyWorldAuthorityClient.CreateCommand(
                    OntologyWorldCommandKinds.RetireEntity,
                    OntologyWorldAuthorityClient.CreateRetireEntityPayload(
                        entityId)),
                value => result = value);

            if (result != null && result.accepted)
            {
                SetStatus(
                    "Authority retired entity at revision " +
                    result.revision + ".");
                // The projection is the only component allowed to remove the
                // presentation. This also prevents a locally deleted object
                // from being recreated on the next refresh.
                yield return authorityClient.LoadWorldRoutine();
            }
            else
            {
                SetStatus(
                    "Authority rejected entity retirement: " +
                    (result?.rejectionCode ?? "unknown_error"));
            }

            retiringEntityIds.Remove(entityKey);
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

        private void HandleMeaningPackageChangeRequested(
            OntologyPlaceableInstance instance,
            OntologyMeaningPackageChange change)
        {
            if (suppressOutgoingChanges || instance == null || change == null ||
                !IsAutomaticAuthorityEnabled())
            {
                return;
            }

            var identity = EnsureIdentity(instance.gameObject);
            var entityId = identity.EnsureGuid();
            if (!publishedEntityIds.Contains(entityId.ToString("D")))
            {
                SetStatus(
                    "Place and confirm the object before changing its meaning package.");
                return;
            }
            StartCoroutine(PublishMeaningPackageChangeRoutine(
                entityId, change));
        }

        private void HandleRuleBlockRemovalRequested(
            OntologyPlaceableInstance instance,
            OntologyRuleBlockBinding binding)
        {
            if (instance == null || binding == null ||
                !Guid.TryParse(binding.bindingId, out var bindingId) ||
                !IsAutomaticAuthorityEnabled())
            {
                worldEditorController?.CompleteRuleBlockRemoval(
                    binding?.bindingId);
                return;
            }
            StartCoroutine(PublishExactRuleBlockRemovalRoutine(
                bindingId, binding.bindingId));
        }

        private IEnumerator PublishExactRuleBlockRemovalRoutine(
            Guid bindingId,
            string bindingIdText)
        {
            OntologyAuthorityCommandResult result = null;
            yield return authorityClient.SendCommandWithRevisionRetryRoutine(
                () => OntologyWorldAuthorityClient.CreateCommand(
                    OntologyWorldCommandKinds.RemoveRuleBlock,
                    OntologyWorldAuthorityClient.CreateRemoveRuleBlockPayload(
                        bindingId)),
                value => result = value);

            if (result != null && result.accepted)
            {
                yield return authorityClient.LoadWorldRoutine();
                SetStatus(
                    "Authority confirmed exact rule binding removal at revision " +
                    result.revision + ".");
            }
            else
            {
                SetStatus("Authority rejected exact rule binding removal: " +
                          (result?.rejectionCode ?? "unknown_error"));
                if (string.Equals(
                        result?.rejectionCode,
                        "stale_revision",
                        StringComparison.Ordinal))
                    yield return authorityClient.LoadWorldRoutine();
            }
            worldEditorController?.CompleteRuleBlockRemoval(bindingIdText);
        }

        private IEnumerator PublishMeaningPackageChangeRoutine(
            Guid targetEntityId,
            OntologyMeaningPackageChange change)
        {
            OntologyAuthorityCommandResult result = null;
            yield return authorityClient.SendCommandWithRevisionRetryRoutine(
                () => OntologyWorldAuthorityClient.CreateCommand(
                    OntologyWorldCommandKinds.ApplyMeaningPackage,
                    OntologyWorldAuthorityClient.CreateMeaningPackagePayload(
                        targetEntityId, change)),
                value => result = value);
            if (result == null || !result.accepted)
            {
                SetStatus(
                    "Authority rejected meaning package '" +
                    change.packageId + "': " +
                    (result?.rejectionCode ?? "unknown_error"));
                // The controller did not mutate locally. Reloading guarantees
                // presentation remains an exact Authority projection.
                yield return authorityClient.LoadWorldRoutine();
                yield break;
            }

            yield return authorityClient.LoadWorldRoutine();
            SetStatus(
                "Authority confirmed meaning package '" +
                change.packageId + "' at revision " +
                result.revision + ".");
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
                        authorityClient.CurrentProjectionZoneKey,
                        BuildInitialFacts(instance.gameObject)));
                yield return authorityClient.SendCommandRoutine(
                    placeCommand,
                    result => placed = result != null && result.accepted);
                if (!placed)
                {
                    SetStatus("Stopped while publishing '" + instance.name + "': " + authorityClient.LastStatus);
                    yield break;
                }

                var bindings = GetRuleBindings(instance.gameObject);
                var definition = placementController?.Catalog?.Find(
                    instance.DefinitionId);
                var baselineOwned = false;
                yield return PublishDefaultMeaningPackage(
                    definition,
                    entityGuid,
                    ResolveLocalDefaultBindings(
                        definition?.defaultRuleBlocks,
                        bindings),
                    value => baselineOwned = value);
                if (!baselineOwned)
                {
                    SetStatus(
                        "Stopped while owning the semantic baseline for '" +
                        instance.name + "': " +
                        authorityClient.LastStatus);
                    yield break;
                }

                var ruleBindingsPublished = false;
                yield return PublishRuleBindings(
                    bindings,
                    entityGuid,
                    value => ruleBindingsPublished = value);
                if (!ruleBindingsPublished)
                {
                    SetStatus(
                        "Stopped while publishing Rule Blocks for '" +
                        instance.name + "': " +
                        authorityClient.LastStatus);
                    yield break;
                }
                if (bindings.Length > 0)
                {
                    var contractMarked = false;
                    yield return PublishSemanticContractMarker(
                        entityGuid,
                        ResolveSemanticContractVersion(
                            instance.DefinitionId),
                        null,
                        value => contractMarked = value);
                    if (!contractMarked)
                    {
                        SetStatus(
                            "Stopped while marking the semantic contract for '" +
                            instance.name + "': " +
                            authorityClient.LastStatus);
                        yield break;
                    }
                }
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
            yield return PublishInstanceRoutine(
                instance,
                destroyRejectedLocalInstance: true);
        }

        private IEnumerator PlaceAuthorityFirstRoutine(
            OntologyPlaceableDefinition definition,
            Vector3 position,
            Vector3 rotationEuler,
            Vector3 localScale)
        {
            if (definition == null || authorityClient == null ||
                !authorityClient.IsWorldRuntimeReady)
            {
                yield break;
            }

            var entityId = Guid.NewGuid();
            var displayName = definition.definitionId + "_" +
                              entityId.ToString("N").Substring(0, 8);
            var command = OntologyWorldAuthorityClient.CreateCommand(
                OntologyWorldCommandKinds.PlaceEntity,
                OntologyWorldAuthorityClient.CreatePlaceEntityPayload(
                    entityId,
                    definition.definitionId,
                    displayName,
                    position,
                    rotationEuler,
                    localScale,
                    authorityClient.CurrentProjectionZoneKey,
                    BuildInitialFacts(definition)));
            OntologyAuthorityCommandResult result = null;
            yield return authorityClient.SendCommandRoutine(
                command,
                value => result = value);
            if (result == null || !result.accepted)
            {
                SetStatus(
                    "Authority rejected placement of '" +
                    definition.definitionId + "': " +
                    authorityClient.LastStatus);
                yield break;
            }

            var loaded = false;
            yield return authorityClient.LoadWorldRoutine(
                value => loaded = value);
            if (!loaded)
            {
                SetStatus(
                    "Authority accepted placement, but its projection could not be loaded.");
                yield break;
            }

            var identity = FindIdentity(entityId.ToString("D"));
            var instance = identity == null
                ? null
                : identity.GetComponent<OntologyPlaceableInstance>();
            if (instance == null)
            {
                SetStatus(
                    "Authority confirmed placement, but no presentation template " +
                    "was available for '" + definition.definitionId + "'.");
                yield break;
            }

            var baselineOwned = false;
            yield return PublishDefaultMeaningPackage(
                definition,
                entityId,
                definition.defaultRuleBlocks,
                value => baselineOwned = value);
            if (!baselineOwned)
            {
                SetStatus(
                    "Authority placed '" + displayName +
                    "', but its semantic baseline was rejected: " +
                    authorityClient.LastStatus);
                yield break;
            }
            if (definition.defaultRuleBlocks != null &&
                definition.defaultRuleBlocks.Count > 0)
            {
                var contractMarked = false;
                yield return PublishSemanticContractMarker(
                    entityId,
                    Mathf.Max(1, definition.semanticContractVersion),
                    null,
                    value => contractMarked = value);
                if (!contractMarked)
                {
                    SetStatus(
                        "Authority placed '" + displayName +
                        "', but its semantic contract marker was rejected: " +
                        authorityClient.LastStatus);
                    yield break;
                }
            }
            publishedEntityIds.Add(entityId.ToString("D"));
            yield return authorityClient.LoadWorldRoutine();
            SetStatus(
                "Authority confirmed placement of '" + displayName + "'.");
        }

        private IEnumerator PublishInstanceRoutine(
            OntologyPlaceableInstance instance,
            bool destroyRejectedLocalInstance)
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
            if (publishingEntityIds.Contains(entityKey))
            {
                while (publishingEntityIds.Contains(entityKey))
                    yield return null;
                yield break;
            }
            if (string.IsNullOrWhiteSpace(instance.DefinitionId))
            {
                SetStatus("Cannot publish '" + instance.name + "': it has no catalog definition id.");
                yield break;
            }

            publishingEntityIds.Add(entityKey);
            var accepted = false;
            var command = OntologyWorldAuthorityClient.CreateCommand(
                    OntologyWorldCommandKinds.PlaceEntity,
                    OntologyWorldAuthorityClient.CreatePlaceEntityPayload(
                        entityId, instance.DefinitionId, instance.name, instance.transform,
                        authorityClient.CurrentProjectionZoneKey,
                        BuildInitialFacts(instance.gameObject)));
            yield return authorityClient.SendCommandRoutine(
                command,
                result => accepted = result != null && result.accepted);
            if (!accepted)
            {
                publishingEntityIds.Remove(entityKey);
                SetStatus("Placement was not confirmed by authority: " +
                          authorityClient.LastStatus);
                if (destroyRejectedLocalInstance)
                {
                    // A newly placed object is optimistic presentation only. A
                    // rejected command must not leave an unsaved world entity.
                    Destroy(instance.gameObject);
                    yield return null;
                    var bootstrap = FindAnyObjectByType<OntologyWorldBootstrap>();
                    bootstrap?.SynchronizeSceneObjects(runSimulation: true);
                }
                yield break;
            }

            var bindings = GetRuleBindings(instance.gameObject);
            var definition = placementController?.Catalog?.Find(
                instance.DefinitionId);
            var baselineOwned = false;
            yield return PublishDefaultMeaningPackage(
                definition,
                entityId,
                ResolveLocalDefaultBindings(
                    definition?.defaultRuleBlocks,
                    bindings),
                value => baselineOwned = value);
            if (!baselineOwned)
            {
                publishingEntityIds.Remove(entityKey);
                SetStatus(
                    "Authority placed '" + instance.name +
                    "', but its semantic baseline was rejected: " +
                    authorityClient.LastStatus);
                yield break;
            }

            var ruleBindingsPublished = false;
            yield return PublishRuleBindings(
                bindings,
                entityId,
                value => ruleBindingsPublished = value);
            if (!ruleBindingsPublished)
            {
                publishingEntityIds.Remove(entityKey);
                SetStatus(
                    "Authority placed '" + instance.name +
                    "', but its Rule Block contract was rejected: " +
                    authorityClient.LastStatus);
                yield break;
            }
            var publishedBindings = bindings;
            if (publishedBindings.Length > 0)
            {
                var contractMarked = false;
                yield return PublishSemanticContractMarker(
                    entityId,
                    ResolveSemanticContractVersion(
                        instance.DefinitionId),
                    null,
                    value => contractMarked = value);
                if (!contractMarked)
                {
                    publishingEntityIds.Remove(entityKey);
                    SetStatus(
                        "Authority placed '" + instance.name +
                        "', but its semantic contract marker was rejected: " +
                        authorityClient.LastStatus);
                    yield break;
                }
            }
            publishedEntityIds.Add(entityKey);
            publishingEntityIds.Remove(entityKey);
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
                    : OntologyWorldAuthorityClient.CreateFactPayload(
                        subjectEntityId,
                        OntologyWorldAuthorityClient.CreateInitialFact(
                            canonicalPredicate,
                            canonicalObject));
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
                var bindingId = CreateRuleBindingId(
                    subjectEntityId,
                    ruleId,
                    bindingVariable);
                yield return authorityClient
                    .SendCommandWithRevisionRetryRoutine(
                    () => OntologyWorldAuthorityClient.CreateCommand(
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

                yield return authorityClient
                    .SendCommandWithRevisionRetryRoutine(
                    () => OntologyWorldAuthorityClient.CreateCommand(
                        OntologyWorldCommandKinds.RemoveRuleBlock,
                        OntologyWorldAuthorityClient.CreateRemoveRuleBlockPayload(bindingId)),
                    value => result = value);
            }

            var alreadyConverged = result != null &&
                ((added && string.Equals(
                      result.rejectionCode,
                      "rule_binding_already_exists",
                      StringComparison.Ordinal)) ||
                 (!added && string.Equals(
                      result.rejectionCode,
                      "rule_binding_not_found",
                      StringComparison.Ordinal)));
            if (result != null && (result.accepted || alreadyConverged))
            {
                if (!added)
                {
                    remoteRuleBindingIds.Remove(key);
                    // Removing a package-owned Rule Block can retract its
                    // associated Triple and Physical Meaning contributions in
                    // the same Authority revision. Reload the full projection
                    // before reporting success so Unity never presents stale
                    // package data.
                    yield return authorityClient.LoadWorldRoutine();
                }
                SetStatus(
                    alreadyConverged
                        ? "Rule-block change already matched Authority state."
                        : "Authority confirmed rule-block change at revision " +
                          result.revision + ".");
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

        private static OntologyAuthorityInitialFact[] BuildInitialFacts(
            GameObject target)
        {
            var ontology = target == null
                ? null
                : target.GetComponent<OntologyObject>();
            if (ontology == null)
                return Array.Empty<OntologyAuthorityInitialFact>();

            var result = new List<OntologyAuthorityInitialFact>();
            foreach (var concept in ontology.Concepts)
            {
                if (string.IsNullOrWhiteSpace(concept)) continue;
                result.Add(new OntologyAuthorityInitialFact
                {
                    predicateId = OntologyPredicates.HasConcept,
                    objectKind = "canonical",
                    objectCanonicalId =
                        OntologyLanguagePackService.CanonicalTerm(concept)
                });
            }

            foreach (var fact in ontology.Facts)
            {
                if (fact == null ||
                    string.IsNullOrWhiteSpace(fact.predicate) ||
                    string.IsNullOrWhiteSpace(fact.obj))
                {
                    continue;
                }

                var predicate =
                    OntologyLanguagePackService.CanonicalTerm(fact.predicate);
                var objectValue =
                    OntologyLanguagePackService.CanonicalObjectForRelation(
                        predicate,
                        fact.obj);
                var referenced = FindIdentityForOntologyEntity(objectValue);
                if (referenced != null)
                {
                    result.Add(new OntologyAuthorityInitialFact
                    {
                        predicateId = predicate,
                        objectKind = "entity",
                        objectEntityId =
                            referenced.EnsureGuid().ToString("D")
                    });
                }
                else
                {
                    result.Add(
                        OntologyWorldAuthorityClient.CreateInitialFact(
                            predicate,
                            objectValue));
                }
            }

            return result.ToArray();
        }

        private static OntologyRuleBlockBinding[] GetRuleBindings(
            GameObject target)
        {
            var assignment = target == null
                ? null
                : target.GetComponent<OntologyRuleBlockAssignment>();
            return assignment == null
                ? Array.Empty<OntologyRuleBlockBinding>()
                : assignment.Bindings
                    .Where(value =>
                        value != null &&
                        !string.IsNullOrWhiteSpace(value.ruleId))
                    .Select(value => new OntologyRuleBlockBinding
                    {
                        ruleId = value.ruleId,
                        bindingVariable = value.bindingVariable
                    })
                    .ToArray();
        }

        private static OntologyAuthorityInitialFact[] BuildInitialFacts(
            OntologyPlaceableDefinition definition)
        {
            var template = definition?.ontologyTemplate;
            if (template == null)
                return Array.Empty<OntologyAuthorityInitialFact>();

            var result = new List<OntologyAuthorityInitialFact>();
            foreach (var concept in template.concepts ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(concept)) continue;
                result.Add(new OntologyAuthorityInitialFact
                {
                    predicateId = OntologyPredicates.HasConcept,
                    objectKind = "canonical",
                    objectCanonicalId =
                        OntologyLanguagePackService.CanonicalTerm(concept)
                });
            }

            foreach (var fact in template.facts ?? Array.Empty<OntologyFactEntry>())
            {
                if (fact == null ||
                    string.IsNullOrWhiteSpace(fact.predicate) ||
                    string.IsNullOrWhiteSpace(fact.obj))
                {
                    continue;
                }

                var predicate =
                    OntologyLanguagePackService.CanonicalTerm(fact.predicate);
                var objectValue =
                    OntologyLanguagePackService.CanonicalObjectForRelation(
                        predicate,
                        fact.obj);
                result.Add(OntologyWorldAuthorityClient.CreateInitialFact(
                    predicate,
                    objectValue));
            }

            AddProfileFact(
                result,
                OntologyPredicates.PhysicalProfile,
                definition.physicalProfile == null
                    ? string.Empty
                    : definition.physicalProfile.profileId);
            AddProfileFact(
                result,
                OntologyPredicates.AttachmentProfile,
                definition.attachmentProfile == null
                    ? string.Empty
                    : definition.attachmentProfile.profileId);
            return result.ToArray();
        }

        private static void AddProfileFact(
            ICollection<OntologyAuthorityInitialFact> facts,
            string predicateId,
            string profileId)
        {
            if (facts == null ||
                string.IsNullOrWhiteSpace(predicateId) ||
                string.IsNullOrWhiteSpace(profileId) ||
                facts.Any(value =>
                    value != null &&
                    string.Equals(
                        value.predicateId,
                        predicateId,
                        StringComparison.Ordinal)))
            {
                return;
            }

            facts.Add(OntologyWorldAuthorityClient.CreateInitialFact(
                predicateId,
                profileId.Trim()));
        }

        private IEnumerator PublishDefaultMeaningPackage(
            OntologyPlaceableDefinition definition,
            Guid subjectEntityId,
            IReadOnlyList<OntologyRuleBlockBinding> activeDefaultBindings,
            Action<bool> completed)
        {
            var bindings = activeDefaultBindings?
                .Where(value =>
                    value != null &&
                    !string.IsNullOrWhiteSpace(value.ruleId))
                .ToArray() ?? Array.Empty<OntologyRuleBlockBinding>();
            if (definition == null || bindings.Length == 0)
            {
                completed?.Invoke(true);
                yield break;
            }

            var contractVersion = Mathf.Max(
                1,
                definition.semanticContractVersion);
            var change = new OntologyMeaningPackageChange
            {
                operation = "apply",
                applicationId = CreateMeaningPackageApplicationId(
                    subjectEntityId,
                    "template_semantic_baseline",
                    definition.definitionId +
                    "_semantic_baseline_v" +
                    contractVersion).ToString("D"),
                slotId = "template_semantic_baseline",
                packageId = definition.definitionId +
                            "_semantic_baseline_v" +
                            contractVersion,
                adoptExistingContributions = true
            };

            foreach (var fact in BuildInitialFacts(definition))
            {
                if (fact == null ||
                    string.IsNullOrWhiteSpace(fact.predicateId) ||
                    string.Equals(
                        fact.predicateId,
                        OntologyPredicates.SemanticContractVersion,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                // Mutable lifecycle state is initialized by placement and
                // thereafter owned by lifecycle Rule Blocks. Reapplying a
                // semantic baseline must never heal or resurrect an entity.
                if (IsMutableLifecyclePredicate(fact.predicateId))
                {
                    continue;
                }

                if (string.Equals(
                        fact.predicateId,
                        OntologyPredicates.HasConcept,
                        StringComparison.Ordinal) &&
                    string.Equals(
                        fact.objectKind,
                        "canonical",
                        StringComparison.Ordinal) &&
                    !string.IsNullOrWhiteSpace(fact.objectCanonicalId))
                {
                    if (!change.requiredConceptIds.Contains(
                            fact.objectCanonicalId))
                    {
                        change.requiredConceptIds.Add(
                            fact.objectCanonicalId);
                    }
                    continue;
                }

                if (change.authorityFacts.Any(value =>
                        value != null &&
                        string.Equals(
                            value.predicateId,
                            fact.predicateId,
                            StringComparison.Ordinal) &&
                        string.Equals(
                            value.objectKind,
                            fact.objectKind,
                            StringComparison.Ordinal) &&
                        string.Equals(
                            value.objectEntityId ?? string.Empty,
                            fact.objectEntityId ?? string.Empty,
                            StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(
                            value.objectCanonicalId ?? string.Empty,
                            fact.objectCanonicalId ?? string.Empty,
                            StringComparison.Ordinal) &&
                        string.Equals(
                            value.objectValueJson ?? string.Empty,
                            fact.objectValueJson ?? string.Empty,
                            StringComparison.Ordinal)))
                {
                    continue;
                }

                change.authorityFacts.Add(new OntologyAuthorityInitialFact
                {
                    predicateId = fact.predicateId,
                    objectKind = fact.objectKind,
                    objectEntityId = fact.objectEntityId,
                    objectCanonicalId = fact.objectCanonicalId,
                    objectValueJson = fact.objectValueJson
                });
                if (OntologyLanguagePackService.GetRelationCardinality(
                        fact.predicateId) ==
                    OntologyCardinalityKind.Single &&
                    !change.replacePredicateIds.Contains(
                        fact.predicateId))
                {
                    change.replacePredicateIds.Add(
                        fact.predicateId);
                }
            }

            foreach (var binding in bindings)
            {
                var variable = string.IsNullOrWhiteSpace(
                    binding.bindingVariable)
                    ? "?target"
                    : binding.bindingVariable;
                change.ruleBlocks.Add(
                    new OntologyMeaningPackageRuleBlock
                    {
                        bindingId = CreateRuleBindingId(
                            subjectEntityId,
                            binding.ruleId,
                            variable).ToString("D"),
                        ruleId = binding.ruleId,
                        ruleVersion = ResolveRuleVersion(binding.ruleId),
                        bindingVariable = variable
                    });
            }

            OntologyAuthorityCommandResult result = null;
            yield return authorityClient
                .SendCommandWithRevisionRetryRoutine(
                () => OntologyWorldAuthorityClient.CreateCommand(
                    OntologyWorldCommandKinds.ApplyMeaningPackage,
                    OntologyWorldAuthorityClient.CreateMeaningPackagePayload(
                        subjectEntityId,
                        change)),
                value => result = value);
            completed?.Invoke(result != null && result.accepted);
        }

        private static bool IsMutableLifecyclePredicate(
            string predicateId) =>
            string.Equals(
                predicateId,
                OntologyPredicates.CurrentHealth,
                StringComparison.Ordinal) ||
            string.Equals(
                predicateId,
                OntologyPredicates.IsAlive,
                StringComparison.Ordinal);

        private static OntologyAuthorityInitialFact[]
            CaptureMutableLifecycleFacts(
                OntologyAuthorityWorldProjection projection,
                string entityId)
        {
            var rows = projection?.facts?
                .Where(value =>
                    value != null &&
                    string.Equals(
                        value.subjectEntityId,
                        entityId,
                        StringComparison.OrdinalIgnoreCase))
                .ToArray() ?? Array.Empty<OntologyAuthorityFactProjection>();
            var result = new List<OntologyAuthorityInitialFact>();

            var healthValues = rows
                .Where(value => string.Equals(
                    value.predicateId,
                    OntologyPredicates.CurrentHealth,
                    StringComparison.Ordinal))
                .Select(value =>
                    double.TryParse(
                        value.objectValueJson,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out var number)
                        ? (double?)number
                        : null)
                .Where(value => value.HasValue)
                .Select(value => value.Value)
                .ToArray();
            if (healthValues.Length > 0)
            {
                result.Add(OntologyWorldAuthorityClient.CreateInitialFact(
                    OntologyPredicates.CurrentHealth,
                    healthValues.Min().ToString(
                        "R",
                        CultureInfo.InvariantCulture)));
            }

            var aliveValues = rows
                .Where(value => string.Equals(
                    value.predicateId,
                    OntologyPredicates.IsAlive,
                    StringComparison.Ordinal))
                .Select(value =>
                    bool.TryParse(value.objectValueJson, out var alive)
                        ? (bool?)alive
                        : null)
                .Where(value => value.HasValue)
                .Select(value => value.Value)
                .ToArray();
            if (aliveValues.Length > 0)
            {
                // Conflicting lifecycle rows fail closed. This also repairs
                // older baselines that accidentally reintroduced true over a
                // durable false defeat result.
                result.Add(OntologyWorldAuthorityClient.CreateInitialFact(
                    OntologyPredicates.IsAlive,
                    aliveValues.All(value => value)
                        ? bool.TrueString
                        : bool.FalseString));
            }

            return result.ToArray();
        }

        private IEnumerator NormalizeMutableLifecycleFacts(
            Guid entityId,
            Action<bool> completed)
        {
            OntologyAuthorityWorldProjection projection = null;
            yield return LoadProjectionCapture(value => projection = value);
            var subjectId = entityId.ToString("D");
            var duplicates = projection?.facts?
                .Where(value =>
                    value != null &&
                    string.Equals(
                        value.subjectEntityId,
                        subjectId,
                        StringComparison.OrdinalIgnoreCase) &&
                    IsMutableLifecyclePredicate(value.predicateId))
                .GroupBy(value =>
                    value.predicateId + "\n" +
                    value.objectKind + "\n" +
                    (value.objectValueJson ?? string.Empty) + "\n" +
                    (value.objectCanonicalId ?? string.Empty),
                    StringComparer.Ordinal)
                .SelectMany(group => group
                    .OrderByDescending(value => string.Equals(
                        value.sourceType,
                        "action",
                        StringComparison.Ordinal))
                    .ThenByDescending(value => value.createdRevision)
                    .Skip(1))
                .ToArray() ?? Array.Empty<OntologyAuthorityFactProjection>();

            foreach (var duplicate in duplicates)
            {
                if (!Guid.TryParse(duplicate.factId, out var factId))
                {
                    completed?.Invoke(false);
                    yield break;
                }
                OntologyAuthorityCommandResult result = null;
                yield return authorityClient.SendCommandWithRevisionRetryRoutine(
                    () => OntologyWorldAuthorityClient.CreateCommand(
                        OntologyWorldCommandKinds.RetractAuthoredFact,
                        OntologyWorldAuthorityClient.CreateRetractFactPayload(
                            factId)),
                    value => result = value);
                if (result == null ||
                    (!result.accepted &&
                     !string.Equals(
                         result.rejectionCode,
                         "fact_not_found",
                         StringComparison.Ordinal)))
                {
                    completed?.Invoke(false);
                    yield break;
                }
            }

            completed?.Invoke(true);
        }

        private static OntologyRuleBlockBinding[]
            ResolveProjectedDefaultBindings(
                OntologyAuthorityWorldProjection projection,
                Guid subjectEntityId,
                IReadOnlyList<OntologyRuleBlockBinding> defaults)
        {
            return defaults?
                .Where(value =>
                    value != null &&
                    !string.IsNullOrWhiteSpace(value.ruleId) &&
                    FindRemoteRuleBinding(
                        projection,
                        subjectEntityId,
                        value.ruleId,
                        string.IsNullOrWhiteSpace(value.bindingVariable)
                            ? "?target"
                            : value.bindingVariable) != null)
                .Select(CloneRuleBlockBinding)
                .ToArray() ?? Array.Empty<OntologyRuleBlockBinding>();
        }

        private static OntologyRuleBlockBinding[] ResolveLocalDefaultBindings(
            IReadOnlyList<OntologyRuleBlockBinding> defaults,
            IReadOnlyList<OntologyRuleBlockBinding> active)
        {
            return defaults?
                .Where(value =>
                    value != null &&
                    !string.IsNullOrWhiteSpace(value.ruleId) &&
                    active != null &&
                    active.Any(candidate =>
                        candidate != null &&
                        string.Equals(
                            candidate.ruleId,
                            value.ruleId,
                            StringComparison.Ordinal) &&
                        string.Equals(
                            string.IsNullOrWhiteSpace(
                                candidate.bindingVariable)
                                ? "?target"
                                : candidate.bindingVariable,
                            string.IsNullOrWhiteSpace(
                                value.bindingVariable)
                                ? "?target"
                                : value.bindingVariable,
                            StringComparison.Ordinal)))
                .Select(CloneRuleBlockBinding)
                .ToArray() ?? Array.Empty<OntologyRuleBlockBinding>();
        }

        private static OntologyRuleBlockBinding CloneRuleBlockBinding(
            OntologyRuleBlockBinding source)
        {
            return new OntologyRuleBlockBinding
            {
                ruleId = source.ruleId,
                bindingVariable = string.IsNullOrWhiteSpace(
                    source.bindingVariable)
                    ? "?target"
                    : source.bindingVariable
            };
        }

        private IEnumerator PublishInitialFacts(
            IReadOnlyList<OntologyAuthorityInitialFact> facts,
            Guid subjectEntityId,
            Action<bool> completed)
        {
            foreach (var fact in facts ??
                     Array.Empty<OntologyAuthorityInitialFact>())
            {
                if (fact == null ||
                    string.IsNullOrWhiteSpace(fact.predicateId))
                {
                    continue;
                }

                OntologyAuthorityCommandResult result = null;
                yield return authorityClient
                    .SendCommandWithRevisionRetryRoutine(
                    () => OntologyWorldAuthorityClient.CreateCommand(
                        OntologyWorldCommandKinds.SetAuthoredFact,
                        OntologyWorldAuthorityClient.CreateFactPayload(
                            subjectEntityId,
                            fact)),
                    value => result = value);
                if (result == null ||
                    (!result.accepted &&
                     !string.Equals(
                         result.rejectionCode,
                         "fact_already_exists",
                         StringComparison.Ordinal)))
                {
                    completed?.Invoke(false);
                    yield break;
                }
            }

            completed?.Invoke(true);
        }

        private IEnumerator PublishRuleBindings(
            IReadOnlyList<OntologyRuleBlockBinding> bindings,
            Guid subjectEntityId,
            Action<bool> completed = null)
        {
            var succeeded = true;
            foreach (var binding in bindings ??
                     Array.Empty<OntologyRuleBlockBinding>())
            {
                if (binding == null ||
                    string.IsNullOrWhiteSpace(binding.ruleId))
                {
                    continue;
                }

                OntologyAuthorityCommandResult result = null;
                yield return authorityClient
                    .SendCommandWithRevisionRetryRoutine(
                    () => OntologyWorldAuthorityClient.CreateCommand(
                        OntologyWorldCommandKinds.AddRuleBlock,
                        OntologyWorldAuthorityClient.CreateRuleBlockPayload(
                            CreateRuleBindingId(
                                subjectEntityId,
                                binding.ruleId,
                                string.IsNullOrWhiteSpace(
                                    binding.bindingVariable)
                                    ? "?target"
                                    : binding.bindingVariable),
                            subjectEntityId,
                            binding.ruleId,
                            string.IsNullOrWhiteSpace(
                                binding.bindingVariable)
                                ? "?target"
                                : binding.bindingVariable,
                            ResolveRuleVersion(binding.ruleId))),
                    value => result = value);
                if (result == null ||
                    (!result.accepted &&
                     !string.Equals(
                         result.rejectionCode,
                         "rule_binding_already_exists",
                         StringComparison.Ordinal)))
                {
                    succeeded = false;
                    break;
                }
            }

            completed?.Invoke(succeeded);
        }

        private IEnumerator PublishSemanticContractMarker(
            Guid subjectEntityId,
            int contractVersion,
            OntologyAuthorityWorldProjection projection,
            Action<bool> completed)
        {
            contractVersion = Mathf.Max(1, contractVersion);
            var desired = contractVersion.ToString();
            var existing = projection?.facts?
                .Where(value =>
                    value != null &&
                    string.Equals(
                        value.subjectEntityId,
                        subjectEntityId.ToString("D"),
                        StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(
                        value.predicateId,
                        OntologyPredicates.SemanticContractVersion,
                        StringComparison.Ordinal))
                .ToArray() ??
                Array.Empty<OntologyAuthorityFactProjection>();
            if (existing.Any(value =>
                    string.Equals(
                        value.objectCanonicalId,
                        desired,
                        StringComparison.Ordinal)))
            {
                completed?.Invoke(true);
                yield break;
            }

            foreach (var marker in existing)
            {
                if (!Guid.TryParse(marker.factId, out var factId)) continue;
                OntologyAuthorityCommandResult retractResult = null;
                yield return authorityClient
                    .SendCommandWithRevisionRetryRoutine(
                    () => OntologyWorldAuthorityClient.CreateCommand(
                        OntologyWorldCommandKinds.RetractAuthoredFact,
                        OntologyWorldAuthorityClient
                            .CreateRetractFactPayload(factId)),
                    value => retractResult = value);
                if (retractResult == null ||
                    (!retractResult.accepted &&
                     !string.Equals(
                         retractResult.rejectionCode,
                         "fact_not_found",
                         StringComparison.Ordinal)))
                {
                    completed?.Invoke(false);
                    yield break;
                }
            }

            OntologyAuthorityCommandResult result = null;
            yield return authorityClient
                .SendCommandWithRevisionRetryRoutine(
                () => OntologyWorldAuthorityClient.CreateCommand(
                    OntologyWorldCommandKinds.SetAuthoredFact,
                    OntologyWorldAuthorityClient.CreateFactPayload(
                        subjectEntityId,
                        OntologyWorldAuthorityClient.CreateInitialFact(
                            OntologyPredicates.SemanticContractVersion,
                            desired))),
                value => result = value);
            completed?.Invoke(
                result != null &&
                (result.accepted ||
                 string.Equals(
                     result.rejectionCode,
                     "fact_already_exists",
                     StringComparison.Ordinal)));
        }

        private int ResolveSemanticContractVersion(
            string definitionId)
        {
            var definition = placementController?.Catalog?.Find(
                definitionId);
            return Mathf.Max(
                1,
                definition?.semanticContractVersion ?? 1);
        }

        public static Guid CreateRuleBindingId(
            Guid subjectEntityId,
            string ruleId,
            string bindingVariable)
        {
            var identity =
                subjectEntityId.ToString("D") + "\n" +
                (ruleId?.Trim() ?? string.Empty) + "\n" +
                (bindingVariable?.Trim() ?? string.Empty);
            using var sha256 = SHA256.Create();
            var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(identity));
            var guidBytes = new byte[16];
            Array.Copy(bytes, guidBytes, guidBytes.Length);
            guidBytes[7] =
                (byte)((guidBytes[7] & 0x0F) | 0x50);
            guidBytes[8] =
                (byte)((guidBytes[8] & 0x3F) | 0x80);
            return new Guid(guidBytes);
        }

        public static Guid CreateMeaningPackageApplicationId(
            Guid subjectEntityId,
            string slotId,
            string packageId)
        {
            var identity =
                "meaning_package\n" +
                subjectEntityId.ToString("D") + "\n" +
                (slotId?.Trim() ?? string.Empty) + "\n" +
                (packageId?.Trim() ?? string.Empty);
            using var sha256 = SHA256.Create();
            var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(identity));
            var guidBytes = new byte[16];
            Array.Copy(bytes, guidBytes, guidBytes.Length);
            guidBytes[7] =
                (byte)((guidBytes[7] & 0x0F) | 0x50);
            guidBytes[8] =
                (byte)((guidBytes[8] & 0x3F) | 0x80);
            return new Guid(guidBytes);
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
            var presentationEntityCount = 0;
            var createdPresentationCount = 0;
            var removedPresentationCount = 0;
            var durableTransformUpdateCount = 0;
            var dynamicTransformSeedCount = 0;
            var attachmentPresentationSyncCount = 0;
            var semanticChangedEntityCount = 0;
            var indexBuildObservation = default(OntologyRuntimePerformanceObservation);
            var presentationReconciliationObservation =
                default(OntologyRuntimePerformanceObservation);
            var semanticApplyObservation = default(OntologyRuntimePerformanceObservation);
            var sceneObjectSynchronizationObservation =
                default(OntologyRuntimePerformanceObservation);
            var simulationObservation = default(OntologyRuntimePerformanceObservation);
            var totalObservation = OntologyRuntimePerformanceObservation.Begin();
            try
            {
                indexBuildObservation = OntologyRuntimePerformanceObservation.Begin();
                projectionIndex.Rebuild(
                    projection,
                    FindObjectsByType<OntologyAuthorityEntityIdentity>(
                        FindObjectsInactive.Exclude));
                indexBuildObservation.Complete();

                var initialPresentationObservation =
                    OntologyRuntimePerformanceObservation.Begin();
                var changed = RemoveUnprojectedPlaceablePresentations(
                    projection,
                    out removedPresentationCount);

                // Phase 1 creates every projected entity before semantic entity
                // references are resolved. No Transform is applied yet because an
                // attachment relation in this same projection may transfer
                // presentation ownership away from the durable world Transform.
                foreach (var remote in projection.entities.Where(value =>
                             value != null && Guid.TryParse(value.entityId, out _)))
                {
                    projectionIndex.TryGetIdentity(remote.entityId, out var identity);
                    var createdFromProjection = false;
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
                        projectionIndex.SetIdentity(identity);
                        if (!string.IsNullOrWhiteSpace(projection.scopeZoneKey))
                        {
                            var presentation = created.GetComponent<OntologyAuthorityRemotePresentation>() ??
                                               created.gameObject.AddComponent<OntologyAuthorityRemotePresentation>();
                            presentation.SetScopeZoneKey(projection.scopeZoneKey);
                        }
                        changed = true;
                        createdFromProjection = true;
                        createdPresentationCount++;
                    }

                    if (identity != null)
                    {
                        presentationEntityCount++;
                        publishedEntityIds.Add(remote.entityId);
                        if (createdFromProjection)
                        {
                            dynamicProjectionSeeded.Add(remote.entityId);
                        }
                    }
                }
                initialPresentationObservation.Complete();
                presentationReconciliationObservation.AddCompleted(
                    initialPresentationObservation);

                semanticApplyObservation = OntologyRuntimePerformanceObservation.Begin();
                var semanticsChanged = ApplyAuthoritySemantics(
                    projection,
                    projectionIndex,
                    out semanticChangedEntityCount,
                    out var bootstrap);
                semanticApplyObservation.Complete();
                if (semanticsChanged && bootstrap != null)
                {
                    bootstrap.SynchronizeSceneObjects(runSimulation: true);
                    sceneObjectSynchronizationObservation =
                        bootstrap.LastSceneObjectSynchronizationObservation;
                    simulationObservation = bootstrap.LastSimulationObservation;
                }

                var finalPresentationObservation =
                    OntologyRuntimePerformanceObservation.Begin();
                attachmentPresentationSyncCount =
                    SynchronizeAttachmentPresentations();

                // Phase 2 applies durable world transforms only after semantic
                // presentation ownership has been resolved. An attached item is
                // controlled by its generic attachment adapter until the relation
                // is removed; it must never be pulled back to its stored ground
                // transform by a later projection.
                foreach (var remote in projection.entities.Where(value =>
                             value != null &&
                             value.transform != null &&
                             Guid.TryParse(value.entityId, out _)))
                {
                    projectionIndex.TryGetIdentity(remote.entityId, out var identity);
                    if (identity == null) continue;
                    var instance = identity.GetComponent<OntologyPlaceableInstance>();
                    var worldEditorOwnsTransform =
                        worldEditorController != null &&
                        worldEditorController.IsMoving &&
                        worldEditorController.Selected == instance;
                    var owner = OntologyTransformOwnershipResolver.Resolve(
                        identity,
                        localAvatarIdentity,
                        worldEditorOwnsTransform);
                    var seedDynamic = OntologyTransformOwnershipResolver
                        .ShouldSeedDynamicProjection(
                            owner,
                            dynamicProjectionSeeded.Contains(remote.entityId));
                    if (!OntologyTransformOwnershipResolver
                            .ShouldApplyDurableProjection(owner) &&
                        !seedDynamic)
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
                        durableTransformUpdateCount++;
                    }
                    if (seedDynamic)
                    {
                        var body = identity.GetComponent<Rigidbody>();
                        if (body != null)
                        {
                            body.position = position;
                            body.rotation = rotation;
                            body.linearVelocity = Vector3.zero;
                            body.angularVelocity = Vector3.zero;
                        }
                        dynamicProjectionSeeded.Add(remote.entityId);
                        dynamicTransformSeedCount++;
                    }
                }

                if (changed) Physics.SyncTransforms();
                finalPresentationObservation.Complete();
                presentationReconciliationObservation.AddCompleted(
                    finalPresentationObservation);
                if (semanticsChanged)
                {
                    SetStatus("Authority world data was updated from the latest server revision.");
                }
            }
            finally
            {
                totalObservation.Complete();
                lastProjectionApplyStatistics.Record(
                    projectionIndex,
                    presentationEntityCount,
                    createdPresentationCount,
                    removedPresentationCount,
                    durableTransformUpdateCount,
                    dynamicTransformSeedCount,
                    attachmentPresentationSyncCount,
                    semanticChangedEntityCount,
                    indexBuildObservation,
                    presentationReconciliationObservation,
                    semanticApplyObservation,
                    sceneObjectSynchronizationObservation,
                    simulationObservation,
                    totalObservation);
            }
        }

        /// <summary>
        /// A running Authority world may present only durable entities contained
        /// in its current projection. Local authoring previews are protected
        /// while their placement command is in flight; stale scene objects are
        /// disabled immediately and destroyed at the end of the frame.
        /// </summary>
        private bool RemoveUnprojectedPlaceablePresentations(
            OntologyAuthorityWorldProjection projection,
            out int removedPresentationCount)
        {
            removedPresentationCount = 0;
            if (!Application.isPlaying ||
                authorityClient == null ||
                !authorityClient.IsWorldRuntimeReady ||
                projection?.entities == null)
            {
                return false;
            }

            var removed = 0;
            foreach (var instance in FindObjectsByType<OntologyPlaceableInstance>(
                         FindObjectsInactive.Include))
            {
                if (instance == null) continue;
                var identity =
                    instance.GetComponent<OntologyAuthorityEntityIdentity>();
                if (identity == null ||
                    !identity.TryGetGuid(out var entityId))
                {
                    continue;
                }

                var entityKey = entityId.ToString("D");
                if (!ShouldRemoveUnprojectedPlaceablePresentation(
                        authorityClient.IsWorldRuntimeReady,
                        publishingEntityIds.Contains(entityKey),
                        projection,
                        identity))
                {
                    continue;
                }

                publishedEntityIds.Remove(entityKey);
                dynamicProjectionSeeded.Remove(entityKey);
                semanticProjectionInitialized.Remove(entityKey);
                instance.gameObject.SetActive(false);
                Destroy(instance.gameObject);
                removed++;
            }

            removedPresentationCount = removed;

            if (removed > 0)
            {
                Debug.LogWarning(
                    "[WorldAuthorityBridge] Removed " + removed +
                    " local placeable presentation(s) absent from the " +
                    "Authority projection.",
                    this);
            }
            return removed > 0;
        }

        private static int SynchronizeAttachmentPresentations()
        {
            var synchronized = 0;
            foreach (var attachment in FindObjectsByType<OntologyAttachmentAdapter>(
                         FindObjectsInactive.Include))
            {
                if (attachment != null && attachment.isActiveAndEnabled)
                {
                    attachment.SynchronizePresentation();
                    synchronized++;
                }
            }
            return synchronized;
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

        private bool ApplyAuthoritySemantics(
            OntologyAuthorityWorldProjection projection,
            OntologyAuthorityProjectionIndex index,
            out int changedEntityCount,
            out OntologyWorldBootstrap bootstrap)
        {
            changedEntityCount = 0;
            bootstrap = null;
            if (projection?.entities == null) return false;
            var changed = false;
            var changedObjects = new List<GameObject>();
            foreach (var remoteEntity in projection.entities.Where(value =>
                         value != null && Guid.TryParse(value.entityId, out _)))
            {
                index.TryGetIdentity(remoteEntity.entityId, out var identity);
                if (identity == null) continue;
                var factRows = index.GetFacts(remoteEntity.entityId);
                var ruleRows = index.GetEnabledRuleBindings(remoteEntity.entityId);
                var hasSemanticRows = factRows.Count > 0 || ruleRows.Count > 0;
                if (!hasSemanticRows &&
                    !semanticProjectionInitialized.Contains(remoteEntity.entityId))
                {
                    // An older entity may legitimately have no semantic rows.
                    // Keep catalog defaults until at least one authoritative
                    // semantic projection has been observed for that entity.
                    continue;
                }

                semanticProjectionInitialized.Add(remoteEntity.entityId);
                var ontology = identity.GetComponent<OntologyObject>() ??
                               identity.gameObject.AddComponent<OntologyObject>();
                var concepts = new List<string>();
                var facts = new List<OntologyFactEntry>();
                for (var factIndex = 0; factIndex < factRows.Count; factIndex++)
                {
                    var remoteFact = factRows[factIndex];
                    if (remoteFact.objectKind == "canonical" &&
                        remoteFact.predicateId == OntologyPredicates.HasConcept)
                    {
                        if (!string.IsNullOrWhiteSpace(remoteFact.objectCanonicalId))
                            concepts.Add(remoteFact.objectCanonicalId);
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(remoteFact.predicateId)) continue;
                    var value = ResolveRemoteFactObject(remoteFact, index);
                    if (string.IsNullOrWhiteSpace(value)) continue;
                    facts.Add(new OntologyFactEntry
                    {
                        predicate = remoteFact.predicateId,
                        obj = value
                    });
                }

                var assignment = identity.GetComponent<OntologyRuleBlockAssignment>();
                var semanticProjection = identity.GetComponent<
                    OntologyAuthoritySemanticProjection>() ??
                    identity.gameObject.AddComponent<
                        OntologyAuthoritySemanticProjection>();
                semanticProjection.Replace(factRows, projection.revision);
                var bindings = new List<OntologyRuleBlockBinding>(ruleRows.Count);
                for (var bindingIndex = 0;
                     bindingIndex < ruleRows.Count;
                     bindingIndex++)
                {
                    var value = ruleRows[bindingIndex];
                    if (string.IsNullOrWhiteSpace(value.ruleId))
                    {
                        continue;
                    }

                    bindings.Add(new OntologyRuleBlockBinding
                    {
                        bindingId = value.bindingId,
                        ruleId = value.ruleId,
                        ruleVersion = value.ruleVersion,
                        bindingVariable = ResolveBindingVariable(value.parameterValuesJson),
                        parameterValuesJson = value.parameterValuesJson,
                        applicationId = value.applicationId,
                        packageId = value.packageId,
                        slotId = value.slotId,
                        createdRevision = value.createdRevision
                    });
                }

                if (!SameOntologyData(ontology, concepts, facts) ||
                    !SameBindings(assignment, bindings))
                {
                    ontology.ReplaceFactsAndConcepts(concepts, facts);
                    if (assignment == null && bindings.Count > 0)
                        assignment = identity.gameObject.AddComponent<OntologyRuleBlockAssignment>();
                    assignment?.Replace(bindings);
                    changed = true;
                    changedObjects.Add(identity.gameObject);
                    changedEntityCount++;
                }
            }

            if (!changed) return false;
            bootstrap = FindAnyObjectByType<OntologyWorldBootstrap>();
            foreach (var changedObject in changedObjects)
            {
                OntologySemanticAdapterSynchronizer.SynchronizeAll(
                    changedObject, bootstrap);
            }
            return true;
        }

        private static string ResolveRemoteFactObject(
            OntologyAuthorityFactProjection remoteFact,
            OntologyAuthorityProjectionIndex index = null)
        {
            if (remoteFact.objectKind == "canonical")
                return remoteFact.objectCanonicalId;
            if (remoteFact.objectKind == "entity")
            {
                var identity = index != null &&
                               index.TryGetIdentity(
                                   remoteFact.objectEntityId,
                                   out var indexedIdentity)
                    ? indexedIdentity
                    : FindIdentity(remoteFact.objectEntityId);
                return identity == null
                    ? string.Empty
                    : identity.GetComponent<OntologyObject>()?.EntityId;
            }
            if (string.IsNullOrWhiteSpace(remoteFact.objectValueJson))
                return string.Empty;
            if (remoteFact.objectKind == "boolean")
            {
                return string.Equals(
                    remoteFact.objectValueJson,
                    "true",
                    StringComparison.OrdinalIgnoreCase)
                    ? bool.TrueString
                    : string.Equals(
                        remoteFact.objectValueJson,
                        "false",
                        StringComparison.OrdinalIgnoreCase)
                        ? bool.FalseString
                        : string.Empty;
            }
            if (remoteFact.objectKind == "text")
            {
                return JsonUtility.FromJson<JsonValueWrapper>(
                    "{\"value\":" + remoteFact.objectValueJson + "}")
                    ?.value ?? string.Empty;
            }
            return remoteFact.objectValueJson;
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
                    .Select(value => !string.IsNullOrWhiteSpace(value.bindingId)
                        ? "id:" + value.bindingId
                        : "semantic:" + (value.ruleId ?? string.Empty) + "\u001f" +
                          (value.bindingVariable ?? string.Empty)),
                    StringComparer.Ordinal);
            var next = new HashSet<string>(bindings.Where(value => value != null)
                .Select(value => !string.IsNullOrWhiteSpace(value.bindingId)
                    ? "id:" + value.bindingId
                    : "semantic:" + (value.ruleId ?? string.Empty) + "\u001f" +
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
                 (referenced == null &&
                  string.Equals(
                      ResolveRemoteFactObject(value),
                      objectValue,
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

        private static int ResolveSemanticContractVersion(
            OntologyAuthorityWorldProjection projection,
            string entityId)
        {
            return OntologyAuthorityProjectionSemantics
                .ResolveSemanticContractVersion(projection, entityId);
        }

        private static bool HasProjectedInitialFact(
            OntologyAuthorityWorldProjection projection,
            string entityId,
            OntologyAuthorityInitialFact expected)
        {
            return projection?.facts != null &&
                   expected != null &&
                   projection.facts.Any(value =>
                       MatchesProjectedInitialFact(
                           value,
                           entityId,
                           expected));
        }

        private static bool HasProjectedInitialFactConflict(
            OntologyAuthorityWorldProjection projection,
            string entityId,
            OntologyAuthorityInitialFact expected)
        {
            if (expected == null ||
                !Guid.TryParse(entityId, out var parsedEntityId))
            {
                return false;
            }

            var cardinality =
                OntologyLanguagePackService.Registry
                    .Find(expected.predicateId)
                    ?.Cardinality ??
                OntologyCardinalityKind.Unknown;
            var predicateAlreadyExists = HasProjectedPredicate(
                projection,
                parsedEntityId,
                expected.predicateId);
            var exactFactAlreadyExists = HasProjectedInitialFact(
                projection,
                entityId,
                expected);
            return !ShouldPublishLegacyInitialFact(
                cardinality,
                predicateAlreadyExists,
                exactFactAlreadyExists);
        }

        private static bool MatchesProjectedInitialFact(
            OntologyAuthorityFactProjection value,
            string entityId,
            OntologyAuthorityInitialFact expected)
        {
            return value != null &&
                   expected != null &&
                   string.Equals(
                       value.subjectEntityId,
                       entityId,
                       StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(
                       value.predicateId,
                       expected.predicateId,
                       StringComparison.Ordinal) &&
                   string.Equals(
                       value.objectKind,
                       expected.objectKind,
                       StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(
                       value.objectEntityId ?? string.Empty,
                       expected.objectEntityId ?? string.Empty,
                       StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(
                       value.objectCanonicalId ?? string.Empty,
                       expected.objectCanonicalId ?? string.Empty,
                       StringComparison.Ordinal) &&
                   string.Equals(
                       value.objectValueJson ?? string.Empty,
                       expected.objectValueJson ?? string.Empty,
                       StringComparison.Ordinal);
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
                placementController =
                    FindAnyObjectByType<OntologyRuntimeObjectPlacementController>(
                        FindObjectsInactive.Include);
            }
            if (worldEditorController == null)
            {
                worldEditorController =
                    FindAnyObjectByType<OntologyRuntimeWorldEditorController>(
                        FindObjectsInactive.Include);
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

            // Bootstrap services can be enabled before the additive World scene
            // creates its authoring controllers. Whenever a later projection or
            // interaction resolves those dependencies, immediately bind them.
            // The tracked subscription references make this operation idempotent.
            if (isActiveAndEnabled)
            {
                SubscribeDependencies();
            }
        }

        /// <summary>
        /// Resolves the central presentation ownership contract. Durable
        /// projection applies only while no higher-priority runtime adapter owns
        /// the Transform.
        /// </summary>
        public static bool ShouldApplyProjectedTransform(
            OntologyAuthorityEntityIdentity identity,
            OntologyAuthorityEntityIdentity localIdentity)
        {
            return OntologyTransformOwnershipResolver.ShouldApplyDurableProjection(
                OntologyTransformOwnershipResolver.Resolve(
                    identity,
                    localIdentity));
        }

        public static bool ShouldApplyProjectedTransform(
            OntologyAuthorityEntityIdentity identity,
            OntologyAuthorityEntityIdentity localIdentity,
            bool presentationOwnsTransform)
        {
            if (identity == null || presentationOwnsTransform)
            {
                return false;
            }

            return OntologyTransformOwnershipResolver.ShouldApplyDurableProjection(
                OntologyTransformOwnershipResolver.Resolve(
                    identity,
                    localIdentity));
        }

        private bool IsAutomaticAuthorityEnabled()
        {
            ResolveDependencies();
            return authorityClient != null &&
                   ShouldPublishAutomatically(
                       authorityClient.IsWorldRuntimeReady,
                       authorityClient.CanEditCurrentWorld);
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

        [Serializable]
        private sealed class JsonValueWrapper
        {
            public string value;
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
