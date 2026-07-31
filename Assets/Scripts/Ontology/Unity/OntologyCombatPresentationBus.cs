using System;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Ephemeral presentation signals only. These signals never add durable Facts
    /// and never decide damage, death, loot, or action availability.
    /// </summary>
    public static class OntologyCombatPresentationBus
    {
        public static event Action<OntologyCombatVfxSignal> VfxRequested;

        public static void RequestVfx(
            string intentId,
            Transform anchor,
            Vector3 worldPosition,
            Quaternion worldRotation)
        {
            if (string.IsNullOrWhiteSpace(intentId)) return;
            VfxRequested?.Invoke(new OntologyCombatVfxSignal(
                intentId, anchor, worldPosition, worldRotation));
        }
    }

    public readonly struct OntologyCombatVfxSignal
    {
        public OntologyCombatVfxSignal(
            string intentId,
            Transform anchor,
            Vector3 worldPosition,
            Quaternion worldRotation)
        {
            IntentId = intentId;
            Anchor = anchor;
            WorldPosition = worldPosition;
            WorldRotation = worldRotation;
        }

        public string IntentId { get; }
        public Transform Anchor { get; }
        public Vector3 WorldPosition { get; }
        public Quaternion WorldRotation { get; }
    }
}
