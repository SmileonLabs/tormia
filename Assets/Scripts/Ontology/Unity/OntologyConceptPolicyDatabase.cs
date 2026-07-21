using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    [Serializable]
    public sealed class OntologyConceptDefinition
    {
        public string id;
        [TextArea] public string description;
        public bool selectable = true;
    }

    [Serializable]
    public sealed class OntologyConceptPolicyRelation
    {
        public string subject;
        public string predicate;
        public string obj;
        [TextArea] public string message;
    }

    public sealed class OntologyConceptValidationResult
    {
        public static readonly OntologyConceptValidationResult Valid = new(false, false, string.Empty);

        public OntologyConceptValidationResult(bool hasWarning, bool blocksSave, string message)
        {
            HasWarning = hasWarning;
            BlocksSave = blocksSave;
            Message = message ?? string.Empty;
        }

        public bool HasWarning { get; }
        public bool BlocksSave { get; }
        public string Message { get; }
    }

    /// <summary>
    /// Data-authored vocabulary and relationships used to validate Concept assignments.
    /// The validator interprets ontology relations; it does not inspect prefab names or visuals.
    /// </summary>
    [CreateAssetMenu(
        fileName = "ConceptPolicyDatabase",
        menuName = "Tormia/Ontology/Concept Policy Database")]
    public sealed class OntologyConceptPolicyDatabase : ScriptableObject
    {
        public const string IncompatibleWith = "incompatible_with";
        public const string RequiresConcept = "requires_concept";
        public const string ApplicableTo = "applicable_to";

        [SerializeField] private bool strictMode;
        [SerializeField] private List<OntologyConceptDefinition> concepts = new();
        [SerializeField] private List<OntologyConceptPolicyRelation> relations = new();

        public bool StrictMode => strictMode;
        public IReadOnlyList<OntologyConceptDefinition> Concepts => concepts;
        public IReadOnlyList<OntologyConceptPolicyRelation> Relations => relations;

        public OntologyConceptValidationResult ValidateAddition(
            IEnumerable<string> existingConcepts,
            string candidate,
            string ignoredExistingConcept = null)
        {
            candidate = candidate?.Trim();
            if (string.IsNullOrWhiteSpace(candidate))
                return new OntologyConceptValidationResult(
                    true,
                    true,
                    L(
                        "concept_validation.empty",
                        "Concept cannot be empty."));

            var existing = new HashSet<string>(
                (existingConcepts ?? Array.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim()),
                StringComparer.Ordinal);

            if (!string.IsNullOrWhiteSpace(ignoredExistingConcept))
                existing.Remove(ignoredExistingConcept.Trim());

            var definition = concepts.FirstOrDefault(value =>
                value != null && string.Equals(value.id?.Trim(), candidate, StringComparison.Ordinal));
            if (definition != null && !definition.selectable)
                return Warning(string.Format(
                    L(
                        "concept_validation.not_selectable",
                        "{0} is not selectable."),
                    OntologyLanguagePackService.Term(candidate)));

            foreach (var relation in ValidRelations(IncompatibleWith))
            {
                var conflicts =
                    Matches(relation.subject, candidate) && existing.Contains(relation.obj.Trim()) ||
                    Matches(relation.obj, candidate) && existing.Contains(relation.subject.Trim());
                if (!conflicts) continue;

                var other = Matches(relation.subject, candidate)
                    ? relation.obj.Trim()
                    : relation.subject.Trim();
                return Warning(ResolveMessage(
                    relation,
                    "concept_validation.conflict",
                    "{0} conflicts with the existing Concept {1}.",
                    candidate,
                    other));
            }

            foreach (var relation in ValidRelations(RequiresConcept)
                         .Where(value => Matches(value.subject, candidate)))
            {
                var required = relation.obj.Trim();
                if (existing.Contains(required)) continue;
                return Warning(ResolveMessage(
                    relation,
                    "concept_validation.requires",
                    "{0} requires the Concept {1}.",
                    candidate,
                    required));
            }

            var applicable = ValidRelations(ApplicableTo)
                .Where(value => Matches(value.subject, candidate))
                .ToList();
            if (applicable.Count > 0 &&
                !applicable.Any(value => existing.Contains(value.obj.Trim())))
            {
                var allowed = string.Join(
                    ", ",
                    applicable
                        .Select(value =>
                            OntologyLanguagePackService.Term(value.obj.Trim()))
                        .Distinct());
                return Warning(string.Format(
                    L(
                        "concept_validation.applicable",
                        "{0} is intended for objects with: {1}."),
                    OntologyLanguagePackService.Term(candidate),
                    allowed));
            }

            return OntologyConceptValidationResult.Valid;
        }

        private IEnumerable<OntologyConceptPolicyRelation> ValidRelations(string predicate)
        {
            return relations.Where(value =>
                value != null &&
                Matches(value.predicate, predicate) &&
                !string.IsNullOrWhiteSpace(value.subject) &&
                !string.IsNullOrWhiteSpace(value.obj));
        }

        private OntologyConceptValidationResult Warning(string message)
        {
            return new OntologyConceptValidationResult(true, strictMode, message);
        }

        private static string ResolveMessage(
            OntologyConceptPolicyRelation relation,
            string key,
            string fallback,
            string first,
            string second)
        {
            if (OntologyLanguagePackService.CurrentLanguage ==
                    OntologyDisplayLanguage.English &&
                !string.IsNullOrWhiteSpace(relation.message))
                return relation.message.Trim();
            return string.Format(
                L(key, fallback),
                OntologyLanguagePackService.Term(first),
                OntologyLanguagePackService.Term(second));
        }

        private static string L(string key, string fallback) =>
            OntologyLanguagePackService.Text(key, fallback);

        private static bool Matches(string left, string right)
        {
            return string.Equals(left?.Trim(), right?.Trim(), StringComparison.Ordinal);
        }
    }
}
