using System.IO;
using NUnit.Framework;
using Tormia.Ontology.Core;
using UnityEngine;

namespace Tormia.Ontology.Tests
{
    public sealed class OntologyEquipCandidateSelectionTests
    {
        [Test]
        public void InteractionDistanceUsesColliderSurfaceInsteadOfRootPivot()
        {
            var candidate = new GameObject("EquipCandidate");
            try
            {
                candidate.transform.position = new Vector3(10f, 0f, 0f);
                var collider = candidate.AddComponent<BoxCollider>();
                collider.size = new Vector3(18f, 2f, 2f);
                Physics.SyncTransforms();

                var surface =
                    OntologyCombatController.ResolveInteractionSurfacePoint(
                        Vector3.zero,
                        candidate.transform);
                var distanceSqr =
                    OntologyCombatController.ResolveInteractionSurfaceDistanceSqr(
                        Vector3.zero,
                        candidate.transform);

                Assert.That(surface.x, Is.EqualTo(1f).Within(0.001f));
                Assert.That(distanceSqr, Is.EqualTo(1f).Within(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(candidate);
            }
        }

        [Test]
        public void InteractionDistanceSelectsNearestEnabledChildCollider()
        {
            var candidate = new GameObject("EquipCandidate");
            try
            {
                var farChild = CreateColliderChild(
                    candidate.transform,
                    "FarCollider",
                    new Vector3(8f, 0f, 0f));
                var nearChild = CreateColliderChild(
                    candidate.transform,
                    "NearCollider",
                    new Vector3(2f, 0f, 0f));
                farChild.size = Vector3.one;
                nearChild.size = Vector3.one;
                Physics.SyncTransforms();

                var surface =
                    OntologyCombatController.ResolveInteractionSurfacePoint(
                        Vector3.zero,
                        candidate.transform);

                Assert.That(surface.x, Is.EqualTo(1.5f).Within(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(candidate);
            }
        }

        [Test]
        public void InteractionDistanceFallsBackToRootWhenNoColliderIsUsable()
        {
            var candidate = new GameObject("EquipCandidate");
            try
            {
                candidate.transform.position = new Vector3(4f, 1f, -2f);
                var collider = candidate.AddComponent<BoxCollider>();
                collider.enabled = false;

                Assert.That(
                    OntologyCombatController.ResolveInteractionSurfacePoint(
                        Vector3.zero,
                        candidate.transform),
                    Is.EqualTo(candidate.transform.position));
                Assert.That(
                    OntologyCombatController.ResolveInteractionSurfaceDistanceSqr(
                        Vector3.zero,
                        candidate.transform),
                    Is.EqualTo(candidate.transform.position.sqrMagnitude)
                        .Within(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(candidate);
            }
        }

        [Test]
        public void CandidatePipelineFiltersInvalidAndOccupiedSlotsBeforeOrdering()
        {
            var source = File.ReadAllText(
                "Assets/Scripts/Ontology/Unity/OntologyCombatController.cs");
            var methodStart = source.IndexOf(
                "FindNearestAvailableEquipCandidate()",
                System.StringComparison.Ordinal);
            var methodEnd = source.IndexOf(
                "private void RefreshEquipAvailabilityPresentation",
                methodStart,
                System.StringComparison.Ordinal);
            var method = source.Substring(methodStart, methodEnd - methodStart);

            var semanticFilter = method.IndexOf(
                "IsEnabledEquipmentActionTarget(candidate)",
                System.StringComparison.Ordinal);
            var ordering = method.IndexOf(
                "ResolveInteractionSurfaceDistanceSqr",
                System.StringComparison.Ordinal);

            Assert.That(semanticFilter, Is.GreaterThanOrEqualTo(0));
            Assert.That(ordering, Is.GreaterThan(semanticFilter),
                "An invalid nearest object must be filtered before distance ordering.");
            Assert.That(source, Does.Contain(
                "authorityClient.TryResolveEnabledAction(actionId, out _)"));
            Assert.That(source, Does.Contain(
                "IsCandidateSlotAvailable(candidate)"),
                "The canonical occupied-slot policy must be applied before prompting.");
        }

        [Test]
        public void StalledAuthorityProbeExpiresAndMayBeRetried()
        {
            Assert.That(
                OntologyCombatController.IsInteractionProbeExpired(
                    startedAt: 4f,
                    now: 4.99f,
                    timeoutSeconds: 1f),
                Is.False);
            Assert.That(
                OntologyCombatController.IsInteractionProbeExpired(
                    startedAt: 4f,
                    now: 5f,
                    timeoutSeconds: 1f),
                Is.True,
                "A stalled position request must not permanently lock the " +
                "equip availability pipeline.");
        }

        private static BoxCollider CreateColliderChild(
            Transform parent,
            string name,
            Vector3 localPosition)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent, false);
            child.transform.localPosition = localPosition;
            return child.AddComponent<BoxCollider>();
        }
    }
}
