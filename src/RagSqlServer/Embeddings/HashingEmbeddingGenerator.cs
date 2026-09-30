using System.Globalization;
using System.Text;
using Microsoft.Extensions.AI;

namespace RagSqlServer.Embeddings;

/// <summary>
/// Deterministic, offline embedding generator for tests and for running the sample without
/// an API key. Each word is hashed into one of the vector dimensions (the "hashing trick"),
/// so the cosine distance reflects shared words.
/// </summary>
/// <remarks>
/// This is not a semantic model: "car" and "vehicle" are unrelated here. It exists so that
/// the SQL, the permission filter and the agent wiring can be tested end to end, in CI,
/// with the same 1536-dimension vectors as text-embedding-3-small.
/// </remarks>
public sealed class HashingEmbeddingGenerator(int dimensions = 1536) : IEmbeddingGenerator<string, Embedding<float>>
{
    public const string ModelId = "hashing-offline-v1";

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var embeddings = new GeneratedEmbeddings<Embedding<float>>();
        foreach (var value in values)
        {
            embeddings.Add(new Embedding<float>(Embed(value)) { ModelId = ModelId });
        }

        return Task.FromResult(embeddings);
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
    }

    private float[] Embed(string text)
    {
        var vector = new float[dimensions];
        foreach (var token in Tokenize(text))
        {
            var hash = Fnv1a(token);
            var index = (int)(hash % (uint)dimensions);
            // A second hash bit decides the sign, which keeps unrelated words from piling up.
            vector[index] += (hash & 0x8000_0000) == 0 ? 1f : -1f;
        }

        var norm = MathF.Sqrt(vector.Sum(v => v * v));
        if (norm == 0f)
        {
            // Cosine distance is undefined for a zero vector: give empty text a fixed direction.
            vector[0] = 1f;
            return vector;
        }

        for (var i = 0; i < vector.Length; i++)
        {
            vector[i] /= norm;
        }

        return vector;
    }

    private static IEnumerable<string> Tokenize(string text)
    {
        var word = new StringBuilder();
        foreach (var c in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue; // drop accents: "équipe" and "equipe" hash the same
            }

            if (char.IsLetterOrDigit(c))
            {
                word.Append(char.ToLowerInvariant(c));
            }
            else if (word.Length > 0)
            {
                if (word.Length > 2)
                {
                    yield return word.ToString();
                }

                word.Clear();
            }
        }

        if (word.Length > 2)
        {
            yield return word.ToString();
        }
    }

    private static uint Fnv1a(string value)
    {
        var hash = 2166136261u;
        foreach (var c in value)
        {
            hash ^= c;
            hash *= 16777619u;
        }

        return hash;
    }
}
