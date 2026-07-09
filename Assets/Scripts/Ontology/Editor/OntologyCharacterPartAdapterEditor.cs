using UnityEditor;
using UnityEngine;

namespace Tormia.Ontology.Core.Editor
{
    [CustomEditor(typeof(OntologyCharacterPartAdapter))]
    public sealed class OntologyCharacterPartAdapterEditor : UnityEditor.Editor
    {
        private string partId;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            if (string.IsNullOrWhiteSpace(partId))
            {
                partId = GetDefaultPartId((OntologyCharacterPartAdapter)target);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Part Test", EditorStyles.boldLabel);
            partId = EditorGUILayout.TextField("Part Id", partId);

            var adapter = (OntologyCharacterPartAdapter)target;
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Equip"))
            {
                Undo.RegisterFullObjectHierarchyUndo(adapter.gameObject, "Equip Ontology Part");
                if (!adapter.EquipPart(partId))
                {
                    Debug.LogWarning("Failed to equip part: " + partId);
                }
                EditorUtility.SetDirty(adapter.gameObject);
            }

            if (GUILayout.Button("Unequip"))
            {
                Undo.RegisterFullObjectHierarchyUndo(adapter.gameObject, "Unequip Ontology Part");
                if (!adapter.UnequipPart(partId))
                {
                    Debug.LogWarning("Failed to unequip part: " + partId);
                }
                EditorUtility.SetDirty(adapter.gameObject);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Apply Default Preset"))
            {
                Undo.RegisterFullObjectHierarchyUndo(adapter.gameObject, "Apply Character Part Preset");
                adapter.ApplyDefaultPreset();
                adapter.InjectActivePartFacts();
                EditorUtility.SetDirty(adapter.gameObject);
            }

            if (GUILayout.Button("Refresh Facts"))
            {
                adapter.InjectActivePartFacts();
            }
            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button("Sync Renderers From Facts"))
            {
                Undo.RegisterFullObjectHierarchyUndo(adapter.gameObject, "Sync Character Parts From Ontology Facts");
                adapter.SyncRenderersFromWorldFacts();
                EditorUtility.SetDirty(adapter.gameObject);
            }
        }

        private static string GetDefaultPartId(OntologyCharacterPartAdapter adapter)
        {
            var database = adapter.PartDatabase;
            if (database == null || database.Definitions == null || database.Definitions.Count == 0)
            {
                return string.Empty;
            }

            var definition = database.Definitions[0];
            return definition == null ? string.Empty : definition.partId;
        }
    }
}
