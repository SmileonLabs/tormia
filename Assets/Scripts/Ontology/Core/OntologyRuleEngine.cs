using System.Collections.Generic;

namespace Tormia.Ontology.Core
{
    public sealed class OntologyRuleEngine
    {
        private readonly List<RuleEntry> rules = new();

        public void AddRule(OntologyRule rule)
        {
            if (rule != null)
            {
                rules.Add(new RuleEntry(rule, null));
            }
        }

        public void AddRule(OntologyRule rule, OntologyRuleDefinition definition)
        {
            if (rule != null)
            {
                rules.Add(new RuleEntry(rule, GetDependencies(definition)));
            }
        }

        public List<OntologyEvent> Evaluate(OntologyWorldState world)
        {
            return EvaluateStep(world).Events;
        }

        public OntologyRuleEvaluationStep EvaluateStep(OntologyWorldState world)
        {
            return EvaluateStep(world, null, null);
        }

        public OntologyRuleEvaluationStep EvaluateStep(OntologyWorldState world, OntologyWorldChangeSet changes)
        {
            return EvaluateStep(world, changes, null);
        }

        /// <summary>
        /// Evaluates one inference step and optionally records facts newly inferred by rules.
        /// The caller can use this record to retract only derived relations before a later recomputation.
        /// </summary>
        public OntologyRuleEvaluationStep EvaluateStep(
            OntologyWorldState world,
            OntologyWorldChangeSet changes,
            ISet<OntologyFact> inferredFacts)
        {
            var events = new List<OntologyEvent>();
            var addedFactCount = 0;
            var changedFactCount = 0;
            if (world == null)
            {
                return new OntologyRuleEvaluationStep(events, addedFactCount, changedFactCount);
            }

            foreach (var entry in rules)
            {
                if (!entry.ShouldEvaluate(changes))
                {
                    continue;
                }

                var ruleEvents = entry.Rule.Evaluate(world);
                foreach (var ontologyEvent in ruleEvents)
                {
                    var eventChanged = false;
                    foreach (var fact in ontologyEvent.AddedFacts)
                    {
                        if (world.AddFact(fact.Subject, fact.Predicate, fact.Object))
                        {
                            inferredFacts?.Add(fact);
                            addedFactCount++;
                            changedFactCount++;
                            eventChanged = true;
                        }
                    }

                    foreach (var fact in ontologyEvent.RemovedFacts)
                    {
                        if (world.RemoveFact(fact.Subject, fact.Predicate, fact.Object))
                        {
                            changedFactCount++;
                            eventChanged = true;
                        }
                    }

                    foreach (var fact in ontologyEvent.SetFacts)
                    {
                        // SetFact is an explicit persistent state transition. Unlike AddedFacts,
                        // it must not be registered in inferredFacts or retracted on recomputation.
                        if (world.SetFact(fact.Subject, fact.Predicate, fact.Object, out var added))
                        {
                            changedFactCount++;
                            eventChanged = true;
                            if (added)
                            {
                                addedFactCount++;
                            }
                        }
                    }

                    foreach (var fact in ontologyEvent.AdjustedNumberFacts)
                    {
                        if (world.AdjustNumberFact(fact.Subject, fact.Predicate, fact.Object))
                        {
                            changedFactCount++;
                            eventChanged = true;
                        }
                    }

                    // Keep history as a state-change record, rather than a trace of every
                    // repeated rule check during a stable simulation.
                    if (eventChanged)
                    {
                        events.Add(ontologyEvent);
                    }
                }
            }

            return new OntologyRuleEvaluationStep(events, addedFactCount, changedFactCount);
        }

        private static HashSet<OntologyId> GetDependencies(OntologyRuleDefinition definition)
        {
            if (definition == null || definition.conditions == null || definition.conditions.Count == 0)
            {
                return null;
            }

            var dependencies = new HashSet<OntologyId>();
            foreach (var condition in definition.conditions)
            {
                if (condition == null)
                {
                    continue;
                }

                if (condition.kind == OntologyConditionKind.HasConcept || condition.kind == OntologyConditionKind.NotConcept)
                {
                    dependencies.Add(OntologyPredicates.HasConcept);
                }
                else if (!string.IsNullOrWhiteSpace(condition.predicate))
                {
                    dependencies.Add(condition.predicate);
                }
            }

            return dependencies.Count > 0 ? dependencies : null;
        }

        private sealed class RuleEntry
        {
            private readonly HashSet<OntologyId> dependencies;

            public RuleEntry(OntologyRule rule, HashSet<OntologyId> dependencies)
            {
                Rule = rule;
                this.dependencies = dependencies;
            }

            public OntologyRule Rule { get; }

            public bool ShouldEvaluate(OntologyWorldChangeSet changes)
            {
                if (dependencies == null || changes == null)
                {
                    return true;
                }

                if (changes.IsEmpty)
                {
                    return false;
                }

                foreach (var dependency in dependencies)
                {
                    if (changes.ContainsPredicate(dependency))
                    {
                        return true;
                    }
                }

                return false;
            }
        }
    }

    public readonly struct OntologyRuleEvaluationStep
    {
        public OntologyRuleEvaluationStep(List<OntologyEvent> events, int addedFactCount, int changedFactCount)
        {
            Events = events;
            AddedFactCount = addedFactCount;
            ChangedFactCount = changedFactCount;
        }

        public List<OntologyEvent> Events { get; }
        public int AddedFactCount { get; }
        public int ChangedFactCount { get; }
    }
}
