using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Tormia.Ontology.Core
{
    public enum OntologyTermKind
    {
        Unknown,
        Relation,
        Concept,
        State,
        Entity,
        Value
    }

    public enum OntologyValueKind
    {
        Unknown,
        ConceptRef,
        EntityRef,
        State,
        Number,
        Boolean,
        UserText,
        Value
    }

    public sealed class OntologyTermDefinition
    {
        public string CanonicalId { get; set; }
        public OntologyTermKind Kind { get; set; }
        public string LabelKey { get; set; }
        public OntologyValueKind ValueKind { get; set; }
        public bool Deprecated { get; set; }
        public string Replacement { get; set; }
    }

    public sealed class OntologyAliasDefinition
    {
        public string Locale { get; set; }
        public string Alias { get; set; }
        public string CanonicalId { get; set; }
    }

    /// <summary>
    /// Language-neutral registry for canonical ontology identifiers.
    /// Comparisons are case-insensitive, but every successful lookup returns the
    /// exact authored canonical spelling.
    /// </summary>
    public sealed class OntologyTermRegistry
    {
        private readonly Dictionary<string, OntologyTermDefinition> terms =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Dictionary<string, HashSet<string>>> aliases =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> validationMessages = new();

        public IReadOnlyCollection<OntologyTermDefinition> Terms => terms.Values;
        public IReadOnlyList<string> ValidationMessages => validationMessages;

        public void Clear()
        {
            terms.Clear();
            aliases.Clear();
            validationMessages.Clear();
        }

        public void AddTerm(OntologyTermDefinition definition)
        {
            if (definition == null || string.IsNullOrWhiteSpace(definition.CanonicalId))
            {
                validationMessages.Add("A term has an empty canonical id.");
                return;
            }

            definition.CanonicalId = OntologyCanonicalId.NormalizeInput(definition.CanonicalId);
            definition.LabelKey = definition.LabelKey?.Trim();
            definition.Replacement = definition.Replacement?.Trim();
            if (terms.TryGetValue(definition.CanonicalId, out var existing))
            {
                if (!string.Equals(
                        existing.CanonicalId,
                        definition.CanonicalId,
                        StringComparison.Ordinal))
                {
                    validationMessages.Add(
                        $"Canonical ids '{existing.CanonicalId}' and " +
                        $"'{definition.CanonicalId}' differ only by letter case.");
                }
                else
                {
                    validationMessages.Add(
                        $"Canonical id '{definition.CanonicalId}' is duplicated.");
                }
                return;
            }

            terms.Add(definition.CanonicalId, definition);
        }

        public void AddAlias(OntologyAliasDefinition definition)
        {
            if (definition == null ||
                string.IsNullOrWhiteSpace(definition.Locale) ||
                string.IsNullOrWhiteSpace(definition.Alias) ||
                string.IsNullOrWhiteSpace(definition.CanonicalId))
            {
                validationMessages.Add("An ontology alias has an empty field.");
                return;
            }

            var locale = definition.Locale.Trim().ToLowerInvariant();
            var alias = OntologyCanonicalId.NormalizeInput(definition.Alias);
            if (!terms.TryGetValue(definition.CanonicalId.Trim(), out var term))
            {
                validationMessages.Add(
                    $"Alias '{definition.Alias}' references unknown canonical id " +
                    $"'{definition.CanonicalId}'.");
                return;
            }

            if (!aliases.TryGetValue(locale, out var localeAliases))
            {
                localeAliases = new Dictionary<string, HashSet<string>>(
                    StringComparer.OrdinalIgnoreCase);
                aliases.Add(locale, localeAliases);
            }

            if (!localeAliases.TryGetValue(alias, out var matches))
            {
                matches = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                localeAliases.Add(alias, matches);
            }

            matches.Add(term.CanonicalId);
            var ambiguousWithinKind = matches
                .Select(value => terms.TryGetValue(value, out var candidate)
                    ? candidate
                    : null)
                .Where(value => value != null)
                .GroupBy(value => value.Kind)
                .Any(group => group.Count() > 1);
            if (ambiguousWithinKind)
            {
                validationMessages.Add(
                    $"Alias '{definition.Alias}' ({locale}) is ambiguous: " +
                    string.Join(", ", matches.OrderBy(value => value)));
            }
        }

        public OntologyTermDefinition Find(string canonicalId)
        {
            if (string.IsNullOrWhiteSpace(canonicalId))
                return null;
            terms.TryGetValue(
                OntologyCanonicalId.NormalizeInput(canonicalId),
                out var definition);
            return definition;
        }

        public bool TryResolve(
            string input,
            string locale,
            OntologyTermKind expectedKind,
            out string canonicalId,
            out IReadOnlyList<string> candidates)
        {
            canonicalId = null;
            candidates = Array.Empty<string>();
            input = OntologyCanonicalId.NormalizeInput(input);
            if (string.IsNullOrWhiteSpace(input))
                return false;

            if (terms.TryGetValue(input, out var direct) &&
                MatchesKind(direct, expectedKind))
            {
                canonicalId = ResolveReplacement(direct);
                return true;
            }

            locale = string.IsNullOrWhiteSpace(locale)
                ? "en"
                : locale.Trim().ToLowerInvariant();
            if (!aliases.TryGetValue(locale, out var localeAliases) ||
                !localeAliases.TryGetValue(input, out var aliasMatches))
            {
                return false;
            }

            var filtered = aliasMatches
                .Select(Find)
                .Where(value => value != null && MatchesKind(value, expectedKind))
                .Select(ResolveReplacement)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value)
                .ToArray();
            candidates = filtered;
            if (filtered.Length != 1)
                return false;

            canonicalId = filtered[0];
            return true;
        }

        private string ResolveReplacement(OntologyTermDefinition definition)
        {
            if (definition == null)
                return string.Empty;
            if (!definition.Deprecated ||
                string.IsNullOrWhiteSpace(definition.Replacement))
            {
                return definition.CanonicalId;
            }

            return terms.TryGetValue(definition.Replacement, out var replacement)
                ? replacement.CanonicalId
                : definition.Replacement.Trim();
        }

        private static bool MatchesKind(
            OntologyTermDefinition definition,
            OntologyTermKind expectedKind)
        {
            return expectedKind == OntologyTermKind.Unknown ||
                   definition.Kind == expectedKind;
        }
    }

    public static class OntologyCanonicalId
    {
        public static string NormalizeInput(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Normalize(NormalizationForm.FormKC).Trim();
        }

        public static bool ContainsNonAscii(string value)
        {
            value = NormalizeInput(value);
            return value.Any(character => character > 127);
        }

        public static string SuggestConceptOrValue(string value)
        {
            value = NormalizeInput(value);
            if (string.IsNullOrEmpty(value) || ContainsNonAscii(value))
                return string.Empty;

            var words = SplitIdentifier(value);
            var builder = new StringBuilder();
            foreach (var word in words)
            {
                if (word.Length == 0) continue;
                builder.Append(char.ToUpperInvariant(word[0]));
                if (word.Length > 1) builder.Append(word.Substring(1));
            }
            return builder.ToString();
        }

        public static string SuggestRelation(string value)
        {
            value = NormalizeInput(value);
            if (string.IsNullOrEmpty(value) || ContainsNonAscii(value))
                return string.Empty;

            var builder = new StringBuilder();
            for (var index = 0; index < value.Length; index++)
            {
                var character = value[index];
                if (char.IsWhiteSpace(character) || character == '-')
                {
                    if (builder.Length > 0 && builder[builder.Length - 1] != '_')
                        builder.Append('_');
                    continue;
                }
                if (char.IsUpper(character) &&
                    index > 0 &&
                    builder.Length > 0 &&
                    builder[builder.Length - 1] != '_')
                {
                    builder.Append('_');
                }
                builder.Append(char.ToLower(character, CultureInfo.InvariantCulture));
            }
            return builder.ToString().Trim('_');
        }

        private static IEnumerable<string> SplitIdentifier(string value)
        {
            return value
                .Replace('-', ' ')
                .Replace('_', ' ')
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        }
    }
}
