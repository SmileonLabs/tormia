using System;

namespace Tormia.Ontology.Core
{
    public enum OntologyAnimationStateKind
    {
        Idle = 0,
        Locomotion = 1,
        Airborne = 2,
        Equipment = 3,
        JumpStart = 4,
        Landing = 5
    }

    public readonly struct OntologyAnimationStateSnapshot
    {
        public OntologyAnimationStateSnapshot(
            bool isMoving,
            bool isRunning,
            bool isGrounded,
            float verticalVelocity,
            string equipmentIdleIntent,
            string equipmentMoveIntent,
            string baseIdleIntent = null,
            string baseMoveIntent = null,
            string baseFastMoveIntent = null,
            uint jumpOccurrence = 0,
            string currentPresentationIntent = null,
            bool currentPresentationCompleted = false)
        {
            IsMoving = isMoving;
            IsRunning = isRunning;
            IsGrounded = isGrounded;
            VerticalVelocity = verticalVelocity;
            EquipmentIdleIntent = equipmentIdleIntent ?? string.Empty;
            EquipmentMoveIntent = equipmentMoveIntent ?? string.Empty;
            BaseIdleIntent = string.IsNullOrWhiteSpace(baseIdleIntent)
                ? OntologyAnimationIntentIds.Idle
                : baseIdleIntent;
            BaseMoveIntent = string.IsNullOrWhiteSpace(baseMoveIntent)
                ? OntologyAnimationIntentIds.Locomotion
                : baseMoveIntent;
            BaseFastMoveIntent = string.IsNullOrWhiteSpace(baseFastMoveIntent)
                ? OntologyAnimationIntentIds.FastLocomotion
                : baseFastMoveIntent;
            JumpOccurrence = jumpOccurrence;
            CurrentPresentationIntent =
                currentPresentationIntent ?? string.Empty;
            CurrentPresentationCompleted =
                currentPresentationCompleted;
        }

        public bool IsMoving { get; }
        public bool IsRunning { get; }
        public bool IsGrounded { get; }
        public float VerticalVelocity { get; }
        public string EquipmentIdleIntent { get; }
        public string EquipmentMoveIntent { get; }
        public string BaseIdleIntent { get; }
        public string BaseMoveIntent { get; }
        public string BaseFastMoveIntent { get; }
        public uint JumpOccurrence { get; }
        public string CurrentPresentationIntent { get; }
        public bool CurrentPresentationCompleted { get; }

        public bool HasCompletedPresentation(string intent)
        {
            return CurrentPresentationCompleted &&
                   string.Equals(
                       CurrentPresentationIntent,
                       intent,
                       StringComparison.Ordinal);
        }
    }

    public readonly struct OntologyResolvedAnimationState
    {
        public OntologyResolvedAnimationState(
            string intent,
            OntologyAnimationStateKind kind)
        {
            Intent = intent ?? string.Empty;
            Kind = kind;
        }

        public string Intent { get; }
        public OntologyAnimationStateKind Kind { get; }
    }

    /// <summary>
    /// Resolves ephemeral movement observations into canonical presentation
    /// intent. It never authors a durable Fact or decides gameplay permission.
    /// </summary>
    public sealed class OntologyAnimationStateResolver
    {
        private readonly float fallVelocityThreshold;
        private bool initialized;
        private bool wasGrounded = true;
        private bool observedAirborne;
        private uint lastJumpOccurrence;
        private OntologyAnimationStateKind phase =
            OntologyAnimationStateKind.Idle;

        public OntologyAnimationStateResolver(
            float fallVelocityThreshold = -2f)
        {
            this.fallVelocityThreshold = fallVelocityThreshold;
        }

        public OntologyResolvedAnimationState Resolve(
            OntologyAnimationStateSnapshot snapshot)
        {
            if (!initialized)
            {
                initialized = true;
                wasGrounded = snapshot.IsGrounded;
                lastJumpOccurrence = snapshot.JumpOccurrence;
            }

            if (snapshot.JumpOccurrence != lastJumpOccurrence)
            {
                lastJumpOccurrence = snapshot.JumpOccurrence;
                observedAirborne = true;
                wasGrounded = false;
                phase = OntologyAnimationStateKind.JumpStart;
                return new OntologyResolvedAnimationState(
                    OntologyAnimationIntentIds.JumpStart,
                    phase);
            }

            if (phase == OntologyAnimationStateKind.JumpStart)
            {
                if (!snapshot.IsGrounded)
                {
                    observedAirborne = true;
                    wasGrounded = false;
                    if (snapshot.VerticalVelocity <= 0f)
                    {
                        // Collision-resolved motion owns the physical phase.
                        // A long takeoff clip cannot keep presenting an upward
                        // phase after the avatar has reached its apex.
                        phase = OntologyAnimationStateKind.Airborne;
                    }
                }

                if (phase == OntologyAnimationStateKind.JumpStart &&
                    !snapshot.HasCompletedPresentation(
                        OntologyAnimationIntentIds.JumpStart))
                {
                    return new OntologyResolvedAnimationState(
                        OntologyAnimationIntentIds.JumpStart,
                        phase);
                }

                phase = OntologyAnimationStateKind.Airborne;
            }

            if (!snapshot.IsGrounded)
            {
                observedAirborne = true;
                wasGrounded = false;
                phase = OntologyAnimationStateKind.Airborne;
                return new OntologyResolvedAnimationState(
                    snapshot.VerticalVelocity <= fallVelocityThreshold
                        ? OntologyAnimationIntentIds.Fall
                        : OntologyAnimationIntentIds.Airborne,
                    phase);
            }

            var justLanded =
                observedAirborne &&
                !wasGrounded &&
                snapshot.VerticalVelocity <= 0f;
            wasGrounded = true;
            if (justLanded)
            {
                phase = OntologyAnimationStateKind.Landing;
            }

            if (phase == OntologyAnimationStateKind.Landing &&
                !snapshot.HasCompletedPresentation(
                    OntologyAnimationIntentIds.Landing))
            {
                return new OntologyResolvedAnimationState(
                    OntologyAnimationIntentIds.Landing,
                    phase);
            }

            phase = OntologyAnimationStateKind.Idle;
            observedAirborne = false;
            if (snapshot.IsMoving)
            {
                if (!string.IsNullOrWhiteSpace(snapshot.EquipmentMoveIntent))
                {
                    return new OntologyResolvedAnimationState(
                        snapshot.EquipmentMoveIntent,
                        OntologyAnimationStateKind.Equipment);
                }

                return new OntologyResolvedAnimationState(
                    snapshot.IsRunning
                        ? snapshot.BaseFastMoveIntent
                        : snapshot.BaseMoveIntent,
                    OntologyAnimationStateKind.Locomotion);
            }

            if (!string.IsNullOrWhiteSpace(snapshot.EquipmentIdleIntent))
            {
                return new OntologyResolvedAnimationState(
                    snapshot.EquipmentIdleIntent,
                    OntologyAnimationStateKind.Equipment);
            }

            return new OntologyResolvedAnimationState(
                snapshot.BaseIdleIntent,
                OntologyAnimationStateKind.Idle);
        }

        public void Reset()
        {
            initialized = false;
            wasGrounded = true;
            observedAirborne = false;
            lastJumpOccurrence = 0;
            phase = OntologyAnimationStateKind.Idle;
        }
    }
}
