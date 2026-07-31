/// <summary>
/// Durable, authored input used to construct a lightweight server collision
/// proxy. It is projected from world entity transforms plus ontology Facts;
/// Unity mesh and prefab names never participate.
/// </summary>
internal sealed record WorldCollisionProxyConfiguration(
    Guid WorldId,
    Guid EntityId,
    string ZoneKey,
    double PositionX,
    double PositionY,
    double PositionZ,
    string CollisionRole,
    string Shape,
    double? Radius,
    double? Height,
    double? SizeX,
    double? SizeY,
    double? SizeZ,
    double? CenterOffsetX,
    double? CenterOffsetY,
    double? CenterOffsetZ);

internal sealed record WorldCollisionProxy(
    Guid WorldId,
    Guid EntityId,
    string ZoneKey,
    string CollisionRole,
    string Shape,
    double CenterX,
    double CenterY,
    double CenterZ,
    double Radius,
    double Height,
    double SizeX,
    double SizeY,
    double SizeZ);

/// <summary>
/// Converts authored ontology data into bounded server geometry. Missing or
/// ambiguous dimensions fail closed; no visual-mesh fallback is permitted.
/// </summary>
internal static class WorldCollisionProxyPolicy
{
    private const double MaximumDimension = 1000d;
    private const double MaximumCenterOffset = 1000d;
    private static readonly HashSet<string> SupportedRoles =
        new(StringComparer.Ordinal)
        {
            "DynamicProp",
            "WalkableSupport",
            "ActorBody",
            "InteractionTrigger",
            "WaterVolume"
        };

    public static bool TryCreate(
        WorldCollisionProxyConfiguration configuration,
        out WorldCollisionProxy? proxy,
        out string rejectionCode)
    {
        proxy = null;
        rejectionCode = string.Empty;
        if (configuration.WorldId == Guid.Empty ||
            configuration.EntityId == Guid.Empty ||
            string.IsNullOrWhiteSpace(configuration.ZoneKey) ||
            !SupportedRoles.Contains(configuration.CollisionRole) ||
            !IsFinitePosition(configuration))
        {
            rejectionCode = "invalid_collision_proxy_identity";
            return false;
        }

        var offsetX = configuration.CenterOffsetX ?? 0d;
        var offsetY = configuration.CenterOffsetY ?? 0d;
        var offsetZ = configuration.CenterOffsetZ ?? 0d;
        if (!IsFiniteBoundedOffset(offsetX) ||
            !IsFiniteBoundedOffset(offsetY) ||
            !IsFiniteBoundedOffset(offsetZ))
        {
            rejectionCode = "invalid_collision_proxy_center";
            return false;
        }

        if (string.Equals(
                configuration.Shape,
                "Capsule",
                StringComparison.Ordinal))
        {
            var radius = configuration.Radius ?? 0d;
            var height = configuration.Height ?? 0d;
            if (!IsPositiveDimension(radius) ||
                !IsPositiveDimension(height) ||
                height < radius * 2d)
            {
                rejectionCode =
                    "invalid_collision_proxy_capsule_dimensions";
                return false;
            }

            proxy = new WorldCollisionProxy(
                configuration.WorldId,
                configuration.EntityId,
                configuration.ZoneKey,
                configuration.CollisionRole,
                configuration.Shape,
                configuration.PositionX + offsetX,
                configuration.PositionY + offsetY,
                configuration.PositionZ + offsetZ,
                radius,
                height,
                0d,
                0d,
                0d);
            return true;
        }

        if (string.Equals(
                configuration.Shape,
                "Box",
                StringComparison.Ordinal))
        {
            var sizeX = configuration.SizeX ?? 0d;
            var sizeY = configuration.SizeY ?? 0d;
            var sizeZ = configuration.SizeZ ?? 0d;
            if (!IsPositiveDimension(sizeX) ||
                !IsPositiveDimension(sizeY) ||
                !IsPositiveDimension(sizeZ))
            {
                rejectionCode =
                    "invalid_collision_proxy_box_dimensions";
                return false;
            }

            proxy = new WorldCollisionProxy(
                configuration.WorldId,
                configuration.EntityId,
                configuration.ZoneKey,
                configuration.CollisionRole,
                configuration.Shape,
                configuration.PositionX + offsetX,
                configuration.PositionY + offsetY,
                configuration.PositionZ + offsetZ,
                0d,
                0d,
                sizeX,
                sizeY,
                sizeZ);
            return true;
        }

        rejectionCode = "unsupported_collision_proxy_shape";
        return false;
    }

    public static bool IsFullyInsideZone(
        WorldCollisionProxy proxy,
        double minimumX,
        double minimumZ,
        double maximumX,
        double maximumZ)
    {
        if (proxy is null ||
            !double.IsFinite(minimumX) ||
            !double.IsFinite(minimumZ) ||
            !double.IsFinite(maximumX) ||
            !double.IsFinite(maximumZ) ||
            maximumX < minimumX ||
            maximumZ < minimumZ)
        {
            return false;
        }

        var extentX = string.Equals(
                proxy.Shape,
                "Capsule",
                StringComparison.Ordinal)
            ? proxy.Radius
            : proxy.SizeX * 0.5d;
        var extentZ = string.Equals(
                proxy.Shape,
                "Capsule",
                StringComparison.Ordinal)
            ? proxy.Radius
            : proxy.SizeZ * 0.5d;
        return proxy.CenterX - extentX >= minimumX &&
               proxy.CenterX + extentX <= maximumX &&
               proxy.CenterZ - extentZ >= minimumZ &&
               proxy.CenterZ + extentZ <= maximumZ;
    }

    public static WorldCollisionProxy PlaceAtEntityPosition(
        WorldCollisionProxy proxyTemplate,
        WorldCollisionProxyConfiguration configuration,
        double positionX,
        double positionY,
        double positionZ)
    {
        return proxyTemplate with
        {
            CenterX =
                positionX +
                (proxyTemplate.CenterX - configuration.PositionX),
            CenterY =
                positionY +
                (proxyTemplate.CenterY - configuration.PositionY),
            CenterZ =
                positionZ +
                (proxyTemplate.CenterZ - configuration.PositionZ)
        };
    }

    public static bool TryClampEntityPositionInsideZone(
        WorldCollisionProxy proxyTemplate,
        WorldCollisionProxyConfiguration configuration,
        double desiredPositionX,
        double desiredPositionZ,
        double minimumX,
        double minimumZ,
        double maximumX,
        double maximumZ,
        out double resolvedPositionX,
        out double resolvedPositionZ)
    {
        resolvedPositionX = desiredPositionX;
        resolvedPositionZ = desiredPositionZ;
        var extentX = ResolveExtentX(proxyTemplate);
        var extentZ = ResolveExtentZ(proxyTemplate);
        var offsetX =
            proxyTemplate.CenterX - configuration.PositionX;
        var offsetZ =
            proxyTemplate.CenterZ - configuration.PositionZ;
        var minimumEntityX = minimumX + extentX - offsetX;
        var maximumEntityX = maximumX - extentX - offsetX;
        var minimumEntityZ = minimumZ + extentZ - offsetZ;
        var maximumEntityZ = maximumZ - extentZ - offsetZ;
        if (!double.IsFinite(desiredPositionX) ||
            !double.IsFinite(desiredPositionZ) ||
            maximumEntityX < minimumEntityX ||
            maximumEntityZ < minimumEntityZ)
        {
            return false;
        }

        resolvedPositionX = Math.Clamp(
            desiredPositionX,
            minimumEntityX,
            maximumEntityX);
        resolvedPositionZ = Math.Clamp(
            desiredPositionZ,
            minimumEntityZ,
            maximumEntityZ);
        return true;
    }

    public static bool BlocksActorMotion(
        WorldCollisionProxy movingActor,
        WorldCollisionProxy obstacle)
    {
        if (movingActor is null ||
            obstacle is null ||
            movingActor.EntityId == obstacle.EntityId ||
            !IsSolidActorObstacle(obstacle.CollisionRole) ||
            !OverlapsVertically(movingActor, obstacle))
        {
            return false;
        }

        if (string.Equals(
                movingActor.Shape,
                "Capsule",
                StringComparison.Ordinal))
        {
            return string.Equals(
                    obstacle.Shape,
                    "Capsule",
                    StringComparison.Ordinal)
                ? CircleOverlapsCircle(
                    movingActor.CenterX,
                    movingActor.CenterZ,
                    movingActor.Radius,
                    obstacle.CenterX,
                    obstacle.CenterZ,
                    obstacle.Radius)
                : CircleOverlapsBox(
                    movingActor.CenterX,
                    movingActor.CenterZ,
                    movingActor.Radius,
                    obstacle.CenterX,
                    obstacle.CenterZ,
                    obstacle.SizeX,
                    obstacle.SizeZ);
        }

        if (string.Equals(
                obstacle.Shape,
                "Capsule",
                StringComparison.Ordinal))
        {
            return CircleOverlapsBox(
                obstacle.CenterX,
                obstacle.CenterZ,
                obstacle.Radius,
                movingActor.CenterX,
                movingActor.CenterZ,
                movingActor.SizeX,
                movingActor.SizeZ);
        }

        return Math.Abs(movingActor.CenterX - obstacle.CenterX) <
                   (movingActor.SizeX + obstacle.SizeX) * 0.5d &&
               Math.Abs(movingActor.CenterZ - obstacle.CenterZ) <
                   (movingActor.SizeZ + obstacle.SizeZ) * 0.5d;
    }

    /// <summary>
    /// Resolves the highest explicitly authored flat support whose Box fully
    /// contains the moving actor footprint. Meshes, scene names, and durable
    /// spawn height never participate. The caller supplies the permitted root
    /// elevation interval from authored movement tuning.
    /// </summary>
    public static bool TryResolveHighestWalkableSupport(
        WorldCollisionProxy actorProxyTemplate,
        WorldCollisionProxyConfiguration actorConfiguration,
        double actorPositionX,
        double actorPositionZ,
        double minimumRootY,
        double maximumRootY,
        double groundClearance,
        IReadOnlyList<WorldCollisionProxy> collisionProxies,
        out WorldCollisionProxy? support,
        out double resolvedRootY)
    {
        support = null;
        resolvedRootY = 0d;
        if (actorProxyTemplate is null ||
            actorConfiguration is null ||
            collisionProxies is null ||
            !double.IsFinite(actorPositionX) ||
            !double.IsFinite(actorPositionZ) ||
            !double.IsFinite(minimumRootY) ||
            !double.IsFinite(maximumRootY) ||
            !double.IsFinite(groundClearance) ||
            groundClearance < 0d ||
            maximumRootY < minimumRootY)
        {
            return false;
        }

        var actorCenterX =
            actorPositionX +
            (actorProxyTemplate.CenterX -
             actorConfiguration.PositionX);
        var actorCenterZ =
            actorPositionZ +
            (actorProxyTemplate.CenterZ -
             actorConfiguration.PositionZ);
        var actorExtentX = ResolveExtentX(actorProxyTemplate);
        var actorExtentZ = ResolveExtentZ(actorProxyTemplate);
        foreach (var candidate in collisionProxies)
        {
            if (candidate is null ||
                candidate.EntityId == actorProxyTemplate.EntityId ||
                candidate.WorldId != actorProxyTemplate.WorldId ||
                !string.Equals(
                    candidate.ZoneKey,
                    actorProxyTemplate.ZoneKey,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    candidate.CollisionRole,
                    "WalkableSupport",
                    StringComparison.Ordinal) ||
                !string.Equals(
                    candidate.Shape,
                    "Box",
                    StringComparison.Ordinal))
            {
                continue;
            }

            var contained =
                actorCenterX - actorExtentX >=
                    candidate.CenterX - candidate.SizeX * 0.5d &&
                actorCenterX + actorExtentX <=
                    candidate.CenterX + candidate.SizeX * 0.5d &&
                actorCenterZ - actorExtentZ >=
                    candidate.CenterZ - candidate.SizeZ * 0.5d &&
                actorCenterZ + actorExtentZ <=
                    candidate.CenterZ + candidate.SizeZ * 0.5d;
            if (!contained)
            {
                continue;
            }

            var rootY =
                candidate.CenterY +
                candidate.SizeY * 0.5d +
                groundClearance;
            if (rootY < minimumRootY ||
                rootY > maximumRootY ||
                (support != null && rootY <= resolvedRootY))
            {
                continue;
            }

            support = candidate;
            resolvedRootY = rootY;
        }

        return support != null;
    }

    private static bool IsFinitePosition(
        WorldCollisionProxyConfiguration value) =>
        double.IsFinite(value.PositionX) &&
        double.IsFinite(value.PositionY) &&
        double.IsFinite(value.PositionZ);

    private static bool IsPositiveDimension(double value) =>
        double.IsFinite(value) &&
        value > 0d &&
        value <= MaximumDimension;

    private static bool IsFiniteBoundedOffset(double value) =>
        double.IsFinite(value) &&
        Math.Abs(value) <= MaximumCenterOffset;

    private static double ResolveExtentX(WorldCollisionProxy proxy) =>
        string.Equals(
            proxy.Shape,
            "Capsule",
            StringComparison.Ordinal)
            ? proxy.Radius
            : proxy.SizeX * 0.5d;

    private static double ResolveExtentY(WorldCollisionProxy proxy) =>
        string.Equals(
            proxy.Shape,
            "Capsule",
            StringComparison.Ordinal)
            ? proxy.Height * 0.5d
            : proxy.SizeY * 0.5d;

    private static double ResolveExtentZ(WorldCollisionProxy proxy) =>
        string.Equals(
            proxy.Shape,
            "Capsule",
            StringComparison.Ordinal)
            ? proxy.Radius
            : proxy.SizeZ * 0.5d;

    private static bool IsSolidActorObstacle(string collisionRole) =>
        string.Equals(
            collisionRole,
            "ActorBody",
            StringComparison.Ordinal) ||
        string.Equals(
            collisionRole,
            "DynamicProp",
            StringComparison.Ordinal);

    private static bool OverlapsVertically(
        WorldCollisionProxy first,
        WorldCollisionProxy second)
    {
        return Math.Abs(first.CenterY - second.CenterY) <
               ResolveExtentY(first) + ResolveExtentY(second);
    }

    private static bool CircleOverlapsCircle(
        double firstX,
        double firstZ,
        double firstRadius,
        double secondX,
        double secondZ,
        double secondRadius)
    {
        var deltaX = firstX - secondX;
        var deltaZ = firstZ - secondZ;
        var radius = firstRadius + secondRadius;
        return deltaX * deltaX + deltaZ * deltaZ <
               radius * radius;
    }

    private static bool CircleOverlapsBox(
        double circleX,
        double circleZ,
        double circleRadius,
        double boxX,
        double boxZ,
        double boxSizeX,
        double boxSizeZ)
    {
        var closestX = Math.Clamp(
            circleX,
            boxX - boxSizeX * 0.5d,
            boxX + boxSizeX * 0.5d);
        var closestZ = Math.Clamp(
            circleZ,
            boxZ - boxSizeZ * 0.5d,
            boxZ + boxSizeZ * 0.5d);
        var deltaX = circleX - closestX;
        var deltaZ = circleZ - closestZ;
        return deltaX * deltaX + deltaZ * deltaZ <
               circleRadius * circleRadius;
    }
}
