using Tormia.Ontology.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Tormia.Ontology.Editor
{
    /// <summary>
    /// Authoring surface for attaching semantic identity to placed scene objects.
    /// Unity owns appearance and placement; this window only edits OntologyObject data.
    /// </summary>
    public sealed class OntologyMapObjectEditorWindow : EditorWindow
    {
        private GameObject selectedObject;
        private OntologyObject ontologyObject;
        private SerializedObject serializedOntologyObject;
        private SerializedProperty entityId;
        private SerializedProperty concepts;
        private SerializedProperty facts;
        private OntologyMapObjectTemplate template;
        private bool replaceExistingTemplateData;
        private string newConcept = "";
        private string newPredicate = "located_in";
        private string newObject = "";
        private string status;

        [MenuItem("Tools/Ontology/Map Object Ontology Editor")]
        public static void Open()
        {
            var window = GetWindow<OntologyMapObjectEditorWindow>("Map Object Ontology");
            window.minSize = new Vector2(470f, 520f);
            window.UseCurrentSelection();
        }

        private void OnFocus()
        {
            if (selectedObject == null)
            {
                UseCurrentSelection();
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Map Object Ontology Editor", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Place visual objects first, then give important objects a stable identity, concepts, and facts. " +
                "The visual prefab remains unchanged; only the Ontology Object component is authored here.",
                MessageType.Info);

            DrawSelection();
            if (selectedObject == null)
            {
                EditorGUILayout.HelpBox("Select a scene GameObject such as a tree, rock, building, or region root.", MessageType.Warning);
                return;
            }

            if (ontologyObject == null)
            {
                EditorGUILayout.HelpBox("This object has no ontology data yet.", MessageType.None);
                if (GUILayout.Button("Add Ontology Object", GUILayout.Height(28f)))
                {
                    AddOntologyObject();
                }
                return;
            }

            DrawIdentity();
            DrawTemplate();
            DrawQuickConcepts();
            DrawFacts();
            DrawInjection();
            if (!string.IsNullOrWhiteSpace(status))
            {
                EditorGUILayout.HelpBox(status, MessageType.None);
            }
        }

        private void DrawSelection()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            var next = (GameObject)EditorGUILayout.ObjectField("Scene Object", selectedObject, typeof(GameObject), true);
            if (EditorGUI.EndChangeCheck())
            {
                Bind(next);
            }
            if (GUILayout.Button("Use Selection", GUILayout.Width(105f)))
            {
                UseCurrentSelection();
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawIdentity()
        {
            serializedOntologyObject.Update();
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("1. Identity", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(entityId, new GUIContent("Entity Id"));
            EditorGUILayout.LabelField("Use a stable, readable name. Empty uses the GameObject name.", EditorStyles.miniLabel);
            ApplyChangesIfNeeded();
            EditorGUILayout.EndVertical();
        }

        private void DrawQuickConcepts()
        {
            serializedOntologyObject.Update();
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("2. Concepts", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(concepts, true);
            EditorGUILayout.BeginHorizontal();
            newConcept = EditorGUILayout.TextField(newConcept);
            if (GUILayout.Button("Add", GUILayout.Width(60f)))
            {
                AddConcept(newConcept);
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Tree")) AddConcept("Tree");
            if (GUILayout.Button("Rock")) AddConcept("Rock");
            if (GUILayout.Button("Building")) AddConcept("Building");
            if (GUILayout.Button("Region")) AddConcept("Region");
            if (GUILayout.Button("Resource")) AddConcept("Resource");
            EditorGUILayout.EndHorizontal();
            ApplyChangesIfNeeded();
            EditorGUILayout.EndVertical();
        }

        private void DrawTemplate()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Template and Batch Apply", EditorStyles.boldLabel);
            template = (OntologyMapObjectTemplate)EditorGUILayout.ObjectField("Ontology Template", template, typeof(OntologyMapObjectTemplate), false);
            EditorGUILayout.LabelField("A template is reusable meaning data. It never stores a scene object reference.", EditorStyles.wordWrappedMiniLabel);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("New from This Object")) CreateTemplateFromSelection();
            using (new EditorGUI.DisabledScope(template == null))
            {
                if (GUILayout.Button("Capture into Template")) CaptureTemplateFromSelection();
            }
            EditorGUILayout.EndHorizontal();

            replaceExistingTemplateData = EditorGUILayout.ToggleLeft("Replace existing concepts and facts when applying", replaceExistingTemplateData);
            using (new EditorGUI.DisabledScope(template == null || Selection.gameObjects.Length == 0))
            {
                if (GUILayout.Button($"Apply Template to {Selection.gameObjects.Length} Selected Object(s)", GUILayout.Height(25f)))
                {
                    ApplyTemplateToMultiSelection();
                }
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawFacts()
        {
            serializedOntologyObject.Update();
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("3. Facts", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("A fact is a relation and a value: for example, located_in → BeachZone.", EditorStyles.miniLabel);
            EditorGUILayout.PropertyField(facts, true);
            EditorGUILayout.BeginHorizontal();
            newPredicate = EditorGUILayout.TextField(newPredicate);
            newObject = EditorGUILayout.TextField(newObject);
            if (GUILayout.Button("Add", GUILayout.Width(60f)))
            {
                AddFact(newPredicate, newObject);
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("provides → Wood")) AddFact("provides", "Wood");
            if (GUILayout.Button("blocks_path → True")) AddFact("blocks_path", "True");
            if (GUILayout.Button("interactable → True")) AddFact("interactable", "True");
            EditorGUILayout.EndHorizontal();
            ApplyChangesIfNeeded();
            EditorGUILayout.EndVertical();
        }

        private void DrawInjection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("4. Inject and Verify", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Reset World rereads every Ontology Object in the loaded scene. It does not alter the visual map.",
                EditorStyles.wordWrappedMiniLabel);
            if (GUILayout.Button("Reset World from Scene", GUILayout.Height(26f)))
            {
                var bootstrap = FindAnyObjectByType<OntologyWorldBootstrap>();
                if (bootstrap == null)
                {
                    status = "No OntologyWorldBootstrap was found in the loaded scene.";
                }
                else
                {
                    bootstrap.ResetWorld(logReport: false);
                    var count = FindObjectsByType<OntologyObject>(
                        FindObjectsInactive.Include).Length;
                    status = $"Injected {count} Ontology Object(s) into the current world.";
                }
            }
            EditorGUILayout.EndVertical();
        }

        private void UseCurrentSelection()
        {
            Bind(Selection.activeGameObject);
        }

        private void Bind(GameObject target)
        {
            selectedObject = target;
            ontologyObject = selectedObject == null ? null : selectedObject.GetComponent<OntologyObject>();
            serializedOntologyObject = ontologyObject == null ? null : new SerializedObject(ontologyObject);
            entityId = serializedOntologyObject == null ? null : serializedOntologyObject.FindProperty("entityId");
            concepts = serializedOntologyObject == null ? null : serializedOntologyObject.FindProperty("concepts");
            facts = serializedOntologyObject == null ? null : serializedOntologyObject.FindProperty("facts");
            status = string.Empty;
            Repaint();
        }

        private void AddOntologyObject()
        {
            ontologyObject = Undo.AddComponent<OntologyObject>(selectedObject);
            serializedOntologyObject = new SerializedObject(ontologyObject);
            entityId = serializedOntologyObject.FindProperty("entityId");
            concepts = serializedOntologyObject.FindProperty("concepts");
            facts = serializedOntologyObject.FindProperty("facts");
            entityId.stringValue = selectedObject.name;
            serializedOntologyObject.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(ontologyObject);
            EditorSceneManager.MarkSceneDirty(selectedObject.scene);
            status = "Ontology Object added. Define its concepts and facts below.";
        }

        private void CreateTemplateFromSelection()
        {
            const string directory = "Assets/Data/Ontology/MapObjectTemplates";
            EnsureFolder("Assets/Data");
            EnsureFolder("Assets/Data/Ontology");
            EnsureFolder(directory);

            template = CreateInstance<OntologyMapObjectTemplate>();
            template.name = selectedObject.name + "Template";
            CopyOntologyDataToTemplate(template);
            var path = AssetDatabase.GenerateUniqueAssetPath(directory + "/" + template.name + ".asset");
            AssetDatabase.CreateAsset(template, path);
            AssetDatabase.SaveAssets();
            status = "Created template at " + path;
        }

        private void CaptureTemplateFromSelection()
        {
            if (template == null) return;
            Undo.RecordObject(template, "Capture ontology template");
            CopyOntologyDataToTemplate(template);
            EditorUtility.SetDirty(template);
            AssetDatabase.SaveAssets();
            status = "Captured concepts and facts from " + selectedObject.name + ".";
        }

        private void CopyOntologyDataToTemplate(OntologyMapObjectTemplate destination)
        {
            serializedOntologyObject.Update();
            destination.concepts = new string[concepts.arraySize];
            for (var index = 0; index < concepts.arraySize; index++)
            {
                destination.concepts[index] = concepts.GetArrayElementAtIndex(index).stringValue;
            }

            destination.facts = new OntologyFactEntry[facts.arraySize];
            for (var index = 0; index < facts.arraySize; index++)
            {
                var source = facts.GetArrayElementAtIndex(index);
                destination.facts[index] = new OntologyFactEntry
                {
                    predicate = source.FindPropertyRelative("predicate").stringValue,
                    obj = source.FindPropertyRelative("obj").stringValue
                };
            }
        }

        private void ApplyTemplateToMultiSelection()
        {
            var count = 0;
            foreach (var target in Selection.gameObjects)
            {
                if (target == null || !target.scene.IsValid()) continue;
                var targetOntology = target.GetComponent<OntologyObject>();
                if (targetOntology == null) targetOntology = Undo.AddComponent<OntologyObject>(target);
                var targetSerialized = new SerializedObject(targetOntology);
                var targetEntityId = targetSerialized.FindProperty("entityId");
                var targetConcepts = targetSerialized.FindProperty("concepts");
                var targetFacts = targetSerialized.FindProperty("facts");

                if (string.IsNullOrWhiteSpace(targetEntityId.stringValue))
                {
                    targetEntityId.stringValue = BuildUniqueSceneEntityId(target.transform, targetOntology);
                }

                if (replaceExistingTemplateData)
                {
                    targetConcepts.arraySize = 0;
                    targetFacts.arraySize = 0;
                }

                MergeTemplateConcepts(targetConcepts);
                MergeTemplateFacts(targetFacts);
                targetSerialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(targetOntology);
                EditorSceneManager.MarkSceneDirty(target.scene);
                count++;
            }
            status = $"Applied template to {count} object(s). Entity IDs use their hierarchy path when empty.";
        }

        private void MergeTemplateConcepts(SerializedProperty targetConcepts)
        {
            foreach (var concept in template.concepts)
            {
                if (string.IsNullOrWhiteSpace(concept) || ContainsString(targetConcepts, concept)) continue;
                targetConcepts.InsertArrayElementAtIndex(targetConcepts.arraySize);
                targetConcepts.GetArrayElementAtIndex(targetConcepts.arraySize - 1).stringValue = concept;
            }
        }

        private void MergeTemplateFacts(SerializedProperty targetFacts)
        {
            foreach (var fact in template.facts)
            {
                if (fact == null || string.IsNullOrWhiteSpace(fact.predicate) || string.IsNullOrWhiteSpace(fact.obj) || ContainsFact(targetFacts, fact.predicate, fact.obj)) continue;
                targetFacts.InsertArrayElementAtIndex(targetFacts.arraySize);
                var entry = targetFacts.GetArrayElementAtIndex(targetFacts.arraySize - 1);
                entry.FindPropertyRelative("predicate").stringValue = fact.predicate;
                entry.FindPropertyRelative("obj").stringValue = fact.obj;
            }
        }

        private static bool ContainsString(SerializedProperty array, string value)
        {
            for (var index = 0; index < array.arraySize; index++)
            {
                if (array.GetArrayElementAtIndex(index).stringValue == value) return true;
            }
            return false;
        }

        private static bool ContainsFact(SerializedProperty array, string predicate, string obj)
        {
            for (var index = 0; index < array.arraySize; index++)
            {
                var entry = array.GetArrayElementAtIndex(index);
                if (entry.FindPropertyRelative("predicate").stringValue == predicate && entry.FindPropertyRelative("obj").stringValue == obj) return true;
            }
            return false;
        }

        private static string BuildUniqueSceneEntityId(Transform transform, OntologyObject targetOntology)
        {
            var value = transform.name;
            for (var parent = transform.parent; parent != null; parent = parent.parent)
            {
                value = parent.name + "_" + value;
            }
            value = value.Replace(" ", "_");

            var usedIds = new System.Collections.Generic.HashSet<string>();
            foreach (var existing in FindObjectsByType<OntologyObject>(
                         FindObjectsInactive.Include))
            {
                if (existing != null && existing != targetOntology) usedIds.Add(existing.EntityId);
            }

            if (!usedIds.Contains(value)) return value;
            var suffix = 2;
            while (usedIds.Contains(value + "_" + suffix)) suffix++;
            return value + "_" + suffix;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var split = path.LastIndexOf('/');
            if (split <= 0) return;
            AssetDatabase.CreateFolder(path.Substring(0, split), path.Substring(split + 1));
        }

        private void AddConcept(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || concepts == null) return;
            serializedOntologyObject.Update();
            var trimmed = value.Trim();
            for (var index = 0; index < concepts.arraySize; index++)
            {
                if (concepts.GetArrayElementAtIndex(index).stringValue == trimmed) return;
            }
            concepts.InsertArrayElementAtIndex(concepts.arraySize);
            concepts.GetArrayElementAtIndex(concepts.arraySize - 1).stringValue = trimmed;
            newConcept = string.Empty;
            ApplyChanges();
        }

        private void AddFact(string predicate, string obj)
        {
            if (string.IsNullOrWhiteSpace(predicate) || string.IsNullOrWhiteSpace(obj) || facts == null) return;
            serializedOntologyObject.Update();
            facts.InsertArrayElementAtIndex(facts.arraySize);
            var entry = facts.GetArrayElementAtIndex(facts.arraySize - 1);
            entry.FindPropertyRelative("predicate").stringValue = predicate.Trim();
            entry.FindPropertyRelative("obj").stringValue = obj.Trim();
            newObject = string.Empty;
            ApplyChanges();
        }

        private void ApplyChangesIfNeeded()
        {
            if (serializedOntologyObject.ApplyModifiedProperties())
            {
                EditorUtility.SetDirty(ontologyObject);
                EditorSceneManager.MarkSceneDirty(selectedObject.scene);
            }
        }

        private void ApplyChanges()
        {
            serializedOntologyObject.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(ontologyObject);
            EditorSceneManager.MarkSceneDirty(selectedObject.scene);
        }
    }
}
