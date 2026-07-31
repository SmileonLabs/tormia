using System;
using System.Linq;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Presents an Authority-approved impact without allowing CharacterController
    /// and Rigidbody to write the same Transform at the same time.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OntologyCharacterImpactAdapter : MonoBehaviour
    {
        [SerializeField] private OntologyObject ontology;
        [SerializeField] private OntologyInputSystemPlayerInput playerInput;
        [SerializeField] private CharacterController characterController;
        [SerializeField] private Rigidbody targetBody;
        [SerializeField] private OntologyPhysicalProfile physicalProfile;
        [SerializeField] private bool ownsWorldTransform;

        private float reactionStartedAt;

        public bool OwnsWorldTransform => ownsWorldTransform;

        public void Configure(OntologyPhysicalProfile profile)
        {
            physicalProfile = profile;
            ResolveDependencies();
            if (profile == null)
            {
                EndTemporaryReaction();
                enabled = false;
            }
        }

        /// <summary>
        /// Called only with a vector produced by an accepted gameplay result.
        /// This adapter does not decide hit permission, damage, or force.
        /// </summary>
        public bool ApplyApprovedImpulse(Vector3 deltaVelocity)
        {
            ResolveDependencies();
            if (physicalProfile == null ||
                playerInput == null ||
                !IsFinite(deltaVelocity) ||
                !TryResolveImpactMode(out var mode))
            {
                return false;
            }

            return mode switch
            {
                OntologyCharacterImpactMode.ControllerImpulse =>
                    playerInput.ApplyApprovedExternalImpulse(
                        deltaVelocity,
                        physicalProfile.controllerImpulseDamping),
                OntologyCharacterImpactMode.TemporaryRigidbodyReaction =>
                    BeginTemporaryReaction(deltaVelocity),
                _ => false
            };
        }

        private void FixedUpdate()
        {
            if (!ownsWorldTransform ||
                targetBody == null ||
                physicalProfile == null)
            {
                return;
            }

            if (Time.time - reactionStartedAt <
                physicalProfile.rigidbodyMinimumReactionSeconds)
            {
                return;
            }

            if (targetBody.linearVelocity.sqrMagnitude >
                physicalProfile.rigidbodySettleSpeed *
                physicalProfile.rigidbodySettleSpeed)
            {
                return;
            }

            EndTemporaryReaction();
        }

        private bool BeginTemporaryReaction(Vector3 deltaVelocity)
        {
            if (ownsWorldTransform)
            {
                targetBody.AddForce(
                    deltaVelocity,
                    ForceMode.VelocityChange);
                return true;
            }

            if (targetBody == null ||
                characterController == null ||
                !HasIndependentSolidCollider())
            {
                return playerInput.ApplyApprovedExternalImpulse(
                    deltaVelocity,
                    physicalProfile.controllerImpulseDamping);
            }

            playerInput.SetPhysicsReactionActive(true);
            characterController.enabled = false;
            targetBody.isKinematic = false;
            targetBody.useGravity = true;
            targetBody.detectCollisions = true;
            targetBody.linearVelocity = Vector3.zero;
            targetBody.angularVelocity = Vector3.zero;
            targetBody.AddForce(deltaVelocity, ForceMode.VelocityChange);
            reactionStartedAt = Time.time;
            ownsWorldTransform = true;
            return true;
        }

        private void EndTemporaryReaction()
        {
            if (!ownsWorldTransform)
            {
                return;
            }

            if (targetBody != null)
            {
                targetBody.linearVelocity = Vector3.zero;
                targetBody.angularVelocity = Vector3.zero;
                targetBody.isKinematic = true;
                targetBody.useGravity = false;
            }
            ownsWorldTransform = false;
            if (characterController != null)
            {
                characterController.enabled = true;
            }
            playerInput?.SetPhysicsReactionActive(false);
        }

        private bool TryResolveImpactMode(
            out OntologyCharacterImpactMode mode)
        {
            mode = OntologyCharacterImpactMode.None;
            var values = ontology == null
                ? Array.Empty<string>()
                : ontology.Facts
                    .Where(value =>
                        value != null &&
                        value.predicate ==
                        OntologyPredicates.ImpactResponseProfile &&
                        !string.IsNullOrWhiteSpace(value.obj))
                    .Select(value => value.obj.Trim())
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
            if (values.Length != 1)
            {
                return false;
            }

            if (values[0] == OntologyObjects.ControllerImpulse)
            {
                mode = OntologyCharacterImpactMode.ControllerImpulse;
                return true;
            }
            if (values[0] ==
                OntologyObjects.TemporaryRigidbodyReaction)
            {
                mode =
                    OntologyCharacterImpactMode
                        .TemporaryRigidbodyReaction;
                return true;
            }
            return false;
        }

        private bool HasIndependentSolidCollider()
        {
            return GetComponentsInChildren<Collider>(true)
                .Any(value =>
                    value != null &&
                    value != characterController &&
                    value.enabled &&
                    !value.isTrigger);
        }

        private void ResolveDependencies()
        {
            ontology ??= GetComponent<OntologyObject>();
            playerInput ??=
                GetComponent<OntologyInputSystemPlayerInput>();
            characterController ??=
                GetComponent<CharacterController>();
            targetBody ??= GetComponent<Rigidbody>();
        }

        private void OnDisable()
        {
            EndTemporaryReaction();
        }

        private static bool IsFinite(Vector3 value)
        {
            return float.IsFinite(value.x) &&
                   float.IsFinite(value.y) &&
                   float.IsFinite(value.z);
        }
    }
}
