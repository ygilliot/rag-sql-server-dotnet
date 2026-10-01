using System.ComponentModel;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace AiInExistingApp;

// --- guide snippet starts (tools over existing services) ---
public sealed class OrderTools(IOrderRepository orders, ICurrentUser user)
{
    [Description("Returns the status of one of the current customer's orders.")]
    public async Task<string> GetOrderStatusAsync(
        [Description("The order number, for example CMD-2026-0142.")] string orderNumber)
    {
        Order? order = await orders.FindAsync(orderNumber);

        // The customer comes from the server-side identity, never from a tool parameter.
        // Same answer for "does not exist" and "belongs to someone else".
        return order is not null && order.CustomerId == user.CustomerId ? order.Status : "unknown order";
    }

    [Description("Cancels one of the current customer's orders.")]
    public async Task<string> CancelOrderAsync(
        [Description("The order number, for example CMD-2026-0142.")] string orderNumber,
        [Description("The reason given by the customer.")] string reason)
    {
        Order? order = await orders.FindAsync(orderNumber);
        if (order is null || order.CustomerId != user.CustomerId)
        {
            return "unknown order";
        }

        await orders.CancelAsync(orderNumber, reason);
        return "cancelled";
    }
}
// --- guide snippet ends ---

public static class SupportAgent
{
    // --- guide snippet starts (agent with a write tool that needs approval) ---
    public static AIAgent Create(IChatClient chatClient, OrderTools tools) =>
        chatClient.AsAIAgent(
            instructions: "Answer questions about the customer's orders. Never guess a status: call the tool.",
            tools:
            [
                AIFunctionFactory.Create(tools.GetOrderStatusAsync, name: "get_order_status"),
                // A write never runs on the model's say-so: the framework stops and asks.
                new ApprovalRequiredAIFunction(
                    AIFunctionFactory.Create(tools.CancelOrderAsync, name: "cancel_order")),
            ]);

    public static async Task<AgentResponse> RunWithApprovalAsync(
        AIAgent agent,
        AgentSession session,
        string message,
        Func<FunctionCallContent, Task<bool>> approve,
        CancellationToken cancellationToken = default)
    {
        AgentResponse response = await agent.RunAsync(message, session, cancellationToken: cancellationToken);

        while (true)
        {
            List<ToolApprovalRequestContent> requests = response.Messages
                .SelectMany(m => m.Contents)
                .OfType<ToolApprovalRequestContent>()
                .ToList();

            if (requests.Count == 0)
            {
                return response;
            }

            List<AIContent> answers = [];
            foreach (ToolApprovalRequestContent request in requests)
            {
                // Show the call and its arguments to a person; the application records who approved.
                bool approved = await approve((FunctionCallContent)request.ToolCall);
                answers.Add(request.CreateResponse(approved));
            }

            response = await agent.RunAsync(
                new ChatMessage(ChatRole.User, answers), session, cancellationToken: cancellationToken);
        }
    }
    // --- guide snippet ends ---
}
