using System;
using System.Collections.Generic;
using System.Linq;
using Tormia.Ontology.Core;
using UnityEditor;
using UnityEngine;

namespace Tormia.Ontology.Editor
{
    /// <summary>
    /// Read-only first stage of the ontology rule authoring workflow.
    /// Rules are presented as observed and inferred relations, while the
    /// underlying OntologyRuleDefinition remains the source of truth.
    /// </summary>
    public sealed class OntologyRuleWizardWindow : EditorWindow
    {
        private static readonly string[] VariableOptions =
        {
            "?actor", "?target", "?tool", "?item", "?tile", "?npc"
        };

        private static readonly string[] VariableLabels =
        {
            "Actor (same subject)", "Target", "Tool", "Item", "Tile", "NPC"
        };

        private OntologyRuleDatabase ruleDatabase;
        private int selectedRuleIndex = -1;
        private Vector2 ruleListScroll;
        private Vector2 detailScroll;
        private Vector2 sandboxScroll;
        private string filter = string.Empty;
        private string newRuleId = "NewRelationRule";
        private readonly List<RelationDraft> sandboxFacts = new();
        private readonly List<string> sandboxInferences = new();
        private string sandboxSummary;

        [MenuItem("Tools/Ontology/Rule Wizard")]
        public static void Open()
        {
            var window = GetWindow<OntologyRuleWizardWindow>("Ontology Rule Wizard");
            window.titleContent = new GUIContent("Ontology Rule Wizard");
            window.TryLoadDefaultDatabase();
        }

        private void OnEnable()
        {
            minSize = new Vector2(900f, 540f);
            TryLoadDefaultDatabase();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Ontology Rule Wizard", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "This window manages world-level inference relations. It does not directly play animations or attach rules to a single character.",
                MessageType.Info);

            EditorGUI.BeginChangeCheck();
            ruleDatabase = (OntologyRuleDatabase)EditorGUILayout.ObjectField(
                "Rule Database", ruleDatabase, typeof(OntologyRuleDatabase), false);
            if (EditorGUI.EndChangeCheck())
            {
                selectedRuleIndex = -1;
            }

            if (ruleDatabase == null)
            {
                EditorGUILayout.HelpBox("Assign a Rule Database to browse its inference relations.", MessageType.Warning);
                return;
            }

            var definitions = ruleDatabase.Definitions ?? Array.Empty<OntologyRuleDefinition>();
            EditorGUILayout.LabelField($"{definitions.Count} rules in {AssetDatabase.GetAssetPath(ruleDatabase)}", EditorStyles.miniLabel);
            EditorGUILayout.BeginHorizontal();
            newRuleId = EditorGUILayout.TextField("New Rule Id", newRuleId);
            if (GUILayout.Button("Create Rule", GUILayout.Width(105f))) CreateRule();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(6f);

            EditorGUILayout.BeginHorizontal();
            DrawRuleLibrary(definitions);
            DrawRuleDetail(definitions);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawRuleLibrary(IReadOnlyList<OntologyRuleDefinition> definitions)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(335f), GUILayout.ExpandHeight(true));
            EditorGUILayout.LabelField("Rule Library", EditorStyles.boldLabel);
            filter = EditorGUILayout.TextField("Search", filter);

            ruleListScroll = EditorGUILayout.BeginScrollView(ruleListScroll);
            for (var index = 0; index < definitions.Count; index++)
            {
                var definition = definitions[index];
                if (definition == null || !MatchesFilter(definition)) continue;

                var label = string.IsNullOrWhiteSpace(definition.id) ? "(Unnamed rule)" : definition.id;
                var selected = index == selectedRuleIndex;
                if (GUILayout.Toggle(selected, label, "Button"))
                {
                    selectedRuleIndex = index;
                }

                EditorGUILayout.LabelField(
                    $"Observed {CountObserved(definition)}  |  Inferred {definition.effects?.Count ?? 0}",
                    EditorStyles.miniLabel);
                EditorGUILayout.Space(4f);
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawRuleDetail(IReadOnlyList<OntologyRuleDefinition> definitions)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            if (selectedRuleIndex < 0 || selectedRuleIndex >= definitions.Count || definitions[selectedRuleIndex] == null)
            {
                EditorGUILayout.HelpBox("Choose a rule in the library to inspect its relation graph.", MessageType.None);
                EditorGUILayout.EndVertical();
                return;
            }

            var definition = definitions[selectedRuleIndex];
            DrawRuleIdentity(definition);

            detailScroll = EditorGUILayout.BeginScrollView(detailScroll);
            DrawConditionEditor("Observed Relations", definition, OntologyConditionKind.Fact, "Add Observed Relation");
            DrawConditionEditor("Excluded Relations", definition, OntologyConditionKind.NotFact, "Add Excluded Relation");
            DrawConditionEditor("Concept Relations", definition, null, "Add Concept Relation");
            DrawEffectEditor(definition);
            DrawInferenceSandbox(definition);
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(5f);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Validate Database")) ValidateDatabase();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Delete Rule", GUILayout.Width(100f))) DeleteSelectedRule(definition);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private void DrawRuleIdentity(OntologyRuleDefinition definition)
        {
            EditorGUI.BeginChangeCheck();
            var id = EditorGUILayout.TextField("Rule Id", definition.id ?? string.Empty);
            var description = EditorGUILayout.TextField("Description", definition.description ?? string.Empty);
            if (EditorGUI.EndChangeCheck())
            {
                RecordChange("Edit ontology rule", () =>
                {
                    definition.id = id;
                    definition.description = description;
                });
            }
            EditorGUILayout.HelpBox(
                "Actor, Target, Tool and similar choices are protected relation roles. The engine stores them as variables, but you choose their meaning instead of typing or deleting ?actor manually.",
                MessageType.None);
        }

        private void DrawConditionEditor(string title, OntologyRuleDefinition definition, OntologyConditionKind? fixedKind, string addLabel)
        {
            EditorGUILayout.Space(7f);
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            definition.conditions ??= new List<OntologyCondition>();
            for (var index = definition.conditions.Count - 1; index >= 0; index--)
            {
                var condition = definition.conditions[index];
                var isInGroup = condition != null && (fixedKind.HasValue
                    ? condition.kind == fixedKind.Value
                    : condition.kind != OntologyConditionKind.Fact && condition.kind != OntologyConditionKind.NotFact);
                if (!isInGroup) continue;
                DrawConditionRow(definition, condition, index, fixedKind);
            }
            if (GUILayout.Button(addLabel))
            {
                RecordChange("Add ontology relation", () => definition.conditions.Add(new OntologyCondition
                {
                    kind = fixedKind ?? OntologyConditionKind.HasConcept,
                    subject = "?actor",
                    predicate = fixedKind.HasValue ? "state" : string.Empty,
                    obj = fixedKind.HasValue ? "NewState" : "NewConcept"
                }));
            }
        }

        private void DrawConditionRow(OntologyRuleDefinition definition, OntologyCondition condition, int index, OntologyConditionKind? fixedKind)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUI.BeginChangeCheck();
            var kind = fixedKind ?? (OntologyConditionKind)EditorGUILayout.EnumPopup("Relation Form", condition.kind);
            var subject = DrawReferenceField("Subject", condition.subject ?? string.Empty);
            var predicate = kind == OntologyConditionKind.Fact || kind == OntologyConditionKind.NotFact
                ? EditorGUILayout.TextField("Relation", condition.predicate ?? string.Empty)
                : condition.predicate;
            var obj = kind == OntologyConditionKind.HasConcept || kind == OntologyConditionKind.NotConcept
                ? EditorGUILayout.TextField("Concept", condition.obj ?? string.Empty)
                : DrawReferenceField("Object", condition.obj ?? string.Empty);
            if (EditorGUI.EndChangeCheck())
            {
                RecordChange("Edit observed relation", () =>
                {
                    condition.kind = kind;
                    condition.subject = subject;
                    condition.predicate = predicate;
                    condition.obj = obj;
                });
            }
            if (GUILayout.Button("Remove Relation")) RecordChange("Remove ontology relation", () => definition.conditions.RemoveAt(index));
            EditorGUILayout.EndVertical();
        }

        private void DrawEffectEditor(OntologyRuleDefinition definition)
        {
            EditorGUILayout.Space(7f);
            EditorGUILayout.LabelField("Effects (Inference / Persistent State)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Infer Fact is recalculated and disappears when its conditions stop matching. " +
                "Set Persistent State remains until another action or rule changes it.",
                MessageType.None);
            definition.effects ??= new List<OntologyEffect>();
            for (var index = definition.effects.Count - 1; index >= 0; index--)
            {
                var effect = definition.effects[index];
                if (effect == null) continue;
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUI.BeginChangeCheck();
                var kind = DrawEffectKind("Effect Form", effect.kind);
                var subject = DrawReferenceField("Subject", effect.subject ?? string.Empty);
                var predicate = EditorGUILayout.TextField("Relation", effect.predicate ?? string.Empty);
                var obj = kind == OntologyEffectKind.AdjustNumberFact
                    ? EditorGUILayout.TextField("Amount", effect.obj ?? string.Empty)
                    : DrawReferenceField("Object", effect.obj ?? string.Empty);
                if (EditorGUI.EndChangeCheck())
                {
                    RecordChange("Edit rule effect", () =>
                    {
                        effect.kind = kind;
                        effect.subject = subject;
                        effect.predicate = predicate;
                        effect.obj = obj;
                    });
                }
                if (GUILayout.Button("Remove Effect")) RecordChange("Remove rule effect", () => definition.effects.RemoveAt(index));
                EditorGUILayout.EndVertical();
            }
            if (GUILayout.Button("Add Effect"))
            {
                RecordChange("Add rule effect", () => definition.effects.Add(new OntologyEffect
                {
                    kind = OntologyEffectKind.AddFact,
                    subject = "?actor",
                    predicate = "state",
                    obj = "NewState"
                }));
            }
        }

        private void DrawInferenceSandbox(OntologyRuleDefinition definition)
        {
            EditorGUILayout.Space(12f);
            EditorGUILayout.LabelField("Inference Sandbox", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Enter temporary observed relations, then run only the selected rule against a separate in-memory world. This never changes the scene or saved world facts.",
                MessageType.None);

            sandboxScroll = EditorGUILayout.BeginScrollView(sandboxScroll, GUILayout.MaxHeight(180f));
            for (var index = sandboxFacts.Count - 1; index >= 0; index--)
            {
                var fact = sandboxFacts[index];
                EditorGUILayout.BeginHorizontal();
                fact.subject = EditorGUILayout.TextField(fact.subject, GUILayout.MinWidth(110f));
                fact.predicate = EditorGUILayout.TextField(fact.predicate, GUILayout.MinWidth(110f));
                fact.obj = EditorGUILayout.TextField(fact.obj, GUILayout.MinWidth(110f));
                if (GUILayout.Button("X", GUILayout.Width(24f))) sandboxFacts.RemoveAt(index);
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Add Observed Relation")) sandboxFacts.Add(new RelationDraft { subject = "Player", predicate = "state", obj = "NewState" });
            if (GUILayout.Button("Run Selected Rule", GUILayout.Width(145f))) RunSandbox(definition);
            EditorGUILayout.EndHorizontal();

            if (string.IsNullOrWhiteSpace(sandboxSummary)) return;
            EditorGUILayout.Space(4f);
            EditorGUILayout.HelpBox(sandboxSummary, sandboxInferences.Count > 0 ? MessageType.Info : MessageType.Warning);
            foreach (var inference in sandboxInferences)
            {
                EditorGUILayout.LabelField("+ " + inference, EditorStyles.wordWrappedMiniLabel);
            }
        }

        private void RunSandbox(OntologyRuleDefinition definition)
        {
            var warnings = OntologyRuleValidator.Validate(new[] { definition });
            if (warnings.Count > 0)
            {
                sandboxInferences.Clear();
                sandboxSummary = "This rule needs structural fixes before it can be simulated:\n" + string.Join("\n", warnings);
                return;
            }

            var world = new OntologyWorldState();
            var before = new HashSet<OntologyFact>();
            foreach (var draft in sandboxFacts)
            {
                if (world.AddFact(draft.subject, draft.predicate, draft.obj))
                {
                    before.Add(new OntologyFact(draft.subject, draft.predicate, draft.obj));
                }
            }

            var engine = new OntologyRuleEngine();
            engine.AddRule(OntologyRuleCompiler.Compile(definition), definition);
            var result = new OntologySimulation(8).RunUntilStable(world, engine);
            sandboxInferences.Clear();
            foreach (var fact in world.Facts)
            {
                if (!before.Contains(fact)) sandboxInferences.Add(fact.ToString());
            }
            sandboxInferences.Sort(StringComparer.Ordinal);
            sandboxSummary = result.ReachedStableState
                ? $"Simulation completed in {result.Iterations} iteration(s). {sandboxInferences.Count} new relation(s) were inferred."
                : $"Simulation reached its safety limit after {result.Iterations} iteration(s). Review this rule for a relation cycle.";
        }

        private static void DrawConditionGroup(string title, List<OntologyCondition> conditions, params OntologyConditionKind[] kinds)
        {
            var filtered = conditions == null
                ? Enumerable.Empty<OntologyCondition>()
                : conditions.Where(condition => condition != null && kinds.Contains(condition.kind));
            DrawRelationSection(title, filtered.Select(FormatCondition), new Color(0.72f, 0.86f, 1f));
        }

        private static void DrawEffects(List<OntologyEffect> effects)
        {
            var relations = effects == null
                ? Enumerable.Empty<string>()
                : effects.Where(effect => effect != null).Select(FormatEffect);
            DrawRelationSection("Effects (Inference / Persistent State)", relations, new Color(0.72f, 1f, 0.76f));
        }

        private static void DrawRelationSection(string title, IEnumerable<string> relations, Color color)
        {
            var items = relations.ToList();
            EditorGUILayout.Space(7f);
            var previous = GUI.contentColor;
            GUI.contentColor = color;
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            GUI.contentColor = previous;

            if (items.Count == 0)
            {
                EditorGUILayout.LabelField("No relations", EditorStyles.miniLabel);
                return;
            }

            foreach (var relation in items)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField(relation, EditorStyles.wordWrappedLabel);
                EditorGUILayout.EndVertical();
            }
        }

        private static string FormatCondition(OntologyCondition condition)
        {
            return condition.kind switch
            {
                OntologyConditionKind.Fact => FormatTriple(condition.subject, condition.predicate, condition.obj),
                OntologyConditionKind.NotFact => "Not present: " + FormatTriple(condition.subject, condition.predicate, condition.obj),
                OntologyConditionKind.HasConcept => $"{Safe(condition.subject)} has concept {Safe(condition.obj)}",
                OntologyConditionKind.NotConcept => $"{Safe(condition.subject)} does not have concept {Safe(condition.obj)}",
                OntologyConditionKind.NotEqual => $"{Safe(condition.subject)} is different from {Safe(condition.obj)}",
                _ => condition.kind.ToString()
            };
        }

        private static string FormatEffect(OntologyEffect effect)
        {
            var relation = FormatTriple(effect.subject, effect.predicate, effect.obj);
            return effect.kind switch
            {
                OntologyEffectKind.AddFact => "Infer (recomputed): " + relation,
                OntologyEffectKind.RemoveFact => "Remove: " + relation,
                OntologyEffectKind.SetFact => "Set persistent state: " + relation,
                OntologyEffectKind.AdjustNumberFact => "Adjust persistent number: " + relation,
                _ => effect.kind + ": " + relation
            };
        }

        private static OntologyEffectKind DrawEffectKind(string label, OntologyEffectKind value)
        {
            var values = (OntologyEffectKind[])Enum.GetValues(typeof(OntologyEffectKind));
            var labels = values.Select(kind => kind.DisplayName()).ToArray();
            var index = Math.Max(0, Array.IndexOf(values, value));
            return values[EditorGUILayout.Popup(label, index, labels)];
        }

        private static string FormatTriple(string subject, string predicate, string obj)
        {
            return $"{Safe(subject)}  --{Safe(predicate)}-->  {Safe(obj)}";
        }

        private static string DrawReferenceField(string label, string value)
        {
            var currentIndex = Array.IndexOf(VariableOptions, value);
            var fixedIndex = VariableOptions.Length;
            var selectedIndex = currentIndex >= 0 ? currentIndex : fixedIndex;
            var labels = VariableLabels.Concat(new[] { "Fixed identifier" }).ToArray();
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel(label);
            var chosenIndex = EditorGUILayout.Popup(selectedIndex, labels, GUILayout.Width(160f));
            EditorGUILayout.EndHorizontal();
            if (chosenIndex != selectedIndex)
            {
                return chosenIndex == fixedIndex ? "NewEntity" : VariableOptions[chosenIndex];
            }

            if (chosenIndex != fixedIndex) return VariableOptions[chosenIndex];
            var fixedValue = EditorGUILayout.TextField("Identifier", value ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(fixedValue)) return fixedValue;
            EditorGUILayout.HelpBox("An identifier cannot be empty. Choose a role or enter a fixed identifier.", MessageType.Warning);
            return value;
        }

        private static string Safe(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "(empty)" : value;
        }

        private static int CountObserved(OntologyRuleDefinition definition)
        {
            return definition.conditions?.Count(condition => condition != null && condition.kind == OntologyConditionKind.Fact) ?? 0;
        }

        private bool MatchesFilter(OntologyRuleDefinition definition)
        {
            if (string.IsNullOrWhiteSpace(filter)) return true;
            return (definition.id ?? string.Empty).IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0
                || (definition.description ?? string.Empty).IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void CreateRule()
        {
            if (string.IsNullOrWhiteSpace(newRuleId))
            {
                EditorUtility.DisplayDialog("Rule Id required", "Enter an id before creating a rule.", "OK");
                return;
            }

            RecordChange("Create ontology rule", () => ruleDatabase.CreateDefinition(newRuleId.Trim()));
            selectedRuleIndex = ruleDatabase.Definitions.Count - 1;
        }

        private void DeleteSelectedRule(OntologyRuleDefinition definition)
        {
            if (!EditorUtility.DisplayDialog("Delete ontology rule", $"Delete '{definition.id}' from this Rule Database?", "Delete", "Cancel")) return;
            RecordChange("Delete ontology rule", () => ruleDatabase.RemoveDefinition(definition));
            selectedRuleIndex = -1;
        }

        private void ValidateDatabase()
        {
            var warnings = OntologyRuleValidator.Validate(ruleDatabase.Definitions);
            if (warnings.Count == 0)
            {
                EditorUtility.DisplayDialog("Ontology validation", "This Rule Database has no structural validation warnings.", "OK");
                return;
            }

            EditorUtility.DisplayDialog("Ontology validation", string.Join("\n", warnings), "OK");
        }

        private void RecordChange(string label, Action mutation)
        {
            Undo.RecordObject(ruleDatabase, label);
            mutation();
            EditorUtility.SetDirty(ruleDatabase);
            AssetDatabase.SaveAssets();
        }

        private void TryLoadDefaultDatabase()
        {
            if (ruleDatabase != null) return;
            var guids = AssetDatabase.FindAssets("t:OntologyRuleDatabase");
            if (guids.Length == 0) return;
            var path = AssetDatabase.GUIDToAssetPath(guids[0]);
            ruleDatabase = AssetDatabase.LoadAssetAtPath<OntologyRuleDatabase>(path);
        }

        private sealed class RelationDraft
        {
            public string subject;
            public string predicate;
            public string obj;
        }
    }
}
