using System;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    [Serializable]
    public struct OntologyCharacterMotionResult
    {
        public CollisionFlags collisionFlags;
        public float requestedVerticalDisplacement;
        public float resolvedVerticalDisplacement;
        public OntologyCharacterSupportState supportBefore;
        public OntologyCharacterSupportState supportAfter;
    }

    /// <summary>
    /// Exclusive local CharacterController movement owner. Input, gravity,
    /// impacts and optional Authority observations contribute displacement, but
    /// only this component invokes CharacterController.Move during locomotion.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(OntologyCharacterSupportProbe))]
    public sealed class OntologyCharacterMotionCoordinator : MonoBehaviour
    {
        [Header("Diagnostics")]
        [SerializeField, Tooltip(
            "Opt-in development tracing for controller motion, grounding, " +
            "visual bounds, and external Transform writes. Disabled by default.")]
        private bool enableMotionDiagnostics;
        [SerializeField, Min(0.001f)] private float diagnosticRiseThreshold = 0.005f;
        [SerializeField, Min(0.05f)] private float diagnosticCooldownSeconds = 0.25f;
        [SerializeField, Min(0.1f)] private float diagnosticSampleSeconds = 0.25f;
        [SerializeField] private OntologyCharacterSupportState lastSupport;
        [SerializeField] private string lastHitCollider;
        [SerializeField] private OntologyCollisionRole lastHitRole;
        [SerializeField] private Vector3 lastHitNormal;
        [SerializeField] private Vector3 lastRequestedDisplacement;
        [SerializeField] private Vector3 lastResolvedDisplacement;
        [SerializeField] private Vector3 pendingAuthorityPlanarCorrection;
        [SerializeField] private long lastAuthorityServerTick;
        [SerializeField] private long lastAuthorityProcessedIntentSequence;
        [SerializeField] private long minimumAuthorityProcessedIntentSequence;
        [SerializeField, Min(0f)] private float authorityCorrectionSpeed;

        private CharacterController controller;
        private OntologyCharacterSupportProbe supportProbe;
        private bool movementInProgress;
        private bool hasLateObservedPosition;
        private Vector3 lastLateObservedPosition;
        private Vector3 lastPositionBeforeMove;
        private Vector3 lastPositionAfterMove;
        private CollisionFlags lastCollisionFlags;
        private int lastMoveFrame = -1;
        private float nextDiagnosticAt;
        private float nextDiagnosticSampleAt;
        private bool hasGroundedDiagnostic;
        private bool previousGroundedDiagnostic;
        private OntologyPhysicalProfile activePhysicalProfile;

        public OntologyCharacterSupportState LastSupport => lastSupport;
        public string LastHitCollider => lastHitCollider;
        public Vector3 LastHitNormal => lastHitNormal;
        public Vector3 LastRequestedDisplacement =>
            lastRequestedDisplacement;
        public Vector3 LastResolvedDisplacement =>
            lastResolvedDisplacement;
        public float MaximumStepHeight =>
            controller == null ? 0f : controller.stepOffset;
        public Vector3 PendingAuthorityPlanarCorrection =>
            pendingAuthorityPlanarCorrection;
        public long LastAuthorityServerTick =>
            lastAuthorityServerTick;
        public long LastAuthorityProcessedIntentSequence =>
            lastAuthorityProcessedIntentSequence;
        public CollisionFlags LastCollisionFlags =>
            lastCollisionFlags;
        public OntologyPhysicalProfile ActivePhysicalProfile =>
            activePhysicalProfile;
        // Grounded is an ephemeral Unity collision observation. Ontology
        // collision roles decide which layers may collide, but must not revoke
        // a support contact that CharacterController already resolved.
        public bool HasGroundContact =>
            controller != null &&
            controller.enabled &&
            (controller.isGrounded ||
             (lastCollisionFlags & CollisionFlags.Below) != 0);
        public bool IsGrounded =>
            HasGroundContact;

        private void Awake()
        {
            ResolveDependencies();
            lastSupport = supportProbe.Probe();
        }

        private void OnEnable()
        {
            ResolveDependencies();
        }

        public void Configure(OntologyPhysicalProfile profile)
        {
            ResolveDependencies();
            activePhysicalProfile = profile;
            if (controller == null)
            {
                return;
            }

            var localController =
                profile != null &&
                profile.motionDriver ==
                OntologyMotionDriver.LocalCharacterController;
            controller.stepOffset = localController
                ? Mathf.Clamp(
                    profile.maximumStepHeight,
                    0f,
                    Mathf.Max(0f, controller.height))
                : 0f;
            if (!localController)
            {
                return;
            }

            controller.slopeLimit = Mathf.Clamp(
                profile.characterSlopeLimit,
                0f,
                89f);
            controller.skinWidth = Mathf.Max(
                0.001f,
                profile.characterSkinWidth);
            controller.minMoveDistance = Mathf.Max(
                0f,
                profile.characterMinimumMoveDistance);
        }

        public OntologyCharacterMotionResult Move(
            Vector3 horizontalDisplacement,
            float verticalDisplacement,
            Vector3 additiveDisplacement)
        {
            ResolveDependencies();
            var result = new OntologyCharacterMotionResult
            {
                requestedVerticalDisplacement =
                    verticalDisplacement + additiveDisplacement.y,
                supportBefore = supportProbe.Probe()
            };
            if (controller == null || !controller.enabled ||
                movementInProgress ||
                !OntologyMotionDriverAdapter.Allows(
                    this,
                    OntologyMotionDriver.LocalCharacterController))
            {
                result.supportAfter = result.supportBefore;
                return result;
            }

            var authorityCorrection =
                ResolveAuthorityPlanarCorrectionStep(Time.deltaTime);
            var planar =
                Vector3.ProjectOnPlane(
                    horizontalDisplacement +
                    additiveDisplacement +
                    authorityCorrection,
                    Vector3.up);
            // CharacterController is the sole collision resolver. Feeding it a
            // support-tangent vector would first manufacture a Y displacement
            // from a sampled triangle normal and then ask the controller to
            // resolve the same surface again. On faceted terrain and shoreline
            // seams that double resolution creates visible upward pops.
            var surfaceDisplacement = planar;
            var gravityOrAdhesion =
                Vector3.up * verticalDisplacement;
            var requested =
                surfaceDisplacement +
                gravityOrAdhesion +
                Vector3.up * additiveDisplacement.y;
            lastRequestedDisplacement = requested;
            var before = transform.position;
            lastPositionBeforeMove = before;
            lastMoveFrame = Time.frameCount;
            movementInProgress = true;
            try
            {
                result.collisionFlags = controller.Move(requested);
                lastCollisionFlags = result.collisionFlags;
            }
            finally
            {
                movementInProgress = false;
            }
            lastPositionAfterMove = transform.position;
            lastResolvedDisplacement = lastPositionAfterMove - before;
            pendingAuthorityPlanarCorrection =
                ResolveRemainingAuthorityCorrection(
                    pendingAuthorityPlanarCorrection,
                    authorityCorrection,
                    lastResolvedDisplacement);
            result.resolvedVerticalDisplacement =
                lastResolvedDisplacement.y;
            result.supportAfter = supportProbe.Probe();
            lastSupport = result.supportAfter;
            TraceUnexpectedControllerRise(result);
            return result;
        }

        /// <summary>
        /// Queues a presentation-only planar correction produced from a newer
        /// Authority snapshot. The Transform is not written here. The residual
        /// is consumed by the next coordinator-owned CharacterController.Move,
        /// preserving a single motion owner and a single collision solve.
        /// </summary>
        public bool QueueAuthorityPlanarCorrection(
            Vector3 correction,
            long serverTick,
            long processedIntentSequence,
            float correctionSpeed,
            float deadZone)
        {
            if (serverTick <= 0 ||
                processedIntentSequence <
                    minimumAuthorityProcessedIntentSequence ||
                serverTick < lastAuthorityServerTick ||
                (processedIntentSequence <
                    lastAuthorityProcessedIntentSequence) ||
                (serverTick == lastAuthorityServerTick &&
                 processedIntentSequence <=
                    lastAuthorityProcessedIntentSequence) ||
                !IsFinite(correction) ||
                !float.IsFinite(correctionSpeed) ||
                !float.IsFinite(deadZone) ||
                correctionSpeed <= 0f ||
                deadZone < 0f)
            {
                return false;
            }

            lastAuthorityServerTick = serverTick;
            lastAuthorityProcessedIntentSequence =
                processedIntentSequence;
            authorityCorrectionSpeed = correctionSpeed;
            correction = Vector3.ProjectOnPlane(
                correction,
                Vector3.up);
            pendingAuthorityPlanarCorrection =
                correction.magnitude <= deadZone
                    ? Vector3.zero
                    : correction;
            return true;
        }

        /// <summary>
        /// Advances the local prediction fence when Authority accepts a newer
        /// input. A residual calculated from an earlier input can no longer be
        /// consumed while the server catches up to that accepted sequence.
        /// </summary>
        public void AdvanceAuthorityIntentFence(long acceptedIntentSequence)
        {
            if (acceptedIntentSequence <=
                minimumAuthorityProcessedIntentSequence)
            {
                return;
            }

            minimumAuthorityProcessedIntentSequence =
                acceptedIntentSequence;
            pendingAuthorityPlanarCorrection = Vector3.zero;
            authorityCorrectionSpeed = 0f;
        }

        public void ClearAuthorityCorrection()
        {
            pendingAuthorityPlanarCorrection = Vector3.zero;
            authorityCorrectionSpeed = 0f;
            lastAuthorityServerTick = 0;
            lastAuthorityProcessedIntentSequence = 0;
            minimumAuthorityProcessedIntentSequence = 0;
        }

        private Vector3 ResolveAuthorityPlanarCorrectionStep(
            float deltaSeconds)
        {
            if (pendingAuthorityPlanarCorrection.sqrMagnitude <=
                    Mathf.Epsilon ||
                authorityCorrectionSpeed <= 0f ||
                !float.IsFinite(deltaSeconds) ||
                deltaSeconds <= 0f)
            {
                return Vector3.zero;
            }

            return Vector3.MoveTowards(
                Vector3.zero,
                pendingAuthorityPlanarCorrection,
                authorityCorrectionSpeed * deltaSeconds);
        }

        public static Vector3 ResolveRemainingAuthorityCorrection(
            Vector3 pendingCorrection,
            Vector3 requestedCorrectionStep,
            Vector3 resolvedDisplacement)
        {
            pendingCorrection = Vector3.ProjectOnPlane(
                pendingCorrection,
                Vector3.up);
            requestedCorrectionStep = Vector3.ProjectOnPlane(
                requestedCorrectionStep,
                Vector3.up);
            var requestedMagnitude = requestedCorrectionStep.magnitude;
            if (!IsFinite(pendingCorrection) ||
                !IsFinite(requestedCorrectionStep) ||
                !IsFinite(resolvedDisplacement) ||
                requestedMagnitude <= Mathf.Epsilon)
            {
                return pendingCorrection;
            }

            var direction = requestedCorrectionStep / requestedMagnitude;
            var resolvedAlongCorrection = Mathf.Clamp(
                Vector3.Dot(
                    Vector3.ProjectOnPlane(
                        resolvedDisplacement,
                        Vector3.up),
                    direction),
                0f,
                requestedMagnitude);
            var remaining = pendingCorrection -
                            direction * resolvedAlongCorrection;
            return remaining.sqrMagnitude <= 0.0000001f
                ? Vector3.zero
                : remaining;
        }

        private static bool IsFinite(Vector3 value) =>
            float.IsFinite(value.x) &&
            float.IsFinite(value.y) &&
            float.IsFinite(value.z);

        private void LateUpdate()
        {
            if (!enableMotionDiagnostics)
            {
                hasLateObservedPosition = false;
                hasGroundedDiagnostic = false;
                return;
            }

            var current = transform.position;
            if (!hasLateObservedPosition)
            {
                hasLateObservedPosition = true;
                lastLateObservedPosition = current;
                previousGroundedDiagnostic =
                    controller != null &&
                    controller.enabled &&
                    controller.isGrounded;
                hasGroundedDiagnostic = true;
                return;
            }

            if (lastMoveFrame == Time.frameCount)
            {
                var beforeMoveExternalDelta =
                    lastPositionBeforeMove - lastLateObservedPosition;
                var afterMoveExternalDelta =
                    current - lastPositionAfterMove;
                if (ShouldTraceExternalWrite(beforeMoveExternalDelta) ||
                    ShouldTraceExternalWrite(afterMoveExternalDelta))
                {
                    Trace(
                        "external_transform_write",
                        "beforeMoveExternal=" +
                        beforeMoveExternalDelta.ToString("F4") +
                        " afterMoveExternal=" +
                        afterMoveExternalDelta.ToString("F4"));
                }
            }
            else
            {
                var uncoordinatedDelta = current - lastLateObservedPosition;
                if (ShouldTraceExternalWrite(uncoordinatedDelta))
                {
                    Trace(
                        "uncoordinated_transform_write",
                        "delta=" + uncoordinatedDelta.ToString("F4"));
                }
            }

            TraceGroundedTransition();
            TraceActiveMotionSample(current - lastLateObservedPosition);
            lastLateObservedPosition = current;
        }

        private void TraceGroundedTransition()
        {
            var grounded =
                controller != null &&
                controller.enabled &&
                controller.isGrounded;
            if (!hasGroundedDiagnostic)
            {
                previousGroundedDiagnostic = grounded;
                hasGroundedDiagnostic = true;
                return;
            }
            if (grounded == previousGroundedDiagnostic)
            {
                return;
            }

            var previous = previousGroundedDiagnostic;
            previousGroundedDiagnostic = grounded;
            Trace(
                "grounded_transition",
                "from=" + previous +
                " to=" + grounded +
                " requested=" +
                lastRequestedDisplacement.ToString("F4") +
                " resolved=" +
                lastResolvedDisplacement.ToString("F4"),
                true);
        }

        private void TraceActiveMotionSample(Vector3 frameDelta)
        {
            if (Time.unscaledTime < nextDiagnosticSampleAt)
            {
                return;
            }

            var planarRequested =
                Vector3.ProjectOnPlane(
                    lastRequestedDisplacement,
                    Vector3.up).sqrMagnitude;
            var active =
                planarRequested > 0.000001f ||
                Mathf.Abs(frameDelta.y) >= diagnosticRiseThreshold ||
                (controller != null && !controller.isGrounded);
            if (!active)
            {
                return;
            }

            nextDiagnosticSampleAt =
                Time.unscaledTime +
                Mathf.Max(0.1f, diagnosticSampleSeconds);
            Trace(
                "motion_sample",
                "frameDelta=" + frameDelta.ToString("F4") +
                " requested=" +
                lastRequestedDisplacement.ToString("F4") +
                " resolved=" +
                lastResolvedDisplacement.ToString("F4"));
        }

        private void TraceUnexpectedControllerRise(
            OntologyCharacterMotionResult result)
        {
            if (lastResolvedDisplacement.y <= diagnosticRiseThreshold ||
                lastRequestedDisplacement.y >
                diagnosticRiseThreshold * 0.25f)
            {
                return;
            }

            Trace(
                "controller_unrequested_rise",
                "flags=" + result.collisionFlags +
                " requested=" +
                lastRequestedDisplacement.ToString("F4") +
                " resolved=" +
                lastResolvedDisplacement.ToString("F4") +
                " unityStepOffset=" +
                controller.stepOffset.ToString("F4"));
        }

        private bool ShouldTraceExternalWrite(Vector3 delta)
        {
            return delta.sqrMagnitude >
                   diagnosticRiseThreshold * diagnosticRiseThreshold;
        }

        private void Trace(
            string reason,
            string details,
            bool ignoreCooldown = false)
        {
            if (!enableMotionDiagnostics)
            {
                return;
            }

            if (!ignoreCooldown &&
                Time.unscaledTime < nextDiagnosticAt)
            {
                return;
            }
            nextDiagnosticAt =
                Time.unscaledTime +
                Mathf.Max(0.05f, diagnosticCooldownSeconds);

            var before = lastSupport;
            var supportName =
                before.collider == null
                    ? "<none>"
                    : before.collider.name;
            var drowning =
                GetComponent<OntologyDrowningRecoveryAdapter>();
            var impact =
                GetComponent<OntologyCharacterImpactAdapter>();
            var input =
                GetComponent<OntologyInputSystemPlayerInput>();
            var animation =
                GetComponent<OntologyAnimationAdapter>();
            var animator =
                GetComponentInChildren<Animator>();
            var camera = Camera.main;
            var hasVisualBounds = TryGetVisualBounds(out var visualBounds);
            Debug.LogWarning(
                "[PlayerMotionTrace] reason=" + reason +
                " frame=" + Time.frameCount +
                " position=" + transform.position.ToString("F4") +
                " ccEnabled=" +
                (controller != null && controller.enabled) +
                " grounded=" +
                (controller != null && controller.isGrounded) +
                " support=" + supportName +
                " supportPoint=" + before.point.ToString("F4") +
                " supportNormal=" + before.normal.ToString("F4") +
                " hit=" + (lastHitCollider ?? "<none>") +
                " hitRole=" + lastHitRole +
                " hitNormal=" + lastHitNormal.ToString("F4") +
                " drowning=" +
                (drowning != null && drowning.RecoveryActive) +
                " impact=" +
                (impact != null && impact.OwnsWorldTransform) +
                " verticalVelocity=" +
                (input == null
                    ? "<none>"
                    : input.VerticalVelocity.ToString("F4")) +
                " collisionFlags=" +
                (input == null
                    ? "<none>"
                    : input.LastCollisionFlags.ToString()) +
                " jumpOccurrence=" +
                (input == null
                    ? "<none>"
                    : input.JumpOccurrence.ToString()) +
                " animationIntent=" +
                (animation == null
                    ? "<none>"
                    : animation.SelectedIntent) +
                " animationClip=" +
                (animation == null
                    ? "<none>"
                    : animation.SelectedClipName) +
                " animatorRootMotion=" +
                (animator != null && animator.applyRootMotion) +
                " visualRoot=" +
                (animator == null
                    ? "<none>"
                    : animator.transform.position.ToString("F4")) +
                " visualLocal=" +
                (animator == null
                    ? "<none>"
                    : animator.transform.localPosition.ToString("F4")) +
                " visualBoundsMinY=" +
                (hasVisualBounds
                    ? visualBounds.min.y.ToString("F4")
                    : "<none>") +
                " visualBoundsCenterY=" +
                (hasVisualBounds
                    ? visualBounds.center.y.ToString("F4")
                    : "<none>") +
                " camera=" +
                (camera == null
                    ? "<none>"
                    : camera.transform.position.ToString("F4")) +
                " " + details,
                this);
        }

        private bool TryGetVisualBounds(out Bounds bounds)
        {
            bounds = default;
            var found = false;
            foreach (var renderer in GetComponentsInChildren<Renderer>())
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

        public bool TrySettleToSupport(
            float maximumDown,
            float maximumUp,
            float clearance)
        {
            ResolveDependencies();
            if (controller == null || !controller.enabled ||
                !OntologyMotionDriverAdapter.Allows(
                    this,
                    OntologyMotionDriver.LocalCharacterController))
            {
                return false;
            }

            // Entry readiness consumes Unity's actual collision result first.
            // A DynamicProp or another ontology-permitted solid may already
            // support the capsule even though it is not classified as the
            // authored fallback surface used by the entry alignment probe.
            if (HasGroundContact)
            {
                return true;
            }

            if (!supportProbe.TryResolveGroundingOffset(
                    maximumDown,
                    maximumUp,
                    clearance,
                    out var offset))
            {
                return false;
            }

            var wasEnabled = controller.enabled;
            controller.enabled = false;
            transform.position += Vector3.up * offset;
            controller.enabled = wasEnabled;
            Physics.SyncTransforms();
            lastSupport = supportProbe.Probe();
            if (lastSupport.hasSupport &&
                wasEnabled &&
                controller.enabled)
            {
                // CharacterController.isGrounded reports the most recent Move.
                // Entry settling therefore performs one coordinator-owned
                // contact sample while input is still gated. This initializes
                // the controller state without waiting for the user's first
                // movement frame or adding a second runtime motion owner.
                lastCollisionFlags = controller.Move(
                    Vector3.down *
                    Mathf.Max(0.001f, controller.skinWidth * 0.25f));
                lastSupport = supportProbe.Probe();
            }
            return HasGroundContact;
        }

        public void RefreshSupport()
        {
            ResolveDependencies();
            lastSupport = supportProbe.Probe();
        }

        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (hit == null || hit.collider == null)
            {
                return;
            }

            lastHitCollider = hit.collider.name;
            lastHitNormal = hit.normal;
            lastHitRole =
                OntologyCollisionRoleAdapter.TryResolve(
                    hit.collider,
                    out var role)
                    ? role
                    : hit.collider.attachedRigidbody == null
                        ? OntologyCollisionRole.WalkableSupport
                        : OntologyCollisionRole.DynamicProp;
        }

        private void ResolveDependencies()
        {
            controller ??= GetComponent<CharacterController>();
            supportProbe ??=
                GetComponent<OntologyCharacterSupportProbe>();
        }

    }
}
