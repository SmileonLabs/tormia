using System;
using System.Collections.Generic;
using System.Linq;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Projects saved placed-object semantics into durable world facts.
    /// Stable entity IDs own ontology meaning; instance names remain editable
    /// presentation metadata and are used only for legacy-record migration.
    /// </summary>
    public static class OntologyPlacedObjectFactProjection
    {
        public static string ResolveSubject(OntologyPlacedObjectRecord record)
        {
            if (record == null)
            {
                return string.Empty;
            }

            return !string.IsNullOrWhiteSpace(record.entityId)
                ? record.entityId.Trim()
                : record.instanceName?.Trim() ?? string.Empty;
        }

        public static void Synchronize(
            IList<OntologyFactRecord> worldFacts,
            OntologyPlacedObjectRecord record)
        {
            if (worldFacts == null || record == null)
            {
                return;
            }

            var subject = ResolveSubject(record);
            if (string.IsNullOrWhiteSpace(subject))
            {
                return;
            }

            MigrateLegacySubject(worldFacts, record, subject);

            foreach (var concept in (record.concepts ?? new List<string>())
                         .Where(value => !string.IsNullOrWhiteSpace(value)))
            {
                AddIfMissing(
                    worldFacts,
                    subject,
                    OntologyPredicates.HasConcept,
                    concept);
            }

            foreach (var fact in (record.facts ?? new List<OntologyFactRecord>())
                         .Where(value => value != null &&
                                         !string.IsNullOrWhiteSpace(value.predicate) &&
                                         !string.IsNullOrWhiteSpace(value.obj)))
            {
                fact.subject = subject;
                AddIfMissing(worldFacts, subject, fact.predicate, fact.obj);
            }

            foreach (var ruleBlock in
                     (record.ruleBlocks ?? new List<OntologyRuleBlockRecord>())
                     .Where(value => value != null &&
                                     !string.IsNullOrWhiteSpace(value.ruleId)))
            {
                AddIfMissing(
                    worldFacts,
                    subject,
                    OntologyPredicates.HasRuleBlock,
                    ruleBlock.ruleId);
            }
        }

        public static bool RemoveRetiredTemplateData(
            List<OntologyFactRecord> worldFacts,
            OntologyPlacedObjectRecord record,
            OntologyMapObjectTemplate template)
        {
            if (record == null || template == null)
            {
                return false;
            }

            var retiredConcepts = new HashSet<string>(
                (template.retiredConcepts ?? Array.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value)),
                StringComparer.Ordinal);
            var retiredFacts = (template.retiredFacts ??
                                Array.Empty<OntologyFactEntry>())
                .Where(value => value != null &&
                                !string.IsNullOrWhiteSpace(value.predicate) &&
                                !string.IsNullOrWhiteSpace(value.obj))
                .ToArray();
            if (retiredConcepts.Count == 0 && retiredFacts.Length == 0)
            {
                return false;
            }

            record.concepts ??= new List<string>();
            record.facts ??= new List<OntologyFactRecord>();
            var changed = record.concepts.RemoveAll(retiredConcepts.Contains) > 0;
            changed |= record.facts.RemoveAll(value => value != null &&
                retiredFacts.Any(retired =>
                    retired.predicate == value.predicate &&
                    retired.obj == value.obj)) > 0;

            if (worldFacts == null)
            {
                return changed;
            }

            var stableSubject = ResolveSubject(record);
            var legacySubject = record.instanceName?.Trim() ?? string.Empty;
            changed |= worldFacts.RemoveAll(value => value != null &&
                IsRecordSubject(value.subject, stableSubject, legacySubject) &&
                ((value.predicate == OntologyPredicates.HasConcept &&
                  retiredConcepts.Contains(value.obj)) ||
                 retiredFacts.Any(retired =>
                     retired.predicate == value.predicate &&
                     retired.obj == value.obj))) > 0;
            return changed;
        }

        private static void MigrateLegacySubject(
            IEnumerable<OntologyFactRecord> worldFacts,
            OntologyPlacedObjectRecord record,
            string stableSubject)
        {
            var legacySubject = record.instanceName?.Trim();
            if (string.IsNullOrWhiteSpace(record.entityId) ||
                string.IsNullOrWhiteSpace(legacySubject) ||
                string.Equals(
                    stableSubject,
                    legacySubject,
                    StringComparison.Ordinal))
            {
                return;
            }

            foreach (var fact in worldFacts)
            {
                if (fact != null &&
                    string.Equals(
                        fact.subject,
                        legacySubject,
                        StringComparison.Ordinal))
                {
                    fact.subject = stableSubject;
                }
            }
        }

        private static bool IsRecordSubject(
            string candidate,
            string stableSubject,
            string legacySubject) =>
            (!string.IsNullOrWhiteSpace(stableSubject) &&
             string.Equals(candidate, stableSubject, StringComparison.Ordinal)) ||
            (!string.IsNullOrWhiteSpace(legacySubject) &&
             string.Equals(candidate, legacySubject, StringComparison.Ordinal));

        private static void AddIfMissing(
            ICollection<OntologyFactRecord> facts,
            string subject,
            string predicate,
            string obj)
        {
            if (facts.Any(value => value != null &&
                                   value.subject == subject &&
                                   value.predicate == predicate &&
                                   value.obj == obj))
            {
                return;
            }

            facts.Add(new OntologyFactRecord
            {
                subject = subject,
                predicate = predicate,
                obj = obj
            });
        }
    }
}
