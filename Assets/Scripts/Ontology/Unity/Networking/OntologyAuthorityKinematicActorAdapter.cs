using System;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Presents ephemeral autonomous-actor motion accepted by World Authority.
    /// It never chooses a target, evaluates combat, or infers that a mesh is a
    /// monster. The adapter is enabled only by the AuthorityKinematic Physical
    /// Meaning selected through the entity's authored ontology package.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(OntologyAuthorityEntityIdentity))]
    public sealed class OntologyAuthorityKinematicActorAdapter :
        MonoBehaviour
    {
        [SerializeField] private OntologyWorldAuthorityClient authorityClient;
        [SerializeField] private OntologyAuthorityEntityIdentity identity;
        [SerializeField] private OntologyAnimationAdapter animationAdapter;
        [SerializeField, Min(0.1f)] private float maximumPresentationSpeed = 12f;
        [SerializeField, Min(1f)] private float rotationSharpness = 12f;
        [SerializeField, TextArea] private string lastStatus;

        private Vector3 targetPosition;
        private Vector3 targetForward = Vector3.forward;
        private bool hasState;
        private long lastPresentationSequence = -1;

        public bool OwnsWorldTransform =>
            enabled &&
            isActiveAndEnabled &&
            hasState &&
            authorityClient != null &&
            authorityClient.IsWorldRuntimeReady &&
            OntologyMotionDriverAdapter.Allows(
                this,
                OntologyMotionDriver.AuthorityKinematic);

        private void Awake()
        {
            ResolveDependencies();
        }

        private void OnEnable()
        {
            ResolveDependencies();
            Subscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
            animationAdapter?.ClearExternalBaseIntent();
            hasState = false;
            lastPresentationSequence = -1;
        }

        private void LateUpdate()
        {
            if (!OwnsWorldTransform) return;

            transform.position = Vector3.MoveTowards(
                transform.position,
                targetPosition,
                Mathf.Max(0.1f, maximumPresentationSpeed) *
                Time.deltaTime);
            var horizontal = targetForward;
            horizontal.y = 0f;
            if (horizontal.sqrMagnitude <= 0.0001f) return;
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                Quaternion.LookRotation(
                    horizontal.normalized,
                    Vector3.up),
                Mathf.Clamp01(
                    Time.deltaTime *
                    Mathf.Max(1f, rotationSharpness)));
        }

        public void Configure(
            OntologyWorldAuthorityClient client = null)
        {
            if (authorityClient != client && client != null)
            {
                Unsubscribe();
                authorityClient = client;
            }
            ResolveDependencies();
            if (isActiveAndEnabled) Subscribe();
        }

        private void ApplySnapshot(
            OntologyAuthorityAutonomousActorMotionState[] states)
        {
            if (states == null ||
                identity == null ||
                !identity.TryGetGuid(out var entityId))
            {
                return;
            }

            OntologyAuthorityAutonomousActorMotionState matched = null;
            var canonical = entityId.ToString("D");
            foreach (var state in states)
            {
                if (state != null &&
                    string.Equals(
                        state.actorEntityId,
                        canonical,
                        StringComparison.OrdinalIgnoreCase))
                {
                    matched = state;
                    break;
                }
            }

            if (matched == null)
            {
                hasState = false;
                animationAdapter?.ClearExternalBaseIntent();
                lastStatus = "No Authority autonomous state.";
                return;
            }

            targetPosition = new Vector3(
                (float)matched.positionX,
                (float)matched.positionY,
                (float)matched.positionZ);
            targetForward = new Vector3(
                (float)matched.forwardX,
                0f,
                (float)matched.forwardZ);
            hasState = true;
            lastStatus = matched.motionStatus ?? string.Empty;

            if (string.IsNullOrWhiteSpace(
                    matched.actorAnimationIntent))
            {
                return;
            }

            if (!string.Equals(
                    matched.motionStatus,
                    "attacking",
                    StringComparison.Ordinal))
            {
                animationAdapter?.SetExternalBaseIntent(
                    matched.actorAnimationIntent.Trim());
                return;
            }

            if (matched.presentationSequence ==
                lastPresentationSequence)
            {
                return;
            }

            lastPresentationSequence = matched.presentationSequence;
            animationAdapter?.PlayTransientIntent(
                matched.actorAnimationIntent.Trim());
        }

        private void ResolveDependencies()
        {
            identity ??= GetComponent<OntologyAuthorityEntityIdentity>();
            authorityClient ??=
                FindAnyObjectByType<OntologyWorldAuthorityClient>();
            animationAdapter ??=
                GetComponentInChildren<OntologyAnimationAdapter>(true);
        }

        private void Subscribe()
        {
            if (authorityClient == null) return;
            authorityClient.AutonomousActorMotionsReceived -=
                ApplySnapshot;
            authorityClient.AutonomousActorMotionsReceived +=
                ApplySnapshot;
        }

        private void Unsubscribe()
        {
            if (authorityClient == null) return;
            authorityClient.AutonomousActorMotionsReceived -=
                ApplySnapshot;
        }
    }
}
