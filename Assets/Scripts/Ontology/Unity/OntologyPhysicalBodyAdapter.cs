using System.Collections.Generic;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Presents a physical_profile through Unity Rigidbody and Collider settings.
    /// It does not infer gameplay meaning such as Floating or RestingOn.
    /// </summary>
    [RequireComponent(typeof(OntologyObject))]
    public sealed class OntologyPhysicalBodyAdapter : MonoBehaviour
    {
        [SerializeField] private OntologyPhysicalProfile physicalProfile;

        private readonly List<TransformState> originalTransformStates = new();
        private readonly List<ColliderState> originalColliderStates = new();
        private readonly List<Collider> generatedColliders = new();
        private RigidbodyState originalBodyState;
        private Rigidbody targetBody;
        private bool baselineCaptured;
        private bool createdBody;
        private OntologyPhysicalProfile pendingAttachmentProfile;
        private bool hasPendingAttachmentProfile;

        public OntologyPhysicalProfile PhysicalProfile => physicalProfile;
        public Rigidbody TargetBody => targetBody;

        private void Awake()
        {
            if (physicalProfile != null)
            {
                ApplyPhysicalProfile();
            }
        }

        public void Configure(OntologyPhysicalProfile profile)
        {
            var attachment = GetComponent<OntologyAttachmentAdapter>();
            if (attachment != null && attachment.OwnsWorldTransform)
            {
                pendingAttachmentProfile = profile;
                hasPendingAttachmentProfile = true;
                return;
            }

            ConfigureImmediately(profile);
        }

        private void ConfigureImmediately(OntologyPhysicalProfile profile)
        {
            if (profile != null &&
                !RequiresRigidbodyPresentation(profile))
            {
                if (baselineCaptured)
                {
                    RestorePhysicalPresentation(
                        releaseOwnership: true);
                }
                physicalProfile = profile;
                return;
            }

            if (physicalProfile == profile && baselineCaptured)
            {
                return;
            }

            if (baselineCaptured)
            {
                RestorePhysicalPresentation(releaseOwnership: profile == null);
            }

            physicalProfile = profile;
            if (physicalProfile == null)
            {
                return;
            }

            if (!baselineCaptured)
            {
                CapturePhysicalBaseline();
            }

            ApplyPhysicalProfile();
        }

        public bool TryGetPhysicalBounds(out Bounds bounds)
        {
            bounds = default;
            var found = false;
            foreach (var collider in GetComponentsInChildren<Collider>())
            {
                if (collider == null || !collider.enabled || collider.isTrigger)
                {
                    continue;
                }

                if (!found)
                {
                    bounds = collider.bounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(collider.bounds);
                }
            }

            return found;
        }

        /// <summary>
        /// Reasserts the current meaning-owned physical presentation after a
        /// temporary attachment/editor lease ends. Temporary adapters must not
        /// become a second owner of durable Collider or Rigidbody policy.
        /// </summary>
        public bool RestoreProfileAfterTemporaryOverride()
        {
            if (hasPendingAttachmentProfile)
            {
                var pending = pendingAttachmentProfile;
                pendingAttachmentProfile = null;
                hasPendingAttachmentProfile = false;
                ConfigureImmediately(pending);
            }

            if (physicalProfile == null ||
                !RequiresRigidbodyPresentation(physicalProfile))
            {
                return false;
            }

            if ((physicalProfile.dynamicColliderMode ==
                    OntologyDynamicColliderMode.BoundsBox ||
                 physicalProfile.dynamicColliderMode ==
                    OntologyDynamicColliderMode.BoundsSphere) &&
                generatedColliders.Count == 0)
            {
                ConfigureDynamicColliders();
            }

            var usesGeneratedSolid =
                physicalProfile.dynamicColliderMode ==
                    OntologyDynamicColliderMode.BoundsBox ||
                physicalProfile.dynamicColliderMode ==
                    OntologyDynamicColliderMode.BoundsSphere;
            foreach (var state in originalColliderStates)
            {
                if (state.collider == null)
                {
                    continue;
                }

                state.collider.enabled = state.enabled &&
                    (!usesGeneratedSolid || state.collider.isTrigger);
            }
            foreach (var collider in generatedColliders)
            {
                if (collider != null)
                {
                    collider.enabled = true;
                }
            }

            targetBody ??= GetComponent<Rigidbody>();
            if (targetBody != null)
            {
                var dynamic = physicalProfile.mobilityMode ==
                              OntologyPhysicalMobilityMode.Dynamic;
                targetBody.detectCollisions = true;
                targetBody.useGravity = dynamic;
                targetBody.isKinematic = !dynamic;
                if (!dynamic)
                {
                    targetBody.linearVelocity = Vector3.zero;
                    targetBody.angularVelocity = Vector3.zero;
                }
            }

            var hasSolidCollider = false;
            foreach (var collider in GetComponentsInChildren<Collider>(true))
            {
                if (collider != null && collider.enabled && !collider.isTrigger)
                {
                    hasSolidCollider = true;
                    break;
                }
            }
            if (!hasSolidCollider && targetBody != null)
            {
                targetBody.linearVelocity = Vector3.zero;
                targetBody.angularVelocity = Vector3.zero;
                targetBody.useGravity = false;
                targetBody.isKinematic = true;
            }

            return hasSolidCollider;
        }

        private void ApplyPhysicalProfile()
        {
            if (physicalProfile == null ||
                !RequiresRigidbodyPresentation(physicalProfile))
            {
                return;
            }

            if (!baselineCaptured)
            {
                CapturePhysicalBaseline();
            }

            foreach (var child in GetComponentsInChildren<Transform>(true))
            {
                child.gameObject.isStatic = false;
            }

            ConfigureDynamicColliders();
            targetBody = GetComponent<Rigidbody>();
            if (targetBody == null)
            {
                targetBody = gameObject.AddComponent<Rigidbody>();
                createdBody = true;
            }

            targetBody.mass = Mathf.Max(0.01f, physicalProfile.mass);
            targetBody.linearDamping = Mathf.Max(0f, physicalProfile.linearDamping);
            targetBody.angularDamping = Mathf.Max(0f, physicalProfile.angularDamping);
            targetBody.detectCollisions = true;
            targetBody.constraints = physicalProfile.constraints;

            var dynamic =
                physicalProfile.mobilityMode == OntologyPhysicalMobilityMode.Dynamic;
            targetBody.useGravity = dynamic;
            if (!dynamic && !targetBody.isKinematic)
            {
                targetBody.linearVelocity = Vector3.zero;
                targetBody.angularVelocity = Vector3.zero;
            }
            targetBody.isKinematic = !dynamic;
            GetComponent<OntologyPhysicsPresentationCoordinator>()?
                .RefreshActiveAttachmentOverride();
        }

        public static bool RequiresRigidbodyPresentation(
            OntologyPhysicalProfile profile)
        {
            return profile != null &&
                   (profile.motionDriver ==
                    OntologyMotionDriver.Rigidbody ||
                    profile.motionDriver ==
                    OntologyMotionDriver.AuthorityKinematic);
        }

        private void CapturePhysicalBaseline()
        {
            originalTransformStates.Clear();
            foreach (var child in GetComponentsInChildren<Transform>(true))
            {
                originalTransformStates.Add(new TransformState
                {
                    transform = child,
                    isStatic = child.gameObject.isStatic
                });
            }

            originalColliderStates.Clear();
            foreach (var collider in GetComponentsInChildren<Collider>(true))
            {
                originalColliderStates.Add(new ColliderState
                {
                    collider = collider,
                    enabled = collider.enabled,
                    meshConvex = collider is MeshCollider mesh && mesh.convex
                });
            }

            targetBody = GetComponent<Rigidbody>();
            createdBody = targetBody == null;
            originalBodyState = targetBody == null
                ? null
                : RigidbodyState.Capture(targetBody);
            baselineCaptured = true;
        }

        private void ConfigureDynamicColliders()
        {
            generatedColliders.Clear();
            switch (physicalProfile.dynamicColliderMode)
            {
                case OntologyDynamicColliderMode.ConvexMesh:
                    foreach (var state in originalColliderStates)
                    {
                        if (state.collider is MeshCollider mesh &&
                            mesh.enabled &&
                            !mesh.isTrigger)
                        {
                            mesh.convex = true;
                        }
                    }
                    break;

                case OntologyDynamicColliderMode.BoundsBox:
                    if (!TryGetPresentationBounds(out var boxBounds)) return;
                    DisableOriginalSolidColliders();
                    var box = gameObject.AddComponent<BoxCollider>();
                    box.center =
                        transform.InverseTransformPoint(boxBounds.center) +
                        physicalProfile.dynamicColliderCenterOffset;
                    box.size = ScaleBoundsSizeToLocal(
                        boxBounds.size,
                        physicalProfile.dynamicColliderSizeMultiplier);
                    generatedColliders.Add(box);
                    break;

                case OntologyDynamicColliderMode.BoundsSphere:
                    if (!TryGetPresentationBounds(out var sphereBounds)) return;
                    DisableOriginalSolidColliders();
                    var sphere = gameObject.AddComponent<SphereCollider>();
                    sphere.center =
                        transform.InverseTransformPoint(sphereBounds.center) +
                        physicalProfile.dynamicColliderCenterOffset;
                    var localSize = ScaleBoundsSizeToLocal(
                        sphereBounds.size,
                        physicalProfile.dynamicColliderSizeMultiplier);
                    sphere.radius = Mathf.Max(
                        0.01f,
                        Mathf.Max(localSize.x, localSize.y, localSize.z) * 0.5f);
                    generatedColliders.Add(sphere);
                    break;
            }
        }

        private void DisableOriginalSolidColliders()
        {
            foreach (var state in originalColliderStates)
            {
                if (state.collider != null && !state.collider.isTrigger)
                {
                    state.collider.enabled = false;
                }
            }
        }

        private bool TryGetPresentationBounds(out Bounds bounds)
        {
            bounds = default;
            var found = false;
            foreach (var renderer in GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null || !renderer.enabled) continue;
                if (!found)
                {
                    bounds = renderer.bounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            if (found) return true;
            foreach (var state in originalColliderStates)
            {
                if (state.collider == null || !state.enabled || state.collider.isTrigger)
                    continue;
                if (!found)
                {
                    bounds = state.collider.bounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(state.collider.bounds);
                }
            }

            return found;
        }

        private Vector3 ScaleBoundsSizeToLocal(Vector3 worldSize, Vector3 multiplier)
        {
            var scale = transform.lossyScale;
            return new Vector3(
                worldSize.x / Mathf.Max(0.0001f, Mathf.Abs(scale.x)) *
                Mathf.Max(0.01f, multiplier.x),
                worldSize.y / Mathf.Max(0.0001f, Mathf.Abs(scale.y)) *
                Mathf.Max(0.01f, multiplier.y),
                worldSize.z / Mathf.Max(0.0001f, Mathf.Abs(scale.z)) *
                Mathf.Max(0.01f, multiplier.z));
        }

        private void RestorePhysicalPresentation(bool releaseOwnership)
        {
            foreach (var collider in generatedColliders)
            {
                if (collider == null) continue;
                collider.enabled = false;
                Destroy(collider);
            }
            generatedColliders.Clear();

            foreach (var state in originalColliderStates)
            {
                if (state.collider == null) continue;
                state.collider.enabled = state.enabled;
                if (state.collider is MeshCollider mesh)
                {
                    mesh.convex = state.meshConvex;
                }
            }

            foreach (var state in originalTransformStates)
            {
                if (state.transform != null)
                {
                    state.transform.gameObject.isStatic = state.isStatic;
                }
            }

            if (targetBody != null)
            {
                if (createdBody)
                {
                    if (!targetBody.isKinematic)
                    {
                        targetBody.linearVelocity = Vector3.zero;
                        targetBody.angularVelocity = Vector3.zero;
                    }
                    targetBody.useGravity = false;
                    targetBody.isKinematic = true;
                    targetBody.detectCollisions = false;
                    if (releaseOwnership)
                    {
                        Destroy(targetBody);
                        targetBody = null;
                    }
                }
                else
                {
                    originalBodyState?.Restore(targetBody);
                }
            }

            if (!releaseOwnership) return;
            baselineCaptured = false;
            createdBody = false;
            originalBodyState = null;
            originalTransformStates.Clear();
            originalColliderStates.Clear();
        }

        private sealed class TransformState
        {
            public Transform transform;
            public bool isStatic;
        }

        private sealed class ColliderState
        {
            public Collider collider;
            public bool enabled;
            public bool meshConvex;
        }

        private sealed class RigidbodyState
        {
            private float mass;
            private float linearDamping;
            private float angularDamping;
            private bool useGravity;
            private bool isKinematic;
            private bool detectCollisions;
            private RigidbodyConstraints constraints;
            private CollisionDetectionMode collisionDetectionMode;
            private RigidbodyInterpolation interpolation;

            public static RigidbodyState Capture(Rigidbody body)
            {
                return new RigidbodyState
                {
                    mass = body.mass,
                    linearDamping = body.linearDamping,
                    angularDamping = body.angularDamping,
                    useGravity = body.useGravity,
                    isKinematic = body.isKinematic,
                    detectCollisions = body.detectCollisions,
                    constraints = body.constraints,
                    collisionDetectionMode = body.collisionDetectionMode,
                    interpolation = body.interpolation
                };
            }

            public void Restore(Rigidbody body)
            {
                body.mass = mass;
                body.linearDamping = linearDamping;
                body.angularDamping = angularDamping;
                body.useGravity = useGravity;
                body.isKinematic = isKinematic;
                body.detectCollisions = detectCollisions;
                body.constraints = constraints;
                body.collisionDetectionMode = collisionDetectionMode;
                body.interpolation = interpolation;
            }
        }
    }
}
