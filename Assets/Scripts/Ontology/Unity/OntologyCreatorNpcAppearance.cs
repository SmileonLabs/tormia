using System;
using System.Collections.Generic;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Presentation-only appearance for a creator assistant NPC.
    /// It reuses account character parts without writing actor or world facts.
    /// </summary>
    public sealed class OntologyCreatorNpcAppearance : MonoBehaviour
    {
        [SerializeField] private string serviceId;
        [SerializeField] private string displayName;
        [SerializeField] private OntologyCharacterPartDatabase partDatabase;
        [SerializeField] private Transform visualRoot;
        [SerializeField] private string[] equippedPartIds = Array.Empty<string>();

        public string ServiceId => serviceId;
        public string DisplayName => displayName;
        public OntologyCharacterPartDatabase PartDatabase => partDatabase;
        public Transform VisualRoot => visualRoot;
        public IReadOnlyList<string> EquippedPartIds => equippedPartIds;
        public event Action<OntologyCreatorNpcAppearance> Clicked;

        /// <summary>
        /// Publishes a transient presentation click without creating a world Fact.
        /// Creator workflow UI can subscribe while the NPC remains non-blocking.
        /// </summary>
        public void NotifyClicked()
        {
            Clicked?.Invoke(this);
        }

        private void Awake()
        {
            ApplyAppearance();
        }

        private void OnEnable()
        {
            ApplyAppearance();
        }

        [ContextMenu("Apply Creator NPC Appearance")]
        public void ApplyAppearance()
        {
            ApplyPresentationOnly(partDatabase, visualRoot, equippedPartIds);
            EnsureNonBlockingPresentation(transform);
        }

        public void Configure(
            string canonicalServiceId,
            string localizedDisplayName,
            OntologyCharacterPartDatabase database,
            Transform characterVisualRoot,
            string[] partIds)
        {
            serviceId = canonicalServiceId ?? string.Empty;
            displayName = localizedDisplayName ?? string.Empty;
            partDatabase = database;
            visualRoot = characterVisualRoot;
            equippedPartIds = partIds ?? Array.Empty<string>();
            ApplyAppearance();
        }

        /// <summary>
        /// Creator assistants are presentation-only conversation handles. Their
        /// visible bounds are used for clicking, so physical colliders and bodies
        /// must never push the player when an appearance or part is re-enabled.
        /// </summary>
        public static int EnsureNonBlockingPresentation(Transform presentationRoot)
        {
            if (presentationRoot == null)
            {
                return 0;
            }

            var changed = 0;
            foreach (var collider in presentationRoot.GetComponentsInChildren<Collider>(true))
            {
                if (collider == null || !collider.enabled)
                {
                    continue;
                }

                collider.enabled = false;
                changed++;
            }

            foreach (var body in presentationRoot.GetComponentsInChildren<Rigidbody>(true))
            {
                if (body == null)
                {
                    continue;
                }

                body.detectCollisions = false;
                body.isKinematic = true;
            }

            return changed;
        }

        public static bool ApplyPresentationOnly(
            OntologyCharacterPartDatabase database,
            Transform characterVisualRoot,
            IReadOnlyList<string> requestedPartIds)
        {
            return OntologyCharacterAppearanceProjector.ApplyPresentation(
                database,
                characterVisualRoot,
                requestedPartIds);
        }
    }
}
