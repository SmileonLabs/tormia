using System.Collections.Generic;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    [DisallowMultipleComponent]
    public sealed class OntologyCombatTargetPresenter : MonoBehaviour
    {
        [SerializeField] private OntologyAuthorityEntityIdentity authorityIdentity;
        [SerializeField] private OntologyAnimationAdapter animationAdapter;
        [SerializeField] private Transform hitAnchor;
        [SerializeField] private string hitAnimationIntent = string.Empty;
        [SerializeField] private string deathAnimationIntent = string.Empty;
        [SerializeField] private string hitVfxIntent = string.Empty;
        [SerializeField] private GameObject lootVisual;
        [SerializeField] private Collider interactionCollider;

        private OntologyWorldAuthorityClient authorityClient;
        private OntologyWorldAuthorityRealtimeClient realtimeClient;
        private OntologyWorldAuthorityClient subscribedAuthorityClient;
        private OntologyWorldAuthorityRealtimeClient subscribedRealtimeClient;
        private bool defeated;
        private bool hasObservedHealth;
        private double observedHealth;
        private long observedHealthRevision = -1;
        private string lastPresentedDamageEventId = string.Empty;
        private long pendingDamageOccurrenceRevision = -1;
        private readonly Dictionary<Collider, bool> initialColliderStates =
            new();

        public OntologyAuthorityEntityIdentity AuthorityIdentity =>
            authorityIdentity != null
                ? authorityIdentity
                : authorityIdentity = GetComponent<OntologyAuthorityEntityIdentity>();
        public Transform HitAnchor => hitAnchor != null ? hitAnchor : transform;
        public Collider InteractionCollider =>
            interactionCollider != null
                ? interactionCollider
                : interactionCollider = GetComponentInChildren<Collider>(true);

        private void Awake()
        {
            OntologyRenderPipelineMaterialAdapter.ApplyTo(gameObject);
            if (animationAdapter == null)
                animationAdapter = GetComponentInChildren<OntologyAnimationAdapter>();
            BindAuthorityClients();
            if (lootVisual != null) lootVisual.SetActive(false);
            CaptureColliderStates();
        }

        private void OnEnable()
        {
            BindAuthorityClients();
        }

        private void OnDisable()
        {
            if (subscribedAuthorityClient != null)
                subscribedAuthorityClient.ProjectionReceived -= ApplyProjection;
            if (subscribedRealtimeClient != null)
                subscribedRealtimeClient.RevisionOccurrenceReceived -=
                    HandleRevisionOccurrenceReceived;
            subscribedAuthorityClient = null;
            subscribedRealtimeClient = null;
        }

        private void BindAuthorityClients()
        {
            var currentAuthorityClient =
                FindAnyObjectByType<OntologyWorldAuthorityClient>();
            if (subscribedAuthorityClient != currentAuthorityClient)
            {
                if (subscribedAuthorityClient != null)
                    subscribedAuthorityClient.ProjectionReceived -= ApplyProjection;
                subscribedAuthorityClient = currentAuthorityClient;
                authorityClient = currentAuthorityClient;
                if (subscribedAuthorityClient != null)
                    subscribedAuthorityClient.ProjectionReceived += ApplyProjection;
            }

            var currentRealtimeClient =
                FindAnyObjectByType<OntologyWorldAuthorityRealtimeClient>();
            if (subscribedRealtimeClient == currentRealtimeClient) return;
            if (subscribedRealtimeClient != null)
                subscribedRealtimeClient.RevisionOccurrenceReceived -=
                    HandleRevisionOccurrenceReceived;
            subscribedRealtimeClient = currentRealtimeClient;
            realtimeClient = currentRealtimeClient;
            if (subscribedRealtimeClient != null)
                subscribedRealtimeClient.RevisionOccurrenceReceived +=
                    HandleRevisionOccurrenceReceived;
        }

        public void Configure(
            OntologyAuthorityEntityIdentity identity,
            Transform targetHitAnchor,
            string canonicalHitAnimationIntent,
            string canonicalDeathAnimationIntent,
            string canonicalHitVfxIntent,
            GameObject targetLootVisual,
            Collider targetInteractionCollider)
        {
            authorityIdentity = identity;
            hitAnchor = targetHitAnchor;
            hitAnimationIntent = canonicalHitAnimationIntent;
            deathAnimationIntent = canonicalDeathAnimationIntent;
            hitVfxIntent = canonicalHitVfxIntent;
            lootVisual = targetLootVisual;
            interactionCollider = targetInteractionCollider;
            animationAdapter = GetComponentInChildren<OntologyAnimationAdapter>();
            CaptureColliderStates();
            if (isActiveAndEnabled) BindAuthorityClients();
        }

        public void ApplyProjection(OntologyAuthorityWorldProjection projection)
        {
            // Runtime actors and the realtime transport are created by separate
            // presentation paths. Rebind on every authoritative projection so
            // initialization order or a transport recreation cannot leave an
            // actor permanently detached from committed damage occurrences.
            BindAuthorityClients();
            if (projection?.facts == null ||
                AuthorityIdentity == null ||
                !AuthorityIdentity.TryGetGuid(out var entityId))
            {
                return;
            }

            var key = entityId.ToString("D");
            var isDefeated = false;
            var lootAvailable = false;
            double? projectedHealth = null;
            foreach (var fact in projection.facts)
            {
                if (fact == null ||
                    !string.Equals(
                        fact.subjectEntityId,
                        key,
                        System.StringComparison.OrdinalIgnoreCase))
                    continue;
                if (fact.predicateId == OntologyPredicates.IsAlive &&
                    (string.Equals(
                         fact.objectCanonicalId,
                         "False",
                         System.StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(
                         fact.objectValueJson,
                         "false",
                         System.StringComparison.OrdinalIgnoreCase)))
                {
                    isDefeated = true;
                }
                if (fact.predicateId == OntologyPredicates.CurrentHealth &&
                    double.TryParse(
                        fact.objectValueJson,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var health))
                {
                    projectedHealth = health;
                    if (health <= 0) isDefeated = true;
                }
                if (fact.predicateId == OntologyPredicates.LootStatus &&
                    string.Equals(
                        fact.objectCanonicalId,
                        "Available",
                        System.StringComparison.Ordinal))
                {
                    lootAvailable = true;
                }
            }

            var confirmedDamage = ShouldPresentConfirmedDamage(
                hasObservedHealth,
                observedHealth,
                observedHealthRevision,
                projectedHealth,
                projection.revision,
                isDefeated);
            if (projectedHealth.HasValue &&
                projection.revision >= observedHealthRevision)
            {
                observedHealth = projectedHealth.Value;
                observedHealthRevision = projection.revision;
                hasObservedHealth = true;
            }

            if (isDefeated)
            {
                defeated = true;
                if (!string.IsNullOrWhiteSpace(deathAnimationIntent))
                    animationAdapter?.SetPersistentIntent(
                        deathAnimationIntent);
            }
            else if (defeated)
            {
                defeated = false;
                if (!string.IsNullOrWhiteSpace(deathAnimationIntent))
                    animationAdapter?.ClearPersistentIntent(
                        deathAnimationIntent);
            }
            else if (confirmedDamage &&
                     !string.IsNullOrWhiteSpace(hitAnimationIntent))
            {
                // The decreasing Authority projection is the approved damage
                // result. This presenter only expresses that result; it never
                // infers damage from a collision, attacker, prefab, or name.
                if (pendingDamageOccurrenceRevision < 0 ||
                    projection.revision < pendingDamageOccurrenceRevision)
                {
                    PlayIntent(hitAnimationIntent);
                }
                pendingDamageOccurrenceRevision = -1;
            }
            ApplyLifecycleColliderState(isDefeated);
            if (lootVisual != null)
                lootVisual.SetActive(lootAvailable);
            if (pendingDamageOccurrenceRevision >= 0 &&
                projection.revision >= pendingDamageOccurrenceRevision)
            {
                pendingDamageOccurrenceRevision = -1;
            }
        }

        private void CaptureColliderStates()
        {
            foreach (var candidate in GetComponentsInChildren<Collider>(true))
            {
                if (candidate != null &&
                    !initialColliderStates.ContainsKey(candidate))
                {
                    initialColliderStates.Add(candidate, candidate.enabled);
                }
            }
        }

        private void ApplyLifecycleColliderState(bool isDefeated)
        {
            if (initialColliderStates.Count == 0)
                CaptureColliderStates();

            foreach (var pair in initialColliderStates)
            {
                if (pair.Key != null)
                    pair.Key.enabled = !isDefeated && pair.Value;
            }
        }

        public static bool IsProjectedAlive(
            OntologyAuthorityWorldProjection projection,
            System.Guid entityId)
        {
            if (projection?.facts == null || entityId == System.Guid.Empty)
                return false;

            var subject = entityId.ToString("D");
            var alive = false;
            var hasAlive = false;
            double? health = null;
            foreach (var fact in projection.facts)
            {
                if (fact == null ||
                    !string.Equals(
                        fact.subjectEntityId,
                        subject,
                        System.StringComparison.OrdinalIgnoreCase))
                    continue;

                if (fact.predicateId == OntologyPredicates.IsAlive)
                {
                    hasAlive = true;
                    alive = string.Equals(
                                fact.objectCanonicalId,
                                "True",
                                System.StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(
                                fact.objectValueJson,
                                "true",
                                System.StringComparison.OrdinalIgnoreCase);
                }
                else if (fact.predicateId == OntologyPredicates.CurrentHealth &&
                         double.TryParse(
                             fact.objectValueJson,
                             System.Globalization.NumberStyles.Float,
                             System.Globalization.CultureInfo.InvariantCulture,
                             out var value))
                {
                    health = value;
                }
            }

            return hasAlive && alive && (!health.HasValue || health.Value > 0d);
        }

        private void HandleRevisionOccurrenceReceived(
            OntologyAuthorityRevisionOccurrence occurrence)
        {
            if (AuthorityIdentity == null ||
                !AuthorityIdentity.TryGetGuid(out var entityId) ||
                !ShouldPresentAuthorityDamageOccurrence(
                    occurrence,
                    entityId.ToString("D"),
                    lastPresentedDamageEventId))
            {
                return;
            }

            lastPresentedDamageEventId = occurrence.eventId;
            if (!defeated &&
                !string.IsNullOrWhiteSpace(hitAnimationIntent))
            {
                // One committed Authority event maps to one transient reaction.
                // The target's projected semantic intent selects the clip; the
                // transport event never invents damage or animation meaning.
                if (PlayIntent(hitAnimationIntent))
                    pendingDamageOccurrenceRevision = occurrence.revision;
            }
        }

        public static bool ShouldPresentAuthorityDamageOccurrence(
            OntologyAuthorityRevisionOccurrence occurrence,
            string targetEntityId,
            string lastPresentedEventId)
        {
            return occurrence != null &&
                   occurrence.damageResult &&
                   !string.IsNullOrWhiteSpace(occurrence.eventId) &&
                   !string.IsNullOrWhiteSpace(occurrence.targetEntityId) &&
                   !string.Equals(
                       occurrence.eventId,
                       lastPresentedEventId,
                       System.StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(
                       occurrence.targetEntityId,
                       targetEntityId,
                       System.StringComparison.OrdinalIgnoreCase);
        }

        public static bool ShouldPresentConfirmedDamage(
            bool hasPreviousHealth,
            double previousHealth,
            long previousRevision,
            double? currentHealth,
            long currentRevision,
            bool defeated)
        {
            return !defeated &&
                   currentHealth.HasValue &&
                   hasPreviousHealth &&
                   currentRevision > previousRevision &&
                   currentHealth.Value < previousHealth;
        }

        public void PresentConfirmedHit(bool defeated, Vector3 hitPoint)
        {
            // Authority projection owns both hit and death animation. Command
            // completion contributes only the ephemeral contact point needed by
            // VFX, preventing a local attack from playing the reaction twice.
            if (!string.IsNullOrWhiteSpace(hitVfxIntent))
                OntologyCombatPresentationBus.RequestVfx(
                    hitVfxIntent,
                    null,
                    hitPoint,
                    Quaternion.identity);
        }

        private bool PlayIntent(string intent)
        {
            // Missing ontology repertoire/intent deliberately means no animation.
            // Do not retain a hidden Animator-state fallback.
            return !string.IsNullOrWhiteSpace(intent) &&
                   animationAdapter != null &&
                   animationAdapter.PlayTransientIntent(intent);
        }
    }
}
