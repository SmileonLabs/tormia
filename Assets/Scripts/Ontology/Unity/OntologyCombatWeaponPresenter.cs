using UnityEngine;

namespace Tormia.Ontology.Core
{
    [DisallowMultipleComponent]
    public sealed class OntologyCombatWeaponPresenter : MonoBehaviour
    {
        [SerializeField] private OntologyAuthorityEntityIdentity authorityIdentity;
        [SerializeField] private OntologyObject ontologyObject;
        [SerializeField] private Transform slashVfxAnchor;
        [SerializeField, Tooltip(
            "Authored blade/contact volume. It remains usable as query geometry " +
            "while attachment presentation disables world colliders.")]
        private BoxCollider contactVolume;
        [SerializeField] private LayerMask contactCandidateLayers = ~0;
        [SerializeField] private string contactModeId =
            OntologyObjects.WeaponContactWindow;
        [SerializeField, Min(0.01f), Tooltip(
            "Maximum linear distance between continuous contact samples.")]
        private float contactSweepLinearStep = 0.08f;
        [SerializeField, Min(1f), Tooltip(
            "Maximum angular change between continuous contact samples.")]
        private float contactSweepAngularStep = 10f;
        [SerializeField, Range(1, 32), Tooltip(
            "Safety cap for per-frame swept contact samples.")]
        private int maximumContactSweepSubsteps = 12;
        [SerializeField] private string swingVfxIntent = string.Empty;
        [SerializeField, Tooltip(
            "Local blade axis used to orient the sweep plane. Adjust this only " +
            "when an authored weapon's long axis is not local Y.")]
        private Vector3 sweepBladeAxis = Vector3.up;
        [SerializeField, Min(0f), Tooltip(
            "Presentation-only movement threshold for establishing a stable " +
            "sweep direction. It never affects contact or damage.")]
        private float minimumSweepSpeed = 0.05f;
        private Collider[] contactBuffer = new Collider[32];
        private Transform dynamicSweepVfxAnchor;
        private Vector3 previousSweepPosition;
        private float sweepTrackingUntil;
        private bool sweepTracking;
        private bool sweepOrientationReady;
        private Vector3 previousContactCenter;
        private Vector3 previousContactHalfExtents;
        private Quaternion previousContactOrientation;
        private bool hasPreviousContactSample;

        public OntologyAuthorityEntityIdentity AuthorityIdentity =>
            authorityIdentity != null
                ? authorityIdentity
                : authorityIdentity = GetComponent<OntologyAuthorityEntityIdentity>();
        public OntologyObject OntologyObject =>
            ontologyObject != null ? ontologyObject : ontologyObject = GetComponent<OntologyObject>();
        public Transform SlashVfxAnchor => slashVfxAnchor != null ? slashVfxAnchor : transform;
        public BoxCollider ContactVolume =>
            contactVolume != null
                ? contactVolume
                : contactVolume = GetComponent<BoxCollider>();
        public string ContactModeId => contactModeId;
        public string SwingVfxIntent => swingVfxIntent;

        private void Awake()
        {
            OntologyRenderPipelineMaterialAdapter.ApplyTo(gameObject);
        }

        private void LateUpdate()
        {
            UpdateSweepTracking();
        }

        private void OnDisable()
        {
            sweepTracking = false;
            sweepOrientationReady = false;
        }

        public void Configure(
            string canonicalSwingVfxIntent,
            Transform effectAnchor,
            BoxCollider authoredContactVolume = null,
            string canonicalContactModeId =
                OntologyObjects.WeaponContactWindow)
        {
            swingVfxIntent = canonicalSwingVfxIntent;
            slashVfxAnchor = effectAnchor;
            contactVolume = authoredContactVolume != null
                ? authoredContactVolume
                : GetComponent<BoxCollider>();
            contactModeId = string.IsNullOrWhiteSpace(
                    canonicalContactModeId)
                ? string.Empty
                : canonicalContactModeId.Trim();
            authorityIdentity = GetComponent<OntologyAuthorityEntityIdentity>();
            ontologyObject = GetComponent<OntologyObject>();
        }

        public bool SupportsContactMode(string canonicalContactModeId)
        {
            return !string.IsNullOrWhiteSpace(canonicalContactModeId) &&
                   string.Equals(
                       contactModeId,
                       canonicalContactModeId.Trim(),
                       System.StringComparison.Ordinal) &&
                   ContactVolume != null;
        }

        /// <summary>
        /// Starts an ephemeral visual observation for one Authority-approved
        /// swing. The clip duration comes from the animation manifest.
        /// </summary>
        public void BeginSweepTracking(float clipDuration)
        {
            var source = SlashVfxAnchor;
            if (source == null)
                return;

            EnsureDynamicSweepAnchor();
            previousSweepPosition = source.position;
            sweepTrackingUntil =
                Time.unscaledTime + Mathf.Max(0.05f, clipDuration);
            sweepTracking = true;
            sweepOrientationReady = false;
            hasPreviousContactSample = false;
            dynamicSweepVfxAnchor.SetPositionAndRotation(
                source.position,
                source.rotation);
        }

        public bool TryGetDynamicSweepVfxAnchor(out Transform anchor)
        {
            anchor = sweepOrientationReady
                ? dynamicSweepVfxAnchor
                : null;
            return anchor != null;
        }

        private void UpdateSweepTracking()
        {
            if (!sweepTracking)
                return;
            if (Time.unscaledTime > sweepTrackingUntil)
            {
                sweepTracking = false;
                return;
            }

            var source = SlashVfxAnchor;
            if (source == null)
            {
                sweepTracking = false;
                return;
            }

            var currentPosition = source.position;
            var movement = currentPosition - previousSweepPosition;
            previousSweepPosition = currentPosition;
            var axisTransform = ContactVolume == null
                ? transform
                : ContactVolume.transform;
            var bladeAxis = axisTransform.TransformDirection(
                sweepBladeAxis.sqrMagnitude <= Mathf.Epsilon
                    ? Vector3.up
                    : sweepBladeAxis.normalized);
            var minimumFrameDistance =
                minimumSweepSpeed * Mathf.Max(Time.unscaledDeltaTime, 0f);
            if (!TryCalculateSweepRotation(
                    bladeAxis,
                    movement,
                    minimumFrameDistance,
                    out var rotation))
            {
                return;
            }

            EnsureDynamicSweepAnchor();
            dynamicSweepVfxAnchor.SetPositionAndRotation(
                currentPosition,
                rotation);
            sweepOrientationReady = true;
        }

        private void EnsureDynamicSweepAnchor()
        {
            if (dynamicSweepVfxAnchor != null)
                return;
            var anchorObject = new GameObject("DynamicSweepVfxAnchor");
            dynamicSweepVfxAnchor = anchorObject.transform;
            dynamicSweepVfxAnchor.SetParent(transform, true);
        }

        public static bool TryCalculateSweepRotation(
            Vector3 bladeAxis,
            Vector3 movement,
            float minimumDistance,
            out Quaternion rotation)
        {
            rotation = Quaternion.identity;
            if (bladeAxis.sqrMagnitude <= Mathf.Epsilon)
                return false;

            var axis = bladeAxis.normalized;
            var planarMovement =
                movement - Vector3.Project(movement, axis);
            var threshold = Mathf.Max(0f, minimumDistance);
            if (planarMovement.sqrMagnitude <= threshold * threshold)
                return false;

            var direction = planarMovement.normalized;
            var planeNormal = Vector3.Cross(direction, axis);
            if (planeNormal.sqrMagnitude <= Mathf.Epsilon)
                return false;

            rotation = Quaternion.LookRotation(
                planeNormal.normalized,
                axis);
            return true;
        }

        /// <summary>
        /// Estimates only the navigation stop distance from authored physical
        /// geometry. Authority attack_range remains the maximum gameplay range,
        /// and an actual overlap is still required during the contact window.
        /// </summary>
        public bool TryEstimateContactApproachDistance(
            Vector3 actorPosition,
            OntologyCombatTargetPresenter approvedTarget,
            out float approachDistance)
        {
            approachDistance = 0f;
            var volume = ContactVolume;
            var targetCollider = approvedTarget?.InteractionCollider;
            if (volume == null || targetCollider == null)
                return false;

            CalculateWorldBox(
                volume,
                out var center,
                out var halfExtents,
                out var orientation);
            approachDistance = CalculatePlanarContactApproachDistance(
                actorPosition,
                center,
                halfExtents,
                orientation,
                targetCollider);
            return approachDistance > 0f;
        }

        /// <summary>
        /// Converts the current authored contact geometry into a conservative
        /// navigation stop distance. The calculation uses the oriented weapon
        /// surface facing the target instead of the Box diagonal, which could
        /// otherwise report that a vertical blade can reach sideways.
        /// </summary>
        public static float CalculatePlanarContactApproachDistance(
            Vector3 actorPosition,
            Vector3 contactCenter,
            Vector3 contactHalfExtents,
            Quaternion contactOrientation,
            Collider targetCollider)
        {
            if (targetCollider == null)
                return 0f;

            var actorToTarget =
                targetCollider.bounds.center - actorPosition;
            actorToTarget.y = 0f;
            var currentActorTargetDistance = actorToTarget.magnitude;
            if (currentActorTargetDistance <= Mathf.Epsilon)
                return 0f;

            var closestTargetPoint =
                targetCollider.ClosestPoint(contactCenter);
            var contactToTarget =
                closestTargetPoint - contactCenter;
            contactToTarget.y = 0f;
            if (contactToTarget.sqrMagnitude <= Mathf.Epsilon)
                return currentActorTargetDistance;

            var direction = contactToTarget.normalized;
            var contactPlanarRadius =
                CalculateOrientedPlanarRadius(
                    contactHalfExtents,
                    contactOrientation,
                    direction);
            var planarGap = Mathf.Max(
                0f,
                contactToTarget.magnitude -
                contactPlanarRadius);
            return Mathf.Max(
                0f,
                currentActorTargetDistance - planarGap);
        }

        public static float CalculateOrientedPlanarRadius(
            Vector3 halfExtents,
            Quaternion orientation,
            Vector3 planarDirection)
        {
            planarDirection.y = 0f;
            if (planarDirection.sqrMagnitude <= Mathf.Epsilon)
                return 0f;

            var direction = planarDirection.normalized;
            var right = orientation * Vector3.right;
            var up = orientation * Vector3.up;
            var forward = orientation * Vector3.forward;
            return
                Mathf.Abs(Vector3.Dot(right, direction)) *
                Mathf.Abs(halfExtents.x) +
                Mathf.Abs(Vector3.Dot(up, direction)) *
                Mathf.Abs(halfExtents.y) +
                Mathf.Abs(Vector3.Dot(forward, direction)) *
                Mathf.Abs(halfExtents.z);
        }

        /// <summary>
        /// Records one non-authoritative pose sample without reporting impact.
        /// The combat controller uses this immediately before the manifest
        /// contact window so the first in-window sweep starts from the adjacent
        /// rendered pose rather than an unrelated idle pose.
        /// </summary>
        public void PrimeContactSample()
        {
            var volume = ContactVolume;
            if (volume == null)
                return;

            CalculateWorldBox(
                volume,
                out var center,
                out var halfExtents,
                out var orientation);
            RememberContactSample(
                center,
                halfExtents,
                orientation);
        }

        /// <summary>
        /// Observes overlap between the authored weapon volume and only the
        /// target already approved by the Authority route preview. This method
        /// never changes Facts, health, death, cooldown, or loot.
        /// </summary>
        public bool TryObserveApprovedContact(
            OntologyCombatTargetPresenter approvedTarget,
            out Vector3 contactPoint)
        {
            contactPoint = default;
            var volume = ContactVolume;
            if (approvedTarget == null || volume == null)
                return false;

            CalculateWorldBox(
                volume,
                out var center,
                out var halfExtents,
                out var orientation);
            if (TryObserveApprovedContactAt(
                    approvedTarget,
                    center,
                    halfExtents,
                    orientation,
                    out contactPoint))
            {
                RememberContactSample(
                    center,
                    halfExtents,
                    orientation);
                return true;
            }

            if (hasPreviousContactSample)
            {
                var substeps = CalculateContactSweepSubsteps(
                    Vector3.Distance(
                        previousContactCenter,
                        center),
                    Quaternion.Angle(
                        previousContactOrientation,
                        orientation),
                    contactSweepLinearStep,
                    contactSweepAngularStep,
                    maximumContactSweepSubsteps);
                for (var step = 1; step < substeps; step++)
                {
                    var amount = step / (float)substeps;
                    var sampleCenter = Vector3.Lerp(
                        previousContactCenter,
                        center,
                        amount);
                    var sampleHalfExtents = Vector3.Lerp(
                        previousContactHalfExtents,
                        halfExtents,
                        amount);
                    var sampleOrientation = Quaternion.Slerp(
                        previousContactOrientation,
                        orientation,
                        amount);
                    if (!TryObserveApprovedContactAt(
                            approvedTarget,
                            sampleCenter,
                            sampleHalfExtents,
                            sampleOrientation,
                            out contactPoint))
                    {
                        continue;
                    }

                    RememberContactSample(
                        center,
                        halfExtents,
                        orientation);
                    return true;
                }
            }

            RememberContactSample(
                center,
                halfExtents,
                orientation);
            return false;
        }

        private bool TryObserveApprovedContactAt(
            OntologyCombatTargetPresenter approvedTarget,
            Vector3 center,
            Vector3 halfExtents,
            Quaternion orientation,
            out Vector3 contactPoint)
        {
            contactPoint = default;
            var count = Physics.OverlapBoxNonAlloc(
                center,
                halfExtents,
                contactBuffer,
                orientation,
                contactCandidateLayers,
                QueryTriggerInteraction.Collide);
            if (count == contactBuffer.Length)
            {
                // A full NonAlloc buffer is ambiguous: Unity may have omitted
                // the approved target. Allocate only on this exceptional path
                // so crowded scenes never produce a false no-contact result.
                contactBuffer = Physics.OverlapBox(
                    center,
                    halfExtents,
                    orientation,
                    contactCandidateLayers,
                    QueryTriggerInteraction.Collide);
                count = contactBuffer.Length;
            }
            for (var index = 0; index < count; index++)
            {
                var candidate = contactBuffer[index];
                if (candidate == null ||
                    candidate.transform.IsChildOf(transform) ||
                    candidate.GetComponentInParent<
                        OntologyCombatTargetPresenter>() != approvedTarget)
                {
                    continue;
                }

                contactPoint = candidate.ClosestPoint(center);
                return true;
            }

            return false;
        }

        private void RememberContactSample(
            Vector3 center,
            Vector3 halfExtents,
            Quaternion orientation)
        {
            previousContactCenter = center;
            previousContactHalfExtents = halfExtents;
            previousContactOrientation = orientation;
            hasPreviousContactSample = true;
        }

        public static int CalculateContactSweepSubsteps(
            float linearDistance,
            float angularDistance,
            float linearStep,
            float angularStep,
            int maximumSubsteps)
        {
            var linearCount = Mathf.CeilToInt(
                Mathf.Max(0f, linearDistance) /
                Mathf.Max(0.01f, linearStep));
            var angularCount = Mathf.CeilToInt(
                Mathf.Max(0f, angularDistance) /
                Mathf.Max(1f, angularStep));
            return Mathf.Clamp(
                Mathf.Max(1, linearCount, angularCount),
                1,
                Mathf.Max(1, maximumSubsteps));
        }

        public static void CalculateWorldBox(
            BoxCollider volume,
            out Vector3 center,
            out Vector3 halfExtents,
            out Quaternion orientation)
        {
            if (volume == null)
            {
                center = default;
                halfExtents = default;
                orientation = Quaternion.identity;
                return;
            }

            var target = volume.transform;
            var scale = target.lossyScale;
            center = target.TransformPoint(volume.center);
            halfExtents = Vector3.Scale(
                volume.size * 0.5f,
                new Vector3(
                    Mathf.Abs(scale.x),
                    Mathf.Abs(scale.y),
                    Mathf.Abs(scale.z)));
            orientation = target.rotation;
        }
    }
}
