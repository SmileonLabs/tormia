using NUnit.Framework;
using Tormia.Ontology.Core;
using UnityEngine;

namespace Tormia.Ontology.Tests
{
    public sealed class OntologyPhysicsPresentationOwnershipTests
    {
        [Test]
        public void AttachmentReleaseRestoresMeaningOwnedSolidCollider()
        {
            var target = new GameObject("PhysicalMeaningReleaseContract");
            var profile = ScriptableObject.CreateInstance<OntologyPhysicalProfile>();
            try
            {
                target.AddComponent<OntologyObject>();
                var sourceCollider = target.AddComponent<BoxCollider>();
                var physical = target.AddComponent<OntologyPhysicalBodyAdapter>();
                var coordinator =
                    target.AddComponent<OntologyPhysicsPresentationCoordinator>();
                profile.motionDriver = OntologyMotionDriver.Rigidbody;
                profile.mobilityMode = OntologyPhysicalMobilityMode.Dynamic;
                profile.dynamicColliderMode = OntologyDynamicColliderMode.BoundsBox;

                physical.Configure(profile);
                coordinator.CaptureWorldBaseline();
                coordinator.SetAttachmentOverride(true, true, true);

                Assert.That(
                    target.GetComponentsInChildren<Collider>(true),
                    Has.All.Matches<Collider>(value => !value.enabled));

                coordinator.SetAttachmentOverride(false, true, true);

                Assert.That(sourceCollider.enabled, Is.False,
                    "BoundsBox meaning keeps the authored solid replaced.");
                Assert.That(
                    target.GetComponentsInChildren<Collider>(true),
                    Has.Some.Matches<Collider>(value =>
                        value.enabled && !value.isTrigger),
                    "Physical Meaning must restore its generated solid collider.");
                var body = target.GetComponent<Rigidbody>();
                Assert.That(body, Is.Not.Null);
                Assert.That(body.detectCollisions, Is.True);
                Assert.That(body.useGravity, Is.True);
                Assert.That(body.isKinematic, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void ColliderAddedDuringAttachmentJoinsTheTemporaryLease()
        {
            var target = new GameObject("AttachmentColliderTopologyContract");
            try
            {
                target.AddComponent<OntologyObject>();
                target.AddComponent<BoxCollider>();
                target.AddComponent<Rigidbody>();
                var coordinator =
                    target.AddComponent<OntologyPhysicsPresentationCoordinator>();
                coordinator.CaptureWorldBaseline();
                coordinator.SetAttachmentOverride(true, true, true);

                var addedDuringAttachment = target.AddComponent<SphereCollider>();
                coordinator.RefreshActiveAttachmentOverride();
                Assert.That(addedDuringAttachment.enabled, Is.False);

                coordinator.SetAttachmentOverride(false, true, true);
                Assert.That(addedDuringAttachment.enabled, Is.True,
                    "A collider introduced by semantic resynchronization must " +
                    "not remain disabled after attachment release.");
            }
            finally
            {
                Object.DestroyImmediate(target);
            }
        }
    }
}
