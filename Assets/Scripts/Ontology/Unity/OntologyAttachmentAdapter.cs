using UnityEngine;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Presents ontology-inferred wearable, carried, and mounted relations.
    /// Rules decide the semantic relation; this adapter only expresses it visually.
    /// </summary>
    [RequireComponent(typeof(OntologyObject))]
    [RequireComponent(typeof(OntologyPhysicsPresentationCoordinator))]
    public sealed class OntologyAttachmentAdapter : MonoBehaviour
    {
        [SerializeField] private OntologyWorldBootstrap bootstrap;
        [SerializeField] private OntologyAttachmentProfile attachmentProfile;
        [SerializeField] private OntologyObject actorObject;
        private OntologyObject itemObject;
        private OntologyPhysicsPresentationCoordinator physicsCoordinator;
        private Transform detachedParent;
        private Transform mountedActorDetachedParent;
        private CharacterController mountedActorController;
        private bool mountedActorControllerWasEnabled;
        private Vector3 mountedActorWorldPosition;
        private Quaternion mountedActorWorldRotation;
        private bool placeInWorldAfterDetach;
        private Transform activeActorAnchor;
        private OntologyAttachmentGripPoint activeGripPoint;
        private Vector3 capturedGripLocalPosition;
        private Quaternion capturedGripLocalRotation;
        private Vector3 capturedGripLocalScale;
        private Vector3 capturedProfileLocalPosition;
        private Vector3 capturedProfileLocalEulerAngles;
        private Vector3 capturedProfileLocalScale;
        private bool hasAttachmentPoseSnapshot;
        private bool missingAnchorLogged;
        private bool relationAmbiguityLogged;

        public bool IsAttached { get; private set; }
        public bool OwnsWorldTransform =>
            IsAttached ||
            (physicsCoordinator != null &&
             physicsCoordinator.IsWorldReleasePending);
        public OntologyAttachmentProfile AttachmentProfile => attachmentProfile;

        private void Awake()
        {
            itemObject = GetComponent<OntologyObject>();
            physicsCoordinator = GetComponent<OntologyPhysicsPresentationCoordinator>();
            ResolveDependencies();
            physicsCoordinator.CaptureWorldBaseline();
        }

        private void Update()
        {
            SynchronizePresentation();
        }

        public void SynchronizePresentation()
        {
            ResolveDependencies();
            var shouldAttach = ShouldBeAttached();
            if (shouldAttach == IsAttached)
            {
                if (shouldAttach &&
                    attachmentProfile != null &&
                    attachmentProfile.kind != OntologyAttachmentKind.Mountable)
                {
                    RefreshAttachedPoseIfAuthoringChanged();
                }
                return;
            }

            if (shouldAttach)
            {
                Attach();
            }
            else
            {
                Detach();
            }
        }

        private void OnDisable()
        {
            if (IsAttached && physicsCoordinator != null && attachmentProfile != null)
            {
                // Do not reparent during hierarchy destruction/deactivation; Unity forbids
                // changing parent while an ancestor is being disabled.
                physicsCoordinator.SetAttachmentOverride(
                    false,
                    attachmentProfile.disableWorldPhysicsWhileAttached,
                    attachmentProfile.disableWorldCollidersWhileAttached);
                RestoreMountedActorController();
                ClearAttachmentPoseSnapshot();
                IsAttached = false;
            }
        }

        public void Configure(
            OntologyAttachmentProfile profile,
            OntologyWorldBootstrap targetBootstrap,
            OntologyObject targetActor)
        {
            if (IsAttached && profile != attachmentProfile)
            {
                Detach();
            }

            attachmentProfile = profile;
            bootstrap = targetBootstrap;
            actorObject = targetActor;
            itemObject = GetComponent<OntologyObject>();
            physicsCoordinator = GetComponent<OntologyPhysicsPresentationCoordinator>();
            ResolveDependencies();
            physicsCoordinator.CaptureWorldBaseline();
        }

        private bool ShouldBeAttached()
        {
            if (attachmentProfile == null ||
                bootstrap == null || bootstrap.World == null ||
                itemObject == null ||
                string.IsNullOrWhiteSpace(attachmentProfile.profileId) ||
                string.IsNullOrWhiteSpace(attachmentProfile.relationPredicate))
            {
                return false;
            }

            if (!bootstrap.World.HasFact(
                    itemObject.EntityId,
                    OntologyPredicates.AttachmentProfile,
                    attachmentProfile.profileId))
            {
                return false;
            }

            string matchedActorId = null;
            OntologyObject matchedActor = null;
            foreach (var fact in bootstrap.World.Facts)
            {
                var actorId = ResolveActorIdFromRelation(fact);
                if (string.IsNullOrWhiteSpace(actorId) ||
                    !bootstrap.EntityRegistry.TryGet(actorId, out var resolvedActor))
                {
                    continue;
                }

                if (matchedActorId == null)
                {
                    matchedActorId = actorId;
                    matchedActor = resolvedActor;
                    continue;
                }

                if (string.Equals(
                        matchedActorId,
                        actorId,
                        System.StringComparison.Ordinal))
                {
                    continue;
                }

                if (!relationAmbiguityLogged)
                {
                    Debug.LogError(
                        "[OntologyAttachment] Multiple actors own the same " +
                        "attachment relation. Presentation is disabled until " +
                        "Authority repairs the relation.",
                        this);
                    relationAmbiguityLogged = true;
                }
                return false;
            }

            relationAmbiguityLogged = false;
            if (matchedActor != null)
                actorObject = matchedActor;
            return matchedActor != null;
        }

        private void Attach()
        {
            if (attachmentProfile.kind == OntologyAttachmentKind.Mountable)
            {
                AttachActorToMount();
                return;
            }

            var anchor = ResolveActorAnchor();
            if (anchor == null)
            {
                if (!missingAnchorLogged)
                {
                    Debug.LogError(
                        "[OntologyAttachment] The authored actor socket could " +
                        "not be resolved. Attachment presentation remains " +
                        "disabled; no actor-root fallback is applied.",
                        this);
                    missingAnchorLogged = true;
                }
                return;
            }
            missingAnchorLogged = false;

            detachedParent = transform.parent;
            GetComponent<OntologyDynamicTransformCheckpointAdapter>()?
                .SuspendForAttachment();
            if (!ApplyAttachmentPose(anchor))
            {
                GetComponent<OntologyDynamicTransformCheckpointAdapter>()?
                    .ResumeAfterAttachmentRelease();
                return;
            }
            physicsCoordinator.SetAttachmentOverride(
                true,
                attachmentProfile.disableWorldPhysicsWhileAttached,
                attachmentProfile.disableWorldCollidersWhileAttached);
            IsAttached = true;
        }

        private void Detach()
        {
            if (attachmentProfile.kind == OntologyAttachmentKind.Mountable)
            {
                DetachActorFromMount();
                return;
            }

            transform.SetParent(detachedParent, true);
            if (placeInWorldAfterDetach ||
                attachmentProfile.placeInWorldOnRelationRemoval)
            {
                PlaceInFrontOfActor();
                placeInWorldAfterDetach = false;
            }

            // The item is deliberately positioned while the attachment override is
            // still active. This prevents the Rigidbody from resolving penetration
            // against the actor, ground, or water while it is being moved out of its
            // attachment socket.
            physicsCoordinator.PrepareForWorldRelease();
            physicsCoordinator.SetAttachmentOverride(
                false,
                attachmentProfile.disableWorldPhysicsWhileAttached,
                attachmentProfile.disableWorldCollidersWhileAttached);
            GetComponent<OntologyDynamicTransformCheckpointAdapter>()?
                .ResumeAfterAttachmentRelease();
            ClearAttachmentPoseSnapshot();
            IsAttached = false;
        }

        /// <summary>
        /// Arms the profile-authored safe world release for the next semantic
        /// relation removal. Authority still owns whether the relation is
        /// removed; this method only selects the resulting Unity presentation.
        /// </summary>
        public void PrepareForAuthorityDetach()
        {
            if (IsAttached &&
                attachmentProfile != null &&
                attachmentProfile.kind != OntologyAttachmentKind.Mountable)
            {
                placeInWorldAfterDetach = true;
            }
        }

        /// <summary>
        /// Cancels a presentation-only release prepared before an Authority
        /// command when that command is rejected. The semantic relation and
        /// attachment remain untouched.
        /// </summary>
        public void CancelPreparedAuthorityDetach()
        {
            placeInWorldAfterDetach = false;
        }

        private void RefreshAttachedPoseIfAuthoringChanged()
        {
            var anchor = ResolveActorAnchor();
            var gripPoint =
                GetComponentInChildren<OntologyAttachmentGripPoint>(true);
            if (anchor == null)
            {
                return;
            }

            var authoredPoseChanged =
                !hasAttachmentPoseSnapshot ||
                anchor != activeActorAnchor ||
                gripPoint != activeGripPoint ||
                (gripPoint != null &&
                 (gripPoint.transform.localPosition !=
                      capturedGripLocalPosition ||
                  gripPoint.transform.localRotation !=
                      capturedGripLocalRotation ||
                  gripPoint.transform.localScale !=
                      capturedGripLocalScale)) ||
                attachmentProfile.localPosition !=
                    capturedProfileLocalPosition ||
                attachmentProfile.localEulerAngles !=
                    capturedProfileLocalEulerAngles ||
                attachmentProfile.localScale !=
                    capturedProfileLocalScale;
            if (authoredPoseChanged)
            {
                if (!ApplyAttachmentPose(anchor))
                {
                    Detach();
                }
            }
        }

        private bool ApplyAttachmentPose(Transform anchor)
        {
            if (!OntologyAttachmentPoseUtility.Apply(
                    transform,
                    anchor,
                    attachmentProfile))
            {
                return false;
            }
            activeActorAnchor = anchor;
            activeGripPoint =
                GetComponentInChildren<OntologyAttachmentGripPoint>(true);
            if (activeGripPoint != null)
            {
                capturedGripLocalPosition =
                    activeGripPoint.transform.localPosition;
                capturedGripLocalRotation =
                    activeGripPoint.transform.localRotation;
                capturedGripLocalScale =
                    activeGripPoint.transform.localScale;
            }

            capturedProfileLocalPosition =
                attachmentProfile.localPosition;
            capturedProfileLocalEulerAngles =
                attachmentProfile.localEulerAngles;
            capturedProfileLocalScale =
                attachmentProfile.localScale;
            hasAttachmentPoseSnapshot = true;
            return true;
        }

        private void ClearAttachmentPoseSnapshot()
        {
            activeActorAnchor = null;
            activeGripPoint = null;
            hasAttachmentPoseSnapshot = false;
        }

        public bool TryUnequip()
        {
            ResolveDependencies();
            if (!IsAttached || bootstrap == null || bootstrap.World == null ||
                itemObject == null || actorObject == null || attachmentProfile == null ||
                attachmentProfile.kind != OntologyAttachmentKind.Wearable)
            {
                return false;
            }

            var actorId = actorObject.EntityId;
            var itemId = itemObject.EntityId;
            var action = new OntologyAction(
                actorId,
                OntologyActions.UnequipWearable,
                itemId);
            if (!bootstrap.IsActionCurrentlyAvailable(action))
            {
                return false;
            }

            placeInWorldAfterDetach = true;
            bootstrap.ExecuteAction(action);
            return true;
        }

        private void AttachActorToMount()
        {
            var mountPoint = ResolveMountPoint();
            if (mountPoint == null || actorObject == null)
            {
                return;
            }

            mountedActorDetachedParent = actorObject.transform.parent;
            mountedActorWorldPosition = actorObject.transform.position;
            mountedActorWorldRotation = actorObject.transform.rotation;
            mountedActorController =
                actorObject.GetComponentInChildren<CharacterController>();
            if (mountedActorController != null)
            {
                mountedActorControllerWasEnabled = mountedActorController.enabled;
                if (attachmentProfile.disableActorControllerWhileMounted)
                {
                    mountedActorController.enabled = false;
                }
            }

            actorObject.transform.SetParent(mountPoint, false);
            actorObject.transform.localPosition = attachmentProfile.localPosition;
            actorObject.transform.localRotation =
                Quaternion.Euler(attachmentProfile.localEulerAngles);
            actorObject.transform.localScale = attachmentProfile.localScale;
            IsAttached = true;
        }

        private void DetachActorFromMount()
        {
            if (actorObject != null)
            {
                actorObject.transform.SetParent(mountedActorDetachedParent, true);
                actorObject.transform.SetPositionAndRotation(
                    mountedActorWorldPosition,
                    mountedActorWorldRotation);
            }

            RestoreMountedActorController();
            IsAttached = false;
        }

        private void RestoreMountedActorController()
        {
            if (mountedActorController != null)
            {
                mountedActorController.enabled = mountedActorControllerWasEnabled;
                mountedActorController = null;
            }
        }

        public static string BuildEquipmentSlotEntityId(
            string actorId,
            string slotId)
        {
            return "EquipmentSlot_" + actorId + "_" + slotId;
        }

        public bool RaycastPresentation(
            Ray ray,
            float maximumDistance,
            out float hitDistance)
        {
            hitDistance = float.PositiveInfinity;
            if (!IsAttached || attachmentProfile == null)
            {
                return false;
            }

            // Do not inspect every child Renderer here. A placeable may be a large
            // environment prefab (for example a tree cluster), so renderer bounds can
            // cover surrounding ground and turn unrelated clicks into an unequip action.
            // The attachment profile owns a compact, presentation-only local hit area;
            // it stays independent from world colliders, which are deliberately disabled
            // while a wearable is attached.
            var localSize = attachmentProfile.detachInteractionLocalSize;
            if (localSize.x <= 0f || localSize.y <= 0f || localSize.z <= 0f)
            {
                localSize = Vector3.one;
            }

            var localBounds = new Bounds(
                attachmentProfile.detachInteractionLocalCenter,
                localSize);
            var localRay = new Ray(
                transform.InverseTransformPoint(ray.origin),
                transform.InverseTransformDirection(ray.direction).normalized);
            if (!localBounds.IntersectRay(localRay, out var localDistance) ||
                localDistance < 0f)
            {
                return false;
            }

            // The profile volume is an interaction contract, not a blanket click
            // trigger. Require the same ray to also pass through the attached
            // object's visible bounds so empty space around a large wearable or
            // mount cannot detach it.
            if (TryGetVisibleBounds(out var visibleBounds) &&
                !visibleBounds.IntersectRay(ray, out _))
            {
                return false;
            }

            var worldHitPoint = transform.TransformPoint(
                localRay.GetPoint(localDistance));
            hitDistance = Vector3.Distance(ray.origin, worldHitPoint);
            return hitDistance <= maximumDistance;
        }

        private bool TryGetVisibleBounds(out Bounds bounds)
        {
            var renderers = GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                bounds = default;
                return false;
            }

            bounds = renderers[0].bounds;
            for (var index = 1; index < renderers.Length; index++)
            {
                bounds.Encapsulate(renderers[index].bounds);
            }

            return bounds.size.sqrMagnitude > Mathf.Epsilon;
        }

        private Transform ResolveActorAnchor()
        {
            if (actorObject == null)
            {
                return null;
            }

            var authoredSocket =
                OntologyAttachmentPoseUtility.ResolveActorSocket(
                    actorObject.transform,
                    attachmentProfile.actorSocketId);
            if (authoredSocket != null)
            {
                return authoredSocket;
            }
            if (!string.IsNullOrWhiteSpace(
                    attachmentProfile.actorSocketId))
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(attachmentProfile.actorAnchorPath))
            {
                var pathAnchor = actorObject.transform.Find(attachmentProfile.actorAnchorPath);
                if (pathAnchor != null)
                {
                    return pathAnchor;
                }
            }

            foreach (var animator in actorObject.GetComponentsInChildren<Animator>())
            {
                if (animator != null && animator.isHuman)
                {
                    var bone = animator.GetBoneTransform(attachmentProfile.actorAnchorBone);
                    if (bone != null)
                    {
                        return bone;
                    }
                }
            }

            return null;
        }

        private Transform ResolveMountPoint()
        {
            if (string.IsNullOrWhiteSpace(attachmentProfile.mountPointPath))
            {
                return transform;
            }

            return transform.Find(attachmentProfile.mountPointPath);
        }

        private void PlaceInFrontOfActor()
        {
            if (actorObject == null || attachmentProfile == null)
            {
                return;
            }

            var actorTransform = actorObject.transform;
            var forward = Vector3.ProjectOnPlane(
                actorTransform.forward,
                Vector3.up);
            if (forward.sqrMagnitude <= Mathf.Epsilon)
            {
                forward = Vector3.forward;
            }

            var actorRadius = ResolveActorHorizontalRadius();
            var objectRadius = ResolveReleaseHorizontalRadius();
            var releaseDistance = Mathf.Max(
                0.1f,
                attachmentProfile.detachForwardDistance,
                actorRadius + objectRadius +
                Mathf.Max(0f, attachmentProfile.detachActorClearance));
            var desiredPosition = actorTransform.position +
                                  forward.normalized * releaseDistance +
                                  Vector3.up * attachmentProfile.detachVerticalOffset;

            if (!TryResolveReleaseSurface(desiredPosition, out var surfacePoint))
            {
                transform.position = desiredPosition;
                return;
            }

            transform.position = new Vector3(
                surfacePoint.x,
                surfacePoint.y + attachmentProfile.detachVerticalOffset,
                surfacePoint.z);
            Physics.SyncTransforms();

            if (TryGetReleaseBounds(out var bounds))
            {
                transform.position += Vector3.up * (
                    surfacePoint.y + Mathf.Max(0f, attachmentProfile.detachSurfaceClearance) -
                    bounds.min.y);
            }

            Physics.SyncTransforms();
        }

        private float ResolveActorHorizontalRadius()
        {
            var controller = actorObject == null
                ? null
                : actorObject.GetComponentInChildren<CharacterController>();
            if (controller != null)
            {
                return Mathf.Max(0.05f, controller.radius);
            }

            return 0.5f;
        }

        private float ResolveReleaseHorizontalRadius()
        {
            return TryGetReleaseBounds(out var bounds)
                ? Mathf.Max(0.05f, Mathf.Max(bounds.extents.x, bounds.extents.z))
                : 0.5f;
        }

        private bool TryResolveReleaseSurface(
            Vector3 desiredPosition,
            out Vector3 surfacePoint)
        {
            surfacePoint = default;
            var probeHeight = Mathf.Max(0.1f, attachmentProfile.detachSurfaceProbeHeight);
            var probeDistance = Mathf.Max(0.1f, attachmentProfile.detachSurfaceProbeDistance);
            var origin = new Vector3(
                desiredPosition.x,
                desiredPosition.y + probeHeight,
                desiredPosition.z);

            var hasSolid = false;
            var solidSurfaceY = float.NegativeInfinity;
            var hits = Physics.RaycastAll(
                origin,
                Vector3.down,
                probeDistance,
                ~0,
                QueryTriggerInteraction.Ignore);
            foreach (var hit in hits)
            {
                if (hit.collider == null || IsIgnoredReleaseSurface(hit.collider))
                {
                    continue;
                }

                if (!hasSolid || hit.point.y > solidSurfaceY)
                {
                    hasSolid = true;
                    solidSurfaceY = hit.point.y;
                }
            }

            var hasWater = false;
            var waterSurfaceY = float.NegativeInfinity;
            foreach (var waterVolume in FindObjectsByType<OntologyWaterRegionVolume>(
                         FindObjectsInactive.Exclude))
            {
                var volumeCollider = waterVolume.GetComponent<Collider>();
                if (volumeCollider == null || !volumeCollider.enabled ||
                    IsIgnoredReleaseSurface(volumeCollider) ||
                    !waterVolume.TryGetSurfaceHeight(out var candidateSurfaceY))
                {
                    continue;
                }

                var volumeBounds = volumeCollider.bounds;
                if (desiredPosition.x < volumeBounds.min.x ||
                    desiredPosition.x > volumeBounds.max.x ||
                    desiredPosition.z < volumeBounds.min.z ||
                    desiredPosition.z > volumeBounds.max.z ||
                    candidateSurfaceY > origin.y ||
                    candidateSurfaceY < origin.y - probeDistance ||
                    !waterVolume.TryClassifyDepthAt(
                        desiredPosition,
                        transform,
                        out var depth) ||
                    depth == OntologyWaterDepth.DryLand)
                {
                    continue;
                }

                if (!hasWater || candidateSurfaceY > waterSurfaceY)
                {
                    hasWater = true;
                    waterSurfaceY = candidateSurfaceY;
                }
            }

            if (!hasSolid && !hasWater)
            {
                return false;
            }

            surfacePoint = new Vector3(
                desiredPosition.x,
                hasWater && (!hasSolid || waterSurfaceY >= solidSurfaceY)
                    ? waterSurfaceY
                    : solidSurfaceY,
                desiredPosition.z);
            return true;
        }

        private bool IsIgnoredReleaseSurface(Collider candidate)
        {
            if (candidate == null)
            {
                return true;
            }

            var candidateTransform = candidate.transform;
            return candidateTransform.IsChildOf(transform) ||
                   (actorObject != null && candidateTransform.IsChildOf(actorObject.transform));
        }

        private bool TryGetReleaseBounds(out Bounds bounds)
        {
            bounds = default;
            var found = false;
            foreach (var renderer in GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null || !renderer.enabled)
                {
                    continue;
                }

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

            return found;
        }

        private void ResolveDependencies()
        {
            if (bootstrap == null)
            {
                bootstrap = FindAnyObjectByType<OntologyWorldBootstrap>();
            }

            if (actorObject != null)
            {
                return;
            }

            if (bootstrap == null)
            {
                return;
            }

            if (bootstrap.World != null && itemObject != null)
            {
                foreach (var fact in bootstrap.World.Facts)
                {
                    var actorId = ResolveActorIdFromRelation(fact);
                    if (!string.IsNullOrWhiteSpace(actorId) &&
                        bootstrap.EntityRegistry.TryGet(actorId, out actorObject))
                    {
                        return;
                    }
                }
            }

            actorObject =
                OntologySemanticAdapterSynchronizer
                    .ResolvePresentationActor(bootstrap);
        }

        private string ResolveActorIdFromRelation(OntologyFact fact)
        {
            if (attachmentProfile == null ||
                itemObject == null ||
                string.IsNullOrWhiteSpace(attachmentProfile.relationPredicate) ||
                fact.Predicate.Value != attachmentProfile.relationPredicate)
            {
                return null;
            }

            if (attachmentProfile.relationDirection ==
                OntologyAttachmentRelationDirection.ActorToItem)
            {
                return fact.Object.Value == itemObject.EntityId
                    ? fact.Subject.Value
                    : null;
            }

            return fact.Subject.Value == itemObject.EntityId &&
                   !string.IsNullOrWhiteSpace(fact.Object.Value)
                ? fact.Object.Value
                : null;
        }
    }
}
