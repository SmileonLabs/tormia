using UnityEditor;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    public sealed class OntologyAnimationUgcWindow : EditorWindow
    {
        private string sourceFile;
        private string uploaderAccountId = "local-developer";
        private string animationId = "Anim_";
        private string intent = OntologyAnimationIntentIds.Idle;
        private string actorType = "Player";
        private string rigType = "Humanoid";
        private OntologyActorProfile targetProfile;
        private string licenseId = "ProjectOwned";
        private string attribution;
        private OntologyAnimationContentManifest manifest;
        private OntologyAnimationUgcSubmissionCatalog catalog;
        private Vector2 scroll;

        [MenuItem("Tools/Ontology/Animation Content/UGC Review")]
        public static void Open()
        {
            GetWindow<OntologyAnimationUgcWindow>("Animation UGC Review");
        }

        private void OnEnable()
        {
            manifest =
                AssetDatabase.LoadAssetAtPath<OntologyAnimationContentManifest>(
                    OntologyAnimationContentPipeline.ManifestPath);
            catalog = OntologyAnimationUgcPipeline.EnsureCatalog();
            targetProfile =
                AssetDatabase.LoadAssetAtPath<OntologyActorProfile>(
                    OntologyAnimationContentPipeline.PlayerProfilePath);
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField(
                "Staged Animation UGC",
                EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "FBX files stay in quarantine until validation and explicit " +
                "approval. This development workflow does not turn file paths " +
                "into world Facts.",
                MessageType.Info);
            EditorGUILayout.BeginHorizontal();
            sourceFile = EditorGUILayout.TextField("FBX File", sourceFile);
            if (GUILayout.Button("Browse", GUILayout.Width(80f)))
                sourceFile = EditorUtility.OpenFilePanel(
                    "Select animation FBX",
                    string.Empty,
                    "fbx");
            EditorGUILayout.EndHorizontal();
            uploaderAccountId =
                EditorGUILayout.TextField("Uploader", uploaderAccountId);
            animationId = EditorGUILayout.TextField("Animation Id", animationId);
            intent = EditorGUILayout.TextField("Canonical Intent", intent);
            actorType = EditorGUILayout.TextField("Actor Type", actorType);
            rigType = EditorGUILayout.TextField("Rig Type", rigType);
            targetProfile =
                (OntologyActorProfile)EditorGUILayout.ObjectField(
                    "Target Profile",
                    targetProfile,
                    typeof(OntologyActorProfile),
                    false);
            licenseId = EditorGUILayout.TextField("License Id", licenseId);
            attribution = EditorGUILayout.TextField("Attribution", attribution);
            manifest =
                (OntologyAnimationContentManifest)EditorGUILayout.ObjectField(
                    "Manifest",
                    manifest,
                    typeof(OntologyAnimationContentManifest),
                    false);
            if (GUILayout.Button("Stage and Validate"))
            {
                try
                {
                    OntologyAnimationUgcPipeline.Stage(
                        sourceFile,
                        uploaderAccountId,
                        animationId,
                        intent,
                        actorType,
                        rigType,
                        targetProfile,
                        licenseId,
                        attribution);
                }
                catch (System.Exception exception)
                {
                    Debug.LogError(exception.Message);
                }
            }

            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (var submission in catalog.Submissions)
            {
                if (submission == null) continue;
                EditorGUILayout.BeginVertical("box");
                EditorGUILayout.LabelField(
                    submission.animationId,
                    submission.status.ToString());
                EditorGUILayout.LabelField(submission.validationMessage);
                using (new EditorGUI.DisabledScope(
                           submission.status !=
                           OntologyAnimationUgcSubmissionStatus.Validated ||
                           manifest == null))
                {
                    if (GUILayout.Button("Approve and Publish"))
                    {
                        OntologyAnimationUgcPipeline.ApproveAndPublish(
                            submission,
                            manifest);
                    }
                }
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndScrollView();
        }
    }
}
