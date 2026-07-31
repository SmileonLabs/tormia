using System;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Resolves persistent equipment presentation from Authority-owned state.
    /// The resolver does not infer meaning from a prefab, mesh, or object name:
    /// the equipped entity's canonical template id must have an explicit
    /// presentation definition in the combat catalog.
    /// </summary>
    public static class OntologyEquipmentAnimationIntentResolver
    {
        public static bool TryResolveIdleIntent(
            OntologyAuthorityWorldProjection projection,
            string actorEntityId,
            OntologyCombatCatalog catalog,
            out string intent)
        {
            return TryResolveIntent(
                projection,
                actorEntityId,
                catalog,
                definition => definition.idleAnimationIntent,
                out intent);
        }

        public static bool TryResolveMoveIntent(
            OntologyAuthorityWorldProjection projection,
            string actorEntityId,
            OntologyCombatCatalog catalog,
            out string intent)
        {
            return TryResolveIntent(
                projection,
                actorEntityId,
                catalog,
                definition => definition.moveAnimationIntent,
                out intent);
        }

        private static bool TryResolveIntent(
            OntologyAuthorityWorldProjection projection,
            string actorEntityId,
            OntologyCombatCatalog catalog,
            Func<OntologyWeaponPresentationDefinition, string> selectIntent,
            out string intent)
        {
            intent = string.Empty;
            if (projection?.facts == null ||
                projection.entities == null ||
                string.IsNullOrWhiteSpace(actorEntityId) ||
                catalog == null ||
                selectIntent == null)
            {
                return false;
            }

            string equippedEntityId = null;
            foreach (var fact in projection.facts)
            {
                if (fact == null ||
                    !string.Equals(
                        fact.predicateId,
                        OntologyPredicates.EquippedBy,
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        fact.objectEntityId,
                        actorEntityId,
                        StringComparison.OrdinalIgnoreCase) ||
                    string.IsNullOrWhiteSpace(fact.subjectEntityId))
                {
                    continue;
                }

                var candidateEntity = Array.Find(
                    projection.entities,
                    value =>
                        value != null &&
                        string.Equals(
                            value.entityId,
                            fact.subjectEntityId,
                            StringComparison.OrdinalIgnoreCase));
                if (candidateEntity == null ||
                    catalog.FindWeapon(candidateEntity.templateId) == null)
                {
                    // Non-weapon slots (for example Floatation) coexist with
                    // MainHand and do not select a combat locomotion pose.
                    continue;
                }

                if (equippedEntityId != null &&
                    !string.Equals(
                        equippedEntityId,
                        fact.subjectEntityId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    // Two equipped weapon presentations are ambiguous even if
                    // other non-weapon equipment legitimately coexists.
                    return false;
                }

                equippedEntityId = fact.subjectEntityId;
            }

            if (equippedEntityId == null)
            {
                return false;
            }

            OntologyAuthorityEntityProjection equippedEntity = null;
            foreach (var entity in projection.entities)
            {
                if (entity == null ||
                    !string.Equals(
                        entity.entityId,
                        equippedEntityId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (equippedEntity != null)
                {
                    return false;
                }

                equippedEntity = entity;
            }

            if (equippedEntity == null ||
                string.IsNullOrWhiteSpace(equippedEntity.templateId))
            {
                return false;
            }

            var definition = catalog.FindWeapon(equippedEntity.templateId);
            var resolvedIntent = definition == null
                ? string.Empty
                : selectIntent(definition);
            if (string.IsNullOrWhiteSpace(resolvedIntent))
            {
                return false;
            }

            intent = resolvedIntent.Trim();
            return true;
        }
    }
}
