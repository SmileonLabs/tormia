using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
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

            void RequireConcepts(IEnumerable<string> canonicalIds, string source)
            {
                foreach (var message in
                         OntologyLanguagePackService.ValidateRegisteredTerms(
                             canonicalIds,
                             OntologyTermKind.Concept))
                {
                    messages.Add(message + " (" + source + ")");
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
                RequireConcepts(
                    ConceptReferences(rule.conditions, rule.effects),
                    database.name + "/" + rule.id);
            }

            foreach (var profile in LoadAssets<OntologyActorProfile>())
            {
                RequireConcepts(profile.defaultConcepts, profile.name);
                RequireConcepts(
                    ConceptReferences(profile.defaultFacts),
                    profile.name);
            }
            foreach (var template in LoadAssets<OntologyMapObjectTemplate>())
            {
                RequireConcepts(template.concepts, template.name);
                RequireConcepts(template.retiredConcepts, template.name);
                RequireConcepts(
                    ConceptReferences(template.facts),
                    template.name);
                RequireConcepts(
                    ConceptReferences(template.retiredFacts),
                    template.name);
            }
            foreach (var database in LoadAssets<OntologyRuleBlockPresetDatabase>())
            foreach (var preset in database.Presets.Where(value => value != null))
            {
                RequireConcepts(
                    preset.requiredConcepts,
                    database.name + "/" + preset.presetId);
                RequireConcepts(
                    preset.requiredExistingConcepts,
                    database.name + "/" + preset.presetId);
                RequireConcepts(
                    ConceptReferences(preset.requiredFacts),
                    database.name + "/" + preset.presetId);
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
                if (string.IsNullOrWhiteSpace(definition.displayNameKey))
                {
                    messages.Add(
                        "Placeable '" + definition.definitionId +
                        "' has no display-name localization key (" +
                        catalog.name + ").");
                }
                else
                {
                    Require(definition.displayNameKey, catalog.name);
                    Require(
                        definition.displayNameKey + ".description",
                        catalog.name);
                }
                RequireConcepts(
                    ConceptReferences(
                        definition.introducedFacts?
                            .Where(value => value != null)
                            .Select(value => value.fact)),
                    catalog.name + "/" + definition.definitionId);
                RequireConcepts(
                    ConceptReferences(
                        definition.retiredFacts?
                            .Where(value => value != null)
                            .Select(value => value.fact)),
                    catalog.name + "/" + definition.definitionId);
                Require(
                    "category." +
                    (string.IsNullOrWhiteSpace(definition.category)
                        ? "Other"
                        : definition.category),
                    catalog.name);
            }

            foreach (var database in LoadAssets<OntologyQuestDatabase>())
            foreach (var quest in database.Definitions.Where(value => value != null))
            {
                if (!string.IsNullOrWhiteSpace(quest.titleKey))
                    Require(quest.titleKey, database.name);
                if (!string.IsNullOrWhiteSpace(quest.reasonKey))
                    Require(quest.reasonKey, database.name);
                foreach (var goal in quest.goals.Where(value => value != null))
                {
                    if (!string.IsNullOrWhiteSpace(goal.descriptionKey))
                        Require(goal.descriptionKey, database.name);
                }
            }

            foreach (var key in RequiredRuntimeKeys)
                Require(key, "runtime localization contract");

            ValidateSourceBoundary(
                messages,
                "Assets/Scripts/Ontology/Unity/Networking/OntologyWorldAuthorityAccountEntryFlow.cs",
                "SetStatus(\"",
                "Account entry status must use a localization key.");
            ValidateSourceBoundary(
                messages,
                "Assets/Scripts/Ontology/UI/OntologyQuestActionPanel.cs",
                "actionEmptyLabel.text = \"",
                "Quest action feedback must use a localization key.");
            ValidateCombatStatusCoverage(messages, Require);

            foreach (var field in typeof(OntologyUILabels).GetFields(
                         BindingFlags.Instance | BindingFlags.Public)
                         .Where(value => value.FieldType == typeof(string)))
                Require("labels." + field.Name, nameof(OntologyUILabels));

            return messages.Distinct().OrderBy(value => value).ToArray();
        }

        private static IEnumerable<string> ConceptReferences(
            IEnumerable<OntologyFactEntry> facts)
        {
            return (facts ?? Enumerable.Empty<OntologyFactEntry>())
                .Where(value =>
                    value != null &&
                    string.Equals(
                        value.predicate,
                        "has_concept",
                        System.StringComparison.OrdinalIgnoreCase))
                .Select(value => value.obj);
        }

        private static IEnumerable<string> ConceptReferences(
            IEnumerable<OntologyCondition> conditions,
            IEnumerable<OntologyEffect> effects)
        {
            var conditionConcepts =
                (conditions ?? Enumerable.Empty<OntologyCondition>())
                .Where(value => value != null)
                .Where(value =>
                    value.kind == OntologyConditionKind.HasConcept ||
                    value.kind == OntologyConditionKind.NotConcept ||
                    (value.kind == OntologyConditionKind.Fact ||
                     value.kind == OntologyConditionKind.NotFact) &&
                    string.Equals(
                        value.predicate,
                        "has_concept",
                        System.StringComparison.OrdinalIgnoreCase))
                .Select(value => value.obj);
            var effectConcepts =
                (effects ?? Enumerable.Empty<OntologyEffect>())
                .Where(value =>
                    value != null &&
                    string.Equals(
                        value.predicate,
                        "has_concept",
                        System.StringComparison.OrdinalIgnoreCase))
                .Select(value => value.obj);
            return conditionConcepts.Concat(effectConcepts);
        }

        private static readonly string[] RequiredRuntimeKeys =
        {
            "category.Combat",
            "physical_profile.AuthorityKinematic",
            "physical_profile.HandheldWeapon",
            "physical_profile.LocalCharacterController",
            "character_template.player",
            "common.none",
            "common.unknown",
            "ui.account.profile_relation",
            "placement.surface.AnyCollider",
            "placement.surface.Ground",
            "placement.surface.Water",
            "ui.account.status.operation_failed",
            "ui.quest_panel.authority_not_ready",
            "ui.quest_panel.target_not_ready",
            "ui.quest_panel.tool_not_ready",
            "ui.quest_panel.definition_missing",
            "ui.quest_panel.action_rejected"
        };

        private static void ValidateSourceBoundary(
            ICollection<string> messages,
            string assetPath,
            string forbiddenText,
            string explanation)
        {
            if (!File.Exists(assetPath))
                return;
            var source = File.ReadAllText(assetPath);
            if (source.Contains(forbiddenText))
                messages.Add(explanation + " (" + assetPath + ")");
        }

        private static void ValidateCombatStatusCoverage(
            ICollection<string> messages,
            System.Action<string, string> require)
        {
            const string assetPath =
                "Assets/Scripts/Ontology/Unity/OntologyCombatController.cs";
            if (!File.Exists(assetPath))
                return;
            var source = File.ReadAllText(assetPath);
            var statusIds = Regex.Matches(
                    source,
                    "ShowCombatStatus\\s*\\(\\s*\"([^\"]+)\"")
                .Cast<Match>()
                .Select(value => value.Groups[1].Value)
                .Concat(new[]
                {
                    "equip_failed",
                    "equip_actor_position_unavailable",
                    "equip_target_position_unavailable",
                    "equip_target_out_of_range"
                })
                .Distinct();
            foreach (var statusId in statusIds)
            {
                require(
                    "ui.combat.status." + statusId + ".title",
                    "combat status");
                require(
                    "ui.combat.status." + statusId + ".detail",
                    "combat status");
            }
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
