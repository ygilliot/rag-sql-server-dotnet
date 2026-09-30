using System.Data;
using System.Text.Json;
using Microsoft.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Data.SqlTypes;
using Microsoft.Extensions.AI;
using RagSqlServer.Security;

namespace RagSqlServer.Retrieval;

/// <summary>
/// Exact nearest-neighbour search in SQL Server, restricted to the documents the caller
/// may read, in the same query.
/// </summary>
/// <remarks>
/// The permission filter runs inside the database, on the permission table the application
/// already uses: no copy of the access rules in a separate vector store, and no way for a
/// chunk the caller cannot read to reach the model. Exact search (VECTOR_DISTANCE) is
/// generally available in SQL Server 2025 and recommended by Microsoft up to about 50,000
/// vectors after filtering. See db/002-vector-index-preview.sql for approximate search.
/// </remarks>
public sealed class ChunkRetriever(
    string connectionString,
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
    string embeddingModel)
{
    private const string SearchSql = """
        SELECT TOP (@top)
            c.ChunkId,
            c.DocumentId,
            d.Title,
            d.SourceUri,
            c.HeadingPath,
            c.Content,
            VECTOR_DISTANCE('cosine', c.Embedding, @query) AS Distance
        FROM dbo.DocumentChunks AS c
        INNER JOIN dbo.Documents AS d ON d.DocumentId = c.DocumentId
        WHERE c.EmbeddingModel = @model
          AND EXISTS (
              SELECT 1
              FROM dbo.DocumentAccess AS a
              INNER JOIN OPENJSON(@roles) WITH (RoleName NVARCHAR(100) '$') AS r
                  ON r.RoleName = a.RoleName
              WHERE a.DocumentId = c.DocumentId)
        ORDER BY Distance;
        """;

    public async Task<IReadOnlyList<RetrievedChunk>> SearchAsync(
        string question,
        ICallerContext caller,
        int top = 5,
        CancellationToken cancellationToken = default)
    {
        // Fail closed: a caller without any role sees nothing, rather than everything.
        if (caller.Roles.Count == 0)
        {
            return [];
        }

        var queryVector = await embeddingGenerator.GenerateVectorAsync(question, cancellationToken: cancellationToken);

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new SqlCommand(SearchSql, connection);
        command.Parameters.Add("@top", SqlDbType.Int).Value = top;
        command.Parameters.Add("@model", SqlDbType.NVarChar, 100).Value = embeddingModel;
        command.Parameters.Add("@roles", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(caller.Roles);
        // Native binary transport of the vector (Microsoft.Data.SqlClient 6.1 or later).
        command.Parameters.Add("@query", SqlDbTypeExtensions.Vector).Value = new SqlVector<float>(queryVector);

        var results = new List<RetrievedChunk>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new RetrievedChunk(
                ChunkId: reader.GetInt32(0),
                DocumentId: reader.GetInt32(1),
                Title: reader.GetString(2),
                SourceUri: reader.IsDBNull(3) ? null : reader.GetString(3),
                HeadingPath: reader.GetString(4),
                Content: reader.GetString(5),
                Distance: reader.GetDouble(6)));
        }

        return results;
    }
}
