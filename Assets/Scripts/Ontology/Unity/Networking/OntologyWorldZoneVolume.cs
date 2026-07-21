using System;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Scene-authoring handle for one durable world Zone. Its BoxCollider is only
    /// an editable X/Z boundary visual; gameplay collision is intentionally not
    /// derived from this component. Publishing turns the boundary into a versioned
    /// authority command, so the server remains the source of truth.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider))]
    public sealed class OntologyWorldZoneVolume : MonoBehaviour
    {
        public enum SimulationMode
        {
            Active,
            Reduced,
            Dormant
        }

        [SerializeField] private string zoneKey = "zone_01";
        [SerializeField] private SimulationMode simulationMode = SimulationMode.Active;
        [SerializeField, TextArea] private string lastValidation;

        public string ZoneKey => zoneKey?.Trim() ?? string.Empty;
        public SimulationMode Mode => simulationMode;
        public string LastValidation => lastValidation;

        public bool TryBuildDefinition(out ZoneDefinition definition, out string error)
        {
            definition = default;
            var key = ZoneKey;
            if (!IsValidKey(key))
            {
                error = "Zone Key must start with a letter and contain only letters, numbers, '-' or '_'.";
                lastValidation = error;
                return false;
            }

            var collider = GetComponent<BoxCollider>();
            if (collider == null)
            {
                error = "Zone Volume requires a Box Collider.";
                lastValidation = error;
                return false;
            }

            var bounds = collider.bounds;
            if (bounds.size.x <= 0f || bounds.size.z <= 0f)
            {
                error = "Zone Volume needs a positive X and Z size.";
                lastValidation = error;
                return false;
            }

            definition = new ZoneDefinition(
                key,
                bounds.min.x,
                bounds.min.z,
                bounds.max.x,
                bounds.max.z,
                ToAuthorityMode(simulationMode));
            error = string.Empty;
            lastValidation = "Ready: " + key + " [" + definition.MinX.ToString("0.##") + ", " +
                             definition.MinZ.ToString("0.##") + "] to [" +
                             definition.MaxX.ToString("0.##") + ", " + definition.MaxZ.ToString("0.##") + "].";
            return true;
        }

        private void Reset()
        {
            var collider = GetComponent<BoxCollider>();
            collider.isTrigger = true;
            collider.size = new Vector3(40f, 8f, 40f);
        }

        private void OnValidate()
        {
            var collider = GetComponent<BoxCollider>();
            if (collider != null)
            {
                collider.isTrigger = true;
            }
            TryBuildDefinition(out _, out _);
        }

        private void OnDrawGizmos()
        {
            var collider = GetComponent<BoxCollider>();
            if (collider == null) return;
            Gizmos.color = simulationMode switch
            {
                SimulationMode.Active => new Color(0.1f, 0.85f, 1f, 0.18f),
                SimulationMode.Reduced => new Color(1f, 0.7f, 0.1f, 0.18f),
                _ => new Color(0.55f, 0.55f, 0.65f, 0.16f)
            };
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawCube(collider.center, collider.size);
            Gizmos.color = new Color(Gizmos.color.r, Gizmos.color.g, Gizmos.color.b, 0.9f);
            Gizmos.DrawWireCube(collider.center, collider.size);
        }

        private static string ToAuthorityMode(SimulationMode value) => value switch
        {
            SimulationMode.Reduced => "reduced",
            SimulationMode.Dormant => "dormant",
            _ => "active"
        };

        private static bool IsValidKey(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 128 || !char.IsLetter(value[0]))
                return false;
            foreach (var character in value)
            {
                if (!char.IsLetterOrDigit(character) && character != '_' && character != '-')
                    return false;
            }
            return true;
        }

        public readonly struct ZoneDefinition
        {
            public readonly string ZoneKey;
            public readonly float MinX;
            public readonly float MinZ;
            public readonly float MaxX;
            public readonly float MaxZ;
            public readonly string SimulationMode;

            public ZoneDefinition(string zoneKey, float minX, float minZ, float maxX, float maxZ, string simulationMode)
            {
                ZoneKey = zoneKey;
                MinX = minX;
                MinZ = minZ;
                MaxX = maxX;
                MaxZ = maxZ;
                SimulationMode = simulationMode;
            }
        }
    }
}
