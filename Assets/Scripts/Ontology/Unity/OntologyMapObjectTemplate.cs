using System;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Reusable semantic definition for a placed map object.
    /// It contains no scene reference and can therefore be applied to many objects.
    /// </summary>
    [CreateAssetMenu(fileName = "NewMapObjectTemplate", menuName = "Tormia/Ontology/Map Object Template")]
    public sealed class OntologyMapObjectTemplate : ScriptableObject
    {
        [TextArea] public string description;
        public string[] concepts = Array.Empty<string>();
        public OntologyFactEntry[] facts = Array.Empty<OntologyFactEntry>();

        [Header("Template Migration")]
        [Tooltip("Former default concepts to remove once from legacy placed instances. Use only when a template meaning has intentionally been retired.")]
        public string[] retiredConcepts = Array.Empty<string>();
        [Tooltip("Former default facts to remove once from legacy placed instances. This does not prevent a user from adding the same fact again later.")]
        public OntologyFactEntry[] retiredFacts = Array.Empty<OntologyFactEntry>();
    }
}
