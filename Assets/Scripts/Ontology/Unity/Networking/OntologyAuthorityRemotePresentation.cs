using UnityEngine;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Marks a GameObject created solely from a server zone projection. Local scene
    /// objects never receive this marker, so unloading a streamed zone cannot erase
    /// authoring data that belongs to the Unity scene.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OntologyAuthorityRemotePresentation : MonoBehaviour
    {
        [SerializeField] private string scopeZoneKey;

        public string ScopeZoneKey => scopeZoneKey;

        public void SetScopeZoneKey(string value)
        {
            scopeZoneKey = value?.Trim() ?? string.Empty;
        }
    }
}
