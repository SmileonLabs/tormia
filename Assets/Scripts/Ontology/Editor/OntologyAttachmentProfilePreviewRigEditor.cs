using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    [CustomEditor(typeof(OntologyAttachmentProfilePreviewRig))]
    public sealed class OntologyAttachmentProfilePreviewRigEditor :
        UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var rig = (OntologyAttachmentProfilePreviewRig)target;
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "Move and rotate EquippedWeaponPreview until the handle sits " +
                "naturally in the hand, then save that pose to the weapon " +
                "prefab grip point. The actor-side socket is authored at " +
                "TormiaWorldRoot/OntologyPlayer/AttachmentSockets/" +
                "RightHandWeaponSocket.",
                MessageType.Info);

            if (GUILayout.Button("Reload Pose From Prefab Grip Point"))
            {
                Undo.RecordObject(
                    rig.AttachmentPreview,
                    "Load attachment prefab grip point");
                if (rig.ApplyProfileToPreview())
                {
                    MarkDirty(rig);
                }
            }

            if (GUILayout.Button("Save Current Pose To Prefab Grip Point"))
            {
                if (rig.CapturePreviewToSourceGripPoint())
                {
                    MarkDirty(rig);
                }
            }

            if (rig.AttachmentPreview != null &&
                GUILayout.Button("Select EquippedWeaponPreview"))
            {
                Selection.activeTransform = rig.AttachmentPreview;
                SceneView.lastActiveSceneView?.FrameSelected();
            }

            if (GUILayout.Button("Select Actor Weapon Socket"))
            {
                var socket =
                    OntologyAttachmentPoseUtility.ResolveActorSocket(
                        rig.transform,
                        rig.Profile == null
                            ? null
                            : rig.Profile.actorSocketId);
                if (socket != null)
                {
                    Selection.activeTransform = socket;
                    SceneView.lastActiveSceneView?.FrameSelected();
                }
            }
        }

        private static void MarkDirty(
            OntologyAttachmentProfilePreviewRig rig)
        {
            EditorUtility.SetDirty(rig);
            if (rig.AttachmentPreview != null)
            {
                EditorUtility.SetDirty(rig.AttachmentPreview);
            }

            EditorSceneManager.MarkSceneDirty(rig.gameObject.scene);
            SceneView.RepaintAll();
        }
    }
}
