using System;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Resolves player-facing names without changing Authority identity or the
    /// durable instance name. Only known system-generated suffixes are hidden;
    /// user-authored names always pass through unchanged.
    /// </summary>
    public static class OntologyEntityDisplayNameResolver
    {
        private const string CloneSuffix = "(Clone)";

        public static string Resolve(
            OntologyPlaceableDefinition definition,
            string storedDisplayName)
        {
            if (definition == null)
                return storedDisplayName ?? string.Empty;

            var baseName = OntologyLanguagePackService.Text(
                definition.displayNameKey,
                definition.EffectiveDisplayName);
            if (string.IsNullOrWhiteSpace(storedDisplayName))
                return baseName;

            var candidate = storedDisplayName.Trim();
            if (!IsSystemGeneratedName(definition.definitionId, candidate))
                return candidate;

            return baseName;
        }

        public static string ResolveForList(
            OntologyPlaceableDefinition definition,
            string storedDisplayName,
            int duplicateOrdinal)
        {
            var resolved = Resolve(definition, storedDisplayName);
            return duplicateOrdinal > 1 &&
                   IsSystemGeneratedName(
                       definition?.definitionId,
                       storedDisplayName)
                ? resolved + " " + duplicateOrdinal
                : resolved;
        }

        public static bool IsSystemGeneratedName(
            string definitionId,
            string storedDisplayName)
        {
            if (string.IsNullOrWhiteSpace(definitionId) ||
                string.IsNullOrWhiteSpace(storedDisplayName))
            {
                return false;
            }

            var candidate = storedDisplayName.Trim();
            if (candidate.EndsWith(
                    CloneSuffix,
                    StringComparison.OrdinalIgnoreCase))
            {
                candidate = candidate.Substring(
                    0,
                    candidate.Length - CloneSuffix.Length).TrimEnd();
            }

            if (string.Equals(
                    candidate,
                    definitionId,
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!candidate.StartsWith(
                    definitionId + "_",
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var suffix = candidate.Substring(definitionId.Length + 1);
            return IsSystemGeneratedSuffix(suffix);
        }

        private static bool IsSystemGeneratedSuffix(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            var segments = value.Split(
                new[] { '_' },
                StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
                return false;

            var index = 0;
            var hasCopyMarker = false;
            if (IsNumericSequence(segments[index]) ||
                IsAuthorityGuidPrefix(segments[index]))
            {
                index++;
            }
            else if (IsCopyMarker(segments[index]))
            {
                hasCopyMarker = true;
                index++;
            }
            else
            {
                return false;
            }

            for (; index < segments.Length; index++)
            {
                if (IsCopyMarker(segments[index]))
                {
                    hasCopyMarker = true;
                    continue;
                }

                // BuildUniqueName adds a final numeric sequence only after a
                // duplicate marker. No other suffix is considered generated.
                if (hasCopyMarker &&
                    index == segments.Length - 1 &&
                    IsNumericSequence(segments[index]))
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        private static bool IsCopyMarker(string value)
        {
            return string.Equals(
                value,
                "Copy",
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsNumericSequence(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;
            for (var index = 0; index < value.Length; index++)
            {
                if (!char.IsDigit(value[index]))
                    return false;
            }
            return true;
        }

        private static bool IsAuthorityGuidPrefix(string value)
        {
            if (value == null || value.Length != 8)
                return false;
            for (var index = 0; index < value.Length; index++)
            {
                var character = value[index];
                var isHex =
                    character >= '0' && character <= '9' ||
                    character >= 'a' && character <= 'f' ||
                    character >= 'A' && character <= 'F';
                if (!isHex)
                    return false;
            }
            return true;
        }
    }
}
