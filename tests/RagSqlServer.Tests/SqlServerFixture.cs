using Microsoft.Data.SqlClient;
using RagSqlServer.Chunking;
using RagSqlServer.Database;
using RagSqlServer.Embeddings;
using RagSqlServer.Ingestion;
using RagSqlServer.Retrieval;
using RagSqlServer.Samples;

namespace RagSqlServer.Tests;

/// <summary>
/// A throwaway database on the SQL Server 2025 instance given by RAG_SQL_CONNECTION_STRING
/// (the GitHub Actions service container, or docker-compose.yml locally), seeded with the
/// sample documents and hashing embeddings.
/// </summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(120);

    public bool Available { get; private set; }

    public string ConnectionString { get; private set; } = "";

    public Dictionary<string, int> DocumentIds { get; private set; } = [];

    public HashingEmbeddingGenerator EmbeddingGenerator { get; } = new();

    public DocumentIngestor Ingestor { get; private set; } = null!;

    public ChunkRetriever Retriever { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        var baseConnectionString = Environment.GetEnvironmentVariable("RAG_SQL_CONNECTION_STRING");
        if (string.IsNullOrEmpty(baseConnectionString))
        {
            return;
        }

        ConnectionString = new SqlConnectionStringBuilder(baseConnectionString)
        {
            InitialCatalog = $"RagTests_{Guid.NewGuid():N}",
        }.ConnectionString;

        await WaitForServerAsync(baseConnectionString);
        await SchemaInstaller.EnsureDatabaseAsync(ConnectionString);
        await SchemaInstaller.EnsureCreatedAsync(ConnectionString);

        Ingestor = new DocumentIngestor(ConnectionString, EmbeddingGenerator, HashingEmbeddingGenerator.ModelId, new StructuredChunker());
        Retriever = new ChunkRetriever(ConnectionString, EmbeddingGenerator, HashingEmbeddingGenerator.ModelId);
        DocumentIds = await SampleSeeder.SeedAsync(ConnectionString, Ingestor, Path.Combine(AppContext.BaseDirectory, "data"));
        Available = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (!Available)
        {
            return;
        }

        var builder = new SqlConnectionStringBuilder(ConnectionString);
        var database = builder.InitialCatalog;
        builder.InitialCatalog = "master";
        SqlConnection.ClearAllPools();

        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            $"ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}];",
            connection);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<int> CountChunksAsync(int documentId)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("SELECT COUNT(*) FROM dbo.DocumentChunks WHERE DocumentId = @id;", connection);
        command.Parameters.AddWithValue("@id", documentId);
        return (int)(await command.ExecuteScalarAsync())!;
    }

    // The container accepts TCP connections before the engine is ready: retry until it answers.
    private static async Task WaitForServerAsync(string connectionString)
    {
        var master = new SqlConnectionStringBuilder(connectionString) { InitialCatalog = "master", ConnectTimeout = 5 }.ConnectionString;
        var deadline = DateTime.UtcNow + StartupTimeout;

        while (true)
        {
            try
            {
                await using var connection = new SqlConnection(master);
                await connection.OpenAsync();
                return;
            }
            catch (SqlException) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(TimeSpan.FromSeconds(3));
            }
        }
    }
}
