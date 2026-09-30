namespace RagSqlServer.Retrieval;

/// <summary>A chunk returned by the search, with its source for citation.</summary>
/// <param name="Distance">Cosine distance to the question: 0 is identical, 2 is opposite.</param>
public sealed record RetrievedChunk(
    int ChunkId,
    int DocumentId,
    string Title,
    string? SourceUri,
    string HeadingPath,
    string Content,
    double Distance);
