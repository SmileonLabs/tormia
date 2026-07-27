using System;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Presentation-only mapping from canonical character-part slot IDs to
    /// category icons. It contains no equipment or ontology behavior.
    /// </summary>
    [CreateAssetMenu(menuName = "Tormia/Ontology/Character Category Icon Set")]
    public sealed class OntologyCharacterCategoryIconSet : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            public string categoryId;
            public Sprite icon;
        }

        [SerializeField] private Entry[] entries = Array.Empty<Entry>();

        public Sprite GetIcon(string categoryId)
        {
            foreach (var entry in entries)
            {
                if (entry != null && entry.categoryId == categoryId) return entry.icon;
            }

            return null;
        }
    }
}
