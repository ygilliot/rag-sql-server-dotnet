using System.Data;
using Microsoft.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Data.SqlTypes;
using Microsoft.Extensions.AI;
using RagSqlServer.Chunking;

namespace RagSqlServer.Ingestion;

/// <summary>
/// Chunks a document that already exists in the application database, embeds the chunks
/// and replaces its previous chunks in one transaction.
/// </summary>
public sealed class DocumentIngestor(
    string connectionString,
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
    string embeddingModel,
    StructuredChunker chunker)
{
    // Embedding APIs cap the number of inputs per call; small batches also keep retries cheap.
    private const int EmbeddingBatchSize = 64;

    private const string InsertSql = """
        INSERT INTO dbo.DocumentChunks (DocumentId, Ordinal, HeadingPath, Content, Embedding, EmbeddingModel)
        VALUES (@documentId, @ordinal, @headingPath, @content, @embedding, @model);
        """;

    /// <returns>The number of chunks written.</returns>
    public async Task<int> IngestAsync(
        int documentId,
        string title,
        string markdown,
        CancellationToken cancellationToken = default)
    {
        var chunks = chunker.Chunk(title, markdown);

        // Embed before opening the transaction: never hold locks during a network call
        // to a model endpoint.
        var vectors = new List<ReadOnlyMemory<float>>(chunks.Count);
        foreach (var batch in chunks.Chunk(EmbeddingBatchSize))
        {
            var embeddings = await embeddingGenerator.GenerateAsync(
                batch.Select(c => c.EmbeddingText),
                cancellationToken: cancellationToken);
            vectors.AddRange(embeddings.Select(e => e.Vector));
        }

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

        await using (var delete = new SqlCommand(
            "DELETE FROM dbo.DocumentChunks WHERE DocumentId = @documentId;", connection, transaction))
        {
            delete.Parameters.Add("@documentId", SqlDbType.Int).Value = documentId;
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var insert = new SqlCommand(InsertSql, connection, transaction))
        {
            var pDocumentId = insert.Parameters.Add("@documentId", SqlDbType.Int);
            var pOrdinal = insert.Parameters.Add("@ordinal", SqlDbType.Int);
            var pHeadingPath = insert.Parameters.Add("@headingPath", SqlDbType.NVarChar, 400);
            var pContent = insert.Parameters.Add("@content", SqlDbType.NVarChar, -1);
            var pEmbedding = insert.Parameters.Add("@embedding", SqlDbTypeExtensions.Vector);
            var pModel = insert.Parameters.Add("@model", SqlDbType.NVarChar, 100);

            for (var i = 0; i < chunks.Count; i++)
            {
                pDocumentId.Value = documentId;
                pOrdinal.Value = chunks[i].Ordinal;
                pHeadingPath.Value = Truncate(chunks[i].HeadingPath, 400);
                pContent.Value = chunks[i].Content;
                pEmbedding.Value = new SqlVector<float>(vectors[i]);
                pModel.Value = embeddingModel;
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return chunks.Count;
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
