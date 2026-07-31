using System;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Persists a coarse transform only after an ontology-selected Dynamic body
    /// settles. Physics samples remain ephemeral and never create per-frame
    /// Authority commands.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(OntologyPhysicalBodyAdapter))]
    public sealed class OntologyDynamicTransformCheckpointAdapter : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] private float settleDurationSeconds = 0.75f;
        [SerializeField, Min(0.1f)] private float retryCooldownSeconds = 2f;
        [SerializeField, Min(0f)] private float minimumPositionChange = 0.02f;
        [SerializeField, Min(0f)] private float minimumRotationChange = 0.5f;
        [SerializeField, Min(0f)] private float maximumLinearSpeed = 0.02f;
        [SerializeField, Min(0f)] private float maximumAngularSpeed = 0.05f;

        private OntologyPhysicalBodyAdapter physicalBody;
        private OntologyPlaceableInstance placeable;
        private OntologyWorldAuthorityBridge authorityBridge;
        private Vector3 lastCommittedPosition;
        private Quaternion lastCommittedRotation;
        private float stableSince = -1f;
        private float nextAttemptAt;
        private bool initialized;
        private bool commandPending;

        private void OnEnable()
        {
            initialized = false;
            commandPending = false;
            stableSince = -1f;
        }

        private void FixedUpdate()
        {
            ResolveDependencies();
            var body = physicalBody == null ? null : physicalBody.TargetBody;
            var profile = physicalBody == null ? null : physicalBody.PhysicalProfile;
            var attachment = GetComponent<OntologyAttachmentAdapter>();
            var canCheckpoint =
                profile != null &&
                profile.mobilityMode == OntologyPhysicalMobilityMode.Dynamic &&
                body != null &&
                !body.isKinematic &&
                (attachment == null || !attachment.OwnsWorldTransform);

            if (!canCheckpoint)
            {
                Rebaseline();
                initialized = false;
                return;
            }

            if (!initialized)
            {
                Rebaseline();
                initialized = true;
                nextAttemptAt = Time.unscaledTime + settleDurationSeconds;
                return;
            }

            var isStable =
                body.IsSleeping() ||
                (body.linearVelocity.sqrMagnitude <=
                 maximumLinearSpeed * maximumLinearSpeed &&
                 body.angularVelocity.sqrMagnitude <=
                 maximumAngularSpeed * maximumAngularSpeed);
            if (!isStable)
            {
                stableSince = -1f;
                return;
            }

            if (stableSince < 0f)
            {
                stableSince = Time.unscaledTime;
                return;
            }

            if (commandPending ||
                Time.unscaledTime < nextAttemptAt ||
                Time.unscaledTime - stableSince < settleDurationSeconds ||
                !HasMeaningfulChange(
                    transform.position,
                    transform.rotation,
                    lastCommittedPosition,
                    lastCommittedRotation,
                    minimumPositionChange,
                    minimumRotationChange))
            {
                return;
            }

            commandPending = authorityBridge != null &&
                             authorityBridge.TryPublishDynamicTransformCheckpoint(
                                 placeable,
                                 HandleCheckpointCompleted);
            if (!commandPending)
            {
                nextAttemptAt = Time.unscaledTime + retryCooldownSeconds;
            }
        }

        public static bool HasMeaningfulChange(
            Vector3 position,
            Quaternion rotation,
            Vector3 baselinePosition,
            Quaternion baselineRotation,
            float minimumPositionChange,
            float minimumRotationChange) =>
            Vector3.Distance(position, baselinePosition) >=
            Mathf.Max(0f, minimumPositionChange) ||
            Quaternion.Angle(rotation, baselineRotation) >=
            Mathf.Max(0f, minimumRotationChange);

        private void HandleCheckpointCompleted(bool accepted)
        {
            commandPending = false;
            nextAttemptAt = Time.unscaledTime + retryCooldownSeconds;
            if (!accepted)
            {
                return;
            }

            lastCommittedPosition = transform.position;
            lastCommittedRotation = transform.rotation;
            stableSince = Time.unscaledTime;
        }

        private void Rebaseline()
        {
            lastCommittedPosition = transform.position;
            lastCommittedRotation = transform.rotation;
            stableSince = -1f;
        }

        private void ResolveDependencies()
        {
            physicalBody ??= GetComponent<OntologyPhysicalBodyAdapter>();
            placeable ??= GetComponent<OntologyPlaceableInstance>();
            authorityBridge ??=
                FindAnyObjectByType<OntologyWorldAuthorityBridge>();
        }
    }
}
