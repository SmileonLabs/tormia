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
        private bool defeated;

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
            authorityClient = FindAnyObjectByType<OntologyWorldAuthorityClient>();
            if (lootVisual != null) lootVisual.SetActive(false);
        }

        private void OnEnable()
        {
            if (authorityClient == null)
                authorityClient = FindAnyObjectByType<OntologyWorldAuthorityClient>();
            if (authorityClient != null)
                authorityClient.ProjectionReceived += ApplyProjection;
        }

        private void OnDisable()
        {
            if (authorityClient != null)
                authorityClient.ProjectionReceived -= ApplyProjection;
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
        }

        public void ApplyProjection(OntologyAuthorityWorldProjection projection)
        {
            if (projection?.facts == null ||
                AuthorityIdentity == null ||
                !AuthorityIdentity.TryGetGuid(out var entityId))
            {
                return;
            }

            var key = entityId.ToString("D");
            var isDefeated = false;
            var lootAvailable = false;
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
                    long.TryParse(fact.objectValueJson, out var health) &&
                    health <= 0)
                {
                    isDefeated = true;
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
            if (interactionCollider != null)
                interactionCollider.enabled = !isDefeated;
            if (lootVisual != null)
                lootVisual.SetActive(lootAvailable);
        }

        public void PresentConfirmedHit(bool defeated, Vector3 hitPoint)
        {
            // ProjectionReceived owns the one-time death transition. The
            // command-completion path still owns ordinary hit feedback.
            if (!defeated || !this.defeated)
            {
                var intent = defeated ? deathAnimationIntent : hitAnimationIntent;
                PlayIntent(intent);
            }

            if (!string.IsNullOrWhiteSpace(hitVfxIntent))
                OntologyCombatPresentationBus.RequestVfx(
                    hitVfxIntent,
                    null,
                    hitPoint,
                    Quaternion.identity);
        }

        private void PlayIntent(string intent)
        {
            // Missing ontology repertoire/intent deliberately means no animation.
            // Do not retain a hidden Animator-state fallback.
            if (string.IsNullOrWhiteSpace(intent)) return;
            animationAdapter?.PlayTransientIntent(intent);
        }
    }
}
