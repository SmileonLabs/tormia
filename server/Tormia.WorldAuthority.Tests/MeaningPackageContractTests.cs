using Xunit;
using System.Text.Json;

public sealed class MeaningPackageContractTests
{
    [Fact]
    public void ApplyPackageAcceptsArbitraryEntityMeaningWithoutVisualTypeGate()
    {
        var request = new ApplyMeaningPackagePayload(
            "apply",
            Guid.NewGuid(),
            Guid.NewGuid(),
            "primary_physical_meaning",
            "rule_preset_water_buoyancy",
            false,
            ["physical_profile", "physical_state"],
            ["FloatableObject"],
            [
                new InitialAuthoredFactPayload(
                    "physical_profile",
                    "canonical",
                    null,
                    "LightBuoyant",
                    null)
            ],
            [
                new MeaningRuleBlockPayload(
                    Guid.NewGuid(),
                    "BuoyantWhenInWater",
                    1,
                    """{"bindingVariable":"?object"}""")
            ]);

        Assert.True(request.IsValid(out var rejectionCode));
        Assert.Equal(string.Empty, rejectionCode);
    }

    [Fact]
    public void RemovePackageRequiresOnlyDurableTargetAndSlotIdentity()
    {
        var request = new ApplyMeaningPackagePayload(
            "remove",
            Guid.Empty,
            Guid.NewGuid(),
            "primary_physical_meaning",
            string.Empty,
            false,
            null,
            null,
            null,
            null);

        Assert.True(request.IsValid(out var rejectionCode));
        Assert.Equal(string.Empty, rejectionCode);
    }

    [Fact]
    public void InvalidRuleOrTripleRejectsWholePackageBeforeMutation()
    {
        var request = new ApplyMeaningPackagePayload(
            "apply",
            Guid.NewGuid(),
            Guid.NewGuid(),
            "primary_physical_meaning",
            "bad_package",
            false,
            ["physical_profile"],
            null,
            [
                new InitialAuthoredFactPayload(
                    "invalid relation",
                    "canonical",
                    null,
                    "LightBuoyant",
                    null)
            ],
            null);

        Assert.False(request.IsValid(out var rejectionCode));
        Assert.Equal("invalid_meaning_package_spec", rejectionCode);
    }

    [Fact]
    public void BaselinePackageCanRequestUnclaimedContributionAdoption()
    {
        var request = new ApplyMeaningPackagePayload(
            "apply",
            Guid.NewGuid(),
            Guid.NewGuid(),
            "template_semantic_baseline",
            "Sword01Bronze_semantic_baseline_v8",
            true,
            null,
            ["Weapon"],
            null,
            [
                new MeaningRuleBlockPayload(
                    Guid.NewGuid(),
                    "EquipHeldToolOnInteract",
                    1,
                    """{"bindingVariable":"?tool"}""")
            ]);

        Assert.True(request.IsValid(out var rejectionCode));
        Assert.True(request.AdoptExistingContributions);
        Assert.Equal(string.Empty, rejectionCode);
    }

    [Fact]
    public void CanonicalFactJsonUsesNullForOptionalEntityGuid()
    {
        const string json =
            """
            {
              "operation": "apply",
              "applicationId": "761d1499-8e47-4de8-aa32-a6713908127c",
              "targetEntityId": "b5f05416-10eb-4ec9-94eb-3a40a663d66e",
              "slotId": "primary_physical_meaning",
              "packageId": "rule_preset_water_buoyancy",
              "authoredFacts": [
                {
                  "predicateId": "physical_profile",
                  "objectKind": "canonical",
                  "objectEntityId": null,
                  "objectCanonicalId": "LightBuoyant",
                  "objectValueJson": null
                }
              ]
            }
            """;

        var request =
            JsonSerializer.Deserialize<ApplyMeaningPackagePayload>(
                json,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(request);
        Assert.True(request!.IsValid(out var rejectionCode));
        Assert.Equal(string.Empty, rejectionCode);
    }
}
