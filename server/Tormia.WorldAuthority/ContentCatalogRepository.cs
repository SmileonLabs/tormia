using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
            var canonicalPayload = JsonSerializer.Serialize(definition, RuleJson);
            validated.Add(new ValidatedRule(
                ruleId,
                requestRule.DefinitionVersion,
                canonicalPayload,
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalPayload))).ToLowerInvariant()));
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
                    return Rejected("rule_definition_version_conflict");
                }
                if (outcome == InsertOutcome.Inserted) published++;
                else unchanged++;
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
            !string.Equals(reader.GetString(0), packageId, StringComparison.Ordinal) ||
            !string.Equals(reader.GetString(1), rule.Checksum, StringComparison.Ordinal) ||
            !reader.GetBoolean(2))
        {
            return InsertOutcome.Conflict;
        }
        return InsertOutcome.Unchanged;
    }

    private sealed record ValidatedRule(string Id, int Version, string PayloadJson, string Checksum);
    private enum InsertOutcome { Inserted, Unchanged, Conflict }
}
