using System.Text;
using System.Text.RegularExpressions;

namespace RagSqlServer.Chunking;

/// <summary>
/// Splits a Markdown document along its own structure: one chunk per section, and a long
/// section is split between paragraphs, never inside one. Tables and fenced code blocks
/// stay whole.
/// </summary>
/// <remarks>
/// A fixed token window is simpler, and it cuts a clause, a table or a procedure step in two:
/// the model then receives half a rule and answers from it. Business documents already
/// carry their structure (clauses, sections, steps); the chunker only has to respect it.
/// Sizes are in characters to stay free of any tokenizer; 1,500 characters is roughly
/// 350 to 400 tokens of English or French prose.
/// </remarks>
public sealed partial class StructuredChunker(int maxChars = 1500)
{
    public IReadOnlyList<DocumentChunk> Chunk(string documentTitle, string markdown)
    {
        var chunks = new List<DocumentChunk>();
        var headings = new List<(int Level, string Text)>();
        var body = new List<string>();
        var inFence = false;

        foreach (var rawLine in markdown.ReplaceLineEndings("\n").Split('\n'))
        {
            var line = rawLine.TrimEnd();

            if (IsFence(line))
            {
                inFence = !inFence;
                body.Add(line);
                continue;
            }

            var heading = inFence ? null : HeadingRegex().Match(line);
            if (heading is { Success: true })
            {
                Flush(documentTitle, headings, body, chunks);

                var level = heading.Groups[1].Value.Length;
                headings.RemoveAll(h => h.Level >= level);
                headings.Add((level, heading.Groups[2].Value.Trim()));
                continue;
            }

            body.Add(line);
        }

        Flush(documentTitle, headings, body, chunks);
        return chunks;
    }

    private void Flush(
        string documentTitle,
        List<(int Level, string Text)> headings,
        List<string> body,
        List<DocumentChunk> chunks)
    {
        var blocks = SplitIntoBlocks(body);
        body.Clear();
        if (blocks.Count == 0)
        {
            return;
        }

        var path = BuildHeadingPath(documentTitle, headings);
        var current = new StringBuilder();

        foreach (var block in blocks)
        {
            // Close the current chunk if this block would overflow it. A single block larger
            // than the limit is kept whole: splitting a table or a code sample is worse than
            // an oversized chunk.
            if (current.Length > 0 && current.Length + 2 + block.Length > maxChars)
            {
                chunks.Add(new DocumentChunk(chunks.Count, path, current.ToString()));
                current.Clear();
            }

            if (current.Length > 0)
            {
                current.Append("\n\n");
            }

            current.Append(block);
        }

        chunks.Add(new DocumentChunk(chunks.Count, path, current.ToString()));
    }

    /// <summary>Groups lines into blocks separated by blank lines, keeping fenced code blocks whole.</summary>
    private static List<string> SplitIntoBlocks(List<string> lines)
    {
        var blocks = new List<string>();
        var current = new List<string>();
        var inFence = false;

        foreach (var line in lines)
        {
            if (IsFence(line))
            {
                inFence = !inFence;
            }

            if (!inFence && line.Length == 0)
            {
                if (current.Count > 0)
                {
                    blocks.Add(string.Join('\n', current));
                    current.Clear();
                }

                continue;
            }

            current.Add(line);
        }

        if (current.Count > 0)
        {
            blocks.Add(string.Join('\n', current));
        }

        return blocks;
    }

    private static string BuildHeadingPath(string documentTitle, List<(int Level, string Text)> headings)
    {
        var parts = new List<string> { documentTitle };
        foreach (var (_, text) in headings)
        {
            // A document whose H1 repeats its title should not read "Title > Title".
            if (!string.Equals(parts[^1], text, StringComparison.OrdinalIgnoreCase))
            {
                parts.Add(text);
            }
        }

        return string.Join(" > ", parts);
    }

    private static bool IsFence(string line)
    {
        var trimmed = line.TrimStart();
        return trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal);
    }

    [GeneratedRegex(@"^(#{1,6})\s+(.+?)(?:\s+#+)?\s*$")]
    private static partial Regex HeadingRegex();
}
