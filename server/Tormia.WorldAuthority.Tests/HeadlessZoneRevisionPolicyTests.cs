using Xunit;

public sealed class HeadlessZoneRevisionPolicyTests
{
    [Fact]
    public void ExactRevisionIsPublishable()
    {
        Assert.True(HeadlessZoneRevisionPolicy.IsPublishable(42L, 42L));
    }

    [Theory]
    [InlineData(42L, 41L)]
    [InlineData(42L, 43L)]
    [InlineData(-1L, 42L)]
    public void MismatchedOrUnavailableInputRevisionIsNotPublishable(
        long inputWorldRevision,
        long observedWorldRevision)
    {
        Assert.False(HeadlessZoneRevisionPolicy.IsPublishable(
            inputWorldRevision,
            observedWorldRevision));
    }

    [Fact]
    public void MissingObservedRevisionIsNotPublishable()
    {
        Assert.False(HeadlessZoneRevisionPolicy.IsPublishable(42L, null));
    }
}
