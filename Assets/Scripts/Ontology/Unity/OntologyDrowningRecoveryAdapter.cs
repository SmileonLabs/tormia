using System.Collections.Generic;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Presents an ontology-derived drowning state by sinking the actor and returning it
    /// to a recently observed safe ground position. Rules decide when drowning occurs;
    /// this adapter only performs the physical presentation and recovery.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(OntologyWaterPresenceSensor))]
    public sealed class OntologyDrowningRecoveryAdapter : MonoBehaviour
    {
        [SerializeField] private OntologyWorldBootstrap bootstrap;
        [SerializeField] private OntologyObject actorObject;
        [SerializeField, Tooltip("Legacy fallback only. Leave empty; the OntologyObject id is authoritative.")]
        private string actorId;
        [SerializeField] private string movementModePredicate = "movement_mode";
        [SerializeField] private string drowningMode = "Drowning";

        [Header("Safe Position Observation")]
        [SerializeField, Min(0.1f)] private float safePositionHistorySeconds = 0.75f;
        [SerializeField, Min(0.02f)] private float safeSampleInterval = 0.1f;
        [SerializeField, Min(1f)] private float retainedHistorySeconds = 4f;

        [Header("Recovery Presentation")]
        [SerializeField, Min(0.1f)] private float sinkDistance = 2f;
        [SerializeField, Min(0.1f)] private float sinkDuration = 0.8f;
        [SerializeField, Min(0f)] private float postRecoveryLockSeconds = 0.35f;
        [SerializeField] private LayerMask recoveryGroundLayers = Physics.DefaultRaycastLayers;
        [SerializeField, Min(0.1f)] private float recoveryGroundProbeHeight = 3f;
        [SerializeField, Min(0.1f)] private float recoveryGroundProbeDistance = 12f;
        [SerializeField, Min(0f)] private float maximumRecoveryGroundRise = 0.5f;
        [SerializeField, Min(0f)] private float recoveryGroundClearance = 0.05f;

        private readonly Queue<SafePositionSample> safeSamples = new();
        private CharacterController characterController;
        private OntologyWaterPresenceSensor waterPresenceSensor;
        private Vector3 fallbackSafePosition;
        private Quaternion fallbackSafeRotation;
        private float nextSafeSampleTime;
        private bool recoveryActive;
        private bool positionRestored;
        private float recoveryStartedAt;
        private float recoveryLockUntil;
        private Vector3 sinkStartPosition;
        private Vector3 recoveryPosition;
        private Quaternion recoveryRotation;
        private string ActorId => actorObject != null &&
                                  !string.IsNullOrWhiteSpace(actorObject.EntityId)
            ? actorObject.EntityId
            : actorId;

        private void Awake()
        {
            characterController = GetComponent<CharacterController>();
            if (actorObject == null) actorObject = GetComponentInParent<OntologyObject>();
            waterPresenceSensor = GetComponent<OntologyWaterPresenceSensor>();
            if (bootstrap == null)
            {
                bootstrap = FindAnyObjectByType<OntologyWorldBootstrap>();
            }

            fallbackSafePosition = transform.position;
            fallbackSafeRotation = transform.rotation;
        }

        private void Update()
        {
            if (!recoveryActive && Time.time >= recoveryLockUntil)
            {
                ObserveSafePosition();
            }
        }

        /// <summary>
        /// Returns true while the drowning presentation owns actor movement.
        /// </summary>
        public bool TryRecover(float deltaTime)
        {
            if (recoveryActive && !IsDrowning())
            {
                CancelRecovery();
                return false;
            }

            if (!recoveryActive)
            {
                if (Time.time < recoveryLockUntil)
                {
                    return true;
                }

                if (!IsDrowning())
                {
                    return false;
                }

                BeginRecovery();
            }

            if (!positionRestored)
            {
                var duration = Mathf.Max(0.1f, sinkDuration);
                var progress = Mathf.Clamp01((Time.time - recoveryStartedAt) / duration);
                transform.position = Vector3.Lerp(
                    sinkStartPosition,
                    sinkStartPosition + Vector3.down * Mathf.Max(0.1f, sinkDistance),
                    progress);

                if (progress < 1f)
                {
                    return true;
                }

                RestoreSafePosition();
                return true;
            }

            if (Time.time < recoveryLockUntil || IsDrowning())
            {
                return true;
            }

            recoveryActive = false;
            positionRestored = false;
            return false;
        }

        private void CancelRecovery()
        {
            // Drowning is an ontology-derived relation. Its presentation must stop as
            // soon as that relation is retracted; otherwise a stale animation/physics
            // process would override the current semantic state (for example Swimming).
            if (characterController != null && !characterController.enabled)
            {
                characterController.enabled = true;
            }

            recoveryActive = false;
            positionRestored = false;
            recoveryLockUntil = 0f;
        }

        private bool IsDrowning()
        {
            if (bootstrap == null)
            {
                bootstrap = FindAnyObjectByType<OntologyWorldBootstrap>();
            }

            return bootstrap != null
                && bootstrap.World != null
                && bootstrap.World.HasFact(ActorId, movementModePredicate, drowningMode);
        }

        private void ObserveSafePosition()
        {
            if (characterController == null || waterPresenceSensor == null ||
                !characterController.enabled || !characterController.isGrounded ||
                waterPresenceSensor.HasActiveWaterOverlap ||
                Time.time < nextSafeSampleTime)
            {
                return;
            }

            nextSafeSampleTime = Time.time + Mathf.Max(0.02f, safeSampleInterval);
            safeSamples.Enqueue(new SafePositionSample(Time.time, transform.position, transform.rotation));
            var oldestAllowedTime = Time.time - Mathf.Max(1f, retainedHistorySeconds);
            while (safeSamples.Count > 1 && safeSamples.Peek().Time < oldestAllowedTime)
            {
                safeSamples.Dequeue();
            }
        }

        private void BeginRecovery()
        {
            recoveryPosition = fallbackSafePosition;
            recoveryRotation = fallbackSafeRotation;
            var requiredAge = Mathf.Max(0.1f, safePositionHistorySeconds);
            foreach (var sample in safeSamples)
            {
                if (Time.time - sample.Time >= requiredAge)
                {
                    recoveryPosition = sample.Position;
                    recoveryRotation = sample.Rotation;
                }
            }

            recoveryActive = true;
            positionRestored = false;
            recoveryStartedAt = Time.time;
            sinkStartPosition = transform.position;
            if (characterController != null)
            {
                characterController.enabled = false;
            }
        }

        private void RestoreSafePosition()
        {
            recoveryPosition = SnapRootToGround(recoveryPosition);
            transform.SetPositionAndRotation(recoveryPosition, recoveryRotation);
            if (characterController != null)
            {
                characterController.enabled = true;
            }

            positionRestored = true;
            recoveryLockUntil = Time.time + Mathf.Max(0f, postRecoveryLockSeconds);
        }

        private Vector3 SnapRootToGround(Vector3 candidate)
        {
            var probeHeight = Mathf.Max(0.1f, recoveryGroundProbeHeight);
            var origin = candidate + Vector3.up * probeHeight;
            var hits = Physics.RaycastAll(
                origin,
                Vector3.down,
                probeHeight + Mathf.Max(0.1f, recoveryGroundProbeDistance),
                recoveryGroundLayers,
                QueryTriggerInteraction.Ignore);

            var maximumSurfaceHeight = candidate.y + Mathf.Max(0f, maximumRecoveryGroundRise);
            var foundGround = false;
            var groundHeight = float.MinValue;
            foreach (var hit in hits)
            {
                if (hit.collider == null || hit.collider.transform.IsChildOf(transform) ||
                    hit.collider.GetComponentInParent<OntologyWaterRegionVolume>() != null ||
                    hit.point.y > maximumSurfaceHeight)
                {
                    continue;
                }

                if (!foundGround || hit.point.y > groundHeight)
                {
                    foundGround = true;
                    groundHeight = hit.point.y;
                }
            }

            if (!foundGround)
            {
                return candidate;
            }

            var controllerBottomFromRoot = characterController == null
                ? 0f
                : (characterController.center.y - characterController.height * 0.5f)
                  * Mathf.Abs(transform.lossyScale.y);
            candidate.y = groundHeight - controllerBottomFromRoot + Mathf.Max(0f, recoveryGroundClearance);
            return candidate;
        }

        private readonly struct SafePositionSample
        {
            public SafePositionSample(float time, Vector3 position, Quaternion rotation)
            {
                Time = time;
                Position = position;
                Rotation = rotation;
            }

            public float Time { get; }
            public Vector3 Position { get; }
            public Quaternion Rotation { get; }
        }
    }
}
