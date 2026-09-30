using System.Data;
using Microsoft.Data.SqlClient;
using RagSqlServer.Ingestion;

namespace RagSqlServer.Samples;

/// <summary>
/// Plays the part of the existing application: inserts three documents and their permissions,
/// then asks the ingestor to chunk and embed them.
/// </summary>
public static class SampleSeeder
{
    public sealed record SampleDocument(string FileName, string Title, string SourceUri, string[] Roles);

    public static readonly SampleDocument[] Documents =
    [
        new("expense-policy.md", "Expense policy", "https://intranet.example/policies/expenses", ["Employee", "HR", "Finance"]),
        new("salary-review.md", "Salary review process", "https://intranet.example/hr/salary-review", ["HR"]),
        new("supplier-contract.md", "Supplier contract template", "https://intranet.example/legal/supplier-contract", ["Purchasing", "Legal"]),
    ];

    /// <returns>The document id of each sample file, keyed by file name.</returns>
    public static async Task<Dictionary<string, int>> SeedAsync(
        string connectionString,
        DocumentIngestor ingestor,
        string dataDirectory,
        CancellationToken cancellationToken = default)
    {
        var ids = new Dictionary<string, int>();

        foreach (var document in Documents)
        {
            var documentId = await UpsertDocumentAsync(connectionString, document, cancellationToken);
            var markdown = await File.ReadAllTextAsync(Path.Combine(dataDirectory, document.FileName), cancellationToken);
            await ingestor.IngestAsync(documentId, document.Title, markdown, cancellationToken);
            ids[document.FileName] = documentId;
        }

        return ids;
    }

    private static async Task<int> UpsertDocumentAsync(
        string connectionString,
        SampleDocument document,
        CancellationToken cancellationToken)
    {
        const string sql = """
            DECLARE @id INT = (SELECT DocumentId FROM dbo.Documents WHERE SourceUri = @sourceUri);
            IF @id IS NULL
            BEGIN
                INSERT INTO dbo.Documents (Title, SourceUri) VALUES (@title, @sourceUri);
                SET @id = SCOPE_IDENTITY();
            END;
            DELETE FROM dbo.DocumentAccess WHERE DocumentId = @id;
            INSERT INTO dbo.DocumentAccess (DocumentId, RoleName)
            SELECT @id, RoleName FROM OPENJSON(@roles) WITH (RoleName NVARCHAR(100) '$');
            SELECT @id;
            """;

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@title", SqlDbType.NVarChar, 200).Value = document.Title;
        command.Parameters.Add("@sourceUri", SqlDbType.NVarChar, 400).Value = document.SourceUri;
        command.Parameters.Add("@roles", SqlDbType.NVarChar, -1).Value = System.Text.Json.JsonSerializer.Serialize(document.Roles);
        return (int)(await command.ExecuteScalarAsync(cancellationToken))!;
    }
}
