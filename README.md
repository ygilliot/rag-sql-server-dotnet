# RAG on SQL Server 2025 with .NET and Microsoft Agent Framework

[![CI](https://github.com/ygilliot/rag-sql-server-dotnet/actions/workflows/ci.yml/badge.svg)](https://github.com/ygilliot/rag-sql-server-dotnet/actions/workflows/ci.yml)

A reference implementation of retrieval-augmented generation (RAG) inside an existing
.NET and SQL Server application: embeddings stored in the `VECTOR` type of SQL Server 2025,
retrieval filtered by the permissions the application already has, and an agent built with
Microsoft Agent Framework 1.x.

It is the companion code of the guide **RAG on SQL Server 2025**
([English](https://gilabs.fr/en/guides/rag-sql-server-2025/) ·
[français](https://gilabs.fr/fr/guides/rag-sql-server-2025/)).

## The three decisions this code makes

1. **The vectors stay next to the data.** Chunks and embeddings live in one new table,
   `dbo.DocumentChunks`, in the application database. No separate vector store, so no
   second copy of the permission rules to keep in sync.
2. **Chunks follow the structure of the document.** One chunk per section, long sections
   split between paragraphs, tables and code blocks never cut. Each chunk carries its
   heading path ("Expense policy > Travel > Hotels") into the embedding.
3. **Permissions are enforced in the query.** The search joins the existing
   `dbo.DocumentAccess` table with the caller's roles, taken from the server-side identity.
   A chunk the caller cannot read never reaches the model, and the tests prove it.

## Quick start

Requirements: .NET 10 SDK, Docker (or any SQL Server 2025 / Azure SQL instance).

```bash
docker compose up -d
dotnet test
dotnet run --project samples/RagSqlServer.Console -- "What is the hotel ceiling in Paris?" --role Employee
dotnet run --project samples/RagSqlServer.Console -- "salary range per band" --role HR
```

Without an API key the sample runs offline with a deterministic hashing embedder: no model
call, it prints the chunks each role may retrieve. To run the Agent Framework agent with
real models:

```bash
export OPENAI_API_KEY=...
# Azure OpenAI works too, through its v1 endpoint:
# export OPENAI_BASE_URL=https://<resource>.openai.azure.com/openai/v1/
dotnet run --project samples/RagSqlServer.Console -- "What is the hotel ceiling in Paris?" --role Employee
```

`RAG_SQL_CONNECTION_STRING` points to another server. `CHAT_MODEL` and `EMBEDDING_MODEL`
override the defaults (`gpt-5-mini`, `text-embedding-3-small`).

## What is in the repository

| Path | Content |
|---|---|
| `db/001-schema.sql` | The existing tables (documents, permissions) and the one new table with its `VECTOR(1536)` column |
| `db/002-vector-index-preview.sql` | Optional DiskANN vector index, with the SQL Server 2025 preview caveats |
| `src/RagSqlServer/Chunking` | `StructuredChunker`: Markdown sections, paragraphs, tables and code blocks kept whole |
| `src/RagSqlServer/Ingestion` | `DocumentIngestor`: embeds outside the transaction, replaces a document's chunks atomically |
| `src/RagSqlServer/Retrieval` | `ChunkRetriever`: exact search with `VECTOR_DISTANCE`, permission filter in the same query |
| `src/RagSqlServer/Agents` | `DocumentAgent`: Agent Framework `ChatClientAgent` with a `TextSearchProvider` |
| `src/RagSqlServer/Embeddings` | `HashingEmbeddingGenerator`: offline embeddings for tests and CI, not semantic |
| `tests/` | Chunker tests, and integration tests against SQL Server 2025, including what reaches the model |
| `guides/semantic-kernel-to-agent-framework` | Compile check of the code published in the guide [Semantic Kernel or Microsoft Agent Framework](https://gilabs.fr/en/guides/semantic-kernel-agent-framework/) |

## SQL Server 2025 vector features, as of September 2026

| Feature | SQL Server 2025 (up to CU9) | Azure SQL Database, Fabric |
|---|---|---|
| `VECTOR` type, `VECTOR_DISTANCE` | Generally available | Generally available |
| `CREATE VECTOR INDEX` (DiskANN), `VECTOR_SEARCH` | Preview, `PREVIEW_FEATURES` required | Generally available |
| DML on an indexed table, filters applied during the search | Not yet: earlier index format, read-only table, post-filtering | Yes (latest index format) |

That is why `ChunkRetriever` uses exact search. Microsoft recommends it up to about 50,000
vectors counted after the `WHERE` clause, and a permission filter usually keeps the
candidate set well under that. Sources: Microsoft Learn,
[Vector search and vector indexes](https://learn.microsoft.com/en-us/sql/sql-server/ai/vectors),
[CREATE VECTOR INDEX](https://learn.microsoft.com/en-us/sql/t-sql/statements/create-vector-index-transact-sql),
[VECTOR_SEARCH](https://learn.microsoft.com/en-us/sql/t-sql/functions/vector-search-transact-sql).

## Packages

`Microsoft.Data.SqlClient` 7.1 (native `SqlVector<float>` transport, 6.1 or later required),
`Microsoft.Extensions.AI` 10.10, `Microsoft.Agents.AI` 1.23. Versions are pinned in
`Directory.Packages.props`.

## Scope

This is sample code: no retries, no telemetry, no document converters (PDF, Word). The
sample documents describe a fictional company. For PDF and Word sources, see
`Microsoft.Extensions.DataIngestion` (preview) or your existing extraction pipeline.

## Author

Written by [Yann Gilliot](https://www.linkedin.com/in/yann-gilliot), [GiLabs](https://gilabs.fr):
AI integration into existing .NET and Microsoft environments. Licensed under the MIT licence.
