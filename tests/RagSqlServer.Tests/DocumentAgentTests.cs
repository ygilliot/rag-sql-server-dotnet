using System.Runtime.CompilerServices;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using RagSqlServer.Agents;
using RagSqlServer.Security;

namespace RagSqlServer.Tests;

/// <summary>
/// Runs the real Agent Framework pipeline against a recording chat client, to check what
/// actually reaches the model.
/// </summary>
public sealed class DocumentAgentTests(SqlServerFixture sql) : IClassFixture<SqlServerFixture>
{
    private const string SkipReason = "Set RAG_SQL_CONNECTION_STRING to a SQL Server 2025 instance (see docker-compose.yml).";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_model_receives_the_chunks_the_caller_may_read()
    {
        Assert.SkipUnless(sql.Available, SkipReason);
        var chatClient = new RecordingChatClient();

        AIAgent agent = DocumentAgent.Create(chatClient, sql.Retriever, new StaticCallerContext("Employee"));
        AgentSession session = await agent.CreateSessionAsync(cancellationToken: Ct);
        await agent.RunAsync("What is the hotel ceiling per night in Paris?", session, cancellationToken: Ct);

        Assert.Contains("180 euros per night in Paris", chatClient.LastPrompt);
    }

    [Fact]
    public async Task The_model_never_receives_a_document_the_caller_may_not_read()
    {
        Assert.SkipUnless(sql.Available, SkipReason);
        var chatClient = new RecordingChatClient();

        AIAgent agent = DocumentAgent.Create(chatClient, sql.Retriever, new StaticCallerContext("Employee"));
        AgentSession session = await agent.CreateSessionAsync(cancellationToken: Ct);
        await agent.RunAsync("What are the salary bands and the salary increase budget?", session, cancellationToken: Ct);

        Assert.DoesNotContain("Workshop lead", chatClient.LastPrompt);
        Assert.DoesNotContain("3 percent of the payroll", chatClient.LastPrompt);
    }

    /// <summary>Records everything sent to the model and answers with a fixed text.</summary>
    private sealed class RecordingChatClient : IChatClient
    {
        public string LastPrompt { get; private set; } = "";

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            LastPrompt = string.Join("\n", messages.Select(m => m.Text).Prepend(options?.Instructions ?? ""));
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Recorded.")));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var response = await GetResponseAsync(messages, options, cancellationToken);
            foreach (var update in response.ToChatResponseUpdates())
            {
                yield return update;
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose()
        {
        }
    }
}
