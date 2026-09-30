// "After": the Microsoft Agent Framework version, as published in the guide.
using System.ComponentModel;
using Azure.AI.OpenAI;
using Azure.Core;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace SkToAgentFramework.After;

public sealed class OrderTools(IOrderRepository orders)
{
    // No framework attribute: a plain method, described for the model.
    [Description("Returns the current status of a customer order.")]
    public async Task<string> GetOrderStatusAsync(
        [Description("The order number, for example CMD-2026-0142.")] string orderNumber)
        => (await orders.FindAsync(orderNumber))?.Status ?? "unknown order";
}

public static class Sample
{
    public static async Task RunAsync(
        Uri endpoint, TokenCredential credential, string deploymentName, IOrderRepository orders, string question)
    {
        // --- guide snippet starts ---
        var tools = new OrderTools(orders);

        // No kernel: the agent is created from the chat client, tools included.
        AIAgent agent = new AzureOpenAIClient(endpoint, credential)
            .GetChatClient(deploymentName)
            .AsAIAgent(
                instructions: "Answer questions about customer orders. Never guess a status: call the tool.",
                tools: [AIFunctionFactory.Create(tools.GetOrderStatusAsync)]);

        AgentSession session = await agent.CreateSessionAsync();
        AgentResponse response = await agent.RunAsync(question, session);
        Console.WriteLine(response.Text);
        // --- guide snippet ends ---
    }

    public static void Register(IServiceCollection services)
    {
        // --- guide snippet starts (dependency injection) ---
        services.AddKeyedSingleton<AIAgent>("support", (sp, _) =>
            sp.GetRequiredService<IChatClient>().AsAIAgent(
                instructions: "Answer questions about customer orders. Never guess a status: call the tool.",
                tools: [AIFunctionFactory.Create(sp.GetRequiredService<OrderTools>().GetOrderStatusAsync)]));
        // --- guide snippet ends ---
    }

    public static AIAgent CreateRagAgent(IChatClient chatClient)
    {
        // --- guide snippet starts (RAG) ---
        AIAgent agent = chatClient.AsAIAgent(new ChatClientAgentOptions
        {
            ChatOptions = new() { Instructions = "Answer from the provided documents and cite them." },
            AIContextProviders = [new TextSearchProvider(SearchDocumentsAsync, new TextSearchProviderOptions
            {
                SearchTime = TextSearchProviderOptions.TextSearchBehavior.BeforeAIInvoke,
            })],
        });
        // --- guide snippet ends ---
        return agent;
    }

    private static Task<IEnumerable<TextSearchProvider.TextSearchResult>> SearchDocumentsAsync(
        string query, CancellationToken cancellationToken) =>
        Task.FromResult(Enumerable.Empty<TextSearchProvider.TextSearchResult>());
}
