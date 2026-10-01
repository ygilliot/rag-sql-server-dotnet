using System.Diagnostics.Metrics;
using Microsoft.Extensions.AI;

namespace AiInExistingApp;

// --- guide snippet starts (token usage per use case) ---
public static class AiUsage
{
    public const string SourceName = "MyApp.AI";

    private static readonly Meter Meter = new(SourceName);
    private static readonly Counter<long> Tokens = Meter.CreateCounter<long>("myapp.ai.tokens");

    /// <summary>Counts tokens per use case, so the bill can be read feature by feature.</summary>
    public static void Record(string useCase, UsageDetails? usage)
    {
        if (usage is null)
        {
            return;
        }

        Tokens.Add(
            usage.InputTokenCount ?? 0,
            new KeyValuePair<string, object?>("use_case", useCase),
            new KeyValuePair<string, object?>("direction", "input"));
        Tokens.Add(
            usage.OutputTokenCount ?? 0,
            new KeyValuePair<string, object?>("use_case", useCase),
            new KeyValuePair<string, object?>("direction", "output"));
    }
}
// --- guide snippet ends ---
