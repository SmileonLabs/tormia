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

        var validated = new List<ValidatedRule>(rules.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var requestRule in rules)
        {
            var ruleId = requestRule.RuleId?.Trim() ?? string.Empty;
            if (!SemanticId.IsValid(ruleId) || requestRule.DefinitionVersion < 1 ||
                string.IsNullOrWhiteSpace(requestRule.PayloadJson) ||
                !seen.Add(ruleId + "\u001f" + requestRule.DefinitionVersion))
            {
                return Rejected("invalid_rule_definition_identity");
            }

            OntologyRuleDefinition? definition;
            try
            {
                definition = JsonSerializer.Deserialize<OntologyRuleDefinition>(
                    requestRule.PayloadJson, RuleJson);
            }
            catch (JsonException)
            {
                return Rejected("invalid_rule_definition_json");
            }

            if (definition == null || !string.Equals(definition.id?.Trim(), ruleId,
                    StringComparison.Ordinal))
            {
                return Rejected("rule_definition_id_mismatch");
            }

            var warnings = OntologyRuleValidator.Validate([definition]);
            if (warnings.Count > 0)
            {
                return new ContentRuleCatalogPublishResult(
                    false, "rule_definition_validation_failed", 0, 0, warnings);
            }

            // Re-serialize before hashing/storage so whitespace or client JSON field
            // order cannot create a false new version of the same ontology rule.
            var canonicalPayload = SerializeCanonicalRulePayload(definition);
            validated.Add(new ValidatedRule(
                ruleId,
                requestRule.DefinitionVersion,
                canonicalPayload,
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalPayload))).ToLowerInvariant(),
                ResolveDurableResultPredicates(definition)));
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            if (!await EnsurePackageAccess(connection, transaction, packageId, actorUserId, cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                return Rejected("content_package_forbidden");
            }

            var published = 0;
            var unchanged = 0;
            foreach (var rule in validated)
            {
                var outcome = await InsertImmutableRule(
                    connection, transaction, packageId, packageVersion, rule, cancellationToken);
                if (outcome == InsertOutcome.Conflict)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return Rejected(
                        "rule_definition_version_conflict:" +
                        rule.Id + ":" + rule.Version);
                }
                if (outcome == InsertOutcome.Inserted) published++;
                else unchanged++;

                // Older rows predate explicit result-lifetime metadata. A
                // newly published immutable Rule Block is the data-owned
                // migration contract for those rows: only facts produced by a
                // binding of the same Rule ID and a predicate now declared as
                // DurableState are promoted. No gameplay predicate is encoded
                // in Authority code.
                await PromoteLegacyDurableRuleResults(
                    connection,
                    transaction,
                    rule,
                    cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return new ContentRuleCatalogPublishResult(true, null, published, unchanged, null);
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

        var validated = new List<ValidatedAction>(actions.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var action in actions)
        {
            var actionId = action.ActionId?.Trim() ?? string.Empty;
            if (!SemanticId.IsValid(actionId) || action.DefinitionVersion < 1 || string.IsNullOrWhiteSpace(action.PayloadJson) ||
                !seen.Add(actionId + "\u001f" + action.DefinitionVersion))
                return new ContentActionCatalogPublishResult(false, "invalid_action_definition_identity", 0, 0, null);

            OntologyActionEffectDefinition? definition;
            try { definition = JsonSerializer.Deserialize<OntologyActionEffectDefinition>(action.PayloadJson, RuleJson); }
            catch (JsonException) { return new ContentActionCatalogPublishResult(false, "invalid_action_definition_json", 0, 0, null); }

            if (definition is null || !string.Equals(definition.actionVerb?.Trim(), actionId, StringComparison.Ordinal) ||
                !AuthoritativeActionEvaluator.IsSupportedDefinition(definition, out _))
                return new ContentActionCatalogPublishResult(false, "unsupported_authoritative_action_definition", 0, 0, null);

            var canonicalPayload =
                SerializeCanonicalActionPayload(definition);
            validated.Add(new ValidatedAction(actionId, action.DefinitionVersion, canonicalPayload,
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalPayload))).ToLowerInvariant()));
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (!await EnsurePackageAccess(connection, transaction, packageId, actorUserId, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return new ContentActionCatalogPublishResult(false, "content_package_forbidden", 0, 0, null);
        }

        var published = 0;
        var unchanged = 0;
        foreach (var action in validated)
        {
            var outcome = await InsertImmutableAction(connection, transaction, packageId, packageVersion, action, cancellationToken);
            if (outcome == InsertOutcome.Conflict)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new ContentActionCatalogPublishResult(false, "action_definition_version_conflict", 0, 0, null);
            }
            if (outcome == InsertOutcome.Inserted) published++; else unchanged++;
        }
        await transaction.CommitAsync(cancellationToken);
        return new ContentActionCatalogPublishResult(true, null, published, unchanged, null);
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

        return node.ToJsonString(RuleJson);
    }

    private static ContentRuleCatalogPublishResult Rejected(string code) =>
        new(false, code, 0, 0, null);

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
    private enum InsertOutcome { Inserted, Unchanged, Conflict }
}
