using System.Collections.Generic;

namespace Tormia.Ontology.Core
{
    public sealed class OntologyRuleEngine
    {
        private readonly List<OntologyRule> rules = new();

        public void AddRule(OntologyRule rule)
        {
            if (rule != null)
            {
                rules.Add(rule);
            }
        }

        public List<OntologyEvent> Evaluate(OntologyWorldState world)
        {
            return EvaluateStep(world).Events;
        }

        public OntologyRuleEvaluationStep EvaluateStep(OntologyWorldState world)
        {
            var events = new List<OntologyEvent>();
            var addedFactCount = 0;
            var changedFactCount = 0;
            if (world == null)
            {
                return new OntologyRuleEvaluationStep(events, addedFactCount, changedFactCount);
            }

            foreach (var rule in rules)
            {
                var ruleEvents = rule.Evaluate(world);
                foreach (var ontologyEvent in ruleEvents)
                {
                    events.Add(ontologyEvent);
                    foreach (var fact in ontologyEvent.AddedFacts)
                    {
                        if (world.AddFact(fact.Subject, fact.Predicate, fact.Object))
                        {
                            addedFactCount++;
                            changedFactCount++;
                        }
                    }

                    foreach (var fact in ontologyEvent.RemovedFacts)
                    {
                        if (world.RemoveFact(fact.Subject, fact.Predicate, fact.Object))
                        {
                            changedFactCount++;
                        }
                    }

                    foreach (var fact in ontologyEvent.SetFacts)
                    {
                        var removed = world.RemoveFacts(fact.Subject, fact.Predicate);
                        changedFactCount += removed;
                        if (world.AddFact(fact.Subject, fact.Predicate, fact.Object))
                        {
                            addedFactCount++;
                            changedFactCount++;
                        }
                    }

                    foreach (var fact in ontologyEvent.AdjustedNumberFacts)
                    {
                        if (world.AdjustNumberFact(fact.Subject, fact.Predicate, fact.Object))
                        {
                            changedFactCount++;
                        }
                    }
                }
            }

            return new OntologyRuleEvaluationStep(events, addedFactCount, changedFactCount);
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
