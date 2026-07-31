using System.Globalization;
using System.Text.Json;
using Tormia.Ontology.Core;

/// <summary>
/// Pure command-scoped evaluation for published action definitions. Background
/// AddFact inference remains owned by HeadlessZoneOntologyEvaluator and is never
/// persisted through this path.
/// </summary>
internal static class AuthoritativeActionEvaluator
{
    private const int MaxConditions = 32;
    private const int MaxEffects = 32;

    public static bool IsSupportedDefinition(
        OntologyActionEffectDefinition? definition,
        out string rejectionCode)
    {
        rejectionCode = string.Empty;
        if (definition is null ||
            !SemanticId.IsValid(definition.actionVerb) ||
            definition.conditions is { Count: > MaxConditions } ||
            definition.effects is { Count: > MaxEffects })
        {
            rejectionCode = "invalid_authoritative_action_definition";
            return false;
        }

        var invokesRule = HasRuleInvocation(definition);
        var hasLegacyEffect = !string.IsNullOrWhiteSpace(definition.predicate);
        var hasStructuredEffects = definition.effects is { Count: > 0 };
        if (!hasLegacyEffect && !hasStructuredEffects && !invokesRule)
        {
            rejectionCode = "action_definition_has_no_effects";
            return false;
        }
        if (invokesRule && (hasLegacyEffect || hasStructuredEffects))
        {
            rejectionCode = "action_definition_mixes_rule_invocation_and_effects";
            return false;
        }
        if (invokesRule &&
            !IsValidRuleInvocation(definition!.ruleInvocation!))
        {
            rejectionCode = "invalid_action_rule_invocation";
            return false;
        }
        if (definition.postRuleInvocations is { Count: > MaxEffects } ||
            (definition.postRuleInvocations ?? []).Any(
                invocation =>
                    invocation is null ||
                    !IsValidRuleInvocation(invocation)))
        {
            rejectionCode = "invalid_action_post_rule_invocation";
            return false;
        }

        if (hasLegacyEffect &&
            (!SemanticId.IsValid(definition.predicate) ||
             definition.subjectPattern != "?actor" ||
             definition.objectPattern is not ("?target" or "?tool")))
        {
            rejectionCode = "invalid_legacy_action_effect";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(
                definition.presentation?.actorAnimationIntent) &&
            !SemanticId.IsValid(
                definition.presentation.actorAnimationIntent))
        {
            rejectionCode = "invalid_action_presentation_intent";
            return false;
        }

        if (definition.runtimeConstraints is not null &&
            (!double.IsFinite(
                 definition.runtimeConstraints.maxActorTargetDistance) ||
             definition.runtimeConstraints.maxActorTargetDistance < 0d ||
             (definition.runtimeConstraints.maxActorTargetDistance > 0d &&
              HasConfiguredNumericSource(
                  definition.runtimeConstraints
                      .maxActorTargetDistanceFrom)) ||
             !IsValidRuntimeNumericSource(
                 definition.runtimeConstraints
                     .maxActorTargetDistanceFrom) ||
             !IsValidRuntimeNumericSource(
                 definition.runtimeConstraints.cooldownSecondsFrom)))
        {
            rejectionCode = "invalid_action_runtime_constraints";
            return false;
        }

        foreach (var condition in definition.conditions ?? [])
        {
            if (condition is null ||
                !Enum.IsDefined(typeof(OntologyConditionKind), condition.kind) ||
                string.IsNullOrWhiteSpace(condition.subject) ||
                string.IsNullOrWhiteSpace(condition.obj) ||
                (condition.kind is OntologyConditionKind.Fact or OntologyConditionKind.NotFact &&
                 !SemanticId.IsValid(condition.predicate)))
            {
                rejectionCode = "invalid_action_condition";
                return false;
            }
        }

        foreach (var effect in definition.effects ?? [])
        {
            // AddFact is retractable inference in the shared ontology semantics.
            // Durable action assertions use the legacy action relation or SetFact.
            if (effect is null ||
                effect.kind is OntologyEffectKind.AddFact ||
                !Enum.IsDefined(typeof(OntologyEffectKind), effect.kind) ||
                !Enum.IsDefined(
                    typeof(OntologyRuleResultLifetime),
                    effect.resultLifetime) ||
                string.IsNullOrWhiteSpace(effect.subject) ||
                !SemanticId.IsValid(effect.predicate) ||
                (effect.kind != OntologyEffectKind.AdjustNumberFact &&
                 string.IsNullOrWhiteSpace(effect.obj)) ||
                (!string.IsNullOrWhiteSpace(effect.inversePredicate) &&
                 (effect.kind != OntologyEffectKind.SetFact ||
                  !SemanticId.IsValid(effect.inversePredicate))))
            {
                rejectionCode = "invalid_action_effect";
                return false;
            }

            if (effect.kind == OntologyEffectKind.AdjustNumberFact &&
                ((!HasConfiguredNumericSource(effect.valueFrom) &&
                  !TryParseLong(effect.obj, out _)) ||
                 (HasConfiguredNumericSource(effect.valueFrom) &&
                  (string.IsNullOrWhiteSpace(effect.valueFrom.subject) ||
                   !SemanticId.IsValid(effect.valueFrom.predicate) ||
                   effect.valueFrom.multiplier == 0)) ||
                 !TryParseOptionalLong(effect.minimum, out _) ||
                 !TryParseOptionalLong(effect.maximum, out var maximum) ||
                 (TryParseOptionalLong(effect.minimum, out var minimum) &&
                  minimum.HasValue && maximum.HasValue && minimum > maximum)))
            {
                rejectionCode = "invalid_numeric_action_effect";
                return false;
            }

            if (HasConfiguredNumericGuard(effect.when) &&
                (effect.when.comparison == OntologyNumericComparison.None ||
                 string.IsNullOrWhiteSpace(effect.when.subject) ||
                 !SemanticId.IsValid(effect.when.predicate) ||
                 !TryParseLong(effect.when.value, out _)))
            {
                rejectionCode = "invalid_action_effect_guard";
                return false;
            }
        }

        return true;
    }

    internal static bool HasConfiguredNumericSource(
        OntologyNumericFactSource? source) =>
        source is not null &&
        (!string.IsNullOrWhiteSpace(source.subject) ||
         !string.IsNullOrWhiteSpace(source.predicate) ||
         source.multiplier != 1);

    private static bool IsValidRuntimeNumericSource(
        OntologyNumericFactSource? source) =>
        !HasConfiguredNumericSource(source) ||
        (IsEntityPattern(source!.subject) &&
         SemanticId.IsValid(source.predicate) &&
         source.multiplier > 0);

    private static bool HasConfiguredNumericGuard(
        OntologyNumericFactGuard? guard) =>
        guard is not null &&
        (guard.comparison != OntologyNumericComparison.None ||
         !string.IsNullOrWhiteSpace(guard.subject) ||
         !string.IsNullOrWhiteSpace(guard.predicate) ||
         !string.IsNullOrWhiteSpace(guard.value));

    public static bool HasRuleInvocation(
        OntologyActionEffectDefinition? definition) =>
        definition?.ruleInvocation is not null &&
        !string.IsNullOrWhiteSpace(definition.ruleInvocation.ruleId);

    private static bool IsValidRuleInvocation(
        OntologyActionRuleInvocationDefinition invocation) =>
        SemanticId.IsValid(invocation.ruleId) &&
        IsVariable(invocation.bindingVariable) &&
        IsEntityPattern(invocation.bindingEntityPattern) &&
        SemanticId.IsValid(invocation.intentPredicate) &&
        IsEntityPattern(invocation.intentSubjectPattern) &&
        IsEntityPattern(invocation.intentObjectPattern);

    public static bool TryResolveRuleBindingEntity(
        OntologyActionEffectDefinition definition,
        Guid actorEntityId,
        Guid targetEntityId,
        Guid? toolEntityId,
        out Guid entityId)
    {
        entityId = Guid.Empty;
        return HasRuleInvocation(definition) &&
               TryResolveRuleBindingEntity(
                   definition.ruleInvocation,
                   actorEntityId,
                   targetEntityId,
                   toolEntityId,
                   out entityId);
    }

    public static bool TryResolveRuleBindingEntity(
        OntologyActionRuleInvocationDefinition invocation,
        Guid actorEntityId,
        Guid targetEntityId,
        Guid? toolEntityId,
        out Guid entityId)
    {
        entityId = Guid.Empty;
        return invocation is not null &&
               TryResolveEntityPattern(
                   invocation.bindingEntityPattern,
                   actorEntityId,
                   targetEntityId,
                   toolEntityId,
                   out entityId);
    }

    public static AuthoritativeActionEvaluation Evaluate(
        OntologyActionEffectDefinition definition,
        Guid actorEntityId,
        Guid targetEntityId,
        Guid? toolEntityId,
        IReadOnlyList<AuthorityFactSnapshot> facts)
    {
        return EvaluateCore(
            definition,
            actorEntityId,
            targetEntityId,
            toolEntityId,
            facts,
            null);
    }

    /// <summary>
    /// Evaluates a command-scoped Rule Block with one ephemeral intent Fact.
    /// The intent never becomes a durable mutation. Only the published rule
    /// definition assigned to the authored binding entity supplies the effects.
    /// </summary>
    public static AuthoritativeActionEvaluation EvaluateInvokedRule(
        OntologyActionEffectDefinition actionDefinition,
        OntologyRuleDefinition ruleDefinition,
        Guid actorEntityId,
        Guid targetEntityId,
        Guid? toolEntityId,
        IReadOnlyList<AuthorityFactSnapshot> facts)
    {
        if (!HasRuleInvocation(actionDefinition) ||
            ruleDefinition is null ||
            !string.Equals(
                actionDefinition.ruleInvocation.ruleId,
                ruleDefinition.id,
                StringComparison.Ordinal))
        {
            return AuthoritativeActionEvaluation.Rejected(
                "action_rule_definition_mismatch");
        }

        var actionEvaluation = Evaluate(
            actionDefinition,
            actorEntityId,
            targetEntityId,
            toolEntityId,
            facts);
        if (!actionEvaluation.Accepted)
            return actionEvaluation;
        if (actionEvaluation.Mutations.Count != 0)
        {
            return AuthoritativeActionEvaluation.Rejected(
                "action_rule_transport_created_mutation");
        }

        var ruleValidation =
            OntologyRuleValidator.Validate([ruleDefinition]);
        if (actionDefinition.evaluationOnly)
        {
            ruleValidation.RemoveAll(message =>
                string.Equals(
                    message,
                    $"Rule '{ruleDefinition.id}' has no effects.",
                    StringComparison.Ordinal));
        }
        if (ruleValidation.Count > 0 ||
            ruleDefinition.effects is null ||
            ruleDefinition.effects.Any(effect =>
                effect is null ||
                effect.kind == OntologyEffectKind.AddFact))
        {
            return AuthoritativeActionEvaluation.Rejected(
                "unsupported_authoritative_rule_definition");
        }

        var invocation = actionDefinition.ruleInvocation;
        if (!TryResolveEntityPattern(
                invocation.bindingEntityPattern,
                actorEntityId,
                targetEntityId,
                toolEntityId,
                out var bindingEntity))
        {
            return AuthoritativeActionEvaluation.Rejected(
                "action_rule_binding_entity_unavailable");
        }
        if (!TryResolveEntityPattern(
                invocation.intentSubjectPattern,
                actorEntityId,
                targetEntityId,
                toolEntityId,
                out var intentSubject) ||
            !TryResolveEntityPattern(
                invocation.intentObjectPattern,
                actorEntityId,
                targetEntityId,
                toolEntityId,
                out var intentObject))
        {
            return AuthoritativeActionEvaluation.Rejected(
                "action_rule_intent_entity_unavailable");
        }

        var scopedFacts = facts
            .Append(new AuthorityFactSnapshot(
                intentSubject,
                invocation.intentPredicate,
                "entity",
                intentObject.ToString()))
            .ToArray();
        var ruleAsAction = new OntologyActionEffectDefinition
        {
            actionVerb = actionDefinition.actionVerb,
            conditions = ruleDefinition.conditions,
            effects = ruleDefinition.effects
        };
        var additionalBindings = new Dictionary<string, OntologyId>
        {
            [invocation.bindingVariable] = bindingEntity.ToString()
        };
        return EvaluateCore(
            ruleAsAction,
            actorEntityId,
            targetEntityId,
            toolEntityId,
            scopedFacts,
            additionalBindings,
            allowNoEffects:
                actionDefinition.evaluationOnly ||
                !string.IsNullOrWhiteSpace(
                    ruleDefinition.runtimePresentation?.actorAnimationIntent));
    }

    private static AuthoritativeActionEvaluation EvaluateCore(
        OntologyActionEffectDefinition definition,
        Guid actorEntityId,
        Guid targetEntityId,
        Guid? toolEntityId,
        IReadOnlyList<AuthorityFactSnapshot> facts,
        IReadOnlyDictionary<string, OntologyId>? additionalBindings,
        bool allowNoEffects = false)
    {
        if (!IsSupportedDefinition(definition, out var invalidCode) &&
            !(allowNoEffects &&
              string.Equals(
                  invalidCode,
                  "action_definition_has_no_effects",
                  StringComparison.Ordinal)))
            return AuthoritativeActionEvaluation.Rejected(invalidCode);
        if (definition.requiresTool && !toolEntityId.HasValue)
            return AuthoritativeActionEvaluation.Rejected("action_tool_required");

        var world = new OntologyWorldState();
        foreach (var fact in facts)
        {
            world.GetOrCreateEntity(fact.SubjectEntityId.ToString());
            world.AddFactContribution(
                fact.SubjectEntityId.ToString(),
                fact.PredicateId,
                fact.ObjectValue,
                OntologyFactOrigin.Durable);
        }

        var initialBinding = new Dictionary<string, OntologyId>
        {
            ["?actor"] = actorEntityId.ToString(),
            ["?target"] = targetEntityId.ToString()
        };
        if (toolEntityId.HasValue)
            initialBinding["?tool"] = toolEntityId.Value.ToString();
        if (additionalBindings is not null)
        {
            foreach (var additionalBinding in additionalBindings)
                initialBinding[additionalBinding.Key] =
                    additionalBinding.Value;
        }

        var matches = OntologyConditionMatcher.Match(
            world,
            definition.conditions ?? [],
            initialBinding);
        if (matches.Count == 0)
            return AuthoritativeActionEvaluation.Rejected("action_conditions_not_met");
        if (matches.Count > 1)
            return AuthoritativeActionEvaluation.Rejected("ambiguous_action_binding");

        var binding = matches[0];
        var mutations = new List<AuthorityMutation>();
        var state = AuthorityEvaluationState.From(facts);
        if (!string.IsNullOrWhiteSpace(definition.predicate))
        {
            var objectPattern = definition.objectPattern == "?tool"
                ? toolEntityId?.ToString()
                : targetEntityId.ToString();
            if (string.IsNullOrWhiteSpace(objectPattern))
                return AuthoritativeActionEvaluation.Rejected("action_tool_required");
            mutations.Add(AuthorityMutation.Assert(
                actorEntityId,
                definition.predicate.Trim(),
                AuthorityObject.Entity(Guid.Parse(objectPattern))));
        }

        foreach (var effect in definition.effects ?? [])
        {
            if (HasConfiguredNumericGuard(effect.when) &&
                !state.Matches(effect.when, binding))
            {
                continue;
            }

            var subjectValue = OntologyConditionMatcher.Resolve(effect.subject, binding).ToString();
            if (!Guid.TryParse(subjectValue, out var subjectEntityId))
                return AuthoritativeActionEvaluation.Rejected("action_effect_subject_not_entity");

            var objectValue = OntologyConditionMatcher.Resolve(effect.obj, binding).ToString();
            if (effect.kind == OntologyEffectKind.AdjustNumberFact)
            {
                long delta;
                if (HasConfiguredNumericSource(effect.valueFrom))
                {
                    var sourceSubjectValue = OntologyConditionMatcher.Resolve(
                        effect.valueFrom.subject,
                        binding).ToString();
                    if (!Guid.TryParse(sourceSubjectValue, out var sourceSubjectId) ||
                        !state.TryGetSingleNumber(
                            sourceSubjectId,
                            effect.valueFrom.predicate.Trim(),
                            out var sourceValue))
                    {
                        return AuthoritativeActionEvaluation.Rejected(
                            "action_numeric_source_missing");
                    }
                    try
                    {
                        delta = checked(
                            sourceValue * effect.valueFrom.multiplier);
                    }
                    catch (OverflowException)
                    {
                        return AuthoritativeActionEvaluation.Rejected(
                            "numeric_effect_overflow");
                    }
                }
                else
                {
                    TryParseLong(effect.obj, out delta);
                }
                TryParseOptionalLong(effect.minimum, out var minimum);
                TryParseOptionalLong(effect.maximum, out var maximum);
                if (!state.TryAdjust(
                        subjectEntityId,
                        effect.predicate.Trim(),
                        delta,
                        minimum,
                        maximum,
                        out var adjustmentRejection))
                {
                    return AuthoritativeActionEvaluation.Rejected(adjustmentRejection);
                }
                mutations.Add(AuthorityMutation.AdjustNumber(
                    subjectEntityId,
                    effect.predicate.Trim(),
                    delta,
                    minimum,
                    maximum,
                    effect.resultLifetime));
                continue;
            }

            var resolvedObject = ResolveObject(objectValue);
            if (effect.kind == OntologyEffectKind.SetFact &&
                !string.IsNullOrWhiteSpace(effect.inversePredicate))
            {
                if (resolvedObject.EntityId is not Guid inverseSubject)
                {
                    return AuthoritativeActionEvaluation.Rejected(
                        "action_inverse_object_not_entity");
                }

                var inverseObject = AuthorityObject.Entity(subjectEntityId);
                foreach (var previousValue in state.GetValues(
                             subjectEntityId,
                             effect.predicate.Trim()))
                {
                    if (!Guid.TryParse(previousValue, out var previousEntityId))
                    {
                        return AuthoritativeActionEvaluation.Rejected(
                            "action_inverse_previous_value_not_entity");
                    }
                    if (previousEntityId == inverseSubject) continue;
                    var removePreviousInverse = AuthorityMutation.Retract(
                        previousEntityId,
                        effect.inversePredicate.Trim(),
                        inverseObject);
                    mutations.Add(removePreviousInverse);
                    state.Apply(removePreviousInverse);
                }

                var setForward = AuthorityMutation.Set(
                    subjectEntityId,
                    effect.predicate.Trim(),
                    resolvedObject,
                    effect.resultLifetime);
                mutations.Add(setForward);
                state.Apply(setForward);

                var setInverse = AuthorityMutation.Set(
                    inverseSubject,
                    effect.inversePredicate.Trim(),
                    inverseObject,
                    effect.resultLifetime);
                mutations.Add(setInverse);
                state.Apply(setInverse);
                continue;
            }

            var mutation = effect.kind switch
            {
                OntologyEffectKind.RemoveFact => AuthorityMutation.Retract(
                    subjectEntityId, effect.predicate.Trim(), resolvedObject),
                OntologyEffectKind.SetFact => AuthorityMutation.Set(
                    subjectEntityId,
                    effect.predicate.Trim(),
                    resolvedObject,
                    effect.resultLifetime),
                _ => throw new InvalidOperationException("Unsupported authoritative effect.")
            };
            mutations.Add(mutation);
            state.Apply(mutation);
        }

        return AuthoritativeActionEvaluation.Succeeded(mutations);
    }

    private static bool IsVariable(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length > 1 &&
        value[0] == '?' &&
        SemanticId.IsValid(value[1..]);

    private static bool IsEntityPattern(string? value) =>
        value is "?actor" or "?target" or "?tool";

    private static bool TryResolveEntityPattern(
        string pattern,
        Guid actorEntityId,
        Guid targetEntityId,
        Guid? toolEntityId,
        out Guid entityId)
    {
        entityId = pattern switch
        {
            "?actor" => actorEntityId,
            "?target" => targetEntityId,
            "?tool" => toolEntityId ?? Guid.Empty,
            _ => Guid.Empty
        };
        return entityId != Guid.Empty;
    }

    private static AuthorityObject ResolveObject(string value)
    {
        if (Guid.TryParse(value, out var entityId))
            return AuthorityObject.Entity(entityId);
        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
            return AuthorityObject.Number(number);
        if (bool.TryParse(value, out var boolean))
            return AuthorityObject.Boolean(boolean);
        return AuthorityObject.Canonical(value);
    }

    private static bool TryParseLong(string? value, out long result) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);

    private static bool TryParseOptionalLong(string? value, out long? result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(value)) return true;
        if (!TryParseLong(value, out var parsed)) return false;
        result = parsed;
        return true;
    }
}

internal sealed class AuthorityEvaluationState
{
    private readonly Dictionary<(Guid Subject, string Predicate), List<string>> values = new();

    public static AuthorityEvaluationState From(IReadOnlyList<AuthorityFactSnapshot> facts)
    {
        var state = new AuthorityEvaluationState();
        foreach (var fact in facts)
        {
            var key = (fact.SubjectEntityId, fact.PredicateId);
            if (!state.values.TryGetValue(key, out var current))
            {
                current = new List<string>();
                state.values[key] = current;
            }
            current.Add(fact.ObjectValue);
        }
        return state;
    }

    public bool Matches(
        OntologyNumericFactGuard guard,
        Dictionary<string, OntologyId> binding)
    {
        var subject = OntologyConditionMatcher.Resolve(guard.subject, binding).ToString();
        return Guid.TryParse(subject, out var subjectId) &&
               TryGetSingleNumber(subjectId, guard.predicate.Trim(), out var current) &&
               long.TryParse(
                   guard.value,
                   NumberStyles.Integer,
                   CultureInfo.InvariantCulture,
                   out var expected) &&
               guard.comparison switch
               {
                   OntologyNumericComparison.LessThan => current < expected,
                   OntologyNumericComparison.LessThanOrEqual => current <= expected,
                   OntologyNumericComparison.Equal => current == expected,
                   OntologyNumericComparison.GreaterThanOrEqual => current >= expected,
                   OntologyNumericComparison.GreaterThan => current > expected,
                   _ => false
               };
    }

    public bool TryAdjust(
        Guid subject,
        string predicate,
        long delta,
        long? minimum,
        long? maximum,
        out string rejectionCode)
    {
        if (!TryGetSingleNumber(subject, predicate, out var current))
        {
            rejectionCode = "required_numeric_fact_missing";
            return false;
        }

        long next;
        try { next = checked(current + delta); }
        catch (OverflowException)
        {
            rejectionCode = "numeric_effect_overflow";
            return false;
        }
        if (minimum.HasValue) next = Math.Max(next, minimum.Value);
        if (maximum.HasValue) next = Math.Min(next, maximum.Value);
        values[(subject, predicate)] =
            [next.ToString(CultureInfo.InvariantCulture)];
        rejectionCode = string.Empty;
        return true;
    }

    public void Apply(AuthorityMutation mutation)
    {
        if (mutation.Object is null) return;
        var key = (mutation.SubjectEntityId, mutation.PredicateId);
        if (mutation.Kind == AuthorityMutationKind.Set)
        {
            values[key] = [mutation.Object.ComparableValue];
            return;
        }
        if (mutation.Kind != AuthorityMutationKind.Retract ||
            !values.TryGetValue(key, out var current))
        {
            return;
        }
        current.RemoveAll(value =>
            string.Equals(
                value,
                mutation.Object.ComparableValue,
                StringComparison.Ordinal));
    }

    public IReadOnlyList<string> GetValues(Guid subject, string predicate)
    {
        return values.TryGetValue((subject, predicate), out var current)
            ? current.ToArray()
            : Array.Empty<string>();
    }

    public bool TryGetSingleNumber(
        Guid subject,
        string predicate,
        out long number)
    {
        number = 0;
        return values.TryGetValue((subject, predicate), out var current) &&
               current.Count == 1 &&
               long.TryParse(
                   current[0],
                   NumberStyles.Integer,
                   CultureInfo.InvariantCulture,
                   out number);
    }
}

internal sealed record AuthorityFactSnapshot(
    Guid SubjectEntityId,
    string PredicateId,
    string ObjectKind,
    string ObjectValue);

internal sealed record AuthoritativeActionEvaluation(
    bool Accepted,
    string? RejectionCode,
    IReadOnlyList<AuthorityMutation> Mutations)
{
    public static AuthoritativeActionEvaluation Rejected(string code) =>
        new(false, code, Array.Empty<AuthorityMutation>());

    public static AuthoritativeActionEvaluation Succeeded(IReadOnlyList<AuthorityMutation> mutations) =>
        new(true, null, mutations);
}

internal enum AuthorityMutationKind
{
    Assert,
    Retract,
    Set,
    AdjustNumber
}

internal sealed record AuthorityMutation(
    AuthorityMutationKind Kind,
    Guid SubjectEntityId,
    string PredicateId,
    AuthorityObject? Object,
    long Delta,
    long? Minimum,
    long? Maximum,
    OntologyRuleResultLifetime ResultLifetime)
{
    public static AuthorityMutation Assert(Guid subject, string predicate, AuthorityObject obj) =>
        new(
            AuthorityMutationKind.Assert,
            subject,
            predicate,
            obj,
            0,
            null,
            null,
            OntologyRuleResultLifetime.DurableState);

    public static AuthorityMutation Retract(Guid subject, string predicate, AuthorityObject obj) =>
        new(
            AuthorityMutationKind.Retract,
            subject,
            predicate,
            obj,
            0,
            null,
            null,
            OntologyRuleResultLifetime.DurableState);

    public static AuthorityMutation Set(
        Guid subject,
        string predicate,
        AuthorityObject obj,
        OntologyRuleResultLifetime resultLifetime =
            OntologyRuleResultLifetime.RuleBound) =>
        new(
            AuthorityMutationKind.Set,
            subject,
            predicate,
            obj,
            0,
            null,
            null,
            resultLifetime);

    public static AuthorityMutation AdjustNumber(
        Guid subject,
        string predicate,
        long delta,
        long? minimum,
        long? maximum,
        OntologyRuleResultLifetime resultLifetime =
            OntologyRuleResultLifetime.RuleBound) =>
        new(
            AuthorityMutationKind.AdjustNumber,
            subject,
            predicate,
            null,
            delta,
            minimum,
            maximum,
            resultLifetime);
}

internal sealed record AuthorityObject(
    string Kind,
    Guid? EntityId,
    string? CanonicalId,
    string? ValueJson,
    string ComparableValue)
{
    public static AuthorityObject Entity(Guid value) =>
        new("entity", value, null, null, value.ToString());

    public static AuthorityObject Canonical(string value) =>
        new("canonical", null, value, null, value);

    public static AuthorityObject Number(long value) =>
        new("number", null, null, value.ToString(CultureInfo.InvariantCulture), value.ToString(CultureInfo.InvariantCulture));

    public static AuthorityObject Boolean(bool value) =>
        new("boolean", null, null, value ? "true" : "false", value ? "true" : "false");
}
