using System.Text.Json;
using Microsoft.Data.Sqlite;
using Thoth.Cognition.Concepts;

namespace Thoth.Memory.Cognition;

public sealed class SqliteConceptGraphStore(string databasePath) : IConceptGraphStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string databasePath = Path.GetFullPath(databasePath);

    public async Task EnsureCreatedAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
        CREATE TABLE IF NOT EXISTS concept_sources (
            source_id TEXT PRIMARY KEY,
            source_type TEXT NOT NULL,
            uri TEXT NOT NULL,
            license TEXT NOT NULL,
            attribution TEXT NOT NULL,
            created_at TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS concepts (
            concept_id TEXT PRIMARY KEY,
            concept_type TEXT NOT NULL,
            canonical_name TEXT NOT NULL,
            status TEXT NOT NULL,
            confidence REAL NOT NULL,
            source_id TEXT NOT NULL,
            metadata_json TEXT NOT NULL,
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL,
            FOREIGN KEY (source_id) REFERENCES concept_sources(source_id)
        );

        CREATE TABLE IF NOT EXISTS concept_aliases (
            alias_id TEXT PRIMARY KEY,
            concept_id TEXT NOT NULL,
            alias TEXT NOT NULL,
            normalized_alias TEXT NOT NULL,
            language TEXT NULL,
            confidence REAL NOT NULL,
            source_id TEXT NOT NULL,
            created_at TEXT NOT NULL,
            FOREIGN KEY (concept_id) REFERENCES concepts(concept_id) ON DELETE CASCADE,
            FOREIGN KEY (source_id) REFERENCES concept_sources(source_id)
        );

        CREATE TABLE IF NOT EXISTS contextual_relations (
            relation_id TEXT PRIMARY KEY,
            relation_type TEXT NOT NULL,
            status TEXT NOT NULL,
            confidence REAL NOT NULL,
            source_id TEXT NOT NULL,
            conditions_json TEXT NOT NULL,
            metadata_json TEXT NOT NULL,
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL,
            FOREIGN KEY (source_id) REFERENCES concept_sources(source_id)
        );

        CREATE TABLE IF NOT EXISTS relation_participants (
            relation_id TEXT NOT NULL,
            role TEXT NOT NULL,
            concept_id TEXT NOT NULL,
            PRIMARY KEY (relation_id, role, concept_id),
            FOREIGN KEY (relation_id) REFERENCES contextual_relations(relation_id) ON DELETE CASCADE,
            FOREIGN KEY (concept_id) REFERENCES concepts(concept_id) ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS relation_context (
            relation_id TEXT NOT NULL,
            key TEXT NOT NULL,
            value TEXT NOT NULL,
            PRIMARY KEY (relation_id, key),
            FOREIGN KEY (relation_id) REFERENCES contextual_relations(relation_id) ON DELETE CASCADE
        );

        CREATE INDEX IF NOT EXISTS idx_concept_aliases_normalized
        ON concept_aliases(normalized_alias);

        CREATE INDEX IF NOT EXISTS idx_concepts_type_status
        ON concepts(concept_type, status);

        CREATE INDEX IF NOT EXISTS idx_relations_type_status
        ON contextual_relations(relation_type, status);

        CREATE INDEX IF NOT EXISTS idx_relation_participants_concept
        ON relation_participants(concept_id, relation_id);

        CREATE INDEX IF NOT EXISTS idx_relation_context_key_value
        ON relation_context(key, value);
        """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SaveSourceAsync(EvidenceSource source, CancellationToken cancellationToken = default)
    {
        await EnsureCreatedAsync(cancellationToken);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
        INSERT INTO concept_sources (source_id, source_type, uri, license, attribution, created_at)
        VALUES ($sourceId, $sourceType, $uri, $license, $attribution, $createdAt)
        ON CONFLICT(source_id) DO UPDATE SET
            source_type = excluded.source_type,
            uri = excluded.uri,
            license = excluded.license,
            attribution = excluded.attribution;
        """;
        command.Parameters.AddWithValue("$sourceId", source.SourceId);
        command.Parameters.AddWithValue("$sourceType", source.SourceType);
        command.Parameters.AddWithValue("$uri", source.Uri);
        command.Parameters.AddWithValue("$license", source.License);
        command.Parameters.AddWithValue("$attribution", source.Attribution);
        command.Parameters.AddWithValue("$createdAt", source.CreatedAt.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpsertConceptAsync(Concept concept, CancellationToken cancellationToken = default)
    {
        await EnsureCreatedAsync(cancellationToken);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
        INSERT INTO concepts
            (concept_id, concept_type, canonical_name, status, confidence, source_id, metadata_json, created_at, updated_at)
        VALUES
            ($conceptId, $conceptType, $canonicalName, $status, $confidence, $sourceId, $metadataJson, $createdAt, $updatedAt)
        ON CONFLICT(concept_id) DO UPDATE SET
            concept_type = excluded.concept_type,
            canonical_name = excluded.canonical_name,
            status = excluded.status,
            confidence = excluded.confidence,
            source_id = excluded.source_id,
            metadata_json = excluded.metadata_json,
            updated_at = excluded.updated_at;
        """;
        command.Parameters.AddWithValue("$conceptId", concept.Id.Value);
        command.Parameters.AddWithValue("$conceptType", concept.ConceptType);
        command.Parameters.AddWithValue("$canonicalName", concept.CanonicalName);
        command.Parameters.AddWithValue("$status", concept.Status.ToString());
        command.Parameters.AddWithValue("$confidence", concept.Confidence.Clamped);
        command.Parameters.AddWithValue("$sourceId", concept.SourceId);
        command.Parameters.AddWithValue("$metadataJson", JsonSerializer.Serialize(concept.Metadata, JsonOptions));
        command.Parameters.AddWithValue("$createdAt", concept.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", concept.UpdatedAt.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task AddAliasAsync(ConceptAlias alias, CancellationToken cancellationToken = default)
    {
        await EnsureCreatedAsync(cancellationToken);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
        INSERT INTO concept_aliases
            (alias_id, concept_id, alias, normalized_alias, language, confidence, source_id, created_at)
        VALUES
            ($aliasId, $conceptId, $alias, $normalizedAlias, $language, $confidence, $sourceId, $createdAt)
        ON CONFLICT(alias_id) DO UPDATE SET
            alias = excluded.alias,
            normalized_alias = excluded.normalized_alias,
            language = excluded.language,
            confidence = excluded.confidence;
        """;
        command.Parameters.AddWithValue("$aliasId", alias.AliasId);
        command.Parameters.AddWithValue("$conceptId", alias.ConceptId.Value);
        command.Parameters.AddWithValue("$alias", alias.Alias);
        command.Parameters.AddWithValue("$normalizedAlias", alias.NormalizedAlias);
        command.Parameters.AddWithValue("$language", (object?)alias.Language ?? DBNull.Value);
        command.Parameters.AddWithValue("$confidence", alias.Confidence.Clamped);
        command.Parameters.AddWithValue("$sourceId", alias.SourceId);
        command.Parameters.AddWithValue("$createdAt", alias.CreatedAt.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task AddRelationAsync(ContextualRelation relation, CancellationToken cancellationToken = default)
    {
        await EnsureCreatedAsync(cancellationToken);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var relationCommand = connection.CreateCommand();
        relationCommand.Transaction = (SqliteTransaction)transaction;
        relationCommand.CommandText = """
        INSERT INTO contextual_relations
            (relation_id, relation_type, status, confidence, source_id, conditions_json, metadata_json, created_at, updated_at)
        VALUES
            ($relationId, $relationType, $status, $confidence, $sourceId, $conditionsJson, $metadataJson, $createdAt, $updatedAt)
        ON CONFLICT(relation_id) DO UPDATE SET
            relation_type = excluded.relation_type,
            status = excluded.status,
            confidence = excluded.confidence,
            source_id = excluded.source_id,
            conditions_json = excluded.conditions_json,
            metadata_json = excluded.metadata_json,
            updated_at = excluded.updated_at;
        """;
        relationCommand.Parameters.AddWithValue("$relationId", relation.RelationId);
        relationCommand.Parameters.AddWithValue("$relationType", relation.RelationType);
        relationCommand.Parameters.AddWithValue("$status", relation.Status.ToString());
        relationCommand.Parameters.AddWithValue("$confidence", relation.Confidence.Clamped);
        relationCommand.Parameters.AddWithValue("$sourceId", relation.SourceId);
        relationCommand.Parameters.AddWithValue("$conditionsJson", JsonSerializer.Serialize(relation.Conditions, JsonOptions));
        relationCommand.Parameters.AddWithValue("$metadataJson", JsonSerializer.Serialize(relation.Metadata, JsonOptions));
        relationCommand.Parameters.AddWithValue("$createdAt", relation.CreatedAt.ToString("O"));
        relationCommand.Parameters.AddWithValue("$updatedAt", relation.UpdatedAt.ToString("O"));
        await relationCommand.ExecuteNonQueryAsync(cancellationToken);

        await DeleteChildrenAsync(connection, (SqliteTransaction)transaction, relation.RelationId, cancellationToken);

        foreach (var participant in relation.Participants)
        {
            var participantCommand = connection.CreateCommand();
            participantCommand.Transaction = (SqliteTransaction)transaction;
            participantCommand.CommandText = """
            INSERT INTO relation_participants (relation_id, role, concept_id)
            VALUES ($relationId, $role, $conceptId);
            """;
            participantCommand.Parameters.AddWithValue("$relationId", relation.RelationId);
            participantCommand.Parameters.AddWithValue("$role", participant.Role);
            participantCommand.Parameters.AddWithValue("$conceptId", participant.ConceptId.Value);
            await participantCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var (key, value) in relation.Context.Values)
        {
            var contextCommand = connection.CreateCommand();
            contextCommand.Transaction = (SqliteTransaction)transaction;
            contextCommand.CommandText = """
            INSERT INTO relation_context (relation_id, key, value)
            VALUES ($relationId, $key, $value);
            """;
            contextCommand.Parameters.AddWithValue("$relationId", relation.RelationId);
            contextCommand.Parameters.AddWithValue("$key", key);
            contextCommand.Parameters.AddWithValue("$value", value);
            await contextCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Concept>> FindConceptsByAliasAsync(
        string normalizedAlias,
        int limit,
        CancellationToken cancellationToken = default)
    {
        await EnsureCreatedAsync(cancellationToken);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
        SELECT DISTINCT c.concept_id, c.concept_type, c.canonical_name, c.status, c.confidence,
               c.source_id, c.metadata_json, c.created_at, c.updated_at
        FROM concept_aliases a
        JOIN concepts c ON c.concept_id = a.concept_id
        WHERE c.status <> 'Removed'
          AND (a.normalized_alias = $alias
               OR a.normalized_alias LIKE $contains
               OR $alias LIKE '%' || a.normalized_alias || '%')
        ORDER BY a.confidence DESC, c.confidence DESC
        LIMIT $limit;
        """;
        command.Parameters.AddWithValue("$alias", normalizedAlias);
        command.Parameters.AddWithValue("$contains", $"%{normalizedAlias}%");
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 100));
        return await ReadConceptsAsync(command, cancellationToken);
    }

    public async Task<IReadOnlyList<ContextualRelation>> GetRelationsForConceptsAsync(
        IReadOnlyList<ConceptId> conceptIds,
        int limit,
        CancellationToken cancellationToken = default)
    {
        await EnsureCreatedAsync(cancellationToken);
        if (conceptIds.Count == 0)
        {
            return [];
        }

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var placeholders = conceptIds.Select((_, index) => $"$id{index}").ToArray();
        var command = connection.CreateCommand();
        command.CommandText = $"""
        SELECT DISTINCT r.relation_id, r.relation_type, r.status, r.confidence, r.source_id,
               r.conditions_json, r.metadata_json, r.created_at, r.updated_at
        FROM contextual_relations r
        JOIN relation_participants p ON p.relation_id = r.relation_id
        WHERE r.status <> 'Removed'
          AND p.concept_id IN ({string.Join(", ", placeholders)})
        ORDER BY r.confidence DESC, r.updated_at DESC
        LIMIT $limit;
        """;
        for (var index = 0; index < conceptIds.Count; index++)
        {
            command.Parameters.AddWithValue(placeholders[index], conceptIds[index].Value);
        }

        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 200));

        var rows = new List<RelationRow>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(new RelationRow(
                    reader.GetString(0),
                    reader.GetString(1),
                    Enum.Parse<RelationStatus>(reader.GetString(2), true),
                    reader.GetDouble(3),
                    reader.GetString(4),
                    JsonSerializer.Deserialize<IReadOnlyList<string>>(reader.GetString(5), JsonOptions) ?? [],
                    JsonSerializer.Deserialize<IReadOnlyDictionary<string, string>>(reader.GetString(6), JsonOptions) ?? new Dictionary<string, string>(),
                    DateTimeOffset.Parse(reader.GetString(7)),
                    DateTimeOffset.Parse(reader.GetString(8))));
            }
        }

        var results = new List<ContextualRelation>();
        foreach (var row in rows)
        {
            var participants = await ReadParticipantsAsync(connection, row.RelationId, cancellationToken);
            var context = await ReadContextAsync(connection, row.RelationId, cancellationToken);
            results.Add(new ContextualRelation(
                row.RelationId,
                row.RelationType,
                participants,
                new RelationContext(context),
                row.Conditions,
                row.Status,
                new ConfidenceScore(row.Confidence),
                row.SourceId,
                row.CreatedAt,
                row.UpdatedAt,
                row.Metadata));
        }

        return results;
    }

    private static async Task DeleteChildrenAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string relationId,
        CancellationToken cancellationToken)
    {
        foreach (var table in new[] { "relation_participants", "relation_context" })
        {
            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"DELETE FROM {table} WHERE relation_id = $relationId;";
            command.Parameters.AddWithValue("$relationId", relationId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task<IReadOnlyList<Concept>> ReadConceptsAsync(SqliteCommand command, CancellationToken cancellationToken)
    {
        var concepts = new List<Concept>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            concepts.Add(new Concept(
                new ConceptId(reader.GetString(0)),
                reader.GetString(1),
                reader.GetString(2),
                Enum.Parse<ConceptStatus>(reader.GetString(3), true),
                new ConfidenceScore(reader.GetDouble(4)),
                reader.GetString(5),
                DateTimeOffset.Parse(reader.GetString(7)),
                DateTimeOffset.Parse(reader.GetString(8)),
                JsonSerializer.Deserialize<IReadOnlyDictionary<string, string>>(reader.GetString(6), JsonOptions) ?? new Dictionary<string, string>()));
        }

        return concepts;
    }

    private static async Task<IReadOnlyList<RelationParticipant>> ReadParticipantsAsync(
        SqliteConnection connection,
        string relationId,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
        SELECT role, concept_id
        FROM relation_participants
        WHERE relation_id = $relationId
        ORDER BY role, concept_id;
        """;
        command.Parameters.AddWithValue("$relationId", relationId);

        var participants = new List<RelationParticipant>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            participants.Add(new RelationParticipant(reader.GetString(0), new ConceptId(reader.GetString(1))));
        }

        return participants;
    }

    private static async Task<IReadOnlyDictionary<string, string>> ReadContextAsync(
        SqliteConnection connection,
        string relationId,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
        SELECT key, value
        FROM relation_context
        WHERE relation_id = $relationId
        ORDER BY key;
        """;
        command.Parameters.AddWithValue("$relationId", relationId);

        var context = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            context[reader.GetString(0)] = reader.GetString(1);
        }

        return context;
    }

    private SqliteConnection CreateConnection() =>
        new($"Data Source={databasePath};Foreign Keys=True;Pooling=False");

    private sealed record RelationRow(
        string RelationId,
        string RelationType,
        RelationStatus Status,
        double Confidence,
        string SourceId,
        IReadOnlyList<string> Conditions,
        IReadOnlyDictionary<string, string> Metadata,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);
}
