using System;
using NUnit.Framework;
using Tormia.Ontology.Core;
using UnityEngine;

namespace Tormia.Ontology.Tests
{
    public sealed class OntologyAuthorityProjectionIndexTests
    {
        private const int ProjectionMeasurementEntityCount = 1000;
        private const int ProjectionMeasurementFactsPerEntity = 100;
        private const int ProjectionMeasurementBindingCount = 100;

        [Test]
        public void GroupsAuthorityRowsBySubjectAndKeepsOnlyEnabledBindings()
        {
            var firstId = Guid.NewGuid();
            var secondId = Guid.NewGuid();
            var projection = new OntologyAuthorityWorldProjection
            {
                entities = new[]
                {
                    Entity(firstId),
                    Entity(secondId)
                },
                facts = new[]
                {
                    Fact(firstId, "first"),
                    Fact(secondId, "second"),
                    Fact(firstId, "first-again")
                },
                ruleBindings = new[]
                {
                    Binding(firstId, "first-enabled", true),
                    Binding(firstId, "first-disabled", false),
                    Binding(secondId, "second-enabled", true)
                }
            };
            var index = new OntologyAuthorityProjectionIndex();

            index.Rebuild(projection, Array.Empty<OntologyAuthorityEntityIdentity>());

            Assert.That(index.EntityCount, Is.EqualTo(2));
            Assert.That(index.FactRowCount, Is.EqualTo(3));
            Assert.That(index.RuleBindingRowCount, Is.EqualTo(3));
            Assert.That(index.EnabledRuleBindingRowCount, Is.EqualTo(2));
            Assert.That(index.GetFacts(firstId.ToString("D")), Has.Count.EqualTo(2));
            Assert.That(index.GetFacts(secondId.ToString("D")), Has.Count.EqualTo(1));
            Assert.That(
                index.GetEnabledRuleBindings(firstId.ToString("D")),
                Has.Count.EqualTo(1));
            Assert.That(
                index.GetEnabledRuleBindings(firstId.ToString("D"))[0].ruleId,
                Is.EqualTo("first-enabled"));
        }

        [Test]
        public void RebuildRetractsPriorProjectionRowsInsteadOfServingStaleData()
        {
            var firstId = Guid.NewGuid();
            var secondId = Guid.NewGuid();
            var index = new OntologyAuthorityProjectionIndex();
            index.Rebuild(
                new OntologyAuthorityWorldProjection
                {
                    entities = new[] { Entity(firstId) },
                    facts = new[] { Fact(firstId, "first") },
                    ruleBindings = new[] { Binding(firstId, "first-rule", true) }
                },
                Array.Empty<OntologyAuthorityEntityIdentity>());

            Assert.That(index.FactSubjectBucketCount, Is.EqualTo(1));
            Assert.That(index.BindingTargetBucketCount, Is.EqualTo(1));

            index.Rebuild(
                new OntologyAuthorityWorldProjection
                {
                    entities = new[] { Entity(secondId) },
                    facts = new[] { Fact(secondId, "second") },
                    ruleBindings = Array.Empty<OntologyAuthorityRuleBindingProjection>()
                },
                Array.Empty<OntologyAuthorityEntityIdentity>());

            Assert.That(
                index.FactSubjectBucketCount,
                Is.EqualTo(1),
                "Rebuild must remove keys from the prior projection instead of " +
                "retaining every Zone's subject bucket.");
            Assert.That(index.BindingTargetBucketCount, Is.EqualTo(0));
            Assert.That(index.GetFacts(firstId.ToString("D")), Is.Empty);
            Assert.That(
                index.GetEnabledRuleBindings(firstId.ToString("D")),
                Is.Empty);
            Assert.That(index.GetFacts(secondId.ToString("D")), Has.Count.EqualTo(1));
        }

        [Test]
        public void RebuildIndexesKnownPresentationIdentityByAuthorityId()
        {
            var entityId = Guid.NewGuid();
            var target = new GameObject("ProjectionIndexIdentity");
            try
            {
                var identity = target.AddComponent<OntologyAuthorityEntityIdentity>();
                identity.SetGuid(entityId);
                var index = new OntologyAuthorityProjectionIndex();

                index.Rebuild(
                    new OntologyAuthorityWorldProjection
                    {
                        entities = new[] { Entity(entityId) }
                    },
                    new[] { identity });

                Assert.That(
                    index.TryGetIdentity(entityId.ToString("D"), out var resolved),
                    Is.True);
                Assert.That(resolved, Is.SameAs(identity));
                Assert.That(index.IdentityCount, Is.EqualTo(1));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void ApplyStatisticsNameSemanticAndPresentationWorkSeparately()
        {
            var entityId = Guid.NewGuid();
            var index = new OntologyAuthorityProjectionIndex();
            index.Rebuild(
                new OntologyAuthorityWorldProjection
                {
                    entities = new[] { Entity(entityId) },
                    facts = new[] { Fact(entityId, "meaning") }
                },
                Array.Empty<OntologyAuthorityEntityIdentity>());
            var statistics = new OntologyAuthorityProjectionApplyStatistics();

            var indexBuild = OntologyRuntimePerformanceObservation.Begin();
            indexBuild.Complete();
            var presentation = OntologyRuntimePerformanceObservation.Begin();
            presentation.Complete();
            var semantic = OntologyRuntimePerformanceObservation.Begin();
            semantic.Complete();
            var synchronization = OntologyRuntimePerformanceObservation.Begin();
            synchronization.Complete();
            var simulation = OntologyRuntimePerformanceObservation.Begin();
            simulation.Complete();
            var total = OntologyRuntimePerformanceObservation.Begin();
            total.Complete();

            statistics.Record(
                index,
                presentationEntities: 1,
                createdPresentations: 1,
                removedPresentations: 2,
                durableTransformUpdates: 3,
                dynamicTransformSeeds: 4,
                attachmentPresentationSynchronizations: 5,
                semanticChangedEntities: 6,
                indexBuild: indexBuild,
                presentationReconciliation: presentation,
                semanticApply: semantic,
                sceneObjectSynchronization: synchronization,
                simulation: simulation,
                total: total);

            Assert.That(statistics.EntityCount, Is.EqualTo(1));
            Assert.That(statistics.PresentationEntityCount, Is.EqualTo(1));
            Assert.That(statistics.CreatedPresentationCount, Is.EqualTo(1));
            Assert.That(statistics.RemovedPresentationCount, Is.EqualTo(2));
            Assert.That(statistics.DurableTransformUpdateCount, Is.EqualTo(3));
            Assert.That(statistics.DynamicTransformSeedCount, Is.EqualTo(4));
            Assert.That(statistics.AttachmentPresentationSyncCount, Is.EqualTo(5));
            Assert.That(statistics.SemanticChangedEntityCount, Is.EqualTo(6));
            Assert.That(
                statistics.IndexBuildObservation.ElapsedMicroseconds,
                Is.GreaterThanOrEqualTo(0));
            Assert.That(
                statistics.PresentationReconciliationObservation.CurrentThreadAllocatedBytes,
                Is.GreaterThanOrEqualTo(0));
            Assert.That(
                statistics.SemanticApplyObservation.ElapsedMicroseconds,
                Is.GreaterThanOrEqualTo(0));
            Assert.That(
                statistics.SceneObjectSynchronizationObservation.ElapsedMicroseconds,
                Is.GreaterThanOrEqualTo(0));
            Assert.That(
                statistics.SimulationObservation.ElapsedMicroseconds,
                Is.GreaterThanOrEqualTo(0));
            Assert.That(statistics.ElapsedMicroseconds, Is.EqualTo(
                statistics.TotalObservation.ElapsedMicroseconds));
        }

        [Test]
        public void BootstrapRecordsSynchronizationAndSimulationAsEphemeralObservations()
        {
            var host = new GameObject("PerformanceObservationBootstrap");
            try
            {
                var bootstrap = host.AddComponent<OntologyWorldBootstrap>();

                bootstrap.SynchronizeSceneObjects(runSimulation: true);

                Assert.That(
                    bootstrap.LastSceneObjectSynchronizationObservation
                        .ElapsedMicroseconds,
                    Is.GreaterThanOrEqualTo(0));
                Assert.That(
                    bootstrap.LastSceneObjectSynchronizationObservation
                        .CurrentThreadAllocatedBytes,
                    Is.GreaterThanOrEqualTo(0));
                Assert.That(
                    bootstrap.LastSimulationObservation.ElapsedMicroseconds,
                    Is.GreaterThanOrEqualTo(0));
                Assert.That(
                    bootstrap.LastSimulationObservation.CurrentThreadAllocatedBytes,
                    Is.GreaterThanOrEqualTo(0));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        [Explicit(
            "Scale measurement; run explicitly when collecting Projection index evidence.")]
        public void MeasureProjectionIndexBuildAtRepresentativeScale()
        {
            var entityCount = ReadScale(
                "TOV_PROJECTION_MEASURE_ENTITIES",
                ProjectionMeasurementEntityCount);
            var factsPerEntity = ReadScale(
                "TOV_PROJECTION_MEASURE_FACTS_PER_ENTITY",
                ProjectionMeasurementFactsPerEntity);
            var bindingCount = ReadScale(
                "TOV_PROJECTION_MEASURE_BINDINGS",
                ProjectionMeasurementBindingCount);
            var projection = CreateProjection(
                entityCount,
                factsPerEntity,
                bindingCount);
            var index = new OntologyAuthorityProjectionIndex();

            // Compile and initialize the empty rebuild path before observing the
            // first real Projection. The measured rebuild still includes all
            // entity, fact, binding, dictionary, and bucket allocations.
            index.Rebuild(
                new OntologyAuthorityWorldProjection
                {
                    entities = Array.Empty<OntologyAuthorityEntityProjection>(),
                    facts = Array.Empty<OntologyAuthorityFactProjection>(),
                    ruleBindings = Array.Empty<OntologyAuthorityRuleBindingProjection>()
                },
                Array.Empty<OntologyAuthorityEntityIdentity>());

            var allocatedBytesBefore = GC.GetAllocatedBytesForCurrentThread();
            var observation = OntologyRuntimePerformanceObservation.Begin();
            index.Rebuild(
                projection,
                Array.Empty<OntologyAuthorityEntityIdentity>());
            observation.Complete();
            var currentThreadAllocatedBytes = Math.Max(
                0L,
                GC.GetAllocatedBytesForCurrentThread() - allocatedBytesBefore);

            Assert.That(index.EntityCount, Is.EqualTo(entityCount));
            Assert.That(index.FactRowCount, Is.EqualTo(entityCount * factsPerEntity));
            Assert.That(index.RuleBindingRowCount, Is.EqualTo(bindingCount));
            Assert.That(index.EnabledRuleBindingRowCount, Is.EqualTo(bindingCount));
            Assert.That(index.FactSubjectBucketCount, Is.EqualTo(entityCount));
            Assert.That(
                index.BindingTargetBucketCount,
                Is.EqualTo(Math.Min(entityCount, bindingCount)));
            Assert.That(observation.ElapsedMicroseconds, Is.GreaterThanOrEqualTo(0));
            Assert.That(
                currentThreadAllocatedBytes,
                Is.GreaterThanOrEqualTo(0));
            TestContext.Out.WriteLine(
                "projectionIndexBuild entities={0} facts={1} bindings={2} " +
                "factBuckets={3} bindingBuckets={4} elapsedUs={5} " +
                "currentThreadAllocatedBytes={6} runtimeObservationAllocatedBytes={7}",
                entityCount,
                entityCount * factsPerEntity,
                bindingCount,
                index.FactSubjectBucketCount,
                index.BindingTargetBucketCount,
                observation.ElapsedMicroseconds,
                currentThreadAllocatedBytes,
                observation.CurrentThreadAllocatedBytes);

            var replacement = CreateProjection(
                entityCount: 3,
                factsPerEntity: 2,
                bindingCount: 2);
            var staleSubject = projection.entities[0].entityId;

            index.Rebuild(
                replacement,
                Array.Empty<OntologyAuthorityEntityIdentity>());

            Assert.That(index.EntityCount, Is.EqualTo(3));
            Assert.That(index.FactRowCount, Is.EqualTo(6));
            Assert.That(index.RuleBindingRowCount, Is.EqualTo(2));
            Assert.That(
                index.FactSubjectBucketCount,
                Is.EqualTo(3),
                "Stale Projection subject keys must not survive replacement.");
            Assert.That(
                index.BindingTargetBucketCount,
                Is.EqualTo(2),
                "Stale Projection binding keys must not survive replacement.");
            Assert.That(index.GetFacts(staleSubject), Is.Empty);
        }

        private static OntologyAuthorityEntityProjection Entity(Guid id) =>
            new() { entityId = id.ToString("D") };

        private static OntologyAuthorityFactProjection Fact(Guid subjectId, string value) =>
            new()
            {
                subjectEntityId = subjectId.ToString("D"),
                predicateId = "test_predicate",
                objectKind = "canonical",
                objectCanonicalId = value
            };

        private static OntologyAuthorityRuleBindingProjection Binding(
            Guid entityId,
            string ruleId,
            bool enabled) =>
            new()
            {
                bindingId = Guid.NewGuid().ToString("D"),
                targetEntityId = entityId.ToString("D"),
                ruleId = ruleId,
                enabled = enabled
            };

        private static OntologyAuthorityWorldProjection CreateProjection(
            int entityCount,
            int factsPerEntity,
            int bindingCount)
        {
            var entities = new OntologyAuthorityEntityProjection[entityCount];
            var facts = new OntologyAuthorityFactProjection[
                entityCount * factsPerEntity];
            var bindings = new OntologyAuthorityRuleBindingProjection[bindingCount];

            for (var entityIndex = 0; entityIndex < entityCount; entityIndex++)
            {
                var entityId = Guid.NewGuid();
                entities[entityIndex] = Entity(entityId);
                for (var factIndex = 0; factIndex < factsPerEntity; factIndex++)
                {
                    facts[entityIndex * factsPerEntity + factIndex] = Fact(
                        entityId,
                        "measure_" + factIndex);
                }
            }

            for (var bindingIndex = 0; bindingIndex < bindingCount; bindingIndex++)
            {
                var targetEntity = entities[bindingIndex % entityCount];
                bindings[bindingIndex] = new OntologyAuthorityRuleBindingProjection
                {
                    bindingId = Guid.NewGuid().ToString("D"),
                    targetEntityId = targetEntity.entityId,
                    ruleId = "measure_rule_" + bindingIndex,
                    enabled = true
                };
            }

            return new OntologyAuthorityWorldProjection
            {
                entities = entities,
                facts = facts,
                ruleBindings = bindings
            };
        }

        private static int ReadScale(string variable, int fallback)
        {
            var raw = Environment.GetEnvironmentVariable(variable);
            return int.TryParse(raw, out var value) && value > 0
                ? value
                : fallback;
        }
    }
}
