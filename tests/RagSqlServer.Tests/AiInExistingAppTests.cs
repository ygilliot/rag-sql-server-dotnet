using System.Runtime.CompilerServices;
using System.Threading.Channels;
using AiInExistingApp;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace RagSqlServer.Tests;

/// <summary>
/// Runs the code of the guide "Integrating AI into an existing .NET application" against a
/// scripted chat client: no API key, no network, and the tests check what the application
/// does with the model's answer rather than the answer itself.
/// </summary>
public sealed class AiInExistingAppTests
{
    private const string OrderNumber = "CMD-2026-0142";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Triage_is_automatic_when_the_order_exists_for_the_customer()
    {
        var chatClient = new ScriptedChatClient(Json("OrderStatus", OrderNumber));
        var service = new RequestTriageService(chatClient, new InMemoryOrders());

        TriageDecision decision = await service.TriageAsync(new("R1", "C1", "Where is my order CMD-2026-0142?"), Ct);

        Assert.Equal(TriageRoute.Automatic, decision.Route);
        Assert.Equal(RequestCategory.OrderStatus, decision.Result?.Category);
    }

    [Fact]
    public async Task Triage_goes_to_a_person_when_the_order_belongs_to_another_customer()
    {
        var chatClient = new ScriptedChatClient(Json("OrderStatus", OrderNumber));
        var service = new RequestTriageService(chatClient, new InMemoryOrders());

        TriageDecision decision = await service.TriageAsync(new("R1", "C2", "Where is order CMD-2026-0142?"), Ct);

        Assert.Equal(TriageRoute.HumanReview, decision.Route);
    }

    [Fact]
    public async Task Triage_goes_to_a_person_when_the_model_invents_an_order_number()
    {
        var chatClient = new ScriptedChatClient(Json("OrderStatus", "42"));
        var service = new RequestTriageService(chatClient, new InMemoryOrders());

        TriageDecision decision = await service.TriageAsync(new("R1", "C1", "Where is my order?"), Ct);

        Assert.Equal(TriageRoute.HumanReview, decision.Route);
    }

    [Fact]
    public async Task Triage_goes_to_a_person_when_the_model_answers_with_free_text()
    {
        var chatClient = new ScriptedChatClient(new ChatMessage(ChatRole.Assistant, "Sure, happy to help!"));
        var service = new RequestTriageService(chatClient, new InMemoryOrders());

        TriageDecision decision = await service.TriageAsync(new("R1", "C1", "Hello"), Ct);

        Assert.Equal(TriageRoute.HumanReview, decision.Route);
        Assert.Null(decision.Result);
    }

    [Fact]
    public async Task A_tool_never_returns_another_customers_order()
    {
        var tools = new OrderTools(new InMemoryOrders(), new FixedUser("C2"));

        Assert.Equal("unknown order", await tools.GetOrderStatusAsync(OrderNumber));
    }

    [Fact]
    public async Task A_write_tool_runs_only_after_approval()
    {
        var orders = new InMemoryOrders();
        var chatClient = new ScriptedChatClient(CancelCall(), new ChatMessage(ChatRole.Assistant, "Your order is cancelled."));
        AIAgent agent = SupportAgent.Create(chatClient, new OrderTools(orders, new FixedUser("C1")));
        AgentSession session = await agent.CreateSessionAsync(cancellationToken: Ct);
        int asked = 0;

        AgentResponse response = await SupportAgent.RunWithApprovalAsync(
            agent,
            session,
            "Cancel my order CMD-2026-0142, I ordered it twice.",
            call =>
            {
                asked++;
                Assert.Equal("cancel_order", call.Name);
                Assert.Empty(orders.Cancelled); // nothing has run yet
                return Task.FromResult(true);
            },
            Ct);

        Assert.Equal(1, asked);
        Assert.Equal([OrderNumber], orders.Cancelled);
        Assert.Contains("cancelled", response.Text);
    }

    [Fact]
    public async Task A_refused_write_tool_never_runs()
    {
        var orders = new InMemoryOrders();
        var chatClient = new ScriptedChatClient(CancelCall(), new ChatMessage(ChatRole.Assistant, "I have not cancelled anything."));
        AIAgent agent = SupportAgent.Create(chatClient, new OrderTools(orders, new FixedUser("C1")));
        AgentSession session = await agent.CreateSessionAsync(cancellationToken: Ct);

        await SupportAgent.RunWithApprovalAsync(
            agent, session, "Cancel my order CMD-2026-0142.", _ => Task.FromResult(false), Ct);

        Assert.Empty(orders.Cancelled);
    }

    [Fact]
    public async Task The_worker_triages_a_request_delivered_twice_only_once()
    {
        var chatClient = new ScriptedChatClient(Json("OrderStatus", OrderNumber));
        var store = new InMemoryRequestStore();
        await using ServiceProvider provider = new ServiceCollection()
            .AddSingleton<IChatClient>(chatClient)
            .AddSingleton<IOrderRepository>(new InMemoryOrders())
            .AddSingleton<IRequestStore>(store)
            .AddScoped<RequestTriageService>()
            .BuildServiceProvider();
        var worker = new TriageWorker(
            Channel.CreateUnbounded<IncomingRequest>().Reader,
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<TriageWorker>.Instance);
        var request = new IncomingRequest("R1", "C1", "Where is my order CMD-2026-0142?");

        await worker.ProcessAsync(request, Ct);
        await worker.ProcessAsync(request, Ct);

        Assert.Equal(1, chatClient.Calls);
        Assert.Equal(TriageRoute.Automatic, store.Decisions["R1"].Route);
    }

    private static ChatMessage Json(string category, string orderNumber) =>
        new(ChatRole.Assistant,
            $$"""{"category":"{{category}}","orderNumber":"{{orderNumber}}","summary":"Asks about an order."}""");

    private static ChatMessage CancelCall() =>
        new(ChatRole.Assistant,
        [
            new FunctionCallContent(
                "call-1",
                "cancel_order",
                new Dictionary<string, object?> { ["orderNumber"] = OrderNumber, ["reason"] = "ordered twice" }),
        ]);

    private sealed record FixedUser(string CustomerId) : ICurrentUser;

    private sealed class InMemoryOrders : IOrderRepository
    {
        private readonly Order _order = new(OrderNumber, "C1", "shipped");

        public List<string> Cancelled { get; } = [];

        public Task<Order?> FindAsync(string orderNumber, CancellationToken cancellationToken = default) =>
            Task.FromResult(orderNumber == _order.Number ? _order : null);

        public Task CancelAsync(string orderNumber, string reason, CancellationToken cancellationToken = default)
        {
            Cancelled.Add(orderNumber);
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryRequestStore : IRequestStore
    {
        private readonly HashSet<string> _begun = [];

        public Dictionary<string, TriageDecision> Decisions { get; } = [];

        public Task<bool> TryBeginAsync(string requestId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_begun.Add(requestId));

        public Task SaveAsync(string requestId, TriageDecision decision, CancellationToken cancellationToken = default)
        {
            Decisions[requestId] = decision;
            return Task.CompletedTask;
        }
    }

    /// <summary>Answers with the scripted messages, in order, and counts the calls.</summary>
    private sealed class ScriptedChatClient(params ChatMessage[] script) : IChatClient
    {
        private readonly Queue<ChatMessage> _script = new(script);

        public int Calls { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new ChatResponse(_script.Dequeue()));
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
