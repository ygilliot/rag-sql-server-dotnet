-- RAG on SQL Server 2025: reference schema.
-- Requires SQL Server 2025 (17.x), Azure SQL Database, Azure SQL Managed Instance
-- or SQL database in Microsoft Fabric (the VECTOR type is generally available on all four).

-------------------------------------------------------------------------------
-- 1. Tables your application already has (simulated here).
--    The point of the pattern: documents and permissions already live in the
--    application database. The RAG layer reuses them instead of copying them.
-------------------------------------------------------------------------------

CREATE TABLE dbo.Documents
(
    DocumentId INT IDENTITY(1, 1) NOT NULL CONSTRAINT PK_Documents PRIMARY KEY,
    Title      NVARCHAR(200)      NOT NULL,
    SourceUri  NVARCHAR(400)      NULL,
    UpdatedAt  DATETIME2(0)       NOT NULL CONSTRAINT DF_Documents_UpdatedAt DEFAULT SYSUTCDATETIME()
);

-- Who may read which document. In a real system this is your existing
-- permission model (roles, departments, row-level security...).
CREATE TABLE dbo.DocumentAccess
(
    DocumentId INT           NOT NULL CONSTRAINT FK_DocumentAccess_Documents
                                      REFERENCES dbo.Documents (DocumentId) ON DELETE CASCADE,
    RoleName   NVARCHAR(100) NOT NULL,
    CONSTRAINT PK_DocumentAccess PRIMARY KEY (DocumentId, RoleName)
);

-------------------------------------------------------------------------------
-- 2. The only new table: chunks and their embeddings, next to the data.
-------------------------------------------------------------------------------

CREATE TABLE dbo.DocumentChunks
(
    -- An INT clustered primary key is required if you later add a vector index
    -- (CREATE VECTOR INDEX, preview in SQL Server 2025). See 002-vector-index-preview.sql.
    ChunkId        INT IDENTITY(1, 1) NOT NULL CONSTRAINT PK_DocumentChunks PRIMARY KEY CLUSTERED,
    DocumentId     INT                NOT NULL CONSTRAINT FK_DocumentChunks_Documents
                                               REFERENCES dbo.Documents (DocumentId) ON DELETE CASCADE,
    Ordinal        INT                NOT NULL,
    HeadingPath    NVARCHAR(400)      NOT NULL,
    Content        NVARCHAR(MAX)      NOT NULL,
    -- 1536 dimensions matches text-embedding-3-small. float32 supports up to 1998 dimensions.
    Embedding      VECTOR(1536)       NOT NULL,
    -- Which model produced the embedding: vectors from two models are not comparable,
    -- so a model change means re-embedding, and this column tells you what is left to do.
    EmbeddingModel NVARCHAR(100)      NOT NULL,
    CONSTRAINT UQ_DocumentChunks_Document_Ordinal UNIQUE (DocumentId, Ordinal)
);

CREATE INDEX IX_DocumentChunks_DocumentId ON dbo.DocumentChunks (DocumentId);
