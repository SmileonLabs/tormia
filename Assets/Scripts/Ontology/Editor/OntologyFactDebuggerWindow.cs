using UnityEditor;
using UnityEngine;

namespace Tormia.Ontology.Core.Editor
{
    public sealed class OntologyFactDebuggerWindow : EditorWindow
    {
        private const string WindowTitle = "Fact Debugger";

        private GameObject previewTarget;
        private OntologyWorldBootstrap bootstrap;
        private OntologyActorToast actorToast;
        private OntologyActorFactToastEmitter toastEmitter;

        private string actorId = "Player";
        private string factSubject = "Player";
        private string factPredicate = OntologyPredicates.EquippedPart;
        private string factObject = "Part_Shoes_Base";
        private string toastMessage = "Status: Test";
        private OntologyActorToast.Severity toastSeverity = OntologyActorToast.Severity.Info;
        private Vector2 factScroll;
        private string report = string.Empty;
        private bool showOnlyActorFacts = true;

        [MenuItem("Tools/Ontology/Fact Debugger")]
        public static void Open()
        {
            GetWindow<OntologyFactDebuggerWindow>(WindowTitle);
        }

        private void OnEnable()
        {
            FindRuntimeTargets();
        }

        private void OnGUI()
        {
            DrawRuntimeTargetFields();
            EditorGUILayout.Space(8f);
            DrawManualFactSection();
            EditorGUILayout.Space(8f);
            DrawToastSection();
            EditorGUILayout.Space(8f);
            DrawCurrentFactsSection();
            DrawReport();
        }

        private void DrawRuntimeTargetFields()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                previewTarget = (GameObject)EditorGUILayout.ObjectField("Preview Target", previewTarget, typeof(GameObject), true);
                bootstrap = (OntologyWorldBootstrap)EditorGUILayout.ObjectField("Bootstrap", bootstrap, typeof(OntologyWorldBootstrap), true);
                actorToast = (OntologyActorToast)EditorGUILayout.ObjectField("Actor Toast", actorToast, typeof(OntologyActorToast), true);
                toastEmitter = (OntologyActorFactToastEmitter)EditorGUILayout.ObjectField("Toast Emitter", toastEmitter, typeof(OntologyActorFactToastEmitter), true);

                if (GUILayout.Button("Find Runtime Targets"))
                {
                    FindRuntimeTargets();
                }
            }
        }

        private void DrawManualFactSection()
        {
            EditorGUILayout.LabelField("Manual Fact", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Add or remove one ontology triple directly in the world. Use known predicates/objects when you want rules, UI, or toast reactions.", MessageType.Info);
            factSubject = EditorGUILayout.TextField("Subject", factSubject);
            factPredicate = EditorGUILayout.TextField("Predicate", factPredicate);
            factObject = EditorGUILayout.TextField("Object", factObject);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Add Fact"))
                {
                    AddRuntimeFact();
                }

                if (GUILayout.Button("Remove Fact"))
                {
                    RemoveRuntimeFact();
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Example: Equipped Shoes"))
                {
                    SetFact("Player", OntologyPredicates.EquippedPart, "Part_Shoes_Base");
                }

                if (GUILayout.Button("Example: Wet"))
                {
                    SetFact("Player", "status", "Wet");
                }

                if (GUILayout.Button("Example: Swamp"))
                {
                    SetFact("Player", "standing_on", "Tile_Swamp");
                }
            }
        }

        private void DrawToastSection()
        {
            EditorGUILayout.LabelField("Toast Test", EditorStyles.boldLabel);
            toastMessage = EditorGUILayout.TextField("Message", toastMessage);
            toastSeverity = (OntologyActorToast.Severity)EditorGUILayout.EnumPopup("Severity", toastSeverity);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Show Toast"))
                {
                    ShowToast();
                }

                if (GUILayout.Button("Reset Toast Baseline"))
                {
                    ResetToastBaseline();
                }
            }
        }

        private void DrawCurrentFactsSection()
        {
            EditorGUILayout.LabelField("Current Facts", EditorStyles.boldLabel);
            actorId = EditorGUILayout.TextField("Actor Filter", actorId);
            showOnlyActorFacts = EditorGUILayout.Toggle("Only Actor Facts", showOnlyActorFacts);

            if (bootstrap == null || bootstrap.World == null)
            {
                EditorGUILayout.HelpBox("World is not ready.", MessageType.Info);
                return;
            }

            factScroll = EditorGUILayout.BeginScrollView(factScroll, GUILayout.MinHeight(220f));
            foreach (var fact in bootstrap.World.Facts)
            {
                if (showOnlyActorFacts && fact.Subject.ToString() != actorId)
                {
                    continue;
                }

                EditorGUILayout.LabelField(fact.ToString());
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawReport()
        {
            if (string.IsNullOrWhiteSpace(report))
            {
                return;
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.HelpBox(report, MessageType.None);
        }

        private void AddRuntimeFact()
        {
            EnsureWorldReady();
            if (bootstrap?.World == null)
            {
                report = "World is not ready.";
                return;
            }

            bootstrap.World.AddFact(factSubject, factPredicate, factObject);
            report = "Added: " + factSubject + " " + factPredicate + " " + factObject;
            Repaint();
        }

        private void RemoveRuntimeFact()
        {
            EnsureWorldReady();
            if (bootstrap?.World == null)
            {
                report = "World is not ready.";
                return;
            }

            bootstrap.World.RemoveFact(factSubject, factPredicate, factObject);
            report = "Removed: " + factSubject + " " + factPredicate + " " + factObject;
            Repaint();
        }

        private void ShowToast()
        {
            if (actorToast == null)
            {
                report = "No Actor Toast target.";
                return;
            }

            actorToast.Show(toastMessage, toastSeverity);
            report = "Toast shown: " + toastMessage;
        }

        private void ResetToastBaseline()
        {
            if (toastEmitter == null)
            {
                report = "No Toast Emitter target.";
                return;
            }

            toastEmitter.ResetBaseline();
            report = "Toast baseline reset.";
        }

        private void SetFact(string subject, string predicate, string obj)
        {
            factSubject = subject;
            factPredicate = predicate;
            factObject = obj;
            GUI.FocusControl(null);
        }

        private void EnsureWorldReady()
        {
            if (bootstrap == null)
            {
                bootstrap = FindAnyObjectByType<OntologyWorldBootstrap>();
            }

            if (bootstrap != null && (bootstrap.World == null || bootstrap.Session == null))
            {
                bootstrap.ResetWorld(false);
            }
        }

        private void FindRuntimeTargets()
        {
            previewTarget = GameObject.Find("OntologyPlayer");
            bootstrap = FindAnyObjectByType<OntologyWorldBootstrap>();
            actorToast = previewTarget != null ? previewTarget.GetComponent<OntologyActorToast>() : FindAnyObjectByType<OntologyActorToast>();
            toastEmitter = previewTarget != null ? previewTarget.GetComponent<OntologyActorFactToastEmitter>() : FindAnyObjectByType<OntologyActorFactToastEmitter>();
        }
    }
}
