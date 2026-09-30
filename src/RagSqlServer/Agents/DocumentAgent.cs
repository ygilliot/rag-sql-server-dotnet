using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using RagSqlServer.Retrieval;
using RagSqlServer.Security;

namespace RagSqlServer.Agents;

/// <summary>
/// A Microsoft Agent Framework agent grounded on the SQL Server chunks the caller may read.
/// </summary>
/// <remarks>
/// The search runs before each model call (BeforeAIInvoke) with the caller captured on the
/// server side. In on-demand mode the model calls a search tool itself: keep the roles out
/// of the tool parameters, or the model decides what the user may read.
/// Create one agent per request: it is a light object around the shared chat client.
/// </remarks>
public static class DocumentAgent
{
    public const string Instructions =
        "You answer questions about internal company documents. " +
        "Use only the provided context. If the context does not contain the answer, say so " +
        "and do not guess. Cite the source section for each fact.";

    public static AIAgent Create(IChatClient chatClient, ChunkRetriever retriever, ICallerContext caller) =>
        chatClient.AsAIAgent(new ChatClientAgentOptions
        {
            ChatOptions = new() { Instructions = Instructions },
            AIContextProviders =
            [
                new TextSearchProvider(
                    (query, cancellationToken) => SearchAsync(retriever, caller, query, cancellationToken),
                    new TextSearchProviderOptions
                    {
                        SearchTime = TextSearchProviderOptions.TextSearchBehavior.BeforeAIInvoke,
                    }),
            ],
        });

    internal static async Task<IEnumerable<TextSearchProvider.TextSearchResult>> SearchAsync(
        ChunkRetriever retriever,
        ICallerContext caller,
        string query,
        CancellationToken cancellationToken)
    {
        var chunks = await retriever.SearchAsync(query, caller, top: 5, cancellationToken);
        return chunks.Select(c => new TextSearchProvider.TextSearchResult
        {
            SourceName = c.HeadingPath,
            SourceLink = c.SourceUri,
            Text = c.Content,
        });
    }
}
