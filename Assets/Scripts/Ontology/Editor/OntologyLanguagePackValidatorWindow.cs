using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Tormia.Ontology.Core;
using UnityEditor;
using UnityEngine;

namespace Tormia.Ontology.Editor
{
    /// <summary>
    /// Gives designers a read-only health check for the CSV language layer.
    /// CSV remains the source of truth and can be edited with a spreadsheet.
    /// </summary>
    public sealed class OntologyLanguagePackValidatorWindow : EditorWindow
    {
        private Vector2 scroll;

        [MenuItem("Tools/Ontology/Language Pack Validator")]
        public static void Open() =>
            GetWindow<OntologyLanguagePackValidatorWindow>("Language Pack");

        [MenuItem("Tools/Ontology/Validate Language Packs")]
        public static void ValidateFromMenu()
        {
            OntologyLanguagePackService.Reload();
            var messages = OntologyLanguagePackService.ValidationMessages
                .Concat(ValidateProjectCoverage())
                .ToArray();
            if (messages.Length == 0)
            {
                Debug.Log(
                    "[Ontology Language Pack] Validation passed. " +
                    OntologyLanguagePackService.Registry.Terms.Count +
                    " canonical terms loaded.");
                return;
            }

            Debug.LogWarning(
                "[Ontology Language Pack] " +
                messages.Length +
                " issue(s):\n- " +
                string.Join("\n- ", messages));
        }

        public static IReadOnlyList<string> ValidateProjectCoverage()
        {
            var messages = new List<string>();
            void Require(string key, string source)
            {
                foreach (var locale in new[] { "en", "ko" })
                {
                    if (!OntologyLanguagePackService.HasText(locale, key))
                        messages.Add(
                            $"Missing {locale} translation for '{key}' ({source}).");
                }
            }

            foreach (var relation in OntologyLanguagePackService.Registry.Terms
                         .Where(value =>
                             value != null &&
                             value.Kind == OntologyTermKind.Relation))
                Require("fact." + relation.CanonicalId, "readable fact");

            foreach (var database in LoadAssets<OntologyRuleDatabase>())
            foreach (var rule in database.Definitions.Where(value => value != null))
            {
                Require("rule." + rule.id + ".name", database.name);
                Require("rule." + rule.id + ".description", database.name);
            }

            foreach (var profile in LoadAssets<OntologyPhysicalProfile>())
                Require(
                    "physical_profile." + profile.profileId,
                    profile.name);
            foreach (var profile in LoadAssets<OntologyAttachmentProfile>())
                Require(
                    "attachment_profile." + profile.profileId,
                    profile.name);
            foreach (var database in LoadAssets<OntologyCharacterPartDatabase>())
            foreach (var part in database.Definitions.Where(value => value != null))
                Require("character_part." + part.partId, database.name);

            foreach (var catalog in LoadAssets<OntologyPlaceableCatalog>())
            foreach (var definition in catalog.Definitions.Where(value => value != null))
            {
                if (!string.IsNullOrWhiteSpace(definition.displayNameKey))
                {
                    Require(definition.displayNameKey, catalog.name);
                    Require(
                        definition.displayNameKey + ".description",
                        catalog.name);
                }
                Require(
                    "category." +
                    (string.IsNullOrWhiteSpace(definition.category)
                        ? "Other"
                        : definition.category),
                    catalog.name);
            }

            foreach (var field in typeof(OntologyUILabels).GetFields(
                         BindingFlags.Instance | BindingFlags.Public)
                         .Where(value => value.FieldType == typeof(string)))
                Require("labels." + field.Name, nameof(OntologyUILabels));

            return messages.Distinct().OrderBy(value => value).ToArray();
        }

        private static IEnumerable<T> LoadAssets<T>() where T : Object
        {
            return AssetDatabase.FindAssets("t:" + typeof(T).Name)
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<T>)
                .Where(value => value != null);
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField(
                "Ontology Language Pack",
                EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Edit the five CSV files under " +
                "Assets/Resources/Ontology/Localization. " +
                "Localized words are display/input aliases only; rules and saves " +
                "always use canonical English identifiers.",
                MessageType.Info);

            EditorGUILayout.LabelField(
                "Current display language",
                OntologyLanguagePackService.CurrentLanguage.ToString());
            EditorGUILayout.LabelField(
                "Canonical terms",
                OntologyLanguagePackService.Registry.Terms.Count.ToString());

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Reload and Validate"))
            {
                OntologyLanguagePackService.Reload();
                Repaint();
            }
            if (GUILayout.Button("Select CSV Folder"))
            {
                var asset = AssetDatabase.LoadAssetAtPath<Object>(
                    "Assets/Resources/Ontology/Localization");
                Selection.activeObject = asset;
                EditorGUIUtility.PingObject(asset);
            }
            EditorGUILayout.EndHorizontal();

            var messages = OntologyLanguagePackService.ValidationMessages
                .Concat(ValidateProjectCoverage())
                .ToArray();
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(
                messages.Length == 0
                    ? "Validation passed"
                    : messages.Length + " issue(s)",
                EditorStyles.boldLabel);

            if (messages.Length == 0)
            {
                EditorGUILayout.HelpBox(
                    "No duplicate IDs, ambiguous aliases, missing translations, " +
                    "or invalid references were found.",
                    MessageType.Info);
                return;
            }

            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (var message in messages.Where(value =>
                         !string.IsNullOrWhiteSpace(value)))
            {
                EditorGUILayout.HelpBox(message, MessageType.Warning);
            }
            EditorGUILayout.EndScrollView();
        }
    }
}
