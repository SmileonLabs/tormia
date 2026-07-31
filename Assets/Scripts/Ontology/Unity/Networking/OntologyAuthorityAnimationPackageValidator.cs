using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace Tormia.Ontology.Core
{
    public static class OntologyAuthorityAnimationPackageValidator
    {
        private static readonly Regex ActorIntentPattern = new(
            "\"actorAnimationIntent\"\\s*:\\s*\"(?<intent>[^\"]+)\"",
            RegexOptions.CultureInvariant);

        public static bool TryValidateConfiguration(
            OntologyAuthorityDevelopmentAction[] actions,
            OntologyAnimationContentManifest manifest,
            out string error)
        {
            return TryValidateConfiguration(
                actions,
                null,
                null,
                manifest,
                out error);
        }

        public static bool TryValidateConfiguration(
            OntologyAuthorityDevelopmentAction[] actions,
            OntologyAuthorityDevelopmentRule[] rules,
            OntologyRuleDatabase ruleDatabase,
            OntologyAnimationContentManifest manifest,
            out string error)
        {
            error = string.Empty;
            if (actions == null || actions.Length == 0)
            {
                error = "No development actions are configured.";
                return false;
            }

            foreach (var action in actions)
            {
                if (action == null || string.IsNullOrWhiteSpace(action.actionId))
                {
                    error = "A development action has no canonical actionId.";
                    return false;
                }

                var intent = ExtractActorAnimationIntent(
                    action.structuredDefinitionJson);
                if (string.IsNullOrWhiteSpace(intent)) continue;
                if (!ManifestContainsIntent(manifest, intent))
                {
                    error = "Action '" + action.actionId +
                            "' references unknown animation intent '" +
                            intent + "'.";
                    return false;
                }
            }

            foreach (var configured in
                     rules ??
                     Array.Empty<OntologyAuthorityDevelopmentRule>())
            {
                if (configured == null ||
                    string.IsNullOrWhiteSpace(configured.ruleId))
                    continue;
                var definition =
                    ruleDatabase?.Definitions.FirstOrDefault(value =>
                        value != null &&
                        string.Equals(
                            value.id,
                            configured.ruleId,
                            StringComparison.Ordinal));
                var intent = definition?.runtimePresentation
                    ?.actorAnimationIntent?.Trim();
                if (string.IsNullOrWhiteSpace(intent)) continue;
                if (!ManifestContainsIntent(manifest, intent))
                {
                    error = "Rule Block '" + configured.ruleId +
                            "' references unknown runtime animation intent '" +
                            intent + "'.";
                    return false;
                }
            }
            return true;
        }

        private static bool ManifestContainsIntent(
            OntologyAnimationContentManifest manifest,
            string intent)
        {
            if (manifest == null || string.IsNullOrWhiteSpace(intent))
                return false;
            foreach (var entry in manifest.Entries)
            {
                if (entry?.intents == null) continue;
                foreach (var candidate in entry.intents)
                {
                    if (string.Equals(
                            candidate,
                            intent,
                            StringComparison.Ordinal))
                        return true;
                }
            }
            return false;
        }

        public static bool TryValidateProjection(
            OntologyAuthorityWorldProjection projection,
            string packageId,
            string packageVersion,
            OntologyAuthorityDevelopmentAction[] expectedActions,
            out string error)
        {
            error = string.Empty;
            if (projection?.actions == null)
            {
                error = "Authority projection contains no action definitions.";
                return false;
            }

            foreach (var expected in
                     expectedActions ?? Array.Empty<OntologyAuthorityDevelopmentAction>())
            {
                if (expected == null) continue;
                OntologyAuthorityActionDefinitionProjection match = null;
                foreach (var candidate in projection.actions)
                {
                    if (candidate == null ||
                        !string.Equals(
                            candidate.packageId,
                            packageId,
                            StringComparison.Ordinal) ||
                        !string.Equals(
                            candidate.packageVersion,
                            packageVersion,
                            StringComparison.Ordinal) ||
                        !string.Equals(
                            candidate.actionId,
                            expected.actionId,
                            StringComparison.Ordinal) ||
                        candidate.definitionVersion !=
                        Math.Max(1, expected.definitionVersion))
                        continue;
                    match = candidate;
                    break;
                }

                if (match == null)
                {
                    error = "Authority projection is missing exact action " +
                            packageId + "@" + packageVersion + "/" +
                            expected.actionId + "@v" +
                            Math.Max(1, expected.definitionVersion) + ".";
                    return false;
                }

                var expectedIntent = ExtractActorAnimationIntent(
                    expected.structuredDefinitionJson);
                if (!string.IsNullOrWhiteSpace(expectedIntent) &&
                    !string.Equals(
                        match.actorAnimationIntent,
                        expectedIntent,
                        StringComparison.Ordinal))
                {
                    error = "Authority projection intent mismatch for '" +
                            expected.actionId + "': expected '" +
                            expectedIntent + "', received '" +
                            (match.actorAnimationIntent ?? string.Empty) + "'.";
                    return false;
                }
            }
            return true;
        }

        public static string ExtractActorAnimationIntent(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return string.Empty;
            var match = ActorIntentPattern.Match(json);
            return match.Success
                ? match.Groups["intent"].Value.Trim()
                : string.Empty;
        }
    }
}
