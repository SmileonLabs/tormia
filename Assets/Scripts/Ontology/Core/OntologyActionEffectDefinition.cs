using System;
using System.Collections.Generic;

namespace Tormia.Ontology.Core
{
    [Serializable]
    public sealed class OntologyActionEffectDefinition
    {
        public string actionVerb;
        // Legacy single-effect fields are retained so existing action assets remain compatible.
        public string subjectPattern = "?actor";
        public string predicate;
        public string objectPattern = "?target";
        public bool requiresTool;
        // Explicitly permits a Rule invocation whose successful result is only
        // an Authority decision (for example target acquisition or chase
        // permission) and therefore produces no durable mutation or
        // presentation intent. This must never be inferred from an empty
        // effect list.
        public bool evaluationOnly;
        public List<OntologyCondition> conditions = new();
        public List<OntologyEffect> effects = new();
        // Optional Rule-Block-owned command path. The action transports an
        // ephemeral intent; the exact Rule Block assigned to the target owns
        // and evaluates the durable result.
        public OntologyActionRuleInvocationDefinition ruleInvocation;
        // Optional Rule Blocks evaluated after the primary mutations have been
        // applied inside the same Authority transaction. This separates
        // lifecycle reactions (for example defeat -> loot availability) from
        // the action that caused the state transition. A missing optional
        // binding disables only that follow-up behavior.
        public List<OntologyActionRuleInvocationDefinition>
            postRuleInvocations = new();
        public OntologyActionPresentationDefinition presentation = new();
        public OntologyActionRuntimeConstraints runtimeConstraints = new();
    }

    [Serializable]
    public sealed class OntologyActionRuleInvocationDefinition
    {
        public string ruleId;
        public string bindingVariable = "?target";
        // Entity whose durable Rule Block assignment authorizes the behavior.
        // Equipment actions bind on ?target; weapon attacks bind on ?tool.
        public string bindingEntityPattern = "?target";
        public string intentSubjectPattern = "?actor";
        public string intentPredicate = OntologyPredicates.InteractionIntent;
        public string intentObjectPattern = "?target";
        public bool required = true;
    }

    /// <summary>
    /// Canonical, non-durable presentation intents emitted only after the
    /// Authority accepts the action. These values select adapter data; they do
    /// not name Animator states, clips, meshes, or prefabs.
    /// </summary>
    [Serializable]
    public sealed class OntologyActionPresentationDefinition
    {
        public string actorAnimationIntent = string.Empty;
    }

    /// <summary>
    /// Generic, non-durable runtime observations that Authority must validate
    /// before applying durable effects. A value of zero disables the matching
    /// constraint. The observations themselves are never written as world Facts.
    /// </summary>
    [Serializable]
    public sealed class OntologyActionRuntimeConstraints
    {
        public double maxActorTargetDistance;
        // Requires the caller to supply a positive, non-durable support
        // observation. The observation authorizes this invocation only and is
        // never written as a world Fact.
        public bool requiresGroundedObservation;
        // Optional authored numeric Fact source. When configured it owns the
        // distance instead of the fixed transport default.
        public OntologyNumericFactSource maxActorTargetDistanceFrom = new();
        // Optional non-durable Authority cooldown sourced from authored data.
        public OntologyNumericFactSource cooldownSecondsFrom = new();
    }
}
