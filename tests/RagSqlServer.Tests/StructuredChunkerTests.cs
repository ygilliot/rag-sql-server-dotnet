using RagSqlServer.Chunking;

namespace RagSqlServer.Tests;

public sealed class StructuredChunkerTests
{
    private static string Sample(string fileName) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", fileName));

    [Fact]
    public void Each_section_becomes_a_chunk_with_its_heading_path()
    {
        var chunks = new StructuredChunker().Chunk("Expense policy", Sample("expense-policy.md"));

        var hotels = Assert.Single(chunks, c => c.HeadingPath == "Expense policy > Travel > Hotels");
        Assert.Contains("120 euros per night", hotels.Content);
        Assert.StartsWith("Expense policy > Travel > Hotels\n\n", hotels.EmbeddingText);
    }

    [Fact]
    public void A_table_is_never_split()
    {
        var chunks = new StructuredChunker(maxChars: 80).Chunk("Salary review process", Sample("salary-review.md"));

        var bands = Assert.Single(chunks, c => c.HeadingPath.EndsWith("Salary bands", StringComparison.Ordinal));
        Assert.Contains("B1", bands.Content);
        Assert.Contains("B3", bands.Content);
    }

    [Fact]
    public void A_long_section_is_split_between_paragraphs_only()
    {
        var paragraphs = Enumerable.Range(1, 5).Select(i => $"Paragraph {i}. " + new string('x', 380)).ToArray();
        var markdown = "# Procedure\n\n" + string.Join("\n\n", paragraphs);

        var chunks = new StructuredChunker(maxChars: 1000).Chunk("Procedure", markdown);

        Assert.True(chunks.Count > 1);
        var rebuilt = chunks.SelectMany(c => c.Content.Split("\n\n")).ToArray();
        Assert.Equal(paragraphs, rebuilt);
        Assert.All(chunks, c => Assert.True(c.Content.Length <= 1000));
    }

    [Fact]
    public void A_fenced_code_block_stays_whole_and_its_comments_are_not_headings()
    {
        const string markdown = """
            # Setup

            ```bash
            # install the tools

            dotnet tool restore
            ```
            """;

        var chunk = Assert.Single(new StructuredChunker().Chunk("Guide", markdown));

        Assert.Equal("Guide > Setup", chunk.HeadingPath);
        Assert.Contains("# install the tools\n\ndotnet tool restore", chunk.Content);
    }

    [Fact]
    public void Ordinals_follow_document_order()
    {
        var chunks = new StructuredChunker().Chunk("Expense policy", Sample("expense-policy.md"));

        Assert.Equal(Enumerable.Range(0, chunks.Count), chunks.Select(c => c.Ordinal));
    }
}
