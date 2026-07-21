using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    public enum OntologyDisplayLanguage
    {
        Korean,
        English
    }

    public sealed class OntologyInputResolution
    {
        public bool Success { get; set; }
        public string CanonicalValue { get; set; }
        public string Message { get; set; }
        public IReadOnlyList<string> Candidates { get; set; } = Array.Empty<string>();
    }

    /// <summary>
    /// Loads editable CSV language packs from Resources. Localized labels never
    /// enter the rule engine; all save and world operations use canonical ids.
    /// </summary>
    public static class OntologyLanguagePackService
    {
        private const string LanguagePreferenceKey = "Tormia.Ontology.Language";
        private const string ResourceRoot = "Ontology/Localization/";
        private static readonly Dictionary<string, Dictionary<string, string>> localizedText =
            new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> termMigrations =
            new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> definitionMigrations =
            new(StringComparer.OrdinalIgnoreCase);
        private static readonly List<string> validationMessages = new();
        private static readonly OntologyTermRegistry registry = new();
        private static bool loaded;
        private static OntologyDisplayLanguage currentLanguage;

        public static event Action LanguageChanged;
        public static OntologyTermRegistry Registry
        {
            get { EnsureLoaded(); return registry; }
        }
        public static IReadOnlyList<string> ValidationMessages
        {
            get { EnsureLoaded(); return validationMessages; }
        }
        public static OntologyDisplayLanguage CurrentLanguage
        {
            get
            {
                EnsureLoaded();
                return currentLanguage;
            }
        }
        public static string CurrentLocale =>
            CurrentLanguage == OntologyDisplayLanguage.Korean ? "ko" : "en";

        public static void SetLanguage(OntologyDisplayLanguage language)
        {
            EnsureLoaded();
            if (currentLanguage == language)
                return;
            currentLanguage = language;
            PlayerPrefs.SetInt(LanguagePreferenceKey, (int)language);
            PlayerPrefs.Save();
            LanguageChanged?.Invoke();
        }

        public static void Reload()
        {
            loaded = false;
            EnsureLoaded();
            LanguageChanged?.Invoke();
        }

        public static string Text(string key, string fallback = null)
        {
            EnsureLoaded();
            if (string.IsNullOrWhiteSpace(key))
                return fallback ?? string.Empty;
            if (TryLocalized(CurrentLocale, key, out var value))
                return value;
            if (TryLocalized("en", key, out value))
                return value;
            return string.IsNullOrWhiteSpace(fallback) ? key : fallback;
        }

        public static bool HasText(string locale, string key)
        {
            EnsureLoaded();
            return !string.IsNullOrWhiteSpace(locale) &&
                   !string.IsNullOrWhiteSpace(key) &&
                   TryLocalized(locale, key, out _);
        }

        public static string RuleName(string ruleId)
        {
            return Text(
                "rule." + (ruleId ?? string.Empty) + ".name",
                Nicify(ruleId));
        }

        public static string RuleDescription(string ruleId, string fallback = null)
        {
            return Text(
                "rule." + (ruleId ?? string.Empty) + ".description",
                string.IsNullOrWhiteSpace(fallback) ? RuleName(ruleId) : fallback);
        }

        public static string PhysicalProfileName(string profileId)
        {
            return Text(
                "physical_profile." + (profileId ?? string.Empty),
                Nicify(profileId));
        }

        public static string AttachmentProfileName(string profileId)
        {
            return Text(
                "attachment_profile." + (profileId ?? string.Empty),
                Nicify(profileId));
        }

        public static string CharacterPartName(string partId, string fallback = null)
        {
            return Text(
                "character_part." + (partId ?? string.Empty),
                string.IsNullOrWhiteSpace(fallback) ? Nicify(partId) : fallback);
        }

        public static string ActionLabel(OntologyActionCandidate candidate)
        {
            if (candidate == null)
                return string.Empty;
            var action = candidate.Action;
            var target = DisplaySemanticObject(action.TargetId.Value);
            var tool = DisplaySemanticObject(action.ToolId.Value);
            var verb = action.Verb.Value;
            var key = "action." + verb;
            var fallback = string.IsNullOrWhiteSpace(candidate.Label)
                ? Nicify(verb) + " " + target
                : candidate.Label;
            var template = Text(key, fallback);
            try
            {
                return template
                    .Replace("{target}", target)
                    .Replace("{tool}", tool)
                    .Replace(
                        "{actor}",
                        DisplaySemanticObject(action.ActorId.Value));
            }
            catch
            {
                return fallback;
            }
        }

        public static string DisplaySemanticObject(string canonicalId)
        {
            if (string.IsNullOrWhiteSpace(canonicalId))
                return string.Empty;
            return canonicalId.StartsWith(
                "Part_",
                StringComparison.OrdinalIgnoreCase)
                ? CharacterPartName(canonicalId)
                : Term(canonicalId);
        }

        public static string PlaceableDescription(
            OntologyPlaceableDefinition definition)
        {
            if (definition == null)
                return string.Empty;
            var key = string.IsNullOrWhiteSpace(definition.displayNameKey)
                ? "placeable." + definition.definitionId
                : definition.displayNameKey;
            return Text(
                key + ".description",
                string.IsNullOrWhiteSpace(definition.description)
                    ? Category(definition.category)
                    : definition.description);
        }

        public static string Category(string canonicalCategory)
        {
            canonicalCategory = string.IsNullOrWhiteSpace(canonicalCategory)
                ? "Other"
                : canonicalCategory.Trim();
            return Text(
                "category." + canonicalCategory,
                Nicify(canonicalCategory));
        }

        public static string Term(string canonicalId)
        {
            EnsureLoaded();
            if (string.IsNullOrWhiteSpace(canonicalId))
                return canonicalId;
            var definition = registry.Find(canonicalId);
            var fallback = Nicify(definition?.CanonicalId ?? canonicalId);
            return definition != null && !string.IsNullOrWhiteSpace(definition.LabelKey)
                ? Text(definition.LabelKey, fallback)
                : fallback;
        }

        public static string FormatInstanceName(
            OntologyPlaceableDefinition definition,
            string instanceName)
        {
            if (definition == null)
                return instanceName;
            var baseName = Text(
                definition.displayNameKey,
                definition.EffectiveDisplayName);
            if (string.IsNullOrWhiteSpace(instanceName) ||
                string.IsNullOrWhiteSpace(definition.definitionId) ||
                !instanceName.StartsWith(
                    definition.definitionId,
                    StringComparison.OrdinalIgnoreCase))
            {
                return baseName;
            }

            var suffix = instanceName.Substring(definition.definitionId.Length)
                .TrimStart('_', ' ');
            return string.IsNullOrWhiteSpace(suffix)
                ? baseName
                : baseName + " " + suffix.Replace('_', ' ');
        }

        public static OntologyInputResolution ResolveObjectInput(
            string input,
            string relation,
            IEnumerable<string> knownValues)
        {
            EnsureLoaded();
            input = OntologyCanonicalId.NormalizeInput(input);
            relation = CanonicalTerm(relation);
            if (string.IsNullOrWhiteSpace(input))
                return Failure(Text("validation.value_required", "A value is required."));

            var known = (knownValues ?? Array.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var directKnown = known.FirstOrDefault(value =>
                string.Equals(value, input, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Term(value), input, StringComparison.OrdinalIgnoreCase) ||
                (IsRuleReferenceRelation(relation) &&
                 string.Equals(RuleName(value), input, StringComparison.OrdinalIgnoreCase)));
            if (!string.IsNullOrWhiteSpace(directKnown))
                return Success(CanonicalTerm(directKnown));

            var relationDefinition = registry.Find(relation);
            var valueKind = relationDefinition?.ValueKind ?? OntologyValueKind.Value;
            var expectedKind = ExpectedTermKind(valueKind);
            if (registry.TryResolve(
                    input,
                    CurrentLocale,
                    expectedKind,
                    out var canonical,
                    out var candidates))
            {
                return Success(CanonicalTerm(canonical));
            }
            if (candidates.Count > 1)
            {
                return new OntologyInputResolution
                {
                    Success = false,
                    Candidates = candidates,
                    Message = Text(
                        "validation.ambiguous_term",
                        "This word matches more than one ontology term.")
                };
            }

            switch (valueKind)
            {
                case OntologyValueKind.Number:
                    return double.TryParse(
                        input,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out var number)
                        ? Success(number.ToString(CultureInfo.InvariantCulture))
                        : Failure(Text(
                            "validation.number_required",
                            "Enter a valid number."));
                case OntologyValueKind.Boolean:
                    if (IsTrue(input)) return Success("true");
                    if (IsFalse(input)) return Success("false");
                    return Failure(Text(
                        "validation.boolean_required",
                        "Choose true or false."));
                case OntologyValueKind.UserText:
                    return Success(input);
            }

            if (OntologyCanonicalId.ContainsNonAscii(input))
            {
                return Failure(Text(
                    "validation.unknown_localized_term",
                    "This word is not registered in the ontology language pack."));
            }

            if (valueKind == OntologyValueKind.EntityRef)
                return Success(input);

            var suggestion = OntologyCanonicalId.SuggestConceptOrValue(input);
            return string.IsNullOrWhiteSpace(suggestion)
                ? Failure(Text("validation.invalid_identifier", "Enter a valid ontology id."))
                : Success(suggestion);
        }

        private static bool IsRuleReferenceRelation(string relation)
        {
            return relation == OntologyPredicates.SkillGrantRequiresRule ||
                   relation == OntologyPredicates.HasRuleBlock;
        }

        public static string CanonicalTerm(string value)
        {
            EnsureLoaded();
            value = OntologyCanonicalId.NormalizeInput(value);
            if (termMigrations.TryGetValue(value, out var migrated))
                value = migrated;
            var definition = registry.Find(value);
            return definition?.CanonicalId ?? value;
        }

        public static string CanonicalObjectForRelation(
            string relation,
            string value)
        {
            EnsureLoaded();
            relation = CanonicalTerm(relation);
            value = OntologyCanonicalId.NormalizeInput(value);
            var definition = registry.Find(relation);
            if (definition == null ||
                definition.ValueKind == OntologyValueKind.UserText ||
                definition.ValueKind == OntologyValueKind.EntityRef ||
                definition.ValueKind == OntologyValueKind.Number)
            {
                return value;
            }

            return CanonicalTerm(value);
        }

        public static IReadOnlyList<string> GetRegisteredObjectCandidates(
            string relation)
        {
            EnsureLoaded();
            var relationDefinition = registry.Find(CanonicalTerm(relation));
            if (relationDefinition == null)
                return Array.Empty<string>();
            // Generic Value relations are intentionally open-ended. Their choices
            // must come from relation-specific world/profile data rather than the
            // entire global value vocabulary.
            if (relationDefinition.ValueKind == OntologyValueKind.Value)
                return Array.Empty<string>();
            var expectedKind = ExpectedTermKind(relationDefinition.ValueKind);
            if (expectedKind == OntologyTermKind.Entity ||
                expectedKind == OntologyTermKind.Unknown)
                return Array.Empty<string>();

            return registry.Terms
                .Where(value =>
                    value != null &&
                    !value.Deprecated &&
                    value.Kind == expectedKind)
                .Select(value => value.CanonicalId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => Term(value))
                .ToArray();
        }

        public static string MigrateDefinitionId(string value)
        {
            EnsureLoaded();
            value = OntologyCanonicalId.NormalizeInput(value);
            return definitionMigrations.TryGetValue(value, out var migrated)
                ? migrated
                : value;
        }

        public static void MigrateSaveData(OntologySaveData saveData)
        {
            if (saveData == null) return;
            EnsureLoaded();
            foreach (var fact in saveData.facts ?? new List<OntologyFactRecord>())
                MigrateFact(fact);
            foreach (var placed in saveData.placedObjects ??
                                   new List<OntologyPlacedObjectRecord>())
            {
                if (placed == null) continue;
                placed.definitionId = MigrateDefinitionId(placed.definitionId);
                if (placed.concepts != null)
                {
                    for (var index = 0; index < placed.concepts.Count; index++)
                        placed.concepts[index] = CanonicalTerm(placed.concepts[index]);
                }
                foreach (var fact in placed.facts ?? new List<OntologyFactRecord>())
                    MigrateFact(fact);
                foreach (var ruleBlock in placed.ruleBlocks ??
                                         new List<OntologyRuleBlockRecord>())
                {
                    if (ruleBlock == null) continue;
                    ruleBlock.ruleId = MigrateDefinitionId(ruleBlock.ruleId);
                }
                foreach (var contribution in placed.semanticContributions ??
                                             new List<OntologySemanticContributionRecord>())
                {
                    if (contribution == null) continue;
                    if (contribution.concepts != null)
                    {
                        for (var index = 0; index < contribution.concepts.Count; index++)
                            contribution.concepts[index] =
                                CanonicalTerm(contribution.concepts[index]);
                    }
                    foreach (var fact in contribution.facts ?? new List<OntologyFactRecord>())
                        MigrateFact(fact);
                    foreach (var ruleBlock in contribution.ruleBlocks ??
                                             new List<OntologyRuleBlockRecord>())
                    {
                        if (ruleBlock == null) continue;
                        ruleBlock.ruleId = MigrateDefinitionId(ruleBlock.ruleId);
                    }
                }
            }
            if (saveData.controlledRuleIds != null)
            {
                for (var index = 0; index < saveData.controlledRuleIds.Count; index++)
                    saveData.controlledRuleIds[index] =
                        MigrateDefinitionId(saveData.controlledRuleIds[index]);
            }
            saveData.version = Math.Max(saveData.version, 4);
        }

        public static string FormatFact(
            OntologyFact fact,
            Func<string, string> displayEntity)
        {
            var subject = displayEntity?.Invoke(fact.Subject.Value) ??
                          Term(fact.Subject.Value);
            var obj = displayEntity?.Invoke(fact.Object.Value);
            if (string.IsNullOrWhiteSpace(obj) ||
                string.Equals(obj, fact.Object.Value, StringComparison.Ordinal))
            {
                obj = Term(fact.Object.Value);
            }
            var predicate = CanonicalTerm(fact.Predicate.Value);
            var templateKey = "fact." + predicate;
            var fallback = "{0}: " + Term(predicate) + " → {1}";
            var template = Text(templateKey, fallback);
            try
            {
                return string.Format(template, subject, obj);
            }
            catch (FormatException)
            {
                return subject + ": " + Term(predicate) + " → " + obj;
            }
        }

        public static void EnsureKoreanFontFallback(Transform root)
        {
            if (root == null) return;
            var texts = root.GetComponentsInChildren<TMP_Text>(true);
            var korean = texts
                .Select(value => value.font)
                .FirstOrDefault(value =>
                    value != null &&
                    value.name.IndexOf("Korean", StringComparison.OrdinalIgnoreCase) >= 0);
            if (korean == null) return;
            foreach (var font in texts.Select(value => value.font)
                         .Where(value => value != null && value != korean)
                         .Distinct())
            {
                font.fallbackFontAssetTable ??= new List<TMP_FontAsset>();
                if (!font.fallbackFontAssetTable.Contains(korean))
                    font.fallbackFontAssetTable.Add(korean);
            }
        }

        private static void EnsureLoaded()
        {
            if (loaded) return;
            loaded = true;
            localizedText.Clear();
            validationMessages.Clear();
            termMigrations.Clear();
            definitionMigrations.Clear();
            registry.Clear();
            currentLanguage = (OntologyDisplayLanguage)Mathf.Clamp(
                PlayerPrefs.GetInt(
                    LanguagePreferenceKey,
                    (int)OntologyDisplayLanguage.Korean),
                0,
                1);

            LoadLocalization("en");
            LoadLocalization("ko");
            LoadTerms();
            LoadLocalizedLabelAliases();
            LoadAliases();
            LoadMigrations();
            validationMessages.AddRange(registry.ValidationMessages);
            ValidateTranslations();
        }

        private static void LoadLocalization(string locale)
        {
            var rows = ReadCsv(ResourceRoot + "Localization_" + locale);
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows)
            {
                var key = Get(row, "key");
                var text = Get(row, "text");
                if (string.IsNullOrWhiteSpace(key)) continue;
                if (values.ContainsKey(key))
                    validationMessages.Add($"Duplicate localization key '{key}' ({locale}).");
                else
                    values.Add(key, text);
            }
            localizedText[locale] = values;
        }

        private static void LoadTerms()
        {
            foreach (var row in ReadCsv(ResourceRoot + "OntologyTerms"))
            {
                Enum.TryParse(Get(row, "type"), true, out OntologyTermKind kind);
                Enum.TryParse(
                    Get(row, "value_type"),
                    true,
                    out OntologyValueKind valueKind);
                registry.AddTerm(new OntologyTermDefinition
                {
                    CanonicalId = Get(row, "canonical_id"),
                    Kind = kind,
                    LabelKey = Get(row, "label_key"),
                    ValueKind = valueKind,
                    Deprecated = ParseBool(Get(row, "deprecated")),
                    Replacement = Get(row, "replacement")
                });
            }
        }

        private static void LoadAliases()
        {
            foreach (var row in ReadCsv(ResourceRoot + "OntologyAliases"))
            {
                registry.AddAlias(new OntologyAliasDefinition
                {
                    Locale = Get(row, "locale"),
                    Alias = Get(row, "alias"),
                    CanonicalId = Get(row, "canonical_id")
                });
            }
        }

        private static void LoadLocalizedLabelAliases()
        {
            foreach (var term in registry.Terms.Where(value =>
                         value != null &&
                         value.Kind != OntologyTermKind.Relation &&
                         !string.IsNullOrWhiteSpace(value.LabelKey)))
            {
                foreach (var locale in new[] { "en", "ko" })
                {
                    if (!TryLocalized(locale, term.LabelKey, out var alias))
                        continue;
                    registry.AddAlias(new OntologyAliasDefinition
                    {
                        Locale = locale,
                        Alias = alias,
                        CanonicalId = term.CanonicalId
                    });
                }
            }
        }

        private static void LoadMigrations()
        {
            foreach (var row in ReadCsv(ResourceRoot + "OntologyMigrations"))
            {
                var oldId = Get(row, "old_id");
                var newId = Get(row, "new_id");
                var scope = Get(row, "scope");
                if (string.IsNullOrWhiteSpace(oldId) ||
                    string.IsNullOrWhiteSpace(newId))
                    continue;
                if (string.Equals(scope, "Definition", StringComparison.OrdinalIgnoreCase))
                    definitionMigrations[oldId] = newId;
                else
                    termMigrations[oldId] = newId;
            }
        }

        private static void ValidateTranslations()
        {
            foreach (var term in registry.Terms)
            {
                if (string.IsNullOrWhiteSpace(term.LabelKey)) continue;
                foreach (var locale in new[] { "en", "ko" })
                {
                    if (!TryLocalized(locale, term.LabelKey, out _))
                    {
                        validationMessages.Add(
                            $"Missing {locale} translation for '{term.LabelKey}'.");
                    }
                }
            }
        }

        private static void MigrateFact(OntologyFactRecord fact)
        {
            if (fact == null) return;
            fact.predicate = CanonicalTerm(fact.predicate);
            fact.obj = CanonicalObjectForRelation(
                fact.predicate,
                fact.obj);
        }

        private static bool TryLocalized(string locale, string key, out string value)
        {
            value = null;
            return localizedText.TryGetValue(locale, out var entries) &&
                   entries.TryGetValue(key, out value) &&
                   !string.IsNullOrWhiteSpace(value);
        }

        private static List<Dictionary<string, string>> ReadCsv(string resourcePath)
        {
            var asset = Resources.Load<TextAsset>(resourcePath);
            if (asset == null)
            {
                validationMessages.Add($"Missing language-pack CSV: {resourcePath}.csv");
                return new List<Dictionary<string, string>>();
            }

            var rows = ParseCsv(asset.text);
            if (rows.Count == 0)
                return new List<Dictionary<string, string>>();
            var headers = rows[0]
                .Select(value => value.Trim().ToLowerInvariant())
                .ToArray();
            var result = new List<Dictionary<string, string>>();
            for (var rowIndex = 1; rowIndex < rows.Count; rowIndex++)
            {
                if (rows[rowIndex].All(string.IsNullOrWhiteSpace)) continue;
                var mapped = new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase);
                for (var index = 0; index < headers.Length; index++)
                {
                    mapped[headers[index]] =
                        index < rows[rowIndex].Count
                            ? rows[rowIndex][index].Trim()
                            : string.Empty;
                }
                result.Add(mapped);
            }
            return result;
        }

        public static List<List<string>> ParseCsv(string text)
        {
            var rows = new List<List<string>>();
            var row = new List<string>();
            var field = new StringBuilder();
            var quoted = false;
            text ??= string.Empty;
            for (var index = 0; index < text.Length; index++)
            {
                var character = text[index];
                if (character == '"')
                {
                    if (quoted && index + 1 < text.Length && text[index + 1] == '"')
                    {
                        field.Append('"');
                        index++;
                    }
                    else quoted = !quoted;
                }
                else if (character == ',' && !quoted)
                {
                    row.Add(field.ToString());
                    field.Clear();
                }
                else if ((character == '\n' || character == '\r') && !quoted)
                {
                    if (character == '\r' &&
                        index + 1 < text.Length &&
                        text[index + 1] == '\n')
                        index++;
                    row.Add(field.ToString());
                    field.Clear();
                    rows.Add(row);
                    row = new List<string>();
                }
                else field.Append(character);
            }
            if (field.Length > 0 || row.Count > 0)
            {
                row.Add(field.ToString());
                rows.Add(row);
            }
            return rows;
        }

        private static string Get(
            IReadOnlyDictionary<string, string> row,
            string key)
        {
            return row != null && row.TryGetValue(key, out var value)
                ? value
                : string.Empty;
        }

        private static bool ParseBool(string value)
        {
            return bool.TryParse(value, out var result) && result;
        }

        private static OntologyTermKind ExpectedTermKind(OntologyValueKind kind)
        {
            return kind switch
            {
                OntologyValueKind.ConceptRef => OntologyTermKind.Concept,
                OntologyValueKind.EntityRef => OntologyTermKind.Entity,
                OntologyValueKind.State => OntologyTermKind.State,
                OntologyValueKind.Value => OntologyTermKind.Value,
                _ => OntologyTermKind.Unknown
            };
        }

        private static OntologyInputResolution Success(string value)
        {
            return new OntologyInputResolution
            {
                Success = true,
                CanonicalValue = value,
                Message = string.Empty
            };
        }

        private static OntologyInputResolution Failure(string message)
        {
            return new OntologyInputResolution
            {
                Success = false,
                Message = message ?? string.Empty
            };
        }

        private static bool IsTrue(string value)
        {
            return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase) ||
                   value == "예" || value == "참";
        }

        private static bool IsFalse(string value)
        {
            return string.Equals(value, "false", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "no", StringComparison.OrdinalIgnoreCase) ||
                   value == "아니오" || value == "거짓";
        }

        private static string Nicify(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return value;
            var builder = new StringBuilder();
            for (var index = 0; index < value.Length; index++)
            {
                var character = value[index];
                if (character == '_')
                {
                    builder.Append(' ');
                    continue;
                }
                if (index > 0 &&
                    char.IsUpper(character) &&
                    char.IsLower(value[index - 1]))
                {
                    builder.Append(' ');
                }
                builder.Append(character);
            }
            return builder.ToString();
        }
    }
}
