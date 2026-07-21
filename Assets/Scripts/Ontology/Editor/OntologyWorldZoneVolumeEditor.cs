#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Tormia.Ontology.Core.Editor
{
    [CustomEditor(typeof(OntologyWorldZoneVolume))]
    public sealed class OntologyWorldZoneVolumeEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var volume = (OntologyWorldZoneVolume)target;
            EditorGUILayout.Space();
            if (volume.TryBuildDefinition(out var definition, out var error))
            {
                EditorGUILayout.HelpBox(
                    "Authority boundary: " + definition.ZoneKey + "\n" +
                    "X " + definition.MinX.ToString("0.##") + " to " + definition.MaxX.ToString("0.##") +
                    " | Z " + definition.MinZ.ToString("0.##") + " to " + definition.MaxZ.ToString("0.##") +
                    " | " + definition.SimulationMode,
                    MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox(error, MessageType.Error);
            }

            EditorGUILayout.HelpBox(
                "This Box Collider is an authoring boundary only. It is a trigger and does not create gameplay collision. " +
                "Enter Play mode, then use OntologyWorldSample > World Zone Publisher > Publish Scene Zone Volumes to Authority.",
                MessageType.None);
        }
    }
}
#endif
