using System.Text.Json;
using Tormia.Ontology.Core;
using Xunit;

public sealed class ContentPackagePreflightPolicyTests
{
    private static readonly JsonSerializerOptions RuleJson =
        new(JsonSerializerDefaults.Web) { IncludeFields = true };

    private static string CreateRulePayload(string description = "same",
        bool explicitDefaultPresentation = false)
    {
        var definition = new OntologyRuleDefinition
        {
            id = "PayloadPreflightRule",
            catalogVersion = 1,
            description = description,
            conditions =
            [
                OntologyCondition.HasConcept("?target", "Actor")
            ],
            effects =
            [
                OntologyEffect.SetFact("?target", "preflight_result", "True")
            ],
            runtimePresentation = explicitDefaultPresentation
                ? new OntologyActionPresentationDefinition
                {
                    actorAnimationIntent = ""
                }
                : null
        };
        return JsonSerializer.Serialize(definition, RuleJson);
    }

    [Fact]
    public void ReleaseManifestChecksumIsOrderIndependentAndKindSensitive()
    {
        var first = ContentCatalogRepository.ComputeManifestChecksumForTest(
        [
            ("rule", "ExampleRule", 2, new string('a', 64)),
            ("action_effect", "example_action", 1, new string('b', 64))
        ]);
        var reordered = ContentCatalogRepository.ComputeManifestChecksumForTest(
        [
            ("action_effect", "example_action", 1, new string('b', 64)),
            ("rule", "ExampleRule", 2, new string('a', 64))
        ]);
        var changedKind = ContentCatalogRepository.ComputeManifestChecksumForTest(
        [
            ("rule", "example_action", 1, new string('b', 64)),
            ("rule", "ExampleRule", 2, new string('a', 64))
        ]);

        Assert.Equal(first, reordered);
        Assert.NotEqual(first, changedKind);
        Assert.Equal(64, first.Length);
    }

    [Fact]
    public void ExactActiveManifestIsReady()
    {
        Assert.Null(ContentPackagePreflightPolicy.ResolveRejectionCode(
            activePackageMatch: true,
            missingDefinitionCount: 0,
            checksumMismatchCount: 0));
    }

    [Fact]
    public void InactivePackageFailsBeforeDefinitionDetails()
    {
        Assert.Equal(
            "content_package_not_active",
            ContentPackagePreflightPolicy.ResolveRejectionCode(
                activePackageMatch: false,
                missingDefinitionCount: 1,
                checksumMismatchCount: 1));
    }

    [Fact]
    public void MissingDefinitionFailsClosed()
    {
        Assert.Equal(
            "content_definition_missing",
            ContentPackagePreflightPolicy.ResolveRejectionCode(
                activePackageMatch: true,
                missingDefinitionCount: 1,
                checksumMismatchCount: 0));
    }

    [Fact]
    public void ChangedImmutablePayloadFailsClosed()
    {
        Assert.Equal(
            "content_definition_checksum_mismatch",
            ContentPackagePreflightPolicy.ResolveRejectionCode(
                activePackageMatch: true,
                missingDefinitionCount: 0,
                checksumMismatchCount: 1));
    }

    [Fact]
    public void SameCanonicalPayloadProducesPublishedIdentityChecksum()
    {
        var identity = new ContentDefinitionPreflightIdentity(
            "PayloadPreflightRule", 1, null, CreateRulePayload());

        var first = ContentCatalogRepository
            .ComputePreflightPayloadChecksumForTest(identity, true);
        var second = ContentCatalogRepository
            .ComputePreflightPayloadChecksumForTest(identity, true);

        Assert.NotNull(first);
        Assert.Equal(first, second);
        Assert.Equal(64, first!.Length);
    }

    [Fact]
    public void ChangedPayloadAtSameVersionProducesMismatchChecksum()
    {
        var original = new ContentDefinitionPreflightIdentity(
            "PayloadPreflightRule", 1, null, CreateRulePayload());
        var changed = original with
        {
            PayloadJson = CreateRulePayload("changed")
        };

        Assert.NotEqual(
            ContentCatalogRepository.ComputePreflightPayloadChecksumForTest(
                original, true),
            ContentCatalogRepository.ComputePreflightPayloadChecksumForTest(
                changed, true));
    }

    [Fact]
    public void DefaultOptionalFieldsDoNotCauseFalsePayloadMismatch()
    {
        var omitted = new ContentDefinitionPreflightIdentity(
            "PayloadPreflightRule", 1, null, CreateRulePayload());
        var explicitDefault = omitted with
        {
            PayloadJson = CreateRulePayload(explicitDefaultPresentation: true)
        };

        Assert.Equal(
            ContentCatalogRepository.ComputePreflightPayloadChecksumForTest(
                omitted, true),
            ContentCatalogRepository.ComputePreflightPayloadChecksumForTest(
                explicitDefault, true));
    }
}
