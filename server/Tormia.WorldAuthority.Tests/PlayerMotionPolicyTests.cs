using Xunit;

public sealed class PlayerMotionPolicyTests
{
    [Theory]
    [InlineData(1.5f, 5d, 1.5d)]
    [InlineData(4f, 5d, 4d)]
    [InlineData(8f, 5d, 5d)]
    [InlineData(-1f, 5d, 0d)]
    public void RequestedLocomotionSpeedIsClampedByAuthoredMaximum(
        float requestedSpeed,
        double authoredMaximumSpeed,
        double expected)
    {
        Assert.Equal(
            expected,
            WorldPlayerMotionPolicy.ResolveAcceptedSpeed(
                requestedSpeed,
                authoredMaximumSpeed));
    }

    [Fact]
    public void InvalidOrRemovedMovementSpeedDisablesMovement()
    {
        Assert.Equal(
            0d,
            WorldPlayerMotionPolicy.ResolveAcceptedSpeed(float.NaN, 5d));
        Assert.Equal(
            0d,
            WorldPlayerMotionPolicy.ResolveAcceptedSpeed(4f, double.NaN));
        Assert.Equal(
            0d,
            WorldPlayerMotionPolicy.ResolveAcceptedSpeed(4f, 0d));
    }

    [Fact]
    public async Task ClearingIntentAllowsANewSessionSequence()
    {
        var registry = new InMemoryWorldPlayerIntentRegistry();
        var worldId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var avatarId = Guid.NewGuid();
        var oldRuntimeSessionId = Guid.NewGuid();
        var oldSession = new WorldPlayerIntent(
            worldId,
            userId,
            avatarId,
            "world_main",
            6000,
            1f,
            0f,
            4f,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            RuntimeSessionId: oldRuntimeSessionId,
            WorldRevision: 1,
            WriterEpoch: 1);

        Assert.True((await registry.InitializeHttpWriter(
            new PlayerMotionTransportWriterState(
                worldId, userId, avatarId, "world_main",
                oldRuntimeSessionId, "http", 1, 1),
            CancellationToken.None)).Accepted);

        Assert.True(await registry.Submit(oldSession, CancellationToken.None));
        await registry.Clear(worldId, avatarId, CancellationToken.None);

        var newRuntimeSessionId = Guid.NewGuid();
        Assert.True((await registry.InitializeHttpWriter(
            new PlayerMotionTransportWriterState(
                worldId, userId, avatarId, "world_main",
                newRuntimeSessionId, "http", 1, 1),
            CancellationToken.None)).Accepted);
        var newSession = oldSession with
        {
            Sequence = 1,
            RuntimeSessionId = newRuntimeSessionId
        };
        Assert.True(await registry.Submit(newSession, CancellationToken.None));
        Assert.Equal(
            1,
            (await registry.Get(
                worldId,
                avatarId,
                CancellationToken.None))?.Sequence);
    }

    [Theory]
    [InlineData(0d, 0d, -10d, -10d, 10d, 10d, true)]
    [InlineData(-10d, 10d, -10d, -10d, 10d, 10d, true)]
    [InlineData(10.01d, 0d, -10d, -10d, 10d, 10d, false)]
    public void CollisionResolvedPoseMustRemainInsideAuthoredZone(
        double x,
        double z,
        double minimumX,
        double minimumZ,
        double maximumX,
        double maximumZ,
        bool expected)
    {
        Assert.Equal(
            expected,
            WorldPlayerMotionPolicy.IsInsideZone(
                x,
                z,
                minimumX,
                minimumZ,
                maximumX,
                maximumZ));
    }

    [Fact]
    public void AuthoredCapsuleCollisionProxyBuildsWithoutVisualFallback()
    {
        var configuration = CreateProxyConfiguration(
            role: "ActorBody",
            shape: "Capsule",
            radius: 0.45d,
            height: 2d,
            centerOffsetY: 1d);

        Assert.True(
            WorldCollisionProxyPolicy.TryCreate(
                configuration,
                out var proxy,
                out var rejectionCode),
            rejectionCode);
        Assert.NotNull(proxy);
        Assert.Equal(configuration.PositionY + 1d, proxy.CenterY);
        Assert.Equal(0.45d, proxy.Radius);
        Assert.Equal(2d, proxy.Height);
    }

    [Fact]
    public void RemovingRequiredCollisionProxyDimensionFailsClosed()
    {
        var configuration = CreateProxyConfiguration(
            role: "ActorBody",
            shape: "Capsule",
            radius: 0.45d,
            height: null,
            centerOffsetY: 1d);

        Assert.False(
            WorldCollisionProxyPolicy.TryCreate(
                configuration,
                out var proxy,
                out var rejectionCode));
        Assert.Null(proxy);
        Assert.Equal(
            "invalid_collision_proxy_capsule_dimensions",
            rejectionCode);
    }

    [Fact]
    public void AuthoredBoxProxySupportsReusableUgcCollisionMeaning()
    {
        var configuration = CreateProxyConfiguration(
            role: "WalkableSupport",
            shape: "Box",
            sizeX: 4d,
            sizeY: 1d,
            sizeZ: 6d);

        Assert.True(
            WorldCollisionProxyPolicy.TryCreate(
                configuration,
                out var proxy,
                out var rejectionCode),
            rejectionCode);
        Assert.NotNull(proxy);
        Assert.Equal(4d, proxy.SizeX);
        Assert.Equal(6d, proxy.SizeZ);
    }

    [Fact]
    public void ZoneValidationUsesWholeProxyInsteadOfOnlyItsCenter()
    {
        Assert.True(
            WorldCollisionProxyPolicy.TryCreate(
                CreateProxyConfiguration(
                    role: "ActorBody",
                    shape: "Capsule",
                    radius: 0.45d,
                    height: 2d),
                out var proxy,
                out var rejectionCode),
            rejectionCode);
        Assert.NotNull(proxy);
        Assert.True(
            WorldCollisionProxyPolicy.IsFullyInsideZone(
                proxy,
                -10d,
                -10d,
                10d,
                10d));

        var nearEdge = proxy with { CenterX = 9.8d };
        Assert.False(
            WorldCollisionProxyPolicy.IsFullyInsideZone(
                nearEdge,
                -10d,
                -10d,
                10d,
                10d));
    }

    [Fact]
    public void FixedTickNormalizesDirectionAndUsesAuthoredSpeed()
    {
        var state = CreateMotionState();
        var configuration = CreateMotionConfiguration();
        var proxyConfiguration = CreateProxyConfiguration(
            role: "ActorBody",
            shape: "Capsule",
            radius: 0.45d,
            height: 2d,
            centerOffsetY: 1d);
        proxyConfiguration = proxyConfiguration with
        {
            WorldId = state.WorldId,
            EntityId = state.AvatarEntityId,
            PositionY = state.PositionY
        };
        Assert.True(
            WorldCollisionProxyPolicy.TryCreate(
                proxyConfiguration,
                out var proxy,
                out var rejectionCode),
            rejectionCode);

        var next = WorldPlayerMotionPolicy.ResolveAuthoritativeStep(
            state,
            new WorldPlayerIntent(
                state.WorldId,
                Guid.NewGuid(),
                state.AvatarEntityId,
                state.ZoneKey,
                1,
                1f,
                1f,
                100f,
                1),
            configuration,
            proxyConfiguration,
            proxy!,
            new[] { CreateGroundSupport(state) },
            CreateZone(state.WorldId),
            null,
            0.05d,
            2);

        Assert.Equal(0.25d, Math.Sqrt(
            next.PositionX * next.PositionX +
            next.PositionZ * next.PositionZ), 6);
        Assert.Equal(5d, Math.Sqrt(
            next.VelocityX * next.VelocityX +
            next.VelocityZ * next.VelocityZ), 6);
        Assert.Equal("moving", next.MotionStatus);
    }

    [Fact]
    public void FixedTickKeepsWholeActorProxyInsideZone()
    {
        var state = CreateMotionState() with { PositionX = 9.5d };
        var configuration = CreateMotionConfiguration();
        var proxyConfiguration = CreateProxyConfiguration(
            role: "ActorBody",
            shape: "Capsule",
            radius: 0.45d,
            height: 2d,
            centerOffsetY: 1d) with
        {
            WorldId = state.WorldId,
            EntityId = state.AvatarEntityId,
            PositionX = 9.5d,
            PositionY = state.PositionY
        };
        Assert.True(
            WorldCollisionProxyPolicy.TryCreate(
                proxyConfiguration,
                out var proxy,
                out var rejectionCode),
            rejectionCode);

        var next = WorldPlayerMotionPolicy.ResolveAuthoritativeStep(
            state,
            new WorldPlayerIntent(
                state.WorldId,
                Guid.NewGuid(),
                state.AvatarEntityId,
                state.ZoneKey,
                1,
                1f,
                0f,
                5f,
                1),
            configuration,
            proxyConfiguration,
            proxy!,
            new[] { CreateGroundSupport(state) },
            CreateZone(state.WorldId),
            null,
            0.05d,
            2);

        Assert.Equal(9.55d, next.PositionX, 6);
        var placed = WorldCollisionProxyPolicy.PlaceAtEntityPosition(
            proxy!,
            proxyConfiguration,
            next.PositionX,
            next.PositionY,
            next.PositionZ);
        Assert.True(WorldCollisionProxyPolicy.IsFullyInsideZone(
            placed,
            -10d,
            -10d,
            10d,
            10d));
    }

    [Fact]
    public void FixedTickCannotPassThroughAuthoredDynamicProp()
    {
        var state = CreateMotionState();
        var configuration = CreateMotionConfiguration();
        var actorConfiguration = CreateProxyConfiguration(
            role: "ActorBody",
            shape: "Capsule",
            radius: 0.45d,
            height: 2d,
            centerOffsetY: 1d) with
        {
            WorldId = state.WorldId,
            EntityId = state.AvatarEntityId,
            PositionY = state.PositionY
        };
        Assert.True(
            WorldCollisionProxyPolicy.TryCreate(
                actorConfiguration,
                out var actorProxy,
                out var actorRejection),
            actorRejection);
        var obstacleConfiguration = CreateProxyConfiguration(
            role: "DynamicProp",
            shape: "Box",
            sizeX: 0.2d,
            sizeY: 2d,
            sizeZ: 2d) with
        {
            WorldId = state.WorldId,
            PositionX = 0.7d,
            PositionY = 1d
        };
        Assert.True(
            WorldCollisionProxyPolicy.TryCreate(
                obstacleConfiguration,
                out var obstacle,
                out var obstacleRejection),
            obstacleRejection);

        var next = WorldPlayerMotionPolicy.ResolveAuthoritativeStep(
            state,
            new WorldPlayerIntent(
                state.WorldId,
                Guid.NewGuid(),
                state.AvatarEntityId,
                state.ZoneKey,
                1,
                1f,
                0f,
                5f,
                1),
            configuration,
            actorConfiguration,
            actorProxy!,
            new[] { CreateGroundSupport(state), obstacle! },
            CreateZone(state.WorldId),
            null,
            0.1d,
            2);

        Assert.True(next.PositionX < 0.3d);
    }

    [Fact]
    public void AcceptedAuthoredJumpOccurrenceIntegratesAndLandsOnce()
    {
        var state = CreateMotionState();
        var configuration = CreateMotionConfiguration();
        var proxyConfiguration = CreateProxyConfiguration(
            role: "ActorBody",
            shape: "Capsule",
            radius: 0.45d,
            height: 2d,
            centerOffsetY: 1d) with
        {
            WorldId = state.WorldId,
            EntityId = state.AvatarEntityId,
            PositionY = state.PositionY
        };
        Assert.True(
            WorldCollisionProxyPolicy.TryCreate(
                proxyConfiguration,
                out var proxy,
                out var rejectionCode),
            rejectionCode);
        var occurrence = Guid.NewGuid();
        var next = WorldPlayerMotionPolicy.ResolveAuthoritativeStep(
            state,
            null,
            configuration,
            proxyConfiguration,
            proxy!,
            new[] { CreateGroundSupport(state) },
            CreateZone(state.WorldId),
            occurrence,
            0.05d,
            2);
        Assert.False(next.Grounded);
        Assert.True(next.PositionY > state.PositionY);
        Assert.Equal(occurrence, next.LastProcessedRuntimeActionOccurrenceId);

        for (var index = 0; index < 30; index++)
        {
            next = WorldPlayerMotionPolicy.ResolveAuthoritativeStep(
                next,
                null,
                configuration,
                proxyConfiguration,
                proxy!,
                new[] { CreateGroundSupport(state) },
                CreateZone(state.WorldId),
                null,
                0.05d,
                3 + index);
        }
        Assert.True(next.Grounded);
        Assert.Equal(state.GroundReferenceY, next.PositionY, 6);
    }

    [Fact]
    public void RemovingJumpTuningPreventsAcceptedOccurrenceFromTakingOff()
    {
        var state = CreateMotionState();
        var configuration = CreateMotionConfiguration() with
        {
            JumpTakeoffSpeed = null
        };
        var proxyConfiguration = CreateProxyConfiguration(
            role: "ActorBody",
            shape: "Capsule",
            radius: 0.45d,
            height: 2d,
            centerOffsetY: 1d) with
        {
            WorldId = state.WorldId,
            EntityId = state.AvatarEntityId,
            PositionY = state.PositionY
        };
        Assert.True(
            WorldCollisionProxyPolicy.TryCreate(
                proxyConfiguration,
                out var proxy,
                out var rejectionCode),
            rejectionCode);

        var next = WorldPlayerMotionPolicy.ResolveAuthoritativeStep(
            state,
            null,
            configuration,
            proxyConfiguration,
            proxy!,
            new[] { CreateGroundSupport(state) },
            CreateZone(state.WorldId),
            Guid.NewGuid(),
            0.05d,
            2);

        Assert.True(next.Grounded);
        Assert.Equal(state.PositionY, next.PositionY, 6);
    }

    [Fact]
    public void RemovingAuthoredWalkableSupportRemovesGrounding()
    {
        var state = CreateMotionState() with
        {
            GroundSupportEntityId = Guid.NewGuid()
        };
        var configuration = CreateMotionConfiguration();
        var proxyConfiguration = CreateProxyConfiguration(
            role: "ActorBody",
            shape: "Capsule",
            radius: 0.45d,
            height: 2d,
            centerOffsetY: 1d) with
        {
            WorldId = state.WorldId,
            EntityId = state.AvatarEntityId,
            PositionY = state.PositionY
        };
        Assert.True(
            WorldCollisionProxyPolicy.TryCreate(
                proxyConfiguration,
                out var proxy,
                out var rejectionCode),
            rejectionCode);

        var next = WorldPlayerMotionPolicy.ResolveAuthoritativeStep(
            state,
            null,
            configuration,
            proxyConfiguration,
            proxy!,
            Array.Empty<WorldCollisionProxy>(),
            CreateZone(state.WorldId),
            null,
            0.05d,
            2);

        Assert.False(next.Grounded);
        Assert.Null(next.GroundSupportEntityId);
        Assert.Equal(0d, next.GroundReferenceY);
        Assert.True(next.PositionY < state.PositionY);
    }

    [Fact]
    public void AuthoredGroundClearanceDefinesAuthorityRootHeight()
    {
        var state = CreateMotionState();
        var configuration = CreateMotionConfiguration() with
        {
            GroundClearance = 0.03d
        };
        var proxyConfiguration = CreateProxyConfiguration(
            role: "ActorBody",
            shape: "Capsule",
            radius: 0.45d,
            height: 2d,
            centerOffsetY: 1d) with
        {
            WorldId = state.WorldId,
            EntityId = state.AvatarEntityId,
            PositionY = state.PositionY
        };
        Assert.True(
            WorldCollisionProxyPolicy.TryCreate(
                proxyConfiguration,
                out var proxy,
                out var rejectionCode),
            rejectionCode);

        var support = CreateGroundSupport(state);
        var next = WorldPlayerMotionPolicy.ResolveAuthoritativeStep(
            state,
            null,
            configuration,
            proxyConfiguration,
            proxy!,
            new[] { support },
            CreateZone(state.WorldId),
            null,
            0.05d,
            2);

        Assert.True(next.Grounded);
        Assert.Equal(1.03d, next.PositionY, 6);
        Assert.Equal(support.EntityId, next.GroundSupportEntityId);
    }

    [Fact]
    public async Task ClientPoseObservationCannotOverwriteAuthorityMotion()
    {
        var motion = new InMemoryWorldPlayerMotionRuntimeRegistry();
        var observations =
            new InMemoryWorldPlayerPoseObservationRegistry();
        var state = CreateMotionState();
        await motion.Set(state, CancellationToken.None);

        Assert.True(await observations.SubmitLatest(
            new WorldPlayerPoseObservation(
                state.WorldId,
                state.AvatarEntityId,
                state.ZoneKey,
                1,
                1,
                999d,
                999d,
                999d,
                "moving",
                1),
            CancellationToken.None));

        var authoritative = await motion.Get(
            state.WorldId,
            state.AvatarEntityId,
            CancellationToken.None);
        Assert.NotNull(authoritative);
        Assert.Equal(state.PositionX, authoritative.PositionX);
        Assert.Equal(state.PositionY, authoritative.PositionY);
        Assert.Equal(state.PositionZ, authoritative.PositionZ);
    }

    [Fact]
    public async Task PoseObservationRegistryRejectsStaleSequence()
    {
        var registry =
            new InMemoryWorldPlayerPoseObservationRegistry();
        var state = CreateMotionState();
        var newest = new WorldPlayerPoseObservation(
            state.WorldId,
            state.AvatarEntityId,
            state.ZoneKey,
            1,
            2,
            2d,
            1d,
            0d,
            "moving",
            2);
        Assert.True(await registry.SubmitLatest(
            newest,
            CancellationToken.None));
        Assert.False(await registry.SubmitLatest(
            newest with
            {
                PoseSequence = 1,
                PositionX = 99d
            },
            CancellationToken.None));

        var stored = await registry.Get(
            state.WorldId,
            state.AvatarEntityId,
            CancellationToken.None);
        Assert.NotNull(stored);
        Assert.Equal(2, stored.PoseSequence);
        Assert.Equal(2d, stored.PositionX);
    }

    private static WorldCollisionProxyConfiguration
        CreateProxyConfiguration(
            string role,
            string shape,
            double? radius = null,
            double? height = null,
            double? sizeX = null,
            double? sizeY = null,
            double? sizeZ = null,
            double? centerOffsetY = null)
    {
        return new WorldCollisionProxyConfiguration(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "world_main",
            0d,
            1d,
            0d,
            role,
            shape,
            radius,
            height,
            sizeX,
            sizeY,
            sizeZ,
            0d,
            centerOffsetY,
            0d);
    }

    private static WorldPlayerMotionState CreateMotionState()
    {
        return new WorldPlayerMotionState(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "world_main",
            0d,
            1d,
            0d,
            0,
            "idle",
            1,
            0d,
            -1d,
            0d,
            1d,
            true,
            0,
            null);
    }

    private static WorldPlayerAvatarMotionConfiguration
        CreateMotionConfiguration()
    {
        return new WorldPlayerAvatarMotionConfiguration(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "world_main",
            0d,
            1d,
            0d,
            5d,
            "jump_avatar",
            -20d,
            6d,
            -1d,
            0.3d,
            0d,
            "development_core",
            "1.0.0",
            "move_avatar",
            1);
    }

    private static WorldCollisionProxy CreateGroundSupport(
        WorldPlayerMotionState state)
    {
        var configuration = CreateProxyConfiguration(
            role: "WalkableSupport",
            shape: "Box",
            sizeX: 20d,
            sizeY: 1d,
            sizeZ: 20d) with
        {
            WorldId = state.WorldId,
            ZoneKey = state.ZoneKey,
            PositionY = 0.5d
        };
        Assert.True(
            WorldCollisionProxyPolicy.TryCreate(
                configuration,
                out var support,
                out var rejectionCode),
            rejectionCode);
        return support!;
    }

    private static WorldZoneScheduleDefinition CreateZone(Guid worldId)
    {
        return new WorldZoneScheduleDefinition(
            worldId,
            "world_main",
            "active",
            -10d,
            -10d,
            10d,
            10d);
    }
}
