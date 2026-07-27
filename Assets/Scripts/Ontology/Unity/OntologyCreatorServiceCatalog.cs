using System;
using System.Collections.Generic;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    [CreateAssetMenu(
        fileName = "CreatorServiceCatalog",
        menuName = "Tormia/Ontology/Creator Service Catalog")]
    public sealed class OntologyCreatorServiceCatalog : ScriptableObject
    {
        [SerializeField] private OntologyCreatorServiceDefinition[] services =
            Array.Empty<OntologyCreatorServiceDefinition>();

        public IReadOnlyList<OntologyCreatorServiceDefinition> Services => services;

        public OntologyCreatorServiceDefinition Find(string serviceId)
        {
            if (string.IsNullOrWhiteSpace(serviceId) || services == null)
            {
                return null;
            }

            foreach (var service in services)
            {
                if (service != null &&
                    string.Equals(
                        service.serviceId,
                        serviceId,
                        StringComparison.Ordinal))
                {
                    return service;
                }
            }

            return null;
        }
    }

    [Serializable]
    public sealed class OntologyCreatorServiceDefinition
    {
        [Tooltip("Canonical English service identifier.")]
        public string serviceId;
        [Tooltip("Localization lookup key used only by presentation.")]
        public string displayNameKey;
        public string fallbackDisplayName;
        [Tooltip("Localized presentation summary. Workflow logic never reads it.")]
        public string summaryKey;
        [Tooltip("Defines the recommended production-line order.")]
        public int stageOrder;
        [Tooltip("This service may start a new natural-language production draft.")]
        public bool acceptsInitialPrompt;
        [TextArea(2, 5)] public string fallbackSummary;
        public Vector3 workspaceOffset;
        public string[] defaultPartIds = Array.Empty<string>();

        public string ObjectName => "CreatorNPC_" + serviceId;
        public bool IsValid => !string.IsNullOrWhiteSpace(serviceId);
    }
}
