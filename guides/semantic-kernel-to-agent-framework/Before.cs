// "Before": the Semantic Kernel version, as published in the guide.
using System.ComponentModel;
using Azure.Core;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;

namespace SkToAgentFramework.Before;

public sealed class OrderTools(IOrderRepository orders)
{
    [KernelFunction, Description("Returns the current status of a customer order.")]
    public async Task<string> GetOrderStatusAsync(
        [Description("The order number, for example CMD-2026-0142.")] string orderNumber)
        => (await orders.FindAsync(orderNumber))?.Status ?? "unknown order";
}

public static class Sample
{
    public static async Task RunAsync(
        string deploymentName, string endpoint, TokenCredential credential, IOrderRepository orders, string question)
    {
        // --- guide snippet starts ---
        // The kernel carries the model connector and the plugins.
        Kernel kernel = Kernel.CreateBuilder()
            .AddAzureOpenAIChatCompletion(deploymentName, endpoint, credential)
            .Build();
        kernel.Plugins.AddFromObject(new OrderTools(orders), "Orders");

        ChatCompletionAgent agent = new()
        {
            Name = "Support",
            Instructions = "Answer questions about customer orders. Never guess a status: call the tool.",
            Kernel = kernel,
            Arguments = new KernelArguments(new PromptExecutionSettings
            {
                FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(),
            }),
        };

        AgentThread thread = new ChatHistoryAgentThread();
        await foreach (AgentResponseItem<ChatMessageContent> item in agent.InvokeAsync(question, thread))
        {
            Console.WriteLine(item.Message.Content);
        }
        // --- guide snippet ends ---
    }
}
