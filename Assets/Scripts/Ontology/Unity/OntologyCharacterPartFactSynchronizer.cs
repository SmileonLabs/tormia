using System;
using System.Collections.Generic;

namespace Tormia.Ontology.Core
{
    public static class OntologyCharacterPartFactSynchronizer
    {
        public static void RebuildActorFacts(
            OntologyWorldState world,
            OntologyId actorId,
            IReadOnlyList<OntologyCharacterPartDefinition> definitions,
            Func<OntologyCharacterPartDefinition, bool> isActive)
        {
            if (world == null || definitions == null || isActive == null)
            {
                return;
            }

            world.RemoveFacts(actorId, OntologyPredicates.EquippedPart);
            world.RemoveFacts(actorId, OntologyPredicates.HasCapability);

            foreach (var definition in definitions)
            {
                if (definition == null || string.IsNullOrWhiteSpace(definition.partId))
                {
                    continue;
                }

                AddDefinitionFacts(world, definition);
                if (!isActive(definition))
                {
                    continue;
                }

                world.AddFact(actorId, OntologyPredicates.EquippedPart, definition.partId);
                foreach (var fact in definition.facts ?? Array.Empty<OntologyFactEntry>())
                {
                    if (fact != null && fact.predicate == OntologyPredicates.GrantsCapability && !string.IsNullOrWhiteSpace(fact.obj))
                    {
                        world.AddFact(actorId, OntologyPredicates.HasCapability, fact.obj);
                    }
                }
            }
        }

        public static void AddDefinitionFacts(OntologyWorldState world, OntologyCharacterPartDefinition definition)
        {
            if (world == null || definition == null || string.IsNullOrWhiteSpace(definition.partId))
            {
                return;
            }

            world.AddFact(definition.partId, OntologyPredicates.HasConcept, OntologyConcepts.CharacterPart);
            if (!string.IsNullOrWhiteSpace(definition.slot))
            {
                world.AddFact(definition.partId, OntologyPredicates.HasSlot, definition.slot);
            }

            foreach (var fact in definition.facts ?? Array.Empty<OntologyFactEntry>())
            {
                if (fact != null && !string.IsNullOrWhiteSpace(fact.predicate) && !string.IsNullOrWhiteSpace(fact.obj))
                {
                    world.AddFact(definition.partId, fact.predicate, fact.obj);
                }
            }
        }

        public static bool SlotsConflict(
            OntologyCharacterPartDefinition equippedDefinition,
            OntologyCharacterPartDefinition existingDefinition)
        {
            if (equippedDefinition == null || existingDefinition == null)
            {
                return false;
            }

            return HasFact(equippedDefinition, OntologyPredicates.ConflictsWithSlot, existingDefinition.slot)
                || HasFact(existingDefinition, OntologyPredicates.ConflictsWithSlot, equippedDefinition.slot);
        }

        private static bool HasFact(OntologyCharacterPartDefinition definition, string predicate, string obj)
        {
            if (definition.facts == null || string.IsNullOrWhiteSpace(predicate) || string.IsNullOrWhiteSpace(obj))
            {
                return false;
            }

            foreach (var fact in definition.facts)
            {
                if (fact != null && fact.predicate == predicate && fact.obj == obj)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
