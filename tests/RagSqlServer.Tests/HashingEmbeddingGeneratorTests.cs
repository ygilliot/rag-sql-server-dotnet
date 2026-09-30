using Microsoft.Extensions.AI;
using RagSqlServer.Embeddings;

namespace RagSqlServer.Tests;

public sealed class HashingEmbeddingGeneratorTests
{
    private readonly HashingEmbeddingGenerator _generator = new();

    [Fact]
    public async Task Vectors_are_deterministic_normalised_and_1536_wide()
    {
        var first = await _generator.GenerateVectorAsync("Hotel ceiling in Paris", cancellationToken: TestContext.Current.CancellationToken);
        var second = await _generator.GenerateVectorAsync("Hotel ceiling in Paris", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1536, first.Length);
        Assert.Equal(first.ToArray(), second.ToArray());
        Assert.Equal(1.0, Math.Sqrt(first.ToArray().Sum(v => (double)v * v)), precision: 5);
    }

    [Fact]
    public async Task Shared_words_bring_vectors_closer()
    {
        var question = await _generator.GenerateVectorAsync("hotel ceiling per night", cancellationToken: TestContext.Current.CancellationToken);
        var hotels = await _generator.GenerateVectorAsync("The hotel ceiling is 120 euros per night", cancellationToken: TestContext.Current.CancellationToken);
        var salaries = await _generator.GenerateVectorAsync("Salary bands for joiners and workshop leads", cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(Cosine(question, hotels) > Cosine(question, salaries));
    }

    private static double Cosine(ReadOnlyMemory<float> a, ReadOnlyMemory<float> b)
    {
        var x = a.Span;
        var y = b.Span;
        double dot = 0;
        for (var i = 0; i < x.Length; i++)
        {
            dot += x[i] * y[i];
        }

        return dot; // both vectors are normalised
    }
}
