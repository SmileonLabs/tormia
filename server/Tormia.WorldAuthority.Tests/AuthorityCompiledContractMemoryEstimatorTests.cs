using Xunit;

public sealed class AuthorityCompiledContractMemoryEstimatorTests
{
    [Fact]
    public void EmptyContractStillAccountsForOwnedContainers()
    {
        var estimate = AuthorityCompiledContractMemoryEstimator.Estimate([]);

        Assert.True(estimate >= 500, $"Unexpected empty estimate: {estimate}");
    }

    [Fact]
    public void DuplicateRowsAccountForRawProvenanceWithoutDuplicatingSemanticIndexes()
    {
        var subject = Guid.NewGuid();
        var one = new[]
        {
            new AuthorityFactSnapshot(subject, "health", "number", "10")
        };
        var duplicate = new[]
        {
            one[0],
            one[0] with { SourceRuleBindingId = Guid.NewGuid() }
        };

        var oneEstimate = AuthorityCompiledContractMemoryEstimator.Estimate(one);
        var duplicateEstimate = AuthorityCompiledContractMemoryEstimator.Estimate(duplicate);

        Assert.True(duplicateEstimate > oneEstimate);
        Assert.True(duplicateEstimate - oneEstimate < oneEstimate);
    }

    [Fact]
    public void EstimateIncludesMoreThanSnapshotRecordsAndStrings()
    {
        var facts = Enumerable.Range(0, 1_000)
            .Select(index => new AuthorityFactSnapshot(
                Guid.NewGuid(),
                $"predicate_{index % 10}",
                "canonical",
                $"value_{index}"))
            .ToArray();
        var formerEstimate = facts.Sum(fact =>
            96L + fact.PredicateId.Length * 2L +
            fact.ObjectKind.Length * 2L + fact.ObjectValue.Length * 2L);

        var estimate = AuthorityCompiledContractMemoryEstimator.Estimate(facts);

        Assert.True(estimate > formerEstimate * 2,
            $"Expected retained indexes to dominate old estimate; old={formerEstimate}, new={estimate}");
    }

    [Fact]
    public void CompiledContractUsesRetainedMemoryEstimator()
    {
        var facts = new[]
        {
            new AuthorityFactSnapshot(Guid.NewGuid(), "kind", "canonical", "monster")
        };

        var compiled = AuthorityCompiledEvaluationContract.Compile(facts);

        Assert.Equal(
            AuthorityCompiledContractMemoryEstimator.Estimate(facts),
            compiled.EstimatedBytes);
    }
}
