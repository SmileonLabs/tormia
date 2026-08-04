using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Npgsql;
using NpgsqlTypes;
using Tormia.Ontology.Core;

/// <summary>
/// Stores immutable, versioned authoring content. World commands only reference a
/// rule version; they never embed a mutable copy of its definition.
/// </summary>
internal sealed class ContentCatalogRepository(NpgsqlDataSource dataSource)
{
    private static readonly JsonSerializerOptions RuleJson = new(JsonSerializerDefaults.Web)
    {
        IncludeFields = true,
        PropertyNameCaseInsensitive = true
    };

    public async Task<ContentPackagePreflightResult> PreflightWorldPackage(
        Guid worldId,
        Guid actorUserId,
        ContentPackagePreflightRequest request,
        CancellationToken cancellationToken)
    {
        var packageId = request.PackageId?.Trim() ?? string.Empty;
        var packageVersion = request.PackageVersion?.Trim() ?? string.Empty;
        var requestedRules = request.Rules ?? [];
        var requestedActions = request.Actions ?? [];
        if (!SemanticId.IsValid(packageId) ||
            packageVersion.Length is < 1 or > 64 ||
            requestedRules.Count > 250 || requestedActions.Count > 250 ||
            !TryValidatePreflightIdentities(requestedRules, true) ||
            !TryValidatePreflightIdentities(requestedActions, false))
        {
            return EmptyPreflight(
                packageId,
                packageVersion,
                "invalid_content_preflight_request");
        }

        await using var connection =
            await dataSource.OpenConnectionAsync(cancellationToken);
        const string accessSql = """
            SELECT
                EXISTS (SELECT 1 FROM worlds WHERE world_id = @worldId),
                EXISTS (
                    SELECT 1 FROM world_members
                    WHERE world_id = @worldId AND user_id = @actorUserId),
                EXISTS (
                    SELECT 1 FROM world_content_packages
                    WHERE world_id = @worldId AND package_id = @packageId
                      AND package_version = @packageVersion AND enabled);
            """;
        await using var access = new NpgsqlCommand(accessSql, connection);
        access.Parameters.AddWithValue("worldId", worldId);
        access.Parameters.AddWithValue("actorUserId", actorUserId);
        access.Parameters.AddWithValue("packageId", packageId);
        access.Parameters.AddWithValue("packageVersion", packageVersion);
        await using var accessReader =
            await access.ExecuteReaderAsync(cancellationToken);
        await accessReader.ReadAsync(cancellationToken);
        var worldExists = accessReader.GetBoolean(0);
        var canRead = accessReader.GetBoolean(1);
        var activeMatch = accessReader.GetBoolean(2);
        await accessReader.DisposeAsync();
        if (!worldExists)
            return EmptyPreflight(packageId, packageVersion, "world_not_found");
        if (!canRead)
            return EmptyPreflight(packageId, packageVersion, "forbidden");

        var rules = new List<PublishedRuleDefinitionSummary>();
        var actions = new List<PublishedActionDefinitionSummary>();
        var missing = new List<ContentDefinitionPreflightFailure>();
        var mismatched = new List<ContentDefinitionPreflightFailure>();

        foreach (var expected in requestedRules)
        {
            const string sql = """
                SELECT package_version, checksum
                FROM content_definitions
                WHERE definition_kind = 'rule'
                  AND definition_id = @definitionId
                  AND definition_version = @definitionVersion
                  AND is_published;
                """;
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue(
                "definitionId",
                expected.DefinitionId!.Trim());
            command.Parameters.AddWithValue(
                "definitionVersion",
                expected.DefinitionVersion);
            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                missing.Add(Failure("rule", expected));
                continue;
            }
            var publishedPackageVersion = reader.GetString(0);
            var checksum = reader.GetString(1);
            rules.Add(new PublishedRuleDefinitionSummary(
                expected.DefinitionId.Trim(),
                expected.DefinitionVersion,
                publishedPackageVersion,
                checksum));
            if (HasChecksumMismatch(expected, checksum, true))
                mismatched.Add(Failure("rule", expected));
        }

        foreach (var expected in requestedActions)
        {
            const string sql = """
                SELECT checksum
                FROM content_definitions
                WHERE definition_kind = 'action_effect'
                  AND definition_id = @definitionId
                  AND definition_version = @definitionVersion
                  AND package_id = @packageId
                  AND package_version = @packageVersion
                  AND is_published;
                """;
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue(
                "definitionId",
                expected.DefinitionId!.Trim());
            command.Parameters.AddWithValue(
                "definitionVersion",
                expected.DefinitionVersion);
            command.Parameters.AddWithValue("packageId", packageId);
            command.Parameters.AddWithValue("packageVersion", packageVersion);
            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                missing.Add(Failure("action_effect", expected));
                continue;
            }
            var checksum = reader.GetString(0);
            actions.Add(new PublishedActionDefinitionSummary(
                expected.DefinitionId.Trim(),
                expected.DefinitionVersion,
                packageVersion,
                checksum));
            if (HasChecksumMismatch(expected, checksum, false))
                mismatched.Add(Failure("action_effect", expected));
        }

        var rejectionCode = ContentPackagePreflightPolicy.ResolveRejectionCode(
            activeMatch,
            missing.Count,
            mismatched.Count);
        var ready = rejectionCode == null;
        return new ContentPackagePreflightResult(
            ready,
            rejectionCode,
            packageId,
            packageVersion,
            activeMatch,
            rules,
            actions,
            missing,
            mismatched);
    }

    private static bool TryValidatePreflightIdentities(
        IReadOnlyList<ContentDefinitionPreflightIdentity> identities,
        bool isRule)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var identity in identities)
        {
            var id = identity.DefinitionId?.Trim() ?? string.Empty;
            if (!SemanticId.IsValid(id) || identity.DefinitionVersion < 1 ||
                !seen.Add(id + "\u001f" + identity.DefinitionVersion))
                return false;
            if (!string.IsNullOrWhiteSpace(identity.Checksum) &&
                (identity.Checksum.Trim().Length != 64 ||
                 identity.Checksum.Trim().Any(value => !Uri.IsHexDigit(value))))
                return false;
            if (!string.IsNullOrWhiteSpace(identity.PayloadJson) &&
                ResolveCanonicalPayloadChecksum(identity, isRule) is null)
                return false;
        }
        return true;
    }

    private static bool HasChecksumMismatch(
        ContentDefinitionPreflightIdentity expected,
        string actual,
        bool isRule)
    {
        if (!string.IsNullOrWhiteSpace(expected.Checksum) &&
            !string.Equals(expected.Checksum.Trim(), actual,
                StringComparison.OrdinalIgnoreCase))
            return true;
        var payloadChecksum = ResolveCanonicalPayloadChecksum(expected, isRule);
        return payloadChecksum is not null &&
               !string.Equals(payloadChecksum, actual,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static string? ResolveCanonicalPayloadChecksum(
        ContentDefinitionPreflightIdentity identity,
        bool isRule)
    {
        if (string.IsNullOrWhiteSpace(identity.PayloadJson)) return null;
        if (isRule)
        {
            var validation = ValidateRules(
            [
                new ContentRuleDefinitionPublishRequest(
                    identity.DefinitionId, identity.DefinitionVersion,
                    identity.PayloadJson)
            ]);
            return validation.Failure is null
                ? validation.Values![0].Checksum
                : null;
        }
        var actionValidation = ValidateActions(
        [
            new ContentActionDefinitionPublishRequest(
                identity.DefinitionId, identity.DefinitionVersion,
                identity.PayloadJson)
        ]);
        return actionValidation.Failure is null
            ? actionValidation.Values![0].Checksum
            : null;
    }

    internal static string? ComputePreflightPayloadChecksumForTest(
        ContentDefinitionPreflightIdentity identity,
        bool isRule) => ResolveCanonicalPayloadChecksum(identity, isRule);

    private static ContentDefinitionPreflightFailure Failure(
        string kind,
        ContentDefinitionPreflightIdentity identity) =>
        new(kind, identity.DefinitionId!.Trim(), identity.DefinitionVersion);

    private static ContentPackagePreflightResult EmptyPreflight(
        string packageId,
        string packageVersion,
        string rejectionCode) =>
        new(
            false,
            rejectionCode,
            packageId,
            packageVersion,
            false,
            [],
            [],
            [],
            []);

    public async Task<ContentRuleCatalogPublishResult> PublishRules(
        string packageId,
        Guid actorUserId,
        ContentRuleCatalogPublishRequest request,
        CancellationToken cancellationToken)
    {
        packageId = packageId?.Trim() ?? string.Empty;
        var packageVersion = request.PackageVersion?.Trim() ?? string.Empty;
        var rules = request.Rules ?? [];
        if (!SemanticId.IsValid(packageId) || packageVersion.Length is < 1 or > 64 ||
            rules.Count is < 1 or > 250)
        {
            return Rejected("invalid_content_publish_request");
        }

        var validation = ValidateRules(rules);
        if (validation.Failure != null) return validation.Failure;
        var validated = validation.Values!;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            if (!await EnsurePackageAccess(connection, transaction, packageId, actorUserId, cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                return Rejected("content_package_forbidden");
            }

            var batch = await InsertRules(
                connection, transaction, packageId, packageVersion,
                validated, cancellationToken);
            if (batch.RejectionCode != null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Rejected(batch.RejectionCode);
            }

            await transaction.CommitAsync(cancellationToken);
            return new ContentRuleCatalogPublishResult(
                true, null, batch.Published, batch.Unchanged, null);
        }
        catch (PostgresException)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    internal static string SerializeCanonicalRulePayload(
        OntologyRuleDefinition definition)
    {
        var canonicalPayload = JsonSerializer.Serialize(definition, RuleJson);
        var node = JsonNode.Parse(canonicalPayload) as JsonObject;
        if (node == null)
            return canonicalPayload;

        // Optional schema fields added after an immutable Rule Block version
        // was published must preserve the legacy wire meaning when they carry
        // only their default value. Configured values remain first-class
        // immutable content.
        if (string.IsNullOrWhiteSpace(
                definition.runtimePresentation?.actorAnimationIntent))
        {
            node.Remove("runtimePresentation");
        }

        if (node["effects"] is JsonArray effects)
        {
            foreach (var effectNode in effects)
            {
                if (effectNode is not JsonObject effect ||
                    effect["resultLifetime"] is not JsonValue lifetime)
                {
                    continue;
                }

                var isRuleBound =
                    lifetime.TryGetValue<int>(out var numericLifetime) &&
                    numericLifetime ==
                    (int)OntologyRuleResultLifetime.RuleBound;
                if (!isRuleBound &&
                    lifetime.TryGetValue<string>(out var namedLifetime))
                {
                    isRuleBound = string.Equals(
                        namedLifetime,
                        nameof(OntologyRuleResultLifetime.RuleBound),
                        StringComparison.OrdinalIgnoreCase);
                }

                if (isRuleBound)
                    effect.Remove("resultLifetime");
            }
        }

        return node.ToJsonString(RuleJson);
    }

    internal static string[] ResolveDurableResultPredicates(
        OntologyRuleDefinition definition) =>
        definition?.effects?
            .Where(effect =>
                effect != null &&
                effect.resultLifetime ==
                OntologyRuleResultLifetime.DurableState &&
                !string.IsNullOrWhiteSpace(effect.predicate))
            .Select(effect => effect.predicate.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray() ?? [];

    public async Task<ContentRuleCatalogReadResult> GetPublishedRules(
        string packageId,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        packageId = packageId?.Trim() ?? string.Empty;
        if (!SemanticId.IsValid(packageId))
        {
            return new ContentRuleCatalogReadResult("invalid_content_package_id", null);
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        const string accessSql = """
            SELECT
                EXISTS (SELECT 1 FROM content_packages WHERE package_id = @packageId),
                EXISTS (
                    SELECT 1 FROM content_package_members
                    WHERE package_id = @packageId AND user_id = @actorUserId);
            """;
        await using var access = new NpgsqlCommand(accessSql, connection);
        access.Parameters.AddWithValue("packageId", packageId);
        access.Parameters.AddWithValue("actorUserId", actorUserId);
        await using var accessReader = await access.ExecuteReaderAsync(cancellationToken);
        await accessReader.ReadAsync(cancellationToken);
        var exists = accessReader.GetBoolean(0);
        var canRead = accessReader.GetBoolean(1);
        await accessReader.DisposeAsync();
        if (!exists) return new ContentRuleCatalogReadResult("content_package_not_found", null);
        if (!canRead) return new ContentRuleCatalogReadResult("content_package_forbidden", null);

        const string rulesSql = """
            SELECT definition_id, definition_version, package_version, checksum
            FROM content_definitions
            WHERE definition_kind = 'rule' AND package_id = @packageId AND is_published
            ORDER BY definition_id, definition_version;
            """;
        await using var rulesCommand = new NpgsqlCommand(rulesSql, connection);
        rulesCommand.Parameters.AddWithValue("packageId", packageId);
        await using var reader = await rulesCommand.ExecuteReaderAsync(cancellationToken);
        var rules = new List<PublishedRuleDefinitionSummary>();
        while (await reader.ReadAsync(cancellationToken))
        {
            rules.Add(new PublishedRuleDefinitionSummary(
                reader.GetString(0), reader.GetInt32(1), reader.GetString(2), reader.GetString(3)));
        }
        return new ContentRuleCatalogReadResult(null, rules);
    }

    /// <summary>
    /// Publishes immutable authoritative action definitions. Clients submit only
    /// actor/target/tool identities; conditions and durable mutations remain owned
    /// by the versioned definition and the server evaluator.
    /// </summary>
    public async Task<ContentActionCatalogPublishResult> PublishActionEffects(
        string packageId,
        Guid actorUserId,
        ContentActionCatalogPublishRequest request,
        CancellationToken cancellationToken)
    {
        packageId = packageId?.Trim() ?? string.Empty;
        var packageVersion = request.PackageVersion?.Trim() ?? string.Empty;
        var actions = request.Actions ?? [];
        if (!SemanticId.IsValid(packageId) || packageVersion.Length is < 1 or > 64 || actions.Count is < 1 or > 250)
            return new ContentActionCatalogPublishResult(false, "invalid_content_publish_request", 0, 0, null);

        var validation = ValidateActions(actions);
        if (validation.Failure != null) return validation.Failure;
        var validated = validation.Values!;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (!await EnsurePackageAccess(connection, transaction, packageId, actorUserId, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return new ContentActionCatalogPublishResult(false, "content_package_forbidden", 0, 0, null);
        }

        var batch = await InsertActions(
            connection, transaction, packageId, packageVersion,
            validated, cancellationToken);
        if (batch.RejectionCode != null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new ContentActionCatalogPublishResult(
                false, batch.RejectionCode, 0, 0, null);
        }
        await transaction.CommitAsync(cancellationToken);
        return new ContentActionCatalogPublishResult(
            true, null, batch.Published, batch.Unchanged, null);
    }

    /// <summary>
    /// Publishes one immutable content release. Validation happens before the
    /// transaction and every Rule and Action insert shares the same connection
    /// and transaction. Any conflict rolls the complete release back.
    /// </summary>
    public async Task<ContentReleasePublishResult> PublishRelease(
        string packageId,
        Guid actorUserId,
        ContentReleasePublishRequest request,
        CancellationToken cancellationToken)
    {
        packageId = packageId?.Trim() ?? string.Empty;
        var packageVersion = request.PackageVersion?.Trim() ?? string.Empty;
        var requestedRules = request.Rules ?? [];
        var requestedActions = request.Actions ?? [];
        if (!SemanticId.IsValid(packageId) ||
            packageVersion.Length is < 1 or > 64 ||
            requestedRules.Count is < 1 or > 250 ||
            requestedActions.Count is < 1 or > 250)
        {
            return ContentReleasePublishResult.Rejected(
                "invalid_content_release_request");
        }

        var ruleValidation = ValidateRules(requestedRules);
        if (ruleValidation.Failure != null)
            return ContentReleasePublishResult.Rejected(
                ruleValidation.Failure.RejectionCode!,
                ruleValidation.Failure.ValidationMessages);
        var actionValidation = ValidateActions(requestedActions);
        if (actionValidation.Failure != null)
            return ContentReleasePublishResult.Rejected(
                actionValidation.Failure.RejectionCode!,
                actionValidation.Failure.ValidationMessages);

        var rules = ruleValidation.Values!;
        var actions = actionValidation.Values!;
        var manifestChecksum = ComputeManifestChecksum(rules, actions);
        if (!string.IsNullOrWhiteSpace(request.ManifestChecksum) &&
            !string.Equals(
                request.ManifestChecksum.Trim(),
                manifestChecksum,
                StringComparison.OrdinalIgnoreCase))
        {
            return ContentReleasePublishResult.Rejected(
                "content_release_manifest_checksum_mismatch");
        }

        await using var connection =
            await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction =
            await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            if (!await EnsurePackageAccess(
                    connection, transaction, packageId, actorUserId,
                    cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                return ContentReleasePublishResult.Rejected(
                    "content_package_forbidden");
            }

            var ruleBatch = await InsertRules(
                connection, transaction, packageId, packageVersion,
                rules, cancellationToken);
            if (ruleBatch.RejectionCode != null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return ContentReleasePublishResult.Rejected(
                    ruleBatch.RejectionCode);
            }

            var actionBatch = await InsertActions(
                connection, transaction, packageId, packageVersion,
                actions, cancellationToken);
            if (actionBatch.RejectionCode != null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return ContentReleasePublishResult.Rejected(
                    actionBatch.RejectionCode);
            }

            await transaction.CommitAsync(cancellationToken);
            return new ContentReleasePublishResult(
                true,
                null,
                manifestChecksum,
                ruleBatch.Published,
                ruleBatch.Unchanged,
                actionBatch.Published,
                actionBatch.Unchanged,
                null);
        }
        catch (PostgresException)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<ContentActionCatalogReadResult> GetPublishedActionEffects(string packageId, Guid actorUserId, CancellationToken cancellationToken)
    {
        packageId = packageId?.Trim() ?? string.Empty;
        if (!SemanticId.IsValid(packageId)) return new ContentActionCatalogReadResult("invalid_content_package_id", null);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        const string sql = """
            SELECT d.definition_id, d.definition_version, d.package_version, d.checksum
            FROM content_definitions d
            WHERE d.definition_kind = 'action_effect' AND d.package_id = @packageId AND d.is_published
              AND EXISTS (SELECT 1 FROM content_package_members m WHERE m.package_id = d.package_id AND m.user_id = @actorUserId)
            ORDER BY d.definition_id, d.definition_version;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("packageId", packageId);
        command.Parameters.AddWithValue("actorUserId", actorUserId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var actions = new List<PublishedActionDefinitionSummary>();
        while (await reader.ReadAsync(cancellationToken)) actions.Add(new PublishedActionDefinitionSummary(reader.GetString(0), reader.GetInt32(1), reader.GetString(2), reader.GetString(3)));
        return new ContentActionCatalogReadResult(null, actions);
    }

    internal static string SerializeCanonicalActionPayload(
        OntologyActionEffectDefinition definition)
    {
        var canonicalPayload = JsonSerializer.Serialize(definition, RuleJson);
        var node = JsonNode.Parse(canonicalPayload) as JsonObject;
        if (node == null)
            return canonicalPayload;

        // A default-only observation constraint was added after existing
        // immutable action versions were published. Omitting false preserves
        // their original wire checksum, while true remains authored immutable
        // content for actions such as grounded jump.
        if (definition.runtimeConstraints
                ?.requiresGroundedObservation != true &&
            node["runtimeConstraints"] is JsonObject constraints)
        {
            constraints.Remove("requiresGroundedObservation");
        }

        // This constraint was introduced for Authority-owned melee contact
        // occurrences. Keep legacy immutable action checksums stable when the
        // capability is not explicitly authored.
        if (definition.runtimeConstraints
                ?.requiresAuthorityAttackOccurrence != true &&
            node["runtimeConstraints"] is JsonObject attackConstraints)
        {
            attackConstraints.Remove("requiresAuthorityAttackOccurrence");
        }

        return node.ToJsonString(RuleJson);
    }

    private static ContentRuleCatalogPublishResult Rejected(string code) =>
        new(false, code, 0, 0, null);

    private static ValidationResult<ValidatedRule,
        ContentRuleCatalogPublishResult> ValidateRules(
        IReadOnlyList<ContentRuleDefinitionPublishRequest> rules)
    {
        var validated = new List<ValidatedRule>(rules.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var requestRule in rules)
        {
            var ruleId = requestRule.RuleId?.Trim() ?? string.Empty;
            if (!SemanticId.IsValid(ruleId) ||
                requestRule.DefinitionVersion < 1 ||
                string.IsNullOrWhiteSpace(requestRule.PayloadJson) ||
                !seen.Add(ruleId + "\u001f" + requestRule.DefinitionVersion))
            {
                return new(null, Rejected("invalid_rule_definition_identity"));
            }

            OntologyRuleDefinition? definition;
            try
            {
                definition = JsonSerializer.Deserialize<OntologyRuleDefinition>(
                    requestRule.PayloadJson, RuleJson);
            }
            catch (JsonException)
            {
                return new(null, Rejected("invalid_rule_definition_json"));
            }

            if (definition == null || !string.Equals(
                    definition.id?.Trim(), ruleId, StringComparison.Ordinal))
            {
                return new(null, Rejected("rule_definition_id_mismatch"));
            }

            var warnings = OntologyRuleValidator.Validate([definition]);
            if (warnings.Count > 0)
            {
                return new(null, new ContentRuleCatalogPublishResult(
                    false, "rule_definition_validation_failed", 0, 0,
                    warnings));
            }

            var canonicalPayload = SerializeCanonicalRulePayload(definition);
            validated.Add(new ValidatedRule(
                ruleId,
                requestRule.DefinitionVersion,
                canonicalPayload,
                Sha256(canonicalPayload),
                ResolveDurableResultPredicates(definition)));
        }
        return new(validated, null);
    }

    private static ValidationResult<ValidatedAction,
        ContentActionCatalogPublishResult> ValidateActions(
        IReadOnlyList<ContentActionDefinitionPublishRequest> actions)
    {
        var validated = new List<ValidatedAction>(actions.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var action in actions)
        {
            var actionId = action.ActionId?.Trim() ?? string.Empty;
            if (!SemanticId.IsValid(actionId) ||
                action.DefinitionVersion < 1 ||
                string.IsNullOrWhiteSpace(action.PayloadJson) ||
                !seen.Add(actionId + "\u001f" + action.DefinitionVersion))
            {
                return new(null, new ContentActionCatalogPublishResult(
                    false, "invalid_action_definition_identity", 0, 0, null));
            }

            OntologyActionEffectDefinition? definition;
            try
            {
                definition = JsonSerializer.Deserialize<
                    OntologyActionEffectDefinition>(action.PayloadJson, RuleJson);
            }
            catch (JsonException)
            {
                return new(null, new ContentActionCatalogPublishResult(
                    false, "invalid_action_definition_json", 0, 0, null));
            }

            if (definition is null || !string.Equals(
                    definition.actionVerb?.Trim(), actionId,
                    StringComparison.Ordinal) ||
                !AuthoritativeActionEvaluator.IsSupportedDefinition(
                    definition, out _))
            {
                return new(null, new ContentActionCatalogPublishResult(
                    false, "unsupported_authoritative_action_definition",
                    0, 0, null));
            }

            var canonicalPayload = SerializeCanonicalActionPayload(definition);
            validated.Add(new ValidatedAction(
                actionId,
                action.DefinitionVersion,
                canonicalPayload,
                Sha256(canonicalPayload)));
        }
        return new(validated, null);
    }

    private static async Task<PublishBatchOutcome> InsertRules(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string packageId,
        string packageVersion,
        IReadOnlyList<ValidatedRule> rules,
        CancellationToken cancellationToken)
    {
        var published = 0;
        var unchanged = 0;
        foreach (var rule in rules)
        {
            var outcome = await InsertImmutableRule(
                connection, transaction, packageId, packageVersion, rule,
                cancellationToken);
            if (outcome == InsertOutcome.Conflict)
            {
                return new(0, 0,
                    "rule_definition_version_conflict:" +
                    rule.Id + ":" + rule.Version);
            }
            if (outcome == InsertOutcome.Inserted) published++;
            else unchanged++;

            await PromoteLegacyDurableRuleResults(
                connection, transaction, rule, cancellationToken);
        }
        return new(published, unchanged, null);
    }

    private static async Task<PublishBatchOutcome> InsertActions(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string packageId,
        string packageVersion,
        IReadOnlyList<ValidatedAction> actions,
        CancellationToken cancellationToken)
    {
        var published = 0;
        var unchanged = 0;
        foreach (var action in actions)
        {
            var outcome = await InsertImmutableAction(
                connection, transaction, packageId, packageVersion, action,
                cancellationToken);
            if (outcome == InsertOutcome.Conflict)
            {
                return new(0, 0,
                    "action_definition_version_conflict:" +
                    action.Id + ":" + action.Version);
            }
            if (outcome == InsertOutcome.Inserted) published++;
            else unchanged++;
        }
        return new(published, unchanged, null);
    }

    internal static string ComputeManifestChecksumForTest(
        IReadOnlyList<(string Kind, string Id, int Version, string Checksum)>
            definitions) =>
        Sha256(string.Join("\n", definitions
            .OrderBy(value => value.Kind, StringComparer.Ordinal)
            .ThenBy(value => value.Id, StringComparer.Ordinal)
            .ThenBy(value => value.Version)
            .Select(value => value.Kind + "\u001f" + value.Id + "\u001f" +
                             value.Version + "\u001f" + value.Checksum)));

    private static string ComputeManifestChecksum(
        IReadOnlyList<ValidatedRule> rules,
        IReadOnlyList<ValidatedAction> actions) =>
        ComputeManifestChecksumForTest(
            rules.Select(value =>
                    ("rule", value.Id, value.Version, value.Checksum))
                .Concat(actions.Select(value =>
                    ("action_effect", value.Id, value.Version, value.Checksum)))
                .ToArray());

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private static async Task<bool> EnsurePackageAccess(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string packageId,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        const string createPackage = """
            INSERT INTO content_packages (package_id, owner_user_id)
            VALUES (@packageId, @actorUserId)
            ON CONFLICT (package_id) DO NOTHING;
            """;
        await using (var command = new NpgsqlCommand(createPackage, connection, transaction))
        {
            command.Parameters.AddWithValue("packageId", packageId);
            command.Parameters.AddWithValue("actorUserId", actorUserId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        const string addOwner = """
            INSERT INTO content_package_members (package_id, user_id, role)
            SELECT @packageId, @actorUserId, 'owner'
            WHERE EXISTS (
                SELECT 1 FROM content_packages
                WHERE package_id = @packageId AND owner_user_id = @actorUserId)
            ON CONFLICT (package_id, user_id) DO NOTHING;
            """;
        await using (var command = new NpgsqlCommand(addOwner, connection, transaction))
        {
            command.Parameters.AddWithValue("packageId", packageId);
            command.Parameters.AddWithValue("actorUserId", actorUserId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        const string canPublish = """
            SELECT EXISTS (
                SELECT 1 FROM content_package_members
                WHERE package_id = @packageId AND user_id = @actorUserId
                  AND role IN ('owner', 'editor'));
            """;
        await using var access = new NpgsqlCommand(canPublish, connection, transaction);
        access.Parameters.AddWithValue("packageId", packageId);
        access.Parameters.AddWithValue("actorUserId", actorUserId);
        return (bool)(await access.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task<InsertOutcome> InsertImmutableRule(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string packageId,
        string packageVersion,
        ValidatedRule rule,
        CancellationToken cancellationToken)
    {
        const string insert = """
            INSERT INTO content_definitions
                (definition_kind, definition_id, definition_version, package_id,
                 package_version, payload, checksum, is_published)
            VALUES
                ('rule', @ruleId, @definitionVersion, @packageId,
                 @packageVersion, CAST(@payload AS jsonb), @checksum, true)
            ON CONFLICT (definition_kind, definition_id, definition_version)
                WHERE definition_kind <> 'action_effect'
            DO NOTHING;
            """;
        await using (var command = new NpgsqlCommand(insert, connection, transaction))
        {
            command.Parameters.AddWithValue("ruleId", rule.Id);
            command.Parameters.AddWithValue("definitionVersion", rule.Version);
            command.Parameters.AddWithValue("packageId", packageId);
            command.Parameters.AddWithValue("packageVersion", packageVersion);
            command.Parameters.Add("payload", NpgsqlDbType.Jsonb).Value = rule.PayloadJson;
            command.Parameters.AddWithValue("checksum", rule.Checksum);
            if (await command.ExecuteNonQueryAsync(cancellationToken) == 1)
            {
                return InsertOutcome.Inserted;
            }
        }

        const string existing = """
            SELECT package_id, checksum, is_published
            FROM content_definitions
            WHERE definition_kind = 'rule' AND definition_id = @ruleId
              AND definition_version = @definitionVersion;
            """;
        await using var check = new NpgsqlCommand(existing, connection, transaction);
        check.Parameters.AddWithValue("ruleId", rule.Id);
        check.Parameters.AddWithValue("definitionVersion", rule.Version);
        await using var reader = await check.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken) ||
            !string.Equals(reader.GetString(1), rule.Checksum, StringComparison.Ordinal) ||
            !reader.GetBoolean(2))
        {
            return InsertOutcome.Conflict;
        }

        // Canonical Rule Blocks are globally identified by id + version.
        // An identical immutable definition may be reused by another package;
        // a different payload at the same identity remains a hard conflict.
        return InsertOutcome.Unchanged;
    }

    private static async Task PromoteLegacyDurableRuleResults(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ValidatedRule rule,
        CancellationToken cancellationToken)
    {
        if (rule.DurableResultPredicates.Length == 0)
        {
            return;
        }

        const string promote = """
            UPDATE world_facts AS fact
            SET rule_result_lifetime = 'durable_state'
            FROM world_rule_bindings AS binding
            WHERE fact.source_rule_binding_id = binding.binding_id
              AND binding.rule_id = @ruleId
              AND fact.predicate_id = ANY(@predicateIds)
              AND fact.source_type = 'action'
              AND fact.retracted_revision IS NULL
              AND fact.rule_result_lifetime = 'rule_bound';
            """;
        await using var command =
            new NpgsqlCommand(promote, connection, transaction);
        command.Parameters.AddWithValue("ruleId", rule.Id);
        command.Parameters.AddWithValue(
            "predicateIds",
            NpgsqlDbType.Array | NpgsqlDbType.Text,
            rule.DurableResultPredicates);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<InsertOutcome> InsertImmutableAction(NpgsqlConnection connection, NpgsqlTransaction transaction, string packageId, string packageVersion, ValidatedAction action, CancellationToken cancellationToken)
    {
        const string insert = """
            INSERT INTO content_definitions (definition_kind, definition_id, definition_version, package_id, package_version, payload, checksum, is_published)
            VALUES ('action_effect', @actionId, @definitionVersion, @packageId, @packageVersion, CAST(@payload AS jsonb), @checksum, true)
            ON CONFLICT
                (definition_kind, definition_id, definition_version,
                 package_id, package_version)
                WHERE definition_kind = 'action_effect'
            DO NOTHING;
            """;
        await using (var command = new NpgsqlCommand(insert, connection, transaction))
        {
            command.Parameters.AddWithValue("actionId", action.Id); command.Parameters.AddWithValue("definitionVersion", action.Version);
            command.Parameters.AddWithValue("packageId", packageId); command.Parameters.AddWithValue("packageVersion", packageVersion);
            command.Parameters.Add("payload", NpgsqlDbType.Jsonb).Value = action.PayloadJson; command.Parameters.AddWithValue("checksum", action.Checksum);
            if (await command.ExecuteNonQueryAsync(cancellationToken) == 1) return InsertOutcome.Inserted;
        }
        const string existing = """
            SELECT checksum, is_published FROM content_definitions
            WHERE definition_kind = 'action_effect'
              AND definition_id = @actionId
              AND definition_version = @definitionVersion
              AND package_id = @packageId
              AND package_version = @packageVersion;
            """;
        await using var check = new NpgsqlCommand(existing, connection, transaction);
        check.Parameters.AddWithValue("actionId", action.Id); check.Parameters.AddWithValue("definitionVersion", action.Version);
        check.Parameters.AddWithValue("packageId", packageId); check.Parameters.AddWithValue("packageVersion", packageVersion);
        await using var reader = await check.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) &&
               string.Equals(reader.GetString(0), action.Checksum, StringComparison.Ordinal) &&
               reader.GetBoolean(1)
            ? InsertOutcome.Unchanged : InsertOutcome.Conflict;
    }

    private sealed record ValidatedRule(
        string Id,
        int Version,
        string PayloadJson,
        string Checksum,
        string[] DurableResultPredicates);
    private sealed record ValidatedAction(string Id, int Version, string PayloadJson, string Checksum);
    private sealed record ValidationResult<TValue, TFailure>(
        IReadOnlyList<TValue>? Values,
        TFailure? Failure);
    private sealed record PublishBatchOutcome(
        int Published,
        int Unchanged,
        string? RejectionCode);
    private enum InsertOutcome { Inserted, Unchanged, Conflict }
}
