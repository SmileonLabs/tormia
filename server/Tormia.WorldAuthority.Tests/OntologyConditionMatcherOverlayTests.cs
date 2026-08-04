using Tormia.Ontology.Core;
using Xunit;

public sealed class OntologyConditionMatcherOverlayTests
{
    [Fact]
    public void OverlayBindsVariablesAcrossLaterBaseConditionsWithoutMutation()
    {
        var actor = (OntologyId)"overlay_actor";
        var target = (OntologyId)"overlay_target";
        var world = new OntologyWorldState();
        world.AddFact(actor, OntologyPredicates.HasConcept, "Actor");
        var overlay = new OntologyFact(
            actor, "request_intent", target);
        var conditions = new[]
        {
            OntologyCondition.Fact(
                "?requester", "request_intent", "?recipient"),
            OntologyCondition.HasConcept("?requester", "Actor")
        };

        var matches = OntologyConditionMatcher.Match(
            world,
            conditions,
            new Dictionary<string, OntologyId>(),
            overlay);

        var binding = Assert.Single(matches);
        Assert.Equal(actor, binding["?requester"]);
        Assert.Equal(target, binding["?recipient"]);
        Assert.False(world.HasFact(actor, "request_intent", target));
        Assert.Empty(OntologyConditionMatcher.Match(world, conditions));
    }

    [Fact]
    public void OverlayEqualToBaseFactDoesNotCreateAmbiguousBinding()
    {
        var actor = (OntologyId)"overlay_actor";
        var target = (OntologyId)"overlay_target";
        var world = new OntologyWorldState();
        world.AddFact(actor, "request_intent", target);
        var overlay = new OntologyFact(
            actor, "request_intent", target);

        var matches = OntologyConditionMatcher.Match(
            world,
            [OntologyCondition.Fact(
                "?requester", "request_intent", "?recipient")],
            new Dictionary<string, OntologyId>(),
            overlay);

        Assert.Single(matches);
    }
}
