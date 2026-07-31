using System.Collections.Generic;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Owns temporary Unity physics overrides requested by presentation adapters.
    /// Semantic rules never change Rigidbody or Collider state directly.
    /// </summary>
    public sealed class OntologyPhysicsPresentationCoordinator : MonoBehaviour
    {
        private readonly Dictionary<Collider, bool> colliderStates = new();
        private Rigidbody targetBody;
        private bool baselineBodyIsKinematic;
        private bool baselineBodyUseGravity;
        private bool baselineBodyDetectCollisions;
        private bool hasBodyBaseline;
        private bool attachmentOverrideActive;
        private bool worldEditOverrideActive;

        public void CaptureWorldBaseline()
        {
            if (attachmentOverrideActive || worldEditOverrideActive)
            {
                return;
            }

            targetBody = GetComponent<Rigidbody>();
            hasBodyBaseline = targetBody != null;
            if (targetBody != null)
            {
                baselineBodyIsKinematic = targetBody.isKinematic;
                baselineBodyUseGravity = targetBody.useGravity;
                baselineBodyDetectCollisions = targetBody.detectCollisions;
            }

            colliderStates.Clear();
            foreach (var collider in GetComponentsInChildren<Collider>(true))
            {
                if (collider != null)
                {
                    colliderStates[collider] = collider.enabled;
                }
            }
        }

        public void SetAttachmentOverride(
            bool active,
            bool disablePhysics,
            bool disableColliders)
        {
            if (active == attachmentOverrideActive)
            {
                return;
            }

            if (active)
            {
                if (worldEditOverrideActive)
                {
                    SetWorldEditOverride(false);
                }

                CaptureWorldBaseline();
                if (targetBody != null && disablePhysics)
                {
                    if (!targetBody.isKinematic)
                    {
                        targetBody.linearVelocity = Vector3.zero;
                        targetBody.angularVelocity = Vector3.zero;
                    }
                    targetBody.useGravity = false;
                    targetBody.isKinematic = true;
                }

                if (disableColliders)
                {
                    foreach (var pair in colliderStates)
                    {
                        if (pair.Key != null)
                        {
                            pair.Key.enabled = false;
                        }
                    }
                }

                attachmentOverrideActive = true;
                return;
            }

            if (targetBody != null && disablePhysics && hasBodyBaseline)
            {
                targetBody.isKinematic = baselineBodyIsKinematic;
                targetBody.useGravity = baselineBodyUseGravity;
                targetBody.detectCollisions = baselineBodyDetectCollisions;
            }

            if (disableColliders)
            {
                foreach (var pair in colliderStates)
                {
                    if (pair.Key != null)
                    {
                        pair.Key.enabled = pair.Value;
                    }
                }
            }

            // An attachment is moved while its Rigidbody is kinematic and its colliders can
            // be disabled. Restoring the body with residual motion would carry a previous
            // contact impulse into the new world position, producing an artificial launch.
            // This is presentation cleanup only; ontology state remains unchanged.
            if (targetBody != null &&
                disablePhysics &&
                !targetBody.isKinematic)
            {
                targetBody.linearVelocity = Vector3.zero;
                targetBody.angularVelocity = Vector3.zero;
            }

            Physics.SyncTransforms();
            attachmentOverrideActive = false;
        }

        /// <summary>
        /// Clears motion while an attachment is still protected by its temporary override.
        /// Call this after selecting a safe world release position and before restoring
        /// Rigidbody and Collider presentation.
        /// </summary>
        public void PrepareForWorldRelease()
        {
            if (targetBody == null)
            {
                return;
            }

            if (!targetBody.isKinematic)
            {
                targetBody.linearVelocity = Vector3.zero;
                targetBody.angularVelocity = Vector3.zero;
            }
            Physics.SyncTransforms();
        }

        /// <summary>
        /// Temporarily suspends Rigidbody simulation while a world object is moved
        /// by the runtime editor. Colliders stay enabled so surface placement and
        /// selection continue to work. The captured world state is restored exactly.
        /// </summary>
        public void SetWorldEditOverride(bool active)
        {
            if (active == worldEditOverrideActive)
            {
                return;
            }

            if (active)
            {
                if (attachmentOverrideActive)
                {
                    return;
                }

                CaptureWorldBaseline();
                if (targetBody != null)
                {
                    if (!targetBody.isKinematic)
                    {
                        targetBody.linearVelocity = Vector3.zero;
                        targetBody.angularVelocity = Vector3.zero;
                    }
                    targetBody.useGravity = false;
                    targetBody.isKinematic = true;
                }

                worldEditOverrideActive = true;
                return;
            }

            if (targetBody != null && hasBodyBaseline)
            {
                targetBody.isKinematic = baselineBodyIsKinematic;
                targetBody.useGravity = baselineBodyUseGravity;
                targetBody.detectCollisions = baselineBodyDetectCollisions;
            }

            worldEditOverrideActive = false;
        }
    }
}
