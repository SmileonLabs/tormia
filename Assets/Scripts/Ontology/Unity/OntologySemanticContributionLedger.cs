using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Records which semantic values were introduced by a user-selected preset.
    /// This lets removal undo only preset-authored values, while preserving values
    /// that were already part of the object template or were authored independently.
    /// </summary>
    [Serializable]
    public sealed class OntologySemanticContribution
    {
        public string ownerId;
        public List<string> concepts = new();
        public List<OntologyFactEntry> facts = new();
        public List<OntologyRuleBlockBinding> ruleBlocks = new();
    }

    public sealed class OntologySemanticContributionLedger : MonoBehaviour
    {
        [SerializeField] private List<OntologySemanticContribution> contributions = new();

        public IReadOnlyList<OntologySemanticContribution> Contributions => contributions;

        public void AddContribution(
            string ownerId,
            IEnumerable<string> concepts,
            IEnumerable<OntologyFactEntry> facts,
            IEnumerable<OntologyRuleBlockBinding> ruleBlocks)
        {
            if (string.IsNullOrWhiteSpace(ownerId)) return;
            var contribution = contributions.FirstOrDefault(value =>
                value != null && value.ownerId == ownerId);
            if (contribution == null)
            {
                contribution = new OntologySemanticContribution { ownerId = ownerId };
                contributions.Add(contribution);
            }

            AddUnique(contribution.concepts, concepts);
            AddUniqueFacts(contribution.facts, facts);
            AddUniqueBindings(contribution.ruleBlocks, ruleBlocks);
        }

        public IReadOnlyList<OntologySemanticContribution> RemoveRuleBinding(
            string ruleId,
            string bindingVariable)
        {
            var completed = new List<OntologySemanticContribution>();
            foreach (var contribution in contributions
                         .Where(value => value != null)
                         .ToArray())
            {
                var removedCount = contribution.ruleBlocks.RemoveAll(binding =>
                    binding != null && binding.ruleId == ruleId &&
                    binding.bindingVariable == bindingVariable);
                if (removedCount == 0 || contribution.ruleBlocks.Count > 0)
                    continue;

                contributions.Remove(contribution);
                completed.Add(contribution);
            }
            return completed;
        }

        public bool IsConceptClaimed(string concept)
        {
            return contributions.Any(value => value != null &&
                value.concepts.Any(candidate => candidate == concept));
        }

        public bool IsFactClaimed(string predicate, string obj)
        {
            return contributions.Any(value => value != null &&
                value.facts.Any(candidate => candidate != null &&
                    candidate.predicate == predicate && candidate.obj == obj));
        }

        public bool IsRuleBlockClaimed(string ruleId, string bindingVariable)
        {
            return contributions.Any(value => value != null &&
                value.ruleBlocks.Any(candidate => candidate != null &&
                    candidate.ruleId == ruleId &&
                    candidate.bindingVariable == bindingVariable));
        }

        public void ReplaceFromRecords(IEnumerable<OntologySemanticContributionRecord> records)
        {
            contributions = records == null
                ? new List<OntologySemanticContribution>()
                : records
                    .Where(value => value != null && !string.IsNullOrWhiteSpace(value.ownerId))
                    .Select(value => new OntologySemanticContribution
                    {
                        ownerId = value.ownerId,
                        concepts = value.concepts?
                            .Where(item => !string.IsNullOrWhiteSpace(item))
                            .Distinct()
                            .ToList() ?? new List<string>(),
                        facts = value.facts?
                            .Where(item => item != null &&
                                           !string.IsNullOrWhiteSpace(item.predicate) &&
                                           !string.IsNullOrWhiteSpace(item.obj))
                            .Select(item => new OntologyFactEntry
                            {
                                predicate = item.predicate,
                                obj = item.obj
                            })
                            .ToList() ?? new List<OntologyFactEntry>(),
                        ruleBlocks = value.ruleBlocks?
                            .Where(item => item != null &&
                                           !string.IsNullOrWhiteSpace(item.ruleId) &&
                                           !string.IsNullOrWhiteSpace(item.bindingVariable))
                            .Select(item => new OntologyRuleBlockBinding
                            {
                                ruleId = item.ruleId,
                                bindingVariable = item.bindingVariable
                            })
                            .ToList() ?? new List<OntologyRuleBlockBinding>()
                    })
                    .ToList();
        }

        private static void AddUnique(List<string> destination, IEnumerable<string> values)
        {
            if (destination == null || values == null) return;
            foreach (var value in values.Where(value => !string.IsNullOrWhiteSpace(value)))
                if (!destination.Contains(value)) destination.Add(value);
        }

        private static void AddUniqueFacts(
            List<OntologyFactEntry> destination,
            IEnumerable<OntologyFactEntry> values)
        {
            if (destination == null || values == null) return;
            foreach (var value in values.Where(value => value != null &&
                                                        !string.IsNullOrWhiteSpace(value.predicate) &&
                                                        !string.IsNullOrWhiteSpace(value.obj)))
            {
                if (destination.Any(item => item != null &&
                                            item.predicate == value.predicate &&
                                            item.obj == value.obj)) continue;
                destination.Add(new OntologyFactEntry
                {
                    predicate = value.predicate,
                    obj = value.obj
                });
            }
        }

        private static void AddUniqueBindings(
            List<OntologyRuleBlockBinding> destination,
            IEnumerable<OntologyRuleBlockBinding> values)
        {
            if (destination == null || values == null) return;
            foreach (var value in values.Where(value => value != null &&
                                                        !string.IsNullOrWhiteSpace(value.ruleId) &&
                                                        !string.IsNullOrWhiteSpace(value.bindingVariable)))
            {
                if (destination.Any(item => item != null &&
                                            item.ruleId == value.ruleId &&
                                            item.bindingVariable == value.bindingVariable)) continue;
                destination.Add(new OntologyRuleBlockBinding
                {
                    ruleId = value.ruleId,
                    bindingVariable = value.bindingVariable
                });
            }
        }
    }
}
