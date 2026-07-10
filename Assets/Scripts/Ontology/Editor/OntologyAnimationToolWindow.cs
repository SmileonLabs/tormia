using System;
using UnityEditor;
using UnityEngine;
using Tormia.Ontology.Core;

namespace Tormia.Ontology.Editor
{
    public sealed class OntologyAnimationToolWindow : EditorWindow
    {
        private OntologyAnimationDatabase database;
        private OntologyActorProfile profile;
        private string intent = "Idle";
        private Vector2 scroll;

        [MenuItem("Tools/Ontology/Animation Tool")]
        public static void Open()
        {
            GetWindow<OntologyAnimationToolWindow>("Ontology Animation Tool");
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox("애니메이션 정의를 Actor/Rig/Capability 조건으로 점검하는 미리보기 도구입니다.", MessageType.Info);
            database = (OntologyAnimationDatabase)EditorGUILayout.ObjectField("Database", database, typeof(OntologyAnimationDatabase), false);
            profile = (OntologyActorProfile)EditorGUILayout.ObjectField("Actor Profile", profile, typeof(OntologyActorProfile), false);
            intent = EditorGUILayout.TextField("Intent", intent);

            if (database == null)
            {
                return;
            }

            if (GUILayout.Button("Validate Database"))
            {
                Validate();
            }

            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (var definition in database.Definitions)
            {
                if (definition == null) continue;
                if (!HasIntent(definition, intent)) continue;
                var usable = CanUse(definition);
                EditorGUILayout.LabelField((usable ? "[OK] " : "[SKIP] ") + definition.animationId,
                    definition.clip == null ? "Missing Clip" : definition.clip.name);
            }
            EditorGUILayout.EndScrollView();
        }

        private bool CanUse(OntologyAnimationDefinition definition)
        {
            if (definition.clip == null || profile == null) return definition.clip != null;
            return Matches(definition.actorTypes, profile.actorType) &&
                   Matches(definition.rigTypes, profile.rigType) &&
                   (definition.requiredCapabilities == null || AllCapabilities(definition));
        }

        private bool AllCapabilities(OntologyAnimationDefinition definition)
        {
            foreach (var capability in definition.requiredCapabilities)
                if (!profile.HasCapability(capability)) return false;
            return true;
        }

        private static bool Matches(string[] values, string expected)
        {
            if (values == null || values.Length == 0) return true;
            foreach (var value in values)
                if (string.Equals(value, expected, StringComparison.Ordinal)) return true;
            return false;
        }

        private static bool HasIntent(OntologyAnimationDefinition definition, string value)
        {
            if (definition.intents == null) return false;
            foreach (var candidate in definition.intents)
                if (candidate == value) return true;
            return false;
        }

        private void Validate()
        {
            var ids = new System.Collections.Generic.HashSet<string>();
            var errors = 0;
            foreach (var definition in database.Definitions)
            {
                if (definition == null || string.IsNullOrWhiteSpace(definition.animationId))
                {
                    Debug.LogError("Animation database contains an empty definition or id.", database);
                    errors++;
                    continue;
                }
                if (!ids.Add(definition.animationId))
                {
                    Debug.LogError("Duplicate animation id: " + definition.animationId, database);
                    errors++;
                }
                if (definition.clip == null)
                {
                    Debug.LogError("Missing clip for animation: " + definition.animationId, database);
                    errors++;
                }
            }
            Debug.Log(errors == 0 ? "Ontology animation database validation passed." : "Ontology animation database validation found " + errors + " error(s).", database);
        }
    }
}
