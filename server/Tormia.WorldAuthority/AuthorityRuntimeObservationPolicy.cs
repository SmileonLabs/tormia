using Tormia.Ontology.Core;

internal static class AuthorityRuntimeObservationPolicy
{
    public static string? Validate(
        OntologyActionEffectDefinition definition,
        bool? groundedObservation)
    {
        if (definition.runtimeConstraints
                ?.requiresGroundedObservation == true &&
            groundedObservation != true)
        {
            return "grounded_observation_required";
        }

        return null;
    }
}
