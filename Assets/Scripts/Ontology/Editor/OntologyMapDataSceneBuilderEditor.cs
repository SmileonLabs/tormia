using Tormia.Ontology.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Tormia.Ontology.Editor
{
    [CustomEditor(typeof(OntologyMapDataSceneBuilder))]
    [CanEditMultipleObjects]
    public sealed class OntologyMapDataSceneBuilderEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField("Map Build Actions", EditorStyles.boldLabel);

            if (targets.Length != 1)
            {
                EditorGUILayout.HelpBox("Select exactly one OntologyMapDataSceneBuilder object to use map build buttons.", MessageType.Info);
                return;
            }

            var builder = (OntologyMapDataSceneBuilder)target;
            if (GUILayout.Button("Build Scene Tiles From Map Data", GUILayout.Height(32f)))
            {
                Undo.RegisterFullObjectHierarchyUndo(builder.gameObject, "Build Ontology Scene Tiles");
                builder.BuildSceneTiles();
                EditorUtility.SetDirty(builder.gameObject);
                EditorSceneManager.MarkSceneDirty(builder.gameObject.scene);
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Clear Generated Tiles"))
            {
                Undo.RegisterFullObjectHierarchyUndo(builder.gameObject, "Clear Ontology Scene Tiles");
                builder.ClearSceneTiles();
                EditorUtility.SetDirty(builder.gameObject);
                EditorSceneManager.MarkSceneDirty(builder.gameObject.scene);
            }

            if (GUILayout.Button("Reset World From Scene"))
            {
                builder.ResetWorldFromScene();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.HelpBox("Use Build to generate Tile_x_y GameObjects from OntologyMapData. Generated tiles receive OntologyObject data and can be read by OntologyWorldBootstrap.", MessageType.Info);
        }
    }
}
