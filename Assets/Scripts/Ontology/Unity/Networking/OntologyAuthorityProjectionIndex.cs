using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// A projection-local lookup index. This caches only rows already supplied by
    /// World Authority; it never evaluates triples, infers semantics, or grants
    /// a presentation permission. Rebuilding it for a newer projection is the
    /// invalidation boundary.
    /// </summary>
    public sealed class OntologyAuthorityProjectionIndex
    {
        private static readonly IReadOnlyList<OntologyAuthorityFactProjection>
            EmptyFacts = Array.Empty<OntologyAuthorityFactProjection>();
        private static readonly IReadOnlyList<OntologyAuthorityRuleBindingProjection>
            EmptyBindings = Array.Empty<OntologyAuthorityRuleBindingProjection>();

        private const int MaximumRetainedBucketsPerKind = 128;

        // Buckets are pooled only up to a bounded amount. Rebuild removes every
        // old subject/target key, so travelling through many Zones cannot retain
        // obsolete entity IDs and list instances indefinitely.
        private readonly Dictionary<string, List<OntologyAuthorityFactProjection>>
            factsBySubject = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<OntologyAuthorityRuleBindingProjection>>
            enabledBindingsByTarget = new(StringComparer.OrdinalIgnoreCase);
        private readonly Stack<List<OntologyAuthorityFactProjection>> factRowPool =
            new();
        private readonly Stack<List<OntologyAuthorityRuleBindingProjection>>
            enabledBindingRowPool = new();
        private readonly Dictionary<string, OntologyAuthorityEntityIdentity>
            identitiesByEntityId = new(StringComparer.OrdinalIgnoreCase);

        public int EntityCount { get; private set; }
        public int FactRowCount { get; private set; }
        public int RuleBindingRowCount { get; private set; }
        public int EnabledRuleBindingRowCount { get; private set; }
        public int IdentityCount => identitiesByEntityId.Count;
        public int FactSubjectBucketCount => factsBySubject.Count;
        public int BindingTargetBucketCount => enabledBindingsByTarget.Count;

        public void Rebuild(
            OntologyAuthorityWorldProjection projection,
            IReadOnlyList<OntologyAuthorityEntityIdentity> identities)
        {
            RecycleBuckets(factsBySubject, factRowPool);
            RecycleBuckets(enabledBindingsByTarget, enabledBindingRowPool);
            identitiesByEntityId.Clear();
            EntityCount = 0;
            FactRowCount = 0;
            RuleBindingRowCount = 0;
            EnabledRuleBindingRowCount = 0;

            if (projection?.entities != null)
            {
                for (var index = 0; index < projection.entities.Length; index++)
                {
                    var entity = projection.entities[index];
                    if (entity != null && Guid.TryParse(entity.entityId, out _))
                    {
                        EntityCount++;
                    }
                }
            }

            if (projection?.facts != null)
            {
                for (var index = 0; index < projection.facts.Length; index++)
                {
                    var fact = projection.facts[index];
                    if (fact == null ||
                        string.IsNullOrWhiteSpace(fact.subjectEntityId))
                    {
                        continue;
                    }

                    FactRowCount++;
                    GetOrCreateFactRows(fact.subjectEntityId).Add(fact);
                }
            }

            if (projection?.ruleBindings != null)
            {
                for (var index = 0; index < projection.ruleBindings.Length; index++)
                {
                    var binding = projection.ruleBindings[index];
                    if (binding == null)
                    {
                        continue;
                    }

                    RuleBindingRowCount++;
                    if (!binding.enabled ||
                        string.IsNullOrWhiteSpace(binding.targetEntityId))
                    {
                        continue;
                    }

                    EnabledRuleBindingRowCount++;
                    GetOrCreateEnabledBindingRows(
                        binding.targetEntityId).Add(binding);
                }
            }

            if (identities == null)
            {
                return;
            }

            for (var index = 0; index < identities.Count; index++)
            {
                SetIdentity(identities[index]);
            }
        }

        public IReadOnlyList<OntologyAuthorityFactProjection> GetFacts(
            string subjectEntityId)
        {
            return !string.IsNullOrWhiteSpace(subjectEntityId) &&
                   factsBySubject.TryGetValue(subjectEntityId, out var rows) &&
                   rows.Count > 0
                ? rows
                : EmptyFacts;
        }

        public IReadOnlyList<OntologyAuthorityRuleBindingProjection>
            GetEnabledRuleBindings(string targetEntityId)
        {
            return !string.IsNullOrWhiteSpace(targetEntityId) &&
                   enabledBindingsByTarget.TryGetValue(
                       targetEntityId,
                       out var rows) &&
                   rows.Count > 0
                ? rows
                : EmptyBindings;
        }

        public bool TryGetIdentity(
            string entityId,
            out OntologyAuthorityEntityIdentity identity)
        {
            identity = null;
            if (string.IsNullOrWhiteSpace(entityId) ||
                !identitiesByEntityId.TryGetValue(entityId, out var resolved) ||
                resolved == null)
            {
                return false;
            }

            identity = resolved;
            return true;
        }

        public void SetIdentity(OntologyAuthorityEntityIdentity identity)
        {
            if (identity == null ||
                !identity.TryGetGuid(out var entityId))
            {
                return;
            }

            identitiesByEntityId[entityId.ToString("D")] = identity;
        }

        private List<OntologyAuthorityFactProjection> GetOrCreateFactRows(
            string entityId)
        {
            if (!factsBySubject.TryGetValue(entityId, out var rows))
            {
                rows = factRowPool.Count > 0
                    ? factRowPool.Pop()
                    : new List<OntologyAuthorityFactProjection>(4);
                factsBySubject.Add(entityId, rows);
            }

            return rows;
        }

        private List<OntologyAuthorityRuleBindingProjection>
            GetOrCreateEnabledBindingRows(string entityId)
        {
            if (!enabledBindingsByTarget.TryGetValue(entityId, out var rows))
            {
                rows = enabledBindingRowPool.Count > 0
                    ? enabledBindingRowPool.Pop()
                    : new List<OntologyAuthorityRuleBindingProjection>(4);
                enabledBindingsByTarget.Add(entityId, rows);
            }

            return rows;
        }

        private static void RecycleBuckets<T>(
            Dictionary<string, List<T>> buckets,
            Stack<List<T>> pool)
        {
            foreach (var rows in buckets.Values)
            {
                rows.Clear();
                if (pool.Count < MaximumRetainedBucketsPerKind)
                {
                    pool.Push(rows);
                }
            }
            buckets.Clear();
        }
    }

    /// <summary>
    /// Thread-local runtime work observation. It is diagnostic-only and never
    /// participates in ontology evaluation, Authority decisions, or persistence.
    /// Unity 2021.2+ exposes exact current-thread allocations. Older supported
    /// Editors retain duration evidence and report allocation as zero rather
    /// than using a global heap sample that could misattribute another thread.
    /// </summary>
    [Serializable]
    public struct OntologyRuntimePerformanceObservation
    {
        [SerializeField] private long elapsedMicroseconds;
        [SerializeField] private long currentThreadAllocatedBytes;

        public long ElapsedMicroseconds => elapsedMicroseconds;
        public long CurrentThreadAllocatedBytes => currentThreadAllocatedBytes;

        public static OntologyRuntimePerformanceObservation Begin()
        {
            return new OntologyRuntimePerformanceObservation
            {
                elapsedMicroseconds = Stopwatch.GetTimestamp(),
                currentThreadAllocatedBytes = GetCurrentThreadAllocatedBytes()
            };
        }

        public void Complete()
        {
            var startedTimestamp = elapsedMicroseconds;
            var startedAllocatedBytes = currentThreadAllocatedBytes;
            elapsedMicroseconds = Math.Max(
                0L,
                (Stopwatch.GetTimestamp() - startedTimestamp) * 1_000_000L /
                Stopwatch.Frequency);
            currentThreadAllocatedBytes = Math.Max(
                0L,
                GetCurrentThreadAllocatedBytes() - startedAllocatedBytes);
        }

        /// <summary>
        /// Adds a completed, non-overlapping phase to this aggregate. This is
        /// used for a presentation phase that has a semantic synchronization
        /// boundary in the middle; nested simulation is intentionally kept in
        /// its own observation instead of being added here.
        /// </summary>
        public void AddCompleted(OntologyRuntimePerformanceObservation phase)
        {
            elapsedMicroseconds = Math.Max(
                0L,
                elapsedMicroseconds + phase.elapsedMicroseconds);
            currentThreadAllocatedBytes = Math.Max(
                0L,
                currentThreadAllocatedBytes + phase.currentThreadAllocatedBytes);
        }

        private static long GetCurrentThreadAllocatedBytes()
        {
#if UNITY_2021_2_OR_NEWER
            return GC.GetAllocatedBytesForCurrentThread();
#else
            return 0L;
#endif
        }
    }

    /// <summary>
    /// Ephemeral projection-application observability. These counters describe
    /// work already requested by Authority and do not become world Facts.
    /// </summary>
    [Serializable]
    public struct OntologyAuthorityProjectionApplyStatistics
    {
        [SerializeField] private int entityCount;
        [SerializeField] private int factRowCount;
        [SerializeField] private int ruleBindingRowCount;
        [SerializeField] private int enabledRuleBindingRowCount;
        [SerializeField] private int identityCount;
        [SerializeField] private int presentationEntityCount;
        [SerializeField] private int createdPresentationCount;
        [SerializeField] private int removedPresentationCount;
        [SerializeField] private int durableTransformUpdateCount;
        [SerializeField] private int dynamicTransformSeedCount;
        [SerializeField] private int attachmentPresentationSyncCount;
        [SerializeField] private int semanticChangedEntityCount;
        [SerializeField] private OntologyRuntimePerformanceObservation
            indexBuildObservation;
        [SerializeField] private OntologyRuntimePerformanceObservation
            presentationReconciliationObservation;
        [SerializeField] private OntologyRuntimePerformanceObservation
            semanticApplyObservation;
        [SerializeField] private OntologyRuntimePerformanceObservation
            sceneObjectSynchronizationObservation;
        [SerializeField] private OntologyRuntimePerformanceObservation
            simulationObservation;
        [SerializeField] private OntologyRuntimePerformanceObservation
            totalObservation;

        public int EntityCount => entityCount;
        public int FactRowCount => factRowCount;
        public int RuleBindingRowCount => ruleBindingRowCount;
        public int EnabledRuleBindingRowCount => enabledRuleBindingRowCount;
        public int IdentityCount => identityCount;
        public int PresentationEntityCount => presentationEntityCount;
        public int CreatedPresentationCount => createdPresentationCount;
        public int RemovedPresentationCount => removedPresentationCount;
        public int DurableTransformUpdateCount => durableTransformUpdateCount;
        public int DynamicTransformSeedCount => dynamicTransformSeedCount;
        public int AttachmentPresentationSyncCount => attachmentPresentationSyncCount;
        public int SemanticChangedEntityCount => semanticChangedEntityCount;
        public OntologyRuntimePerformanceObservation IndexBuildObservation =>
            indexBuildObservation;
        public OntologyRuntimePerformanceObservation
            PresentationReconciliationObservation =>
            presentationReconciliationObservation;
        public OntologyRuntimePerformanceObservation SemanticApplyObservation =>
            semanticApplyObservation;
        public OntologyRuntimePerformanceObservation
            SceneObjectSynchronizationObservation =>
            sceneObjectSynchronizationObservation;
        public OntologyRuntimePerformanceObservation SimulationObservation =>
            simulationObservation;
        public OntologyRuntimePerformanceObservation TotalObservation =>
            totalObservation;
        public long ElapsedMicroseconds => totalObservation.ElapsedMicroseconds;

        public void Record(
            OntologyAuthorityProjectionIndex index,
            int presentationEntities,
            int createdPresentations,
            int removedPresentations,
            int durableTransformUpdates,
            int dynamicTransformSeeds,
            int attachmentPresentationSynchronizations,
            int semanticChangedEntities,
            OntologyRuntimePerformanceObservation indexBuild,
            OntologyRuntimePerformanceObservation presentationReconciliation,
            OntologyRuntimePerformanceObservation semanticApply,
            OntologyRuntimePerformanceObservation sceneObjectSynchronization,
            OntologyRuntimePerformanceObservation simulation,
            OntologyRuntimePerformanceObservation total)
        {
            entityCount = index == null ? 0 : index.EntityCount;
            factRowCount = index == null ? 0 : index.FactRowCount;
            ruleBindingRowCount = index == null ? 0 : index.RuleBindingRowCount;
            enabledRuleBindingRowCount = index == null
                ? 0
                : index.EnabledRuleBindingRowCount;
            identityCount = index == null ? 0 : index.IdentityCount;
            presentationEntityCount = Math.Max(0, presentationEntities);
            createdPresentationCount = Math.Max(0, createdPresentations);
            removedPresentationCount = Math.Max(0, removedPresentations);
            durableTransformUpdateCount = Math.Max(0, durableTransformUpdates);
            dynamicTransformSeedCount = Math.Max(0, dynamicTransformSeeds);
            attachmentPresentationSyncCount = Math.Max(
                0,
                attachmentPresentationSynchronizations);
            semanticChangedEntityCount = Math.Max(0, semanticChangedEntities);
            indexBuildObservation = indexBuild;
            presentationReconciliationObservation = presentationReconciliation;
            semanticApplyObservation = semanticApply;
            sceneObjectSynchronizationObservation = sceneObjectSynchronization;
            simulationObservation = simulation;
            totalObservation = total;
        }
    }
}
