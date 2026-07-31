using System;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Resolves a non-durable actor animation intent from the exact versioned
    /// action definition confirmed by World Authority.
    /// </summary>
    public static class OntologyAuthorityActionAnimationIntentResolver
    {
        public static bool IsExecuteActionForActor(
            OntologyWorldCommand command,
            string actorEntityId)
        {
            return TryReadExecuteActionPayload(command, out var payload) &&
                   !string.IsNullOrWhiteSpace(actorEntityId) &&
                   string.Equals(
                       payload.actorEntityId,
                       actorEntityId,
                       StringComparison.OrdinalIgnoreCase);
        }

        public static bool TryResolveActorIntent(
            OntologyWorldCommand command,
            OntologyAuthorityCommandResult result,
            OntologyAuthorityWorldProjection projection,
            string actorEntityId,
            out string intent)
        {
            intent = string.Empty;
            if (command == null ||
                result == null ||
                !result.accepted ||
                result.isReplay ||
                string.IsNullOrWhiteSpace(actorEntityId) ||
                projection?.actions == null)
            {
                return false;
            }

            if (!TryReadExecuteActionPayload(command, out var payload) ||
                !string.Equals(
                    payload.actorEntityId,
                    actorEntityId,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            OntologyAuthorityActionDefinitionProjection match = null;
            foreach (var candidate in projection.actions)
            {
                if (candidate == null ||
                    !string.Equals(
                        candidate.packageId,
                        payload.packageId,
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        candidate.packageVersion,
                        payload.packageVersion,
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        candidate.actionId,
                        payload.actionId,
                        StringComparison.Ordinal) ||
                    candidate.definitionVersion != payload.definitionVersion)
                {
                    continue;
                }

                if (match != null)
                {
                    return false;
                }

                match = candidate;
            }

            if (match == null ||
                string.IsNullOrWhiteSpace(match.actorAnimationIntent))
            {
                return false;
            }

            intent = match.actorAnimationIntent.Trim();
            return true;
        }

        private static bool TryReadExecuteActionPayload(
            OntologyWorldCommand command,
            out ExecuteActionPayload payload)
        {
            payload = null;
            if (command == null ||
                !string.Equals(
                    command.commandType,
                    OntologyWorldCommandKinds.ExecuteAction,
                    StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(command.payloadJson))
            {
                return false;
            }

            try
            {
                payload = JsonUtility.FromJson<ExecuteActionPayload>(
                    command.payloadJson);
            }
            catch (ArgumentException)
            {
                return false;
            }

            return payload != null &&
                   !string.IsNullOrWhiteSpace(payload.actorEntityId);
        }

        [Serializable]
        private sealed class ExecuteActionPayload
        {
            public string actorEntityId;
            public string packageId;
            public string packageVersion;
            public string actionId;
            public int definitionVersion;
        }
    }
}
