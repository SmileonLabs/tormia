using UnityEngine;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Ephemeral presentation lease derived from the selected Physical Meaning.
    /// It prevents multiple Unity adapters from moving the same Transform
    /// without turning a component or prefab name into a gameplay rule.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OntologyMotionDriverAdapter : MonoBehaviour
    {
        [SerializeField] private OntologyMotionDriver driver =
            OntologyMotionDriver.Rigidbody;

        public OntologyMotionDriver Driver => driver;

        public void Configure(OntologyMotionDriver value)
        {
            driver = value;
        }

        public bool Allows(OntologyMotionDriver value) =>
            enabled && isActiveAndEnabled && driver == value;

        public static bool Allows(
            Component component,
            OntologyMotionDriver expected)
        {
            if (component == null)
            {
                return false;
            }

            var lease =
                component.GetComponent<OntologyMotionDriverAdapter>();
            // A missing Physical Meaning lease is not permission to move.
            // This must stay fail-closed so removing the physical profile also
            // removes the Unity presentation behavior.
            return lease != null && lease.Allows(expected);
        }
    }
}
