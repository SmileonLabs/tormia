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
        return Evaluate(
            definition,
            actorEntityId,
            targetEntityId,
            toolEntityId,
            AuthorityEvaluationSnapshot.Create(facts));
    }

    public static AuthoritativeActionEvaluation Evaluate(
        OntologyActionEffectDefinition definition,
        Guid actorEntityId,
        Guid targetEntityId,
        Guid? toolEntityId,
        AuthorityEvaluationSnapshot snapshot)
    {
        return EvaluateCore(
            definition,
            actorEntityId,
            targetEntityId,
            toolEntityId,
            snapshot,
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
        return EvaluateInvokedRule(
            actionDefinition,
            ruleDefinition,
            actorEntityId,
            targetEntityId,
            toolEntityId,
            AuthorityEvaluationSnapshot.Create(facts));
    }

    public static AuthoritativeActionEvaluation EvaluateInvokedRule(
        OntologyActionEffectDefinition actionDefinition,
        OntologyRuleDefinition ruleDefinition,
        Guid actorEntityId,
        Guid targetEntityId,
        Guid? toolEntityId,
        AuthorityEvaluationSnapshot snapshot)
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
            snapshot);
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

        var intentFact = new AuthorityFactSnapshot(
            intentSubject,
            invocation.intentPredicate,
            "entity",
            intentObject.ToString());
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
            snapshot,
            additionalBindings,
            allowNoEffects:
                actionDefinition.evaluationOnly ||
                !string.IsNullOrWhiteSpace(
                    ruleDefinition.runtimePresentation?.actorAnimationIntent),
            ephemeralFact: intentFact);
    }

    private static AuthoritativeActionEvaluation EvaluateCore(
        OntologyActionEffectDefinition definition,
        Guid actorEntityId,
        Guid targetEntityId,
        Guid? toolEntityId,
        AuthorityEvaluationSnapshot snapshot,
        IReadOnlyDictionary<string, OntologyId>? additionalBindings,
        bool allowNoEffects = false,
        AuthorityFactSnapshot? ephemeralFact = null)
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

        var matches = snapshot.MatchConditions(
            definition.conditions ?? [],
            initialBinding,
            ephemeralFact);
        if (matches.Count == 0)
            return AuthoritativeActionEvaluation.Rejected("action_conditions_not_met");
        if (matches.Count > 1)
            return AuthoritativeActionEvaluation.Rejected("ambiguous_action_binding");

        var binding = matches[0];
        var mutations = new List<AuthorityMutation>();
        var state = snapshot.CreateEvaluationState();
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

/// <summary>
/// Command-scoped input projection for Authority action evaluation. It compiles
/// the durable input once, then may receive only mutations that PostgreSQL has
/// already accepted in the same command transaction. It never stores an
/// approval result and is never shared across revisions or requests.
/// </summary>
internal sealed class AuthorityEvaluationSnapshot
{
    private readonly object gate = new();
    private readonly AuthorityCompiledEvaluationContract compiledBase;
    private readonly Dictionary<
        (Guid Subject, string Predicate), List<AuthorityFactSnapshot>> changedRows = new();
    private readonly AuthoritySemanticFactOverlay semanticOverlay = new();

    private AuthorityEvaluationSnapshot(
        AuthorityCompiledEvaluationContract compiledBase)
    {
        this.compiledBase = compiledBase;
        RequestScopeId = Guid.NewGuid();
        SourceFactCount = compiledBase.SourceFactCount;
    }

    internal Guid RequestScopeId { get; }
    internal int SourceFactCount { get; }

    public static AuthorityEvaluationSnapshot Create(
        IReadOnlyList<AuthorityFactSnapshot> facts)
        => AuthorityCompiledEvaluationContract.Compile(facts)
            .CreateCommandSnapshot();

    internal static AuthorityEvaluationSnapshot Create(
        AuthorityCompiledEvaluationContract compiledBase) =>
        new(compiledBase);

    internal List<Dictionary<string, OntologyId>> MatchConditions(
        IReadOnlyList<OntologyCondition> conditions,
        Dictionary<string, OntologyId> initialBinding,
        AuthorityFactSnapshot? ephemeralFact)
    {
        // A prepared snapshot has a read phase followed by one single-threaded
        // committed-mutation phase. Concurrent read-only evaluations never
        // acquire the mutation gate or serialize on each other.
        if (ephemeralFact is null)
            return compiledBase.MatchConditions(
                conditions, initialBinding, semanticOverlay);

        // The canonical intent is a request-local read overlay. It never
        // becomes a contribution owned by the command snapshot.
        var overlayFact = new OntologyFact(
            ephemeralFact.SubjectEntityId.ToString(),
            ephemeralFact.PredicateId,
            ephemeralFact.ObjectValue);
        return compiledBase.MatchConditions(
            conditions,
            initialBinding,
            new AuthorityCompositeFactOverlay(semanticOverlay, overlayFact));
    }

    internal AuthorityEvaluationState CreateEvaluationState() => new(this);

    internal IReadOnlyList<string> GetValues(Guid subject, string predicate)
    {
        return changedRows.TryGetValue((subject, predicate), out var current)
            ? current.Select(row => row.ObjectValue).ToArray()
            : compiledBase.GetValues(subject, predicate);
    }

    internal bool TryGetSingleNumber(
        Guid subject,
        string predicate,
        out long number)
    {
        number = 0;
        if (!changedRows.TryGetValue((subject, predicate), out var current))
            return compiledBase.TryGetSingleNumber(subject, predicate, out number);
        return
               current.Count == 1 &&
               long.TryParse(
                   current[0].ObjectValue,
                   NumberStyles.Integer,
                   CultureInfo.InvariantCulture,
                   out number);
    }

    /// <summary>
    /// Mirrors one mutation only after the matching SQL operation succeeded.
    /// Raw row provenance is retained because different Rule bindings may own
    /// the same semantic value and numeric evaluation requires exact row
    /// cardinality. Rule-binding projection rows are never changed by Fact SQL.
    /// </summary>
    internal bool TryApplyCommittedMutation(
        AuthorityMutation mutation,
        Guid? sourceRuleBindingId,
        out string rejectionCode)
    {
        lock (gate)
        {
            var key = (mutation.SubjectEntityId, mutation.PredicateId);
            if (!changedRows.TryGetValue(key, out var current))
            {
                current = new List<AuthorityFactSnapshot>(compiledBase.GetRows(key));
                changedRows.Add(key, current);
            }
            var beforeValues = SemanticValues(current);

            switch (mutation.Kind)
            {
                case AuthorityMutationKind.Assert:
                    if (mutation.Object is null)
                        return RejectOverlay("action_snapshot_overlay_invalid_assert", out rejectionCode);
                    AddRowIfMissing(
                        current, mutation, mutation.Object,
                        sourceRuleBindingId);
                    break;
                case AuthorityMutationKind.Retract:
                    if (mutation.Object is null)
                        return RejectOverlay("action_snapshot_overlay_invalid_retract", out rejectionCode);
                    current.RemoveAll(row =>
                        !row.IsRuleBindingProjection &&
                        MatchesObject(row, mutation.Object));
                    break;
                case AuthorityMutationKind.Set:
                    if (mutation.Object is null)
                        return RejectOverlay("action_snapshot_overlay_invalid_set", out rejectionCode);
                    current.RemoveAll(row =>
                        !row.IsRuleBindingProjection &&
                        !MatchesObject(row, mutation.Object));
                    AddRowIfMissing(
                        current, mutation, mutation.Object,
                        sourceRuleBindingId);
                    break;
                case AuthorityMutationKind.AdjustNumber:
                    var databaseRows = current
                        .Where(row => !row.IsRuleBindingProjection)
                        .ToArray();
                    if (databaseRows.Length != 1 ||
                        !long.TryParse(
                            databaseRows[0].ObjectValue,
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out var currentNumber))
                    {
                        return RejectOverlay(
                            "action_snapshot_overlay_numeric_state_diverged",
                            out rejectionCode);
                    }
                    long next;
                    try { next = checked(currentNumber + mutation.Delta); }
                    catch (OverflowException)
                    {
                        return RejectOverlay(
                            "action_snapshot_overlay_numeric_overflow",
                            out rejectionCode);
                    }
                    if (mutation.Minimum.HasValue)
                        next = Math.Max(next, mutation.Minimum.Value);
                    if (mutation.Maximum.HasValue)
                        next = Math.Min(next, mutation.Maximum.Value);
                    current.Remove(databaseRows[0]);
                    AddRowIfMissing(
                        current,
                        mutation,
                        AuthorityObject.Number(next),
                        sourceRuleBindingId);
                    break;
                default:
                    return RejectOverlay(
                        "action_snapshot_overlay_unsupported_mutation",
                        out rejectionCode);
            }

            var afterValues = SemanticValues(current);
            SynchronizeOverlay(
                mutation.SubjectEntityId,
                mutation.PredicateId,
                beforeValues,
                afterValues);
            rejectionCode = string.Empty;
            return true;
        }
    }

    private static void AddRowIfMissing(
        List<AuthorityFactSnapshot> current,
        AuthorityMutation mutation,
        AuthorityObject value,
        Guid? sourceRuleBindingId)
    {
        if (current.Any(row =>
                !row.IsRuleBindingProjection &&
                MatchesObject(row, value) &&
                row.SourceRuleBindingId == sourceRuleBindingId &&
                row.ResultLifetime == mutation.ResultLifetime))
        {
            return;
        }
        current.Add(new AuthorityFactSnapshot(
            mutation.SubjectEntityId,
            mutation.PredicateId,
            value.Kind,
            SnapshotComparableValue(value),
            sourceRuleBindingId,
            mutation.ResultLifetime));
    }

    private static bool MatchesObject(
        AuthorityFactSnapshot row,
        AuthorityObject value) =>
        string.Equals(row.ObjectKind, value.Kind, StringComparison.Ordinal) &&
        string.Equals(
            row.ObjectValue,
            SnapshotComparableValue(value),
            StringComparison.Ordinal);

    private static string SnapshotComparableValue(AuthorityObject value) =>
        string.Equals(value.Kind, "boolean", StringComparison.Ordinal) &&
        bool.TryParse(value.ComparableValue, out var boolean)
            ? boolean.ToString()
            : value.ComparableValue;

    private static HashSet<string> SemanticValues(
        IEnumerable<AuthorityFactSnapshot> current) =>
        current.Select(row => row.ObjectValue).ToHashSet(StringComparer.Ordinal);

    private void SynchronizeOverlay(
        Guid subject,
        string predicate,
        HashSet<string> beforeValues,
        HashSet<string> afterValues)
    {
        foreach (var removed in beforeValues.Except(afterValues))
            semanticOverlay.Remove(new OntologyFact(
                subject.ToString(), predicate, removed));
        foreach (var added in afterValues.Except(beforeValues))
            semanticOverlay.Add(new OntologyFact(
                subject.ToString(), predicate, added));
    }

    private static bool RejectOverlay(
        string code,
        out string rejectionCode)
    {
        rejectionCode = code;
        return false;
    }
}

/// <summary>
/// Immutable, revision-keyed rule input. A cached instance contains only the
/// compiled durable base. Every command receives a separate copy-on-write
/// snapshot, so request intent and committed transaction mutations can never
/// leak into another request or revision.
/// </summary>
internal sealed class AuthorityCompiledEvaluationContract
{
    private readonly OntologyWorldState world;
    private readonly Dictionary<(Guid Subject, string Predicate),
        List<AuthorityFactSnapshot>> rows;
    private readonly AuthorityFactSnapshot[] facts;
    private readonly IReadOnlyList<AuthorityFactSnapshot> factView;
    private readonly long estimatedBytes;

    private AuthorityCompiledEvaluationContract(
        OntologyWorldState world,
        Dictionary<(Guid Subject, string Predicate),
            List<AuthorityFactSnapshot>> rows,
        AuthorityFactSnapshot[] facts,
        long estimatedBytes)
    {
        this.world = world;
        this.rows = rows;
        this.facts = facts;
        this.estimatedBytes = estimatedBytes;
        factView = Array.AsReadOnly(facts);
    }

    internal IReadOnlyList<AuthorityFactSnapshot> Facts => factView;
    internal int SourceFactCount => facts.Length;
    internal long EstimatedBytes => estimatedBytes;

    internal List<Dictionary<string, OntologyId>> MatchConditions(
        IReadOnlyList<OntologyCondition> conditions,
        Dictionary<string, OntologyId> initialBinding,
        OntologyFact? overlayFact) => overlayFact.HasValue
            ? OntologyConditionMatcher.Match(
                world, conditions, initialBinding, overlayFact.Value)
            : OntologyConditionMatcher.Match(
                world, conditions, initialBinding);

    internal List<Dictionary<string, OntologyId>> MatchConditions(
        IReadOnlyList<OntologyCondition> conditions,
        Dictionary<string, OntologyId> initialBinding,
        IOntologyFactOverlay overlay) => OntologyConditionMatcher.Match(
            world, conditions, initialBinding, overlay);

    internal IReadOnlyList<AuthorityFactSnapshot> GetRows(
        (Guid Subject, string Predicate) key) =>
        rows.TryGetValue(key, out var current)
            ? current
            : Array.Empty<AuthorityFactSnapshot>();

    internal IReadOnlyList<string> GetValues(Guid subject, string predicate) =>
        rows.TryGetValue((subject, predicate), out var current)
            ? current.Select(row => row.ObjectValue).ToArray()
            : Array.Empty<string>();

    internal bool TryGetSingleNumber(
        Guid subject, string predicate, out long number)
    {
        number = 0;
        return rows.TryGetValue((subject, predicate), out var current) &&
               current.Count == 1 &&
               long.TryParse(current[0].ObjectValue,
                   NumberStyles.Integer, CultureInfo.InvariantCulture,
                   out number);
    }

    internal static AuthorityCompiledEvaluationContract Compile(
        IReadOnlyList<AuthorityFactSnapshot> source)
        => Compile(source, CancellationToken.None);

    internal static AuthorityCompiledEvaluationContract Compile(
        IReadOnlyList<AuthorityFactSnapshot> source,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        var facts = new AuthorityFactSnapshot[source.Count];
        for (var index = 0; index < source.Count; index++)
        {
            if ((index & 255) == 0)
                cancellationToken.ThrowIfCancellationRequested();
            facts[index] = source[index];
        }
        var world = new OntologyWorldState();
        var rows = new Dictionary<(Guid Subject, string Predicate),
            List<AuthorityFactSnapshot>>();
        for (var index = 0; index < facts.Length; index++)
        {
            if ((index & 255) == 0)
                cancellationToken.ThrowIfCancellationRequested();
            var fact = facts[index];
            world.GetOrCreateEntity(fact.SubjectEntityId.ToString());
            world.AddFactContribution(
                fact.SubjectEntityId.ToString(), fact.PredicateId,
                fact.ObjectValue, OntologyFactOrigin.Durable);
            var key = (fact.SubjectEntityId, fact.PredicateId);
            if (!rows.TryGetValue(key, out var values))
            {
                values = new List<AuthorityFactSnapshot>();
                rows.Add(key, values);
            }
            values.Add(fact);
        }
        var estimatedBytes = AuthorityCompiledContractMemoryEstimator.Estimate(
            facts, cancellationToken);
        return new AuthorityCompiledEvaluationContract(
            world, rows, facts, estimatedBytes);
    }

    internal AuthorityEvaluationSnapshot CreateCommandSnapshot() =>
        AuthorityEvaluationSnapshot.Create(this);
}

internal sealed class AuthoritySemanticFactOverlay : IOntologyFactOverlay
{
    private readonly HashSet<OntologyFact> additions = new();
    private readonly HashSet<OntologyFact> tombstones = new();
    private readonly Dictionary<OntologyId, HashSet<OntologyFact>> additionsByPredicate = new();

    public IEnumerable<OntologyFact> GetAddedFacts() => additions;

    public IEnumerable<OntologyFact> GetAddedFacts(OntologyId predicate) =>
        additionsByPredicate.TryGetValue(predicate, out var current)
            ? current
            : Array.Empty<OntologyFact>();

    public bool IsRemoved(OntologyFact fact) => tombstones.Contains(fact);

    internal void Add(OntologyFact fact)
    {
        tombstones.Remove(fact);
        if (!additions.Add(fact)) return;
        if (!additionsByPredicate.TryGetValue(fact.Predicate, out var current))
        {
            current = new HashSet<OntologyFact>();
            additionsByPredicate.Add(fact.Predicate, current);
        }
        current.Add(fact);
    }

    internal void Remove(OntologyFact fact)
    {
        if (additions.Remove(fact) &&
            additionsByPredicate.TryGetValue(fact.Predicate, out var current))
        {
            current.Remove(fact);
            if (current.Count == 0) additionsByPredicate.Remove(fact.Predicate);
        }
        tombstones.Add(fact);
    }
}

internal sealed class AuthorityCompositeFactOverlay : IOntologyFactOverlay
{
    private readonly IOntologyFactOverlay baseOverlay;
    private readonly OntologyFact ephemeral;

    internal AuthorityCompositeFactOverlay(
        IOntologyFactOverlay baseOverlay,
        OntologyFact ephemeral)
    {
        this.baseOverlay = baseOverlay;
        this.ephemeral = ephemeral;
    }

    public IEnumerable<OntologyFact> GetAddedFacts()
    {
        foreach (var fact in baseOverlay.GetAddedFacts()) yield return fact;
        yield return ephemeral;
    }

    public IEnumerable<OntologyFact> GetAddedFacts(OntologyId predicate)
    {
        foreach (var fact in baseOverlay.GetAddedFacts(predicate)) yield return fact;
        if (ephemeral.Predicate.Equals(predicate)) yield return ephemeral;
    }

    public bool IsRemoved(OntologyFact fact) =>
        !fact.Equals(ephemeral) && baseOverlay.IsRemoved(fact);
}

internal sealed class AuthorityEvaluationState
{
    private readonly AuthorityEvaluationSnapshot snapshot;
    private readonly Dictionary<(Guid Subject, string Predicate), List<string>>
        overrides = new();

    internal AuthorityEvaluationState(AuthorityEvaluationSnapshot snapshot)
    {
        this.snapshot = snapshot;
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
        overrides[(subject, predicate)] =
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
            overrides[key] = [mutation.Object.ComparableValue];
            return;
        }
        if (mutation.Kind != AuthorityMutationKind.Retract)
        {
            return;
        }
        var current = GetMutableValues(key);
        current.RemoveAll(value =>
            string.Equals(
                value,
                mutation.Object.ComparableValue,
                StringComparison.Ordinal));
    }

    public IReadOnlyList<string> GetValues(Guid subject, string predicate)
    {
        var key = (subject, predicate);
        if (overrides.TryGetValue(key, out var current))
            return current.ToArray();
        return snapshot.GetValues(subject, predicate);
    }

    public bool TryGetSingleNumber(
        Guid subject,
        string predicate,
        out long number)
    {
        number = 0;
        var key = (subject, predicate);
        if (!overrides.TryGetValue(key, out var current))
            return snapshot.TryGetSingleNumber(subject, predicate, out number);
        return current.Count == 1 &&
               long.TryParse(current[0], NumberStyles.Integer,
                   CultureInfo.InvariantCulture, out number);
    }

    private List<string> GetMutableValues(
        (Guid Subject, string Predicate) key)
    {
        if (overrides.TryGetValue(key, out var current))
            return current;
        current = new List<string>(
            snapshot.GetValues(key.Subject, key.Predicate));
        overrides[key] = current;
        return current;
    }
}

internal sealed record AuthorityFactSnapshot(
    Guid SubjectEntityId,
    string PredicateId,
    string ObjectKind,
    string ObjectValue,
    Guid? SourceRuleBindingId = null,
    OntologyRuleResultLifetime ResultLifetime =
        OntologyRuleResultLifetime.RuleBound,
    bool IsRuleBindingProjection = false);

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
