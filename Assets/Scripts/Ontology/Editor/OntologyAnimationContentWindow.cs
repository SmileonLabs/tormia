using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    public sealed class OntologyAnimationContentWindow : EditorWindow
    {
        private OntologyAnimationContentManifest manifest;
        private Vector2 scroll;

        [MenuItem("Tools/Ontology/Animation Content/Production Line")]
        public static void Open()
        {
            GetWindow<OntologyAnimationContentWindow>(
                "Animation Production Line");
        }

        private void OnEnable()
        {
            manifest =
                AssetDatabase.LoadAssetAtPath<OntologyAnimationContentManifest>(
                    OntologyAnimationContentPipeline.ManifestPath);
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField(
                "TOV Animation Production Line",
                EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "The manifest owns Unity presentation metadata. Authority rules " +
                "own when a canonical intent is accepted; profiles own which " +
                "animation IDs an actor may express.",
                MessageType.Info);
            manifest =
                (OntologyAnimationContentManifest)EditorGUILayout.ObjectField(
                    "Manifest",
                    manifest,
                    typeof(OntologyAnimationContentManifest),
                    false);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Create / Migrate"))
                manifest =
                    OntologyAnimationContentPipeline.CreateOrMigrateManifest();
            using (new EditorGUI.DisabledScope(manifest == null))
            {
                if (GUILayout.Button("Validate + Synchronize"))
                    OntologyAnimationContentPipeline.ValidateAndSynchronize(
                        manifest,
                        true);
            }
            EditorGUILayout.EndHorizontal();

            if (manifest == null) return;
            var issues = manifest.ValidateEntries();
            var errors = issues.Count(value =>
                value.Severity == OntologyAnimationContentIssueSeverity.Error);
            var warnings = issues.Count(value =>
                value.Severity == OntologyAnimationContentIssueSeverity.Warning);
            EditorGUILayout.LabelField(
                "Entries",
                manifest.Entries.Count.ToString());
            EditorGUILayout.LabelField(
                "Validation",
                errors + " error(s), " + warnings + " warning(s)");

            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (var issue in issues)
            {
                EditorGUILayout.HelpBox(
                    (string.IsNullOrWhiteSpace(issue.AnimationId)
                        ? string.Empty
                        : issue.AnimationId + ": ") +
                    issue.Message,
                    issue.Severity == OntologyAnimationContentIssueSeverity.Error
                        ? MessageType.Error
                        : issue.Severity ==
                          OntologyAnimationContentIssueSeverity.Warning
                            ? MessageType.Warning
                            : MessageType.Info);
            }
            EditorGUILayout.EndScrollView();

            if (GUILayout.Button("Select Manifest Asset"))
            {
                Selection.activeObject = manifest;
                EditorGUIUtility.PingObject(manifest);
            }
        }
    }
}
