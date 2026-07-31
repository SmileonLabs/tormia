using System.Globalization;
using Tormia.Ontology.Core;

internal static class AuthoritativeActionRuntimeConstraintEvaluator
{
    public static string? Validate(
        OntologyActionEffectDefinition definition,
        AuthoritySpatialPosition? actorPosition,
        AuthoritySpatialPosition? targetPosition)
    {
        return Validate(
            definition,
            actorPosition,
            targetPosition,
            Guid.Empty,
            Guid.Empty,
            null,
            Array.Empty<AuthorityFactSnapshot>(),
            out _);
    }

    public static string? Validate(
        OntologyActionEffectDefinition definition,
        AuthoritySpatialPosition? actorPosition,
        AuthoritySpatialPosition? targetPosition,
        Guid actorEntityId,
        Guid targetEntityId,
        Guid? toolEntityId,
        IReadOnlyList<AuthorityFactSnapshot> facts,
        out TimeSpan cooldown)
    {
        cooldown = TimeSpan.Zero;
        var constraints = definition.runtimeConstraints;
        if (constraints is null) return null;

        var maximumDistance = constraints.maxActorTargetDistance;
        if (AuthoritativeActionEvaluator.HasConfiguredNumericSource(
                constraints.maxActorTargetDistanceFrom))
        {
            var sourceRejection = TryResolvePositiveSource(
                constraints.maxActorTargetDistanceFrom,
                actorEntityId,
                targetEntityId,
                toolEntityId,
                facts,
                out maximumDistance);
            if (sourceRejection is not null) return sourceRejection;
        }

        if (AuthoritativeActionEvaluator.HasConfiguredNumericSource(
                constraints.cooldownSecondsFrom))
        {
            var sourceRejection = TryResolvePositiveSource(
                constraints.cooldownSecondsFrom,
                actorEntityId,
                targetEntityId,
                toolEntityId,
                facts,
                out var cooldownSeconds);
            if (sourceRejection is not null) return sourceRejection;
            cooldown = TimeSpan.FromSeconds(cooldownSeconds);
        }

        if (maximumDistance <= 0d) return null;
        if (actorPosition is null)
            return "action_actor_runtime_position_unavailable";
        if (targetPosition is null)
            return "action_target_position_unavailable";

        var deltaX = actorPosition.X - targetPosition.X;
        var deltaY = actorPosition.Y - targetPosition.Y;
        var deltaZ = actorPosition.Z - targetPosition.Z;
        var squaredDistance =
            deltaX * deltaX + deltaY * deltaY + deltaZ * deltaZ;
        return squaredDistance <= maximumDistance * maximumDistance
            ? null
            : "action_target_out_of_range";
    }

    private static string? TryResolvePositiveSource(
        OntologyNumericFactSource source,
        Guid actorEntityId,
        Guid targetEntityId,
        Guid? toolEntityId,
        IReadOnlyList<AuthorityFactSnapshot> facts,
        out double value)
    {
        value = 0d;
        var subject = source.subject switch
        {
            "?actor" => actorEntityId,
            "?target" => targetEntityId,
            "?tool" => toolEntityId ?? Guid.Empty,
            _ => Guid.Empty
        };
        if (subject == Guid.Empty)
            return "action_runtime_constraint_subject_unavailable";
        var matches = facts
            .Where(fact =>
                fact.SubjectEntityId == subject &&
                string.Equals(
                    fact.PredicateId,
                    source.predicate,
                    StringComparison.Ordinal))
            .ToArray();
        if (matches.Length != 1 ||
            !double.TryParse(
                matches[0].ObjectValue,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var authored) ||
            !double.IsFinite(authored))
        {
            return "action_runtime_constraint_source_missing";
        }

        value = authored * source.multiplier;
        return value > 0d && double.IsFinite(value)
            ? null
            : "action_runtime_constraint_source_invalid";
    }
}

internal sealed record AuthoritySpatialPosition(double X, double Y, double Z);
