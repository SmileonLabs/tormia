using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

internal static class SemanticContractPolicy
{
    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web);

    public static bool OwnershipCanSatisfyReady(long? releasedRevision) =>
        releasedRevision is null;

    public static string ComputeManifestChecksum(
        PrepareSemanticContractPayload request)
    {
        var manifest = new
        {
            request.SlotId,
            request.ContractId,
            request.ContractVersion,
            request.PackageId,
            request.PackageVersion,
            request.VersionPredicateId,
            request.ChecksumPredicateId,
            request.AdoptExistingContributions,
            request.RequiresOwnedBinding,
            ReplacePredicateIds = (request.ReplacePredicateIds ?? [])
                .OrderBy(value => value, StringComparer.Ordinal),
            RequiredConceptIds = (request.RequiredConceptIds ?? [])
                .OrderBy(value => value, StringComparer.Ordinal),
            AuthoredFacts = (request.AuthoredFacts ?? [])
                .OrderBy(value => value.PredicateId, StringComparer.Ordinal)
                .ThenBy(value => value.ObjectKind, StringComparer.Ordinal)
                .ThenBy(value => value.ObjectCanonicalId, StringComparer.Ordinal)
                .ThenBy(value => value.ObjectEntityId)
                .ThenBy(value => value.ObjectValueJson, StringComparer.Ordinal),
            RuleBlocks = (request.RuleBlocks ?? [])
                .OrderBy(value => value.RuleId, StringComparer.Ordinal)
                .ThenBy(value => value.RuleVersion)
                .ThenBy(value => value.NormalizedParametersJson(),
                    StringComparer.Ordinal)
                .Select(value => new
                {
                    value.RuleId,
                    value.RuleVersion,
                    ParametersJson = value.NormalizedParametersJson()
                })
        };
        var payload = JsonSerializer.Serialize(manifest, Json);
        return Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(payload)))
            .ToLowerInvariant();
    }
}
