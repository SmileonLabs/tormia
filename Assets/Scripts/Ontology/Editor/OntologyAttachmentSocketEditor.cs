using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    [CustomEditor(typeof(OntologyAttachmentSocket))]
    public sealed class OntologyAttachmentSocketEditor :
        UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var socket = (OntologyAttachmentSocket)target;
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "In TormiaWorld, move or rotate this socket with the normal " +
                "Transform gizmo, then capture that pose. All weapons using " +
                "the same socket id will follow it.",
                MessageType.Info);

            if (GUILayout.Button("Apply Authored Socket Pose"))
            {
                Undo.RecordObject(
                    socket.transform,
                    "Apply attachment socket pose");
                if (socket.ApplyAuthoredPose())
                {
                    MarkDirty(socket);
                }
            }

            if (GUILayout.Button("Capture Current Transform As Socket Pose"))
            {
                Undo.RecordObject(
                    socket,
                    "Capture attachment socket pose");
                if (socket.CaptureCurrentPose())
                {
                    MarkDirty(socket);
                }
            }
        }

        private static void MarkDirty(OntologyAttachmentSocket socket)
        {
            EditorUtility.SetDirty(socket);
            EditorUtility.SetDirty(socket.transform);
            EditorSceneManager.MarkSceneDirty(socket.gameObject.scene);
            SceneView.RepaintAll();
        }
    }
}
