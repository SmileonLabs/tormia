using Xunit;

public sealed class PlayerAttackOccurrenceRuntimeTests
{
    [Fact]
    public async Task ContactOccurrenceCanBeResolvedExactlyOnce()
    {
        var registry = new InMemoryWorldPlayerAttackOccurrenceRegistry();
        var occurrence = CreateOccurrence();
        await registry.Set(occurrence, CancellationToken.None);

        Assert.True(await registry.TryResolve(
            occurrence.WorldId, occurrence.OccurrenceId,
            CancellationToken.None));
        Assert.False(await registry.TryResolve(
            occurrence.WorldId, occurrence.OccurrenceId,
            CancellationToken.None));
        var resolved = await registry.Get(
            occurrence.WorldId, occurrence.OccurrenceId,
            CancellationToken.None);
        Assert.True(resolved?.ContactResolved);
    }

    [Fact]
    public async Task RemovedOccurrenceCannotAuthorizeContact()
    {
        var registry = new InMemoryWorldPlayerAttackOccurrenceRegistry();
        var occurrence = CreateOccurrence();
        await registry.Set(occurrence, CancellationToken.None);
        await registry.Remove(
            occurrence.WorldId, occurrence.OccurrenceId,
            CancellationToken.None);

        Assert.Null(await registry.Get(
            occurrence.WorldId, occurrence.OccurrenceId,
            CancellationToken.None));
        Assert.False(await registry.TryResolve(
            occurrence.WorldId, occurrence.OccurrenceId,
            CancellationToken.None));
    }

    private static WorldPlayerAttackOccurrence CreateOccurrence()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return new WorldPlayerAttackOccurrence(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), "package", "1.0.0", "attack",
            1, now, now + 1000);
    }
}
