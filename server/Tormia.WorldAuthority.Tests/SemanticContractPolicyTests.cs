using Xunit;

public sealed class SemanticContractPolicyTests
{
    [Fact]
    public void ReleasedOwnershipCannotSatisfyReadyContract()
    {
        Assert.True(SemanticContractPolicy.OwnershipCanSatisfyReady(null));
        Assert.False(SemanticContractPolicy.OwnershipCanSatisfyReady(42));
    }

    [Fact]
    public void ManifestChecksumIsOrderIndependentAndVersionSensitive()
    {
        var original = CreateRequest(2);
        var reordered = original with
        {
            RequiredConceptIds = ["PlayerControlled", "Actor"],
            ReplacePredicateIds = ["movement_speed", "locomotion_action"]
        };
        var upgraded = original with { ContractVersion = 3 };
        var differentSlot = original with { SlotId = "alternate_slot" };
        var differentOwnershipPolicy = original with
        {
            RequiresOwnedBinding = !original.RequiresOwnedBinding
        };

        Assert.Equal(
            SemanticContractPolicy.ComputeManifestChecksum(original),
            SemanticContractPolicy.ComputeManifestChecksum(reordered));
        Assert.NotEqual(
            SemanticContractPolicy.ComputeManifestChecksum(original),
            SemanticContractPolicy.ComputeManifestChecksum(upgraded));
        Assert.NotEqual(
            SemanticContractPolicy.ComputeManifestChecksum(original),
            SemanticContractPolicy.ComputeManifestChecksum(differentSlot));
        Assert.NotEqual(
            SemanticContractPolicy.ComputeManifestChecksum(original),
            SemanticContractPolicy.ComputeManifestChecksum(
                differentOwnershipPolicy));
    }

    [Fact]
    public void MeaningPackageContainsServerOwnedVersionAndHashMarkers()
    {
        var request = CreateRequest(2);
        var hash = SemanticContractPolicy.ComputeManifestChecksum(request);
        var package = request.ToMeaningPackage(
        [
            new InitialAuthoredFactPayload(
                request.VersionPredicateId, "number", null, null, "2"),
            new InitialAuthoredFactPayload(
                request.ChecksumPredicateId, "text", null, null,
                "\"" + hash + "\"")
        ]);

        Assert.Contains(package.AuthoredFacts!, value =>
            value.PredicateId == request.VersionPredicateId);
        Assert.Contains(package.AuthoredFacts!, value =>
            value.PredicateId == request.ChecksumPredicateId);
        Assert.Contains(request.VersionPredicateId,
            package.ReplacePredicateIds!);
        Assert.Contains(request.ChecksumPredicateId,
            package.ReplacePredicateIds!);
    }

    [Fact]
    public void InvalidBindingRejectsContractBeforeMutation()
    {
        var request = CreateRequest(2) with
        {
            RuleBlocks =
            [
                new MeaningRuleBlockPayload(
                    Guid.NewGuid(), "invalid rule id", 1, "{}")
            ]
        };

        Assert.False(request.IsValid(out var rejectionCode));
        Assert.Equal("invalid_semantic_contract_payload", rejectionCode);
    }

    [Fact]
    public void PlayerAvatarPreparationRequiresOneSharedEntityIdentity()
    {
        var contract = CreateRequest(1);
        var request = new PreparePlayerAvatarPayload(
            CreateEntity(contract.TargetEntityId), contract);

        Assert.True(request.IsValid(out var rejectionCode), rejectionCode);

        var mismatched = request with
        {
            SemanticContract = contract with { TargetEntityId = Guid.NewGuid() }
        };
        Assert.False(mismatched.IsValid(out rejectionCode));
        Assert.Equal("invalid_prepare_player_avatar_payload", rejectionCode);
    }

    [Fact]
    public void InvalidRuleRejectsPlayerAvatarPreparationBeforeMutation()
    {
        var entityId = Guid.NewGuid();
        var contract = CreateRequest(1) with
        {
            TargetEntityId = entityId,
            RuleBlocks =
            [
                new MeaningRuleBlockPayload(
                    Guid.NewGuid(), "invalid rule id", 1, "{}")
            ]
        };
        var request = new PreparePlayerAvatarPayload(
            CreateEntity(entityId), contract);

        Assert.False(request.IsValid(out var rejectionCode));
        Assert.Equal("invalid_prepare_player_avatar_payload", rejectionCode);
    }

    private static PlaceEntityPayload CreateEntity(Guid entityId) =>
        new(entityId, "project_player_avatar", 1, "Player", "main",
            new TransformPayload(0, 1, 0, 0, 0, 0, 1, 1, 1), []);

    private static PrepareSemanticContractPayload CreateRequest(int version) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "player_runtime_contract",
            "player_avatar_contract",
            version,
            "social_village",
            "4.0.0",
            "semantic_contract_version",
            "semantic_contract_checksum",
            true,
            ["locomotion_action", "movement_speed"],
            ["Actor", "PlayerControlled"],
            [
                new InitialAuthoredFactPayload(
                    "movement_speed", "number", null, null, "5")
            ],
            [
                new MeaningRuleBlockPayload(
                    Guid.NewGuid(), "MovePlayerFromIntent", 3,
                    "{\"bindingVariable\":\"?actor\"}")
            ],
            true);
}
