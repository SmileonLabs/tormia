using System.Collections.Generic;

namespace Tormia.Ontology.Core
{
    public static class OntologyRuleCompiler
    {
        public static OntologyRule Compile(OntologyRuleDefinition definition)
        {
            return new OntologyRule(
                definition.id,
                definition.description,
                world => Evaluate(definition, world));
        }

        private static IReadOnlyList<OntologyEvent> Evaluate(OntologyRuleDefinition definition, OntologyWorldState world)
        {
            var events = new List<OntologyEvent>();
            var bindings = OntologyConditionMatcher.Match(world, definition.conditions);

            foreach (var binding in bindings)
            {
                var ontologyEvent = new OntologyEvent(definition.id, definition.description);
                foreach (var effect in definition.effects)
                {
                    var fact = new OntologyFact(
                        Resolve(effect.subject, binding),
                        Resolve(effect.predicate, binding),
                        Resolve(effect.obj, binding));

                    switch (effect.kind)
                    {
                        case OntologyEffectKind.AdjustNumberFact:
                            ontologyEvent.AdjustedNumberFacts.Add(fact);
                            break;
                        case OntologyEffectKind.RemoveFact:
                            ontologyEvent.RemovedFacts.Add(fact);
                            break;
                        case OntologyEffectKind.SetFact:
                            ontologyEvent.SetFacts.Add(fact);
                            break;
                        default:
                            ontologyEvent.AddedFacts.Add(fact);
                            break;
                    }
                }

                if (ontologyEvent.AddedFacts.Count > 0
                    || ontologyEvent.RemovedFacts.Count > 0
                    || ontologyEvent.SetFacts.Count > 0
                    || ontologyEvent.AdjustedNumberFacts.Count > 0)
                {
                    events.Add(ontologyEvent);
                }
            }

            return events;
        }

        private static OntologyId Resolve(string pattern, Dictionary<string, OntologyId> binding)
        {
            return OntologyConditionMatcher.Resolve(pattern, binding);
        }
    }
}
