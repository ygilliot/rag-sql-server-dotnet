-- Optional: approximate search with a DiskANN vector index.
--
-- Read this before running it on SQL Server 2025 (checked against Microsoft Learn, September 2026):
--
-- * CREATE VECTOR INDEX and VECTOR_SEARCH are generally available on Azure SQL Database,
--   SQL database in Fabric and Azure SQL Managed Instance (Always-up-to-date policy).
-- * On SQL Server 2025 (up to CU9, 15 September 2026) they are a preview feature, behind
--   PREVIEW_FEATURES, and the index uses the earlier format: the table becomes read-only
--   once indexed (unless ALLOW_STALE_VECTOR_INDEX is on), and WHERE predicates are applied
--   after the approximate search (post-filtering), so a permission filter can return
--   fewer rows than requested, sometimes none.
-- * The index needs at least 100 rows with non-NULL vectors and an INT clustered primary key.
--
-- Microsoft's guidance: exact search (VECTOR_DISTANCE) is recommended below about 50,000
-- vectors, counted after your WHERE predicates. With a permission filter, that threshold
-- is often never reached. The retriever in this repository uses exact search.

ALTER DATABASE SCOPED CONFIGURATION SET PREVIEW_FEATURES = ON;
GO

CREATE VECTOR INDEX VIX_DocumentChunks_Embedding
    ON dbo.DocumentChunks (Embedding)
    WITH (METRIC = 'cosine', TYPE = 'DiskANN');
GO

-- Latest index format (Azure SQL, Fabric): iterative filtering, the WHERE clause is applied
-- during the search.
--
-- DECLARE @q VECTOR(1536) = ...;
-- SELECT TOP (5) WITH APPROXIMATE c.ChunkId, c.HeadingPath, r.distance
-- FROM VECTOR_SEARCH(
--         TABLE = dbo.DocumentChunks AS c,
--         COLUMN = Embedding,
--         SIMILAR_TO = @q,
--         METRIC = 'cosine') AS r
-- WHERE EXISTS (SELECT 1 FROM dbo.DocumentAccess AS a
--               WHERE a.DocumentId = c.DocumentId AND a.RoleName IN (N'Employee'))
-- ORDER BY r.distance;
--
-- Earlier index format (SQL Server 2025 box product today): TOP_N inside VECTOR_SEARCH,
-- predicates applied afterwards.
--
-- SELECT TOP (5) c.ChunkId, c.HeadingPath, r.distance
-- FROM VECTOR_SEARCH(
--         TABLE = dbo.DocumentChunks AS c,
--         COLUMN = Embedding,
--         SIMILAR_TO = @q,
--         METRIC = 'cosine',
--         TOP_N = 50) AS r
-- WHERE EXISTS (...)
-- ORDER BY r.distance;
