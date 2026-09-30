namespace RagSqlServer.Chunking;

/// <summary>A piece of a document that follows its structure: one section, or part of one.</summary>
/// <param name="Ordinal">Position of the chunk in the document, from 0.</param>
/// <param name="HeadingPath">Titles leading to the section, for example "Travel policy > Hotels".</param>
/// <param name="Content">The section text, never cut in the middle of a paragraph, table or code block.</param>
public sealed record DocumentChunk(int Ordinal, string HeadingPath, string Content)
{
    /// <summary>
    /// Text sent to the embedding model. The heading path is prepended so that a chunk
    /// such as "Ceiling: 120 euros per night" still carries what it is about.
    /// </summary>
    public string EmbeddingText => $"{HeadingPath}\n\n{Content}";
}
