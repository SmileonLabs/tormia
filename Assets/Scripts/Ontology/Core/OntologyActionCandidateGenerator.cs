using System.Collections.Generic;

namespace Tormia.Ontology.Core
{
    public sealed class OntologyActionCandidateGenerator
    {
        private readonly List<OntologyActionCandidateDefinition> definitions;

        public OntologyActionCandidateGenerator()
        {
            definitions = CreateDefaultDefinitions();
        }

        public OntologyActionCandidateGenerator(IReadOnlyList<OntologyActionCandidateDefinition> definitions)
        {
            this.definitions = definitions != null && definitions.Count > 0
                ? new List<OntologyActionCandidateDefinition>(definitions)
                : CreateDefaultDefinitions();
        }

        public List<OntologyActionCandidate> Generate(OntologyWorldState world, OntologyId actorId)
        {
            var candidates = new List<OntologyActionCandidate>();
            if (world == null || actorId.IsEmpty)
            {
                return candidates;
            }

            foreach (var definition in definitions)
            {
                AddCandidates(world, actorId, definition, candidates);
            }

            return candidates;
        }

        private static void AddCandidates(
            OntologyWorldState world,
            OntologyId actorId,
            OntologyActionCandidateDefinition definition,
            List<OntologyActionCandidate> candidates)
        {
            if (definition == null || string.IsNullOrWhiteSpace(definition.actionVerb))
            {
                return;
            }

            var initialBinding = new Dictionary<string, OntologyId> { ["?actor"] = actorId };
            foreach (var binding in OntologyConditionMatcher.Match(world, definition.conditions, initialBinding))
            {
                var target = OntologyConditionMatcher.Resolve(definition.targetPattern, binding);
                var tool = string.IsNullOrWhiteSpace(definition.toolPattern)
                    ? default
                    : OntologyConditionMatcher.Resolve(definition.toolPattern, binding);
                candidates.Add(new OntologyActionCandidate(
                    new OntologyAction(actorId, definition.actionVerb, target, tool),
                    Format(definition.labelFormat, binding),
                    Format(definition.reasonFormat, binding)));
            }
        }

        private static string Format(string format, Dictionary<string, OntologyId> binding)
        {
            if (string.IsNullOrWhiteSpace(format))
            {
                return string.Empty;
            }

            foreach (var pair in binding)
            {
                format = format.Replace("{" + pair.Key.TrimStart('?') + "}", pair.Value.ToString());
            }

            return format;
        }

        public static List<OntologyActionCandidateDefinition> CreateDefaultDefinitions()
        {
            return new List<OntologyActionCandidateDefinition>
            {
                new OntologyActionCandidateDefinition
                {
                    actionVerb = "attack",
                    targetPattern = "?target",
                    toolPattern = "?tool",
                    labelFormat = "Attack {target} with {tool}",
                    reasonFormat = "{tool} has Fire and {target} is a Dry Plant",
                    conditions =
                    {
                        OntologyCondition.Fact("?actor", "has_skill", "Attack"),
                        OntologyCondition.Fact("?actor", "equipped", "?tool"),
                        OntologyCondition.HasConcept("?tool", "Fire"),
                        OntologyCondition.HasConcept("?target", "Plant"),
                        OntologyCondition.HasConcept("?target", "Dry"),
                        OntologyCondition.NotFact("?target", "state", "Burning")
                    }
                },
                new OntologyActionCandidateDefinition
                {
                    actionVerb = "inspect",
                    targetPattern = "?target",
                    labelFormat = "Inspect smoking {target}",
                    reasonFormat = "{target} emits Smoke",
                    conditions =
                    {
                        OntologyCondition.Fact("?actor", "has_skill", "Inspect"),
                        OntologyCondition.Fact("?target", "emits", "Smoke"),
                        OntologyCondition.NotFact("?actor", "inspects", "?target")
                    }
                },
                new OntologyActionCandidateDefinition
                {
                    actionVerb = "talk",
                    targetPattern = "?target",
                    labelFormat = "Talk to {target}",
                    reasonFormat = "{target} is a Creature",
                    conditions =
                    {
                        OntologyCondition.Fact("?actor", "has_skill", "Talk"),
                        OntologyCondition.HasConcept("?target", "Creature"),
                        OntologyCondition.NotEqual("?target", "?actor")
                    }
                },
                new OntologyActionCandidateDefinition
                {
                    actionVerb = "help",
                    targetPattern = "?target",
                    labelFormat = "Help {target}",
                    reasonFormat = "{target} is Disturbed",
                    conditions =
                    {
                        OntologyCondition.Fact("?actor", "has_skill", "Help"),
                        OntologyCondition.Fact("?target", "state", "Disturbed"),
                        OntologyCondition.NotFact("?actor", "helps", "?target")
                    }
                },
                new OntologyActionCandidateDefinition
                {
                    actionVerb = "equip_part",
                    targetPattern = "?part",
                    labelFormat = "Equip {part}",
                    reasonFormat = "{part} is an available character part",
                    conditions =
                    {
                        OntologyCondition.HasConcept("?part", OntologyConcepts.CharacterPart),
                        OntologyCondition.NotFact("?actor", OntologyPredicates.EquippedPart, "?part")
                    }
                },
                new OntologyActionCandidateDefinition
                {
                    actionVerb = "unequip_part",
                    targetPattern = "?part",
                    labelFormat = "Unequip {part}",
                    reasonFormat = "{part} is currently equipped",
                    conditions =
                    {
                        OntologyCondition.Fact("?actor", OntologyPredicates.EquippedPart, "?part")
                    }
                }
            };
        }
    }
}
