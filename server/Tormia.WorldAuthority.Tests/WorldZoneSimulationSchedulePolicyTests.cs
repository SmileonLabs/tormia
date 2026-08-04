using Xunit;

public sealed class WorldZoneSimulationSchedulePolicyTests
{
    [Fact]
    public void ActiveZoneRunsAtOneSecondIntervals()
    {
        var completedAt = DateTimeOffset.Parse("2026-08-02T00:00:00Z");

        Assert.False(WorldZoneSimulationSchedulePolicy.IsDue(
            "active", completedAt.AddMilliseconds(999), completedAt));
        Assert.True(WorldZoneSimulationSchedulePolicy.IsDue(
            "active", completedAt.AddSeconds(1), completedAt));
        Assert.Equal(
            TimeSpan.FromSeconds(1),
            WorldZoneSimulationSchedulePolicy.ResolveInterval("active"));
    }

    [Fact]
    public void ReducedZoneRunsAtFiveSecondIntervals()
    {
        var completedAt = DateTimeOffset.Parse("2026-08-02T00:00:00Z");

        Assert.False(WorldZoneSimulationSchedulePolicy.IsDue(
            "reduced", completedAt.AddMilliseconds(4999), completedAt));
        Assert.True(WorldZoneSimulationSchedulePolicy.IsDue(
            "reduced", completedAt.AddSeconds(5), completedAt));
        Assert.Equal(
            TimeSpan.FromSeconds(5),
            WorldZoneSimulationSchedulePolicy.ResolveInterval("reduced"));
    }

    [Theory]
    [InlineData("idle")]
    [InlineData("dormant")]
    [InlineData("unknown")]
    public void NonRunningZoneNeverBecomesDue(string runState)
    {
        Assert.Null(
            WorldZoneSimulationSchedulePolicy.ResolveInterval(runState));
        Assert.False(WorldZoneSimulationSchedulePolicy.IsDue(
            runState,
            DateTimeOffset.UtcNow,
            null));
    }

    [Theory]
    [InlineData("active")]
    [InlineData("reduced")]
    public void RunningZoneWithoutPriorCompletionIsImmediatelyDue(
        string runState)
    {
        Assert.True(WorldZoneSimulationSchedulePolicy.IsDue(
            runState,
            DateTimeOffset.UtcNow,
            null));
    }

    [Fact]
    public void ExecutionLeaseUsesObservedEvaluationDurationWithSafetyMargin()
    {
        var duration =
            WorldZoneSimulationSchedulePolicy.ResolveExecutionLeaseDuration(
                TimeSpan.FromSeconds(1),
                previousEvaluationDurationMilliseconds: 15_000d);

        Assert.Equal(TimeSpan.FromSeconds(17), duration);
        Assert.True(duration > TimeSpan.FromSeconds(15));
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(-1d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void ExecutionLeaseHasSafeMinimumWithoutUsableMeasurement(
        double previousEvaluationDurationMilliseconds)
    {
        Assert.Equal(
            WorldZoneSimulationSchedulePolicy.MinimumExecutionLeaseDuration,
            WorldZoneSimulationSchedulePolicy.ResolveExecutionLeaseDuration(
                TimeSpan.FromSeconds(1),
                previousEvaluationDurationMilliseconds));
    }

    [Theory]
    [InlineData("not_due")]
    [InlineData("execution_lease_not_acquired")]
    [InlineData("stale_world_revision")]
    public void DeferredObservationPreservesLastCompletedEvaluation(
        string scheduleStatus)
    {
        var worldId = Guid.NewGuid();
        var zone = new WorldZoneScheduleDefinition(
            worldId, "main", "active", -10d, -10d, 10d, 10d);
        var session = new ZoneSessionCounts(1, 1, 100L);
        var initial = WorldZoneRuntimeSnapshotPolicy.CreateDeferredSnapshot(
            null,
            zone,
            "active",
            session,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(10),
            "not_due",
            100L);
        var completed = initial with
        {
            EngineStatus = "inference_only",
            EvaluatedRuleBindingCount = 7,
            CoreEvaluatedRuleCount = 14,
            DatabaseLoadDurationMilliseconds = 2.5d,
            EvaluationDurationMilliseconds = 8.5d,
            EvaluatedWorldRevision = 27L,
            InferredFacts = new[]
            {
                new HeadlessInferredFact("actor", "can_move", "true")
            },
            EvaluationCompletedAtUnixMilliseconds = 90L,
            ScheduleStatus = "evaluated"
        };

        var deferred = WorldZoneRuntimeSnapshotPolicy.CreateDeferredSnapshot(
            completed,
            zone,
            "active",
            new ZoneSessionCounts(2, 2, 110L),
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(10),
            scheduleStatus,
            110L);

        Assert.Equal("inference_only", deferred.EngineStatus);
        Assert.Equal(7, deferred.EvaluatedRuleBindingCount);
        Assert.Equal(14, deferred.CoreEvaluatedRuleCount);
        Assert.Equal(2.5d, deferred.DatabaseLoadDurationMilliseconds);
        Assert.Equal(8.5d, deferred.EvaluationDurationMilliseconds);
        Assert.Equal(27L, deferred.EvaluatedWorldRevision);
        Assert.Single(deferred.InferredFacts);
        Assert.Equal(90L, deferred.EvaluationCompletedAtUnixMilliseconds);
        Assert.Equal(scheduleStatus, deferred.ScheduleStatus);
        Assert.Equal(2, deferred.ConnectionCount);
        Assert.False(deferred.ExecutionLeaseHeld);
    }
}
