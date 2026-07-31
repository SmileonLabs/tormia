using System.Collections;
using NUnit.Framework;
using Tormia.Ontology.Core;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tormia.Ontology.Tests
{
    public sealed class OntologyHybridCharacterMotionTests
    {
        [UnityTest]
        public IEnumerator ApprovedParabolicJumpUsesOneControllerMoveAndLands()
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var avatar = new GameObject("HybridJumpAvatar");
            try
            {
                ground.transform.position = new Vector3(0f, -0.5f, 0f);
                ground.transform.localScale = new Vector3(20f, 1f, 20f);
                avatar.transform.position = new Vector3(0f, 0.02f, 0f);
                var controller = avatar.AddComponent<CharacterController>();
                controller.height = 2f;
                controller.center = Vector3.up;
                controller.radius = 0.35f;
                controller.Move(Vector3.down * 0.05f);
                Physics.SyncTransforms();

                const float step = 0.02f;
                const float gravity = -19.62f;
                const float groundStick = -1f;
                var verticalVelocity = 7f;
                var peak = avatar.transform.position.y;
                var landed = false;
                var landingCount = 0;

                for (var index = 0; index < 160; index++)
                {
                    var groundedBeforeMove =
                        controller.isGrounded &&
                        verticalVelocity <= 0f;
                    verticalVelocity =
                        OntologyInputSystemPlayerInput
                            .IntegrateVerticalVelocity(
                                groundedBeforeMove,
                                verticalVelocity,
                                gravity,
                                step,
                                groundStick);
                    var flags = controller.Move(
                        Vector3.up * verticalVelocity * step);
                    verticalVelocity =
                        OntologyInputSystemPlayerInput
                            .ResolveVerticalVelocityAfterMove(
                                flags,
                                verticalVelocity,
                                groundStick);
                    peak = Mathf.Max(peak, avatar.transform.position.y);

                    var groundedAfterMove =
                        (flags & CollisionFlags.Below) != 0 ||
                        controller.isGrounded;
                    if (groundedAfterMove && index > 1)
                    {
                        if (!landed) landingCount++;
                        landed = true;
                    }

                    yield return new WaitForFixedUpdate();
                }

                Assert.That(peak, Is.GreaterThan(1f));
                Assert.That(landed, Is.True);
                Assert.That(landingCount, Is.EqualTo(1));
                Assert.That(
                    avatar.transform.position.y,
                    Is.EqualTo(controller.skinWidth).Within(0.03f));
            }
            finally
            {
                Object.Destroy(avatar);
                Object.Destroy(ground);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator ApprovedJumpCreatesOneOccurrenceAndConsumesSupport()
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var avatar = new GameObject("ApprovedJumpOccurrenceAvatar");
            try
            {
                ground.transform.position =
                    new Vector3(0f, -0.5f, 0f);
                ground.transform.localScale =
                    new Vector3(20f, 1f, 20f);
                ground
                    .AddComponent<OntologyCollisionRoleAdapter>()
                    .Configure(OntologyCollisionRole.WalkableSupport);
                avatar.transform.position =
                    new Vector3(0f, 0.02f, 0f);
                var controller =
                    avatar.AddComponent<CharacterController>();
                controller.height = 2f;
                controller.center = Vector3.up;
                controller.radius = 0.35f;
                var input =
                    avatar.AddComponent<
                        OntologyInputSystemPlayerInput>();
                var coordinator =
                    avatar.GetComponent<
                        OntologyCharacterMotionCoordinator>();
                avatar
                    .AddComponent<OntologyMotionDriverAdapter>()
                    .Configure(
                        OntologyMotionDriver.LocalCharacterController);
                Physics.SyncTransforms();
                coordinator.Move(
                    Vector3.zero,
                    -0.05f,
                    Vector3.zero);
                input.ResetVerticalMotionAfterGrounding();

                Assert.That(input.IsGrounded, Is.True);
                Assert.That(input.JumpOccurrence, Is.EqualTo(0u));
                Assert.That(input.ApplyApprovedJump(7f), Is.True);
                Assert.That(input.JumpOccurrence, Is.EqualTo(1u));
                Assert.That(
                    input.IsGrounded,
                    Is.False,
                    "Positive takeoff velocity must immediately consume the " +
                    "support observation even if CharacterController still " +
                    "reports the takeoff-frame contact.");
                Assert.That(
                    input.ApplyApprovedJump(7f),
                    Is.False,
                    "A delayed duplicate approval cannot create a second jump.");
                Assert.That(input.JumpOccurrence, Is.EqualTo(1u));
            }
            finally
            {
                Object.Destroy(avatar);
                Object.Destroy(ground);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator ActorBodyCannotBeUsedAsACharacterStep()
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var actorBody = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var avatar = new GameObject("CollisionRoleStepPolicyAvatar");
            try
            {
                ground.transform.position = new Vector3(0f, -0.5f, 0f);
                ground.transform.localScale = new Vector3(20f, 1f, 20f);
                ground
                    .AddComponent<OntologyCollisionRoleAdapter>()
                    .Configure(OntologyCollisionRole.WalkableSupport);
                actorBody.transform.position = new Vector3(0.8f, 0.2f, 0f);
                actorBody.transform.localScale = new Vector3(0.6f, 0.4f, 1f);
                actorBody
                    .AddComponent<OntologyCollisionRoleAdapter>()
                    .Configure(OntologyCollisionRole.ActorBody);
                avatar.transform.position = new Vector3(0f, 0.02f, 0f);

                var controller = avatar.AddComponent<CharacterController>();
                controller.height = 2f;
                controller.center = Vector3.up;
                controller.radius = 0.35f;
                controller.stepOffset = 0.3f;
                controller.skinWidth = 0.035f;
                var coordinator =
                    avatar.AddComponent<OntologyCharacterMotionCoordinator>();
                avatar
                    .AddComponent<OntologyMotionDriverAdapter>()
                    .Configure(
                        OntologyMotionDriver.LocalCharacterController);
                Physics.SyncTransforms();
                coordinator.Move(Vector3.zero, -0.05f, Vector3.zero);
                coordinator.RefreshSupport();

                var startY = avatar.transform.position.y;
                for (var index = 0; index < 30; index++)
                {
                    var result = coordinator.Move(
                        Vector3.right * 0.04f,
                        -0.02f,
                        Vector3.zero);
                    Assert.That(
                        result.resolvedStepRise,
                        Is.EqualTo(0f).Within(0.0001f),
                        "An ActorBody collision role is an obstacle, never a " +
                        "walkable step.");
                    yield return new WaitForFixedUpdate();
                }

                Assert.That(controller.stepOffset, Is.EqualTo(0f));
                Assert.That(
                    avatar.transform.position.y,
                    Is.LessThanOrEqualTo(startY + 0.01f));
            }
            finally
            {
                Object.Destroy(avatar);
                Object.Destroy(actorBody);
                Object.Destroy(ground);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator GroundAdhesionOnWalkableSlopeDoesNotCreatePlanarDrift()
        {
            var slope = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var avatar = new GameObject("SlopeAdhesionAvatar");
            try
            {
                slope.name = "WalkableSlope";
                slope.transform.position = new Vector3(0f, -0.5f, 0f);
                slope.transform.rotation = Quaternion.Euler(12f, 0f, 0f);
                slope.transform.localScale = new Vector3(20f, 1f, 20f);
                slope
                    .AddComponent<OntologyCollisionRoleAdapter>()
                    .Configure(OntologyCollisionRole.WalkableSupport);

                avatar.transform.position = new Vector3(0f, 3f, 0f);
                var controller = avatar.AddComponent<CharacterController>();
                controller.height = 2f;
                controller.center = Vector3.up;
                controller.radius = 0.35f;
                controller.skinWidth = 0.035f;
                var coordinator =
                    avatar.AddComponent<OntologyCharacterMotionCoordinator>();
                avatar
                    .AddComponent<OntologyMotionDriverAdapter>()
                    .Configure(
                        OntologyMotionDriver.LocalCharacterController);

                Physics.SyncTransforms();
                Assert.That(
                    coordinator.TrySettleToSupport(5f, 0.5f, 0.01f),
                    Is.True);
                var settled = avatar.transform.position;
                var groundedBeforeFrames = 0;
                var supportBeforeFrames = 0;
                var belowAfterFrames = 0;
                var largestFrameDrift = 0f;

                for (var index = 0; index < 60; index++)
                {
                    if (controller.isGrounded)
                    {
                        groundedBeforeFrames++;
                    }
                    var before = avatar.transform.position;
                    var result = coordinator.Move(
                        Vector3.zero,
                        -0.02f,
                        Vector3.zero);
                    if (result.supportBefore.hasSupport)
                    {
                        supportBeforeFrames++;
                    }
                    if ((result.collisionFlags & CollisionFlags.Below) != 0)
                    {
                        belowAfterFrames++;
                    }
                    largestFrameDrift = Mathf.Max(
                        largestFrameDrift,
                        Vector3.ProjectOnPlane(
                            avatar.transform.position - before,
                            Vector3.up).magnitude);
                    yield return new WaitForFixedUpdate();
                }

                var planarDrift = Vector3.ProjectOnPlane(
                    avatar.transform.position - settled,
                    Vector3.up);
                Assert.That(
                    planarDrift.magnitude,
                    Is.LessThan(0.01f),
                    "Ground adhesion must remain world-vertical; projecting " +
                    "adhesion into the support normal creates planar drift. " +
                    "groundedBefore=" + groundedBeforeFrames +
                    ", supportBefore=" + supportBeforeFrames +
                    ", belowAfter=" + belowAfterFrames +
                    ", largestFrameDrift=" + largestFrameDrift);
            }
            finally
            {
                Object.Destroy(avatar);
                Object.Destroy(slope);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator WalkableSlopeIsNotMisclassifiedAsExplicitStep()
        {
            var slope = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var avatar = new GameObject("SlopeStepPolicyAvatar");
            try
            {
                slope.name = "WalkableSlope";
                slope.transform.position = new Vector3(0f, -0.5f, 0f);
                slope.transform.rotation = Quaternion.Euler(12f, 0f, 0f);
                slope.transform.localScale = new Vector3(20f, 1f, 20f);
                slope
                    .AddComponent<OntologyCollisionRoleAdapter>()
                    .Configure(OntologyCollisionRole.WalkableSupport);

                avatar.transform.position = new Vector3(0f, 3f, 0f);
                var controller = avatar.AddComponent<CharacterController>();
                controller.height = 2f;
                controller.center = Vector3.up;
                controller.radius = 0.35f;
                controller.skinWidth = 0.035f;
                var coordinator =
                    avatar.AddComponent<OntologyCharacterMotionCoordinator>();
                avatar
                    .AddComponent<OntologyMotionDriverAdapter>()
                    .Configure(
                        OntologyMotionDriver.LocalCharacterController);

                Physics.SyncTransforms();
                Assert.That(
                    coordinator.TrySettleToSupport(5f, 0.5f, 0.01f),
                    Is.True);

                for (var index = 0; index < 30; index++)
                {
                    var result = coordinator.Move(
                        Vector3.forward * 0.04f,
                        -0.02f,
                        Vector3.zero);
                    Assert.That(
                        result.resolvedStepRise,
                        Is.EqualTo(0f).Within(0.0001f),
                        "A surface inside CharacterController.slopeLimit is " +
                        "continuous walkable support, not an explicit step.");
                    yield return new WaitForFixedUpdate();
                }
            }
            finally
            {
                Object.Destroy(avatar);
                Object.Destroy(slope);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator WalkableSlopeDoesNotInjectVerticalDisplacement()
        {
            var slope = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var avatar = new GameObject("SlopeVerticalInjectionAvatar");
            try
            {
                slope.name = "FacetedWalkableSlope";
                slope.transform.position = new Vector3(0f, -0.5f, 0f);
                slope.transform.rotation = Quaternion.Euler(12f, 0f, 0f);
                slope.transform.localScale = new Vector3(20f, 1f, 20f);
                slope
                    .AddComponent<OntologyCollisionRoleAdapter>()
                    .Configure(OntologyCollisionRole.WalkableSupport);

                avatar.transform.position = new Vector3(0f, 3f, 0f);
                var controller = avatar.AddComponent<CharacterController>();
                controller.height = 2f;
                controller.center = Vector3.up;
                controller.radius = 0.35f;
                controller.skinWidth = 0.035f;
                var coordinator =
                    avatar.AddComponent<OntologyCharacterMotionCoordinator>();
                avatar
                    .AddComponent<OntologyMotionDriverAdapter>()
                    .Configure(
                        OntologyMotionDriver.LocalCharacterController);

                Physics.SyncTransforms();
                Assert.That(
                    coordinator.TrySettleToSupport(5f, 0.5f, 0.01f),
                    Is.True);

                const float downwardDisplacement = -0.02f;
                coordinator.Move(
                    Vector3.forward * 0.04f,
                    downwardDisplacement,
                    Vector3.zero);

                Assert.That(
                    coordinator.LastRequestedDisplacement.y,
                    Is.EqualTo(downwardDisplacement).Within(0.0001f),
                    "A sampled support normal is observation evidence only; " +
                    "it must not manufacture vertical locomotion.");
            }
            finally
            {
                Object.Destroy(avatar);
                Object.Destroy(slope);
            }

            yield return null;
        }

        [Test]
        public void CurrentSupportColliderCannotBecomeItsOwnStepObstacle()
        {
            var support = new GameObject("ContinuousSupport");
            var separateStep = new GameObject("SeparateStep");
            try
            {
                var supportCollider = support.AddComponent<BoxCollider>();
                var stepCollider = separateStep.AddComponent<BoxCollider>();
                const float minimumUpwardNormal = 0.7f;

                Assert.That(
                    OntologyCharacterSupportProbe.CanTreatAsStepObstacle(
                        supportCollider,
                        supportCollider,
                        true,
                        Vector3.forward,
                        minimumUpwardNormal),
                    Is.False,
                    "A triangle or vertical face on the current terrain " +
                    "collider is continuous support, not a step command.");
                Assert.That(
                    OntologyCharacterSupportProbe.CanTreatAsStepObstacle(
                        supportCollider,
                        stepCollider,
                        true,
                        Vector3.forward,
                        minimumUpwardNormal),
                    Is.True,
                    "A separate walkable collider can still be resolved as a " +
                    "real discrete step.");
            }
            finally
            {
                Object.DestroyImmediate(separateStep);
                Object.DestroyImmediate(support);
            }
        }

        [Test]
        public void UnauthoredStaticColliderIsNotImplicitWalkableSupport()
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                Assert.That(
                    OntologyCollisionRoleAdapter.IsWalkableSupport(
                        ground.GetComponent<Collider>()),
                    Is.False,
                    "A static Unity collider must not acquire ontology " +
                    "WalkableSupport meaning from the absence of a Rigidbody.");

                ground
                    .AddComponent<OntologyCollisionRoleAdapter>()
                    .Configure(OntologyCollisionRole.WalkableSupport);
                Assert.That(
                    OntologyCollisionRoleAdapter.IsWalkableSupport(
                        ground.GetComponent<Collider>()),
                    Is.True);
            }
            finally
            {
                Object.DestroyImmediate(ground);
            }
        }

        [UnityTest]
        public IEnumerator SupportProximityBeforeLandingCannotCreateStepRise()
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var step = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var avatar = new GameObject("AirborneStepPolicyAvatar");
            try
            {
                ground.transform.position = new Vector3(0f, -0.5f, 0f);
                ground.transform.localScale = new Vector3(20f, 1f, 20f);
                ground
                    .AddComponent<OntologyCollisionRoleAdapter>()
                    .Configure(OntologyCollisionRole.WalkableSupport);
                step.transform.position = new Vector3(0.8f, 0.1f, 0f);
                step.transform.localScale = new Vector3(0.4f, 0.2f, 1f);
                step
                    .AddComponent<OntologyCollisionRoleAdapter>()
                    .Configure(OntologyCollisionRole.WalkableSupport);

                // The support probe can already observe the floor at this
                // height, but CharacterController has not made contact.
                avatar.transform.position = new Vector3(0f, 0.12f, 0f);
                var controller = avatar.AddComponent<CharacterController>();
                controller.height = 2f;
                controller.center = Vector3.up;
                controller.radius = 0.35f;
                controller.skinWidth = 0.035f;
                var coordinator =
                    avatar.AddComponent<OntologyCharacterMotionCoordinator>();
                avatar
                    .AddComponent<OntologyMotionDriverAdapter>()
                    .Configure(
                        OntologyMotionDriver.LocalCharacterController);
                Physics.SyncTransforms();
                coordinator.RefreshSupport();

                Assert.That(controller.isGrounded, Is.False);
                Assert.That(
                    coordinator.LastSupport.hasSupport,
                    Is.True,
                    "The regression requires observable support before actual " +
                    "controller contact.");
                Assert.That(
                    coordinator.IsGrounded,
                    Is.False,
                    "A nearby support probe hit is not physical ground contact " +
                    "and cannot end the airborne presentation.");

                var result = coordinator.Move(
                    Vector3.right * 0.4f,
                    -0.02f,
                    Vector3.zero);

                Assert.That(
                    result.resolvedStepRise,
                    Is.EqualTo(0f).Within(0.0001f),
                    "Proximity is an observation, not grounded permission to " +
                    "inject an upward step displacement.");
            }
            finally
            {
                Object.Destroy(avatar);
                Object.Destroy(step);
                Object.Destroy(ground);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator TemporaryRigidbodyReactionOwnsTransformExclusively()
        {
            var avatar = new GameObject("HybridImpactAvatar");
            var profile =
                ScriptableObject.CreateInstance<OntologyPhysicalProfile>();
            try
            {
                var ontology = avatar.AddComponent<OntologyObject>();
                ontology.ConfigureOntologyData(
                    avatar.name,
                    new[] { OntologyConcepts.Actor },
                    new[]
                    {
                        new OntologyFactEntry
                        {
                            predicate =
                                OntologyPredicates.ImpactResponseProfile,
                            obj =
                                OntologyObjects
                                    .TemporaryRigidbodyReaction
                        }
                    });
                var controller =
                    avatar.AddComponent<CharacterController>();
                avatar.AddComponent<BoxCollider>();
                var body = avatar.AddComponent<Rigidbody>();
                body.isKinematic = true;
                body.useGravity = false;
                avatar.AddComponent<OntologyInputSystemPlayerInput>();
                var adapter =
                    avatar.AddComponent<OntologyCharacterImpactAdapter>();
                profile.profileId = "HybridImpactTest";
                profile.rigidbodyMinimumReactionSeconds = 10f;
                adapter.Configure(profile);

                Assert.That(
                    adapter.ApplyApprovedImpulse(Vector3.right),
                    Is.True);
                Assert.That(adapter.OwnsWorldTransform, Is.True);
                Assert.That(controller.enabled, Is.False);
                Assert.That(body.isKinematic, Is.False);

                adapter.Configure(null);
                yield return null;

                Assert.That(adapter.OwnsWorldTransform, Is.False);
                Assert.That(controller.enabled, Is.True);
                Assert.That(body.isKinematic, Is.True);
            }
            finally
            {
                Object.Destroy(avatar);
                Object.Destroy(profile);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator AuthorityCorrectionUsesTheSingleCoordinatorMove()
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var avatar = new GameObject("AuthorityCorrectionAvatar");
            try
            {
                ground.transform.position =
                    new Vector3(0f, -0.5f, 0f);
                ground.transform.localScale =
                    new Vector3(20f, 1f, 20f);
                ground
                    .AddComponent<OntologyCollisionRoleAdapter>()
                    .Configure(OntologyCollisionRole.WalkableSupport);
                avatar.transform.position = new Vector3(0f, 0.08f, 0f);
                var controller =
                    avatar.AddComponent<CharacterController>();
                controller.height = 2f;
                controller.center = Vector3.up;
                controller.radius = 0.35f;
                var coordinator =
                    avatar.AddComponent<
                        OntologyCharacterMotionCoordinator>();
                avatar
                    .AddComponent<OntologyMotionDriverAdapter>()
                    .Configure(
                        OntologyMotionDriver.LocalCharacterController);
                controller.Move(Vector3.down * 0.05f);
                Physics.SyncTransforms();
                var before = avatar.transform.position;

                Assert.That(
                    coordinator.QueueAuthorityPlanarCorrection(
                        Vector3.right,
                        10,
                        2f,
                        0.01f),
                    Is.True);
                Assert.That(
                    avatar.transform.position,
                    Is.EqualTo(before),
                    "Queueing a snapshot must not write the Transform.");
                yield return null;

                coordinator.Move(
                    Vector3.zero,
                    -0.01f,
                    Vector3.zero);

                Assert.That(
                    avatar.transform.position.x,
                    Is.GreaterThan(before.x));
                Assert.That(
                    coordinator.PendingAuthorityPlanarCorrection.x,
                    Is.LessThan(1f));
                Assert.That(
                    coordinator.QueueAuthorityPlanarCorrection(
                        Vector3.left,
                        9,
                        2f,
                        0.01f),
                    Is.False,
                    "An older Authority tick cannot replace a newer residual.");
            }
            finally
            {
                Object.Destroy(avatar);
                Object.Destroy(ground);
            }
            yield return null;
        }
    }
}
