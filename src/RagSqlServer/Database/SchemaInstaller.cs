using Microsoft.Data.SqlClient;

namespace RagSqlServer.Database;

/// <summary>Creates the sample schema (db/001-schema.sql) if it is not there yet.</summary>
public static class SchemaInstaller
{
    /// <summary>Creates the database named in the connection string, if missing (sample and tests only).</summary>
    public static async Task EnsureDatabaseAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        var database = builder.InitialCatalog;
        if (string.IsNullOrEmpty(database) || database.Equals("master", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        builder.InitialCatalog = "master";
        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            """
            IF DB_ID(@name) IS NULL
            BEGIN
                DECLARE @sql NVARCHAR(300) = N'CREATE DATABASE ' + QUOTENAME(@name);
                EXEC (@sql);
            END;
            """, connection);
        command.Parameters.AddWithValue("@name", database);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public static async Task EnsureCreatedAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using (var check = new SqlCommand("SELECT OBJECT_ID(N'dbo.DocumentChunks', N'U');", connection))
        {
            if (await check.ExecuteScalarAsync(cancellationToken) is not (null or DBNull))
            {
                return;
            }
        }

        await using var stream = typeof(SchemaInstaller).Assembly.GetManifestResourceStream("001-schema.sql")
            ?? throw new InvalidOperationException("Embedded resource 001-schema.sql not found.");
        using var reader = new StreamReader(stream);
        var script = await reader.ReadToEndAsync(cancellationToken);

        await using var create = new SqlCommand(script, connection);
        await create.ExecuteNonQueryAsync(cancellationToken);
    }
}
